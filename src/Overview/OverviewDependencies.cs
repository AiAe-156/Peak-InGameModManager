#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Snapshot built at catalogue refresh. Identifiers are joined only using confirmed package provenance.</summary>
internal sealed class OverviewDependencies
{
    private readonly Dictionary<OverviewMod, List<OverviewDependencyItem>> _forward = new();
    private readonly Dictionary<string, List<OverviewDependencyItem>> _chainForward = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<OverviewDependencyItem>> _chainReverse = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OverviewDependencyItem> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private sealed class Package { internal string Key = "", Name = "", Version = ""; internal OverviewMod? Mod; internal List<string> Dependencies = new(); }

    internal OverviewDependencies(IReadOnlyList<OverviewMod> mods)
    {
        var guids = mods.SelectMany(m=>m.PluginGuids.Concat(new[]{m.Guid}).Where(g=>g.Length>0).Select(g=>(Guid:g,Mod:m)))
            .GroupBy(m => m.Guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Mod, StringComparer.OrdinalIgnoreCase);
        var packages = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Where(m => m.SourceConfirmed&&m.IdentityConflict.Length==0&&ShopInstalled.Key(m.SourceKey)))
            packages[mod.SourceKey] = new Package { Key = mod.SourceKey, Name = mod.SourceKey, Version = mod.PackageVersion, Mod = mod };
        // Resource-only packages have no BepInPlugin row. Read their own manifest and exact namespace/folder evidence.
        var packageRoots = new[] { Paths.PluginPath, Path.Combine(Paths.BepInExRootPath, "patchers"), Path.Combine(Paths.BepInExRootPath, "core") };
        foreach (string file in packageRoots.Where(Directory.Exists).SelectMany(OverviewRepository.SafeFiles).Where(f => Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("manifest.json.old", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                JObject data = JObject.Parse(File.ReadAllText(file)); string name = data.Value<string>("name") ?? "";
                string ns = data.Value<string>("namespace") ?? data.Value<string>("author") ?? "";
                var physicalOwner = mods.FirstOrDefault(m => m.SourceConfirmed && m.IdentityConflict.Length == 0 && m.PackageRoot.Equals(Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase)
                    && m.SourceName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (!ThunderstoreUpdateService.ValidPart(ns) && physicalOwner != null) ns = physicalOwner.SourceNamespace;
                if (!ThunderstoreUpdateService.ValidPart(ns))
                {
                    if (!ThunderstoreUpdateService.TryParseSource(Path.GetFileName(Path.GetDirectoryName(file)), out ns, out string folderName) || !folderName.Equals(name, StringComparison.OrdinalIgnoreCase))
                        if (!ThunderstoreUpdateService.TryParseSource(data.Value<string>("website_url") ?? "", out ns, out string linkName) || !linkName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (!ThunderstoreUpdateService.ValidPart(name) || !ThunderstoreUpdateService.ValidPart(ns)) continue;
                string key = ns + "-" + name;
                if (!packages.TryGetValue(key, out var package)) packages[key] = package = new Package { Key = key, Name = key };
                package.Version = data.Value<string>("version_number") ?? package.Version;
                package.Dependencies = data["dependencies"]?.Values<string>().Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d!).ToList() ?? new();
                // Match physical manifest ownership, not a similar name or an online source choice.
                package.Mod ??= mods.FirstOrDefault(m => string.Equals(m.PackageRoot, Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { Plugin.Log.LogDebug("[依赖] 资料包读取失败：" + ex.Message); }
        }
        foreach (var mod in mods)
        {
            var items = new List<OverviewDependencyItem>();
            foreach (var dependency in mod.RuntimeDependencies)
            {
                if (dependency.Id.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) || dependency.Id.StartsWith("BepInEx.", StringComparison.OrdinalIgnoreCase)) continue;
                guids.TryGetValue(dependency.Id, out var target);
                items.Add(new OverviewDependencyItem { Identity = target!=null ? Identity(target) : "guid:" + dependency.Id, Name = target?.Name ?? dependency.Id,
                    Mod = target, Installed = target != null, Version = target?.PluginVersion ?? "", Required = dependency.MinimumVersion, Optional = dependency.Optional });
            }
            foreach (string declaration in mod.PackageDependencies.Concat(packages.Values.Where(p => p.Mod == mod).SelectMany(p => p.Dependencies)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var match = Regex.Match(declaration, @"^([A-Za-z0-9_]+)-([A-Za-z0-9_]+)-(\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?)$");
                if (!match.Success) { items.Add(new OverviewDependencyItem { Identity = declaration, Name = declaration, Unknown = true }); continue; }
                string key = match.Groups[1].Value + "-" + match.Groups[2].Value;
                bool framework = key.Equals("BepInEx-BepInExPack_PEAK", StringComparison.OrdinalIgnoreCase) || key.Equals("BepInEx-BepInExPack", StringComparison.OrdinalIgnoreCase);
                if (framework) continue;
                packages.TryGetValue(key, out var package);
                // Distribution package releases cannot be compared to BepInEx's internal assembly version.
                items.Add(new OverviewDependencyItem { Identity = package?.Mod!=null ? Identity(package.Mod) : "package:" + key, Name = package?.Mod?.Name ?? key,
                    Mod = package?.Mod, Installed = package != null || framework, Framework = framework,
                    Unknown = package == null && !framework && mods.Any(m => m.PackageName.Equals(match.Groups[2].Value, StringComparison.OrdinalIgnoreCase) && !m.SourceConfirmed),
                    Version = package?.Version ?? "", Required = match.Groups[3].Value });
            }
            _forward[mod] = items.GroupBy(i => i.Identity, StringComparer.OrdinalIgnoreCase).Select(g =>
            {
                var item = g.First(); item.Optional = g.All(i => i.Optional);
                // Preserve both metadata requirements where the same package is declared through GUID and package ID.
                var unmet = g.FirstOrDefault(i => VersionState(i) != "");
                if (unmet != null) { item.Required = unmet.Required; item.Version = unmet.Version; }
                return item;
            }).ToList();
        }
        // Complete graph snapshot also includes resource-only packages, so both directions
        // can cross a package node. Hover only reads these dictionaries.
        var nodes = _nodes;
        foreach (var mod in mods)
        {
            string identity=Identity(mod);
            if (!nodes.ContainsKey(identity)) nodes[identity] = new OverviewDependencyItem { Identity = identity, Name = mod.Name, Mod = mod, Installed = true, Version = mod.PluginVersion };
            if (!_chainForward.TryGetValue(identity, out var edges)) _chainForward[identity] = edges = new();
            foreach (var item in _forward[mod]) if (!edges.Any(e => e.Identity.Equals(item.Identity, StringComparison.OrdinalIgnoreCase))) edges.Add(item);
        }
        foreach (var resource in packages.Values.Where(p => p.Mod == null))
        {
            string identity = "package:" + resource.Key;
            nodes[identity] = new OverviewDependencyItem { Identity = identity, Name = resource.Name, Installed = true, Version = resource.Version };
            var edges = new List<OverviewDependencyItem>();
            foreach (string declaration in resource.Dependencies)
            {
                var match = Regex.Match(declaration, @"^([A-Za-z0-9_]+)-([A-Za-z0-9_]+)-(\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?)$");
                if (!match.Success) { edges.Add(new OverviewDependencyItem { Identity = declaration, Name = declaration, Unknown = true }); continue; }
                string key = match.Groups[1].Value + "-" + match.Groups[2].Value;
                if (key.Equals("BepInEx-BepInExPack_PEAK", StringComparison.OrdinalIgnoreCase) || key.Equals("BepInEx-BepInExPack", StringComparison.OrdinalIgnoreCase)) continue;
                packages.TryGetValue(key, out var target);
                edges.Add(new OverviewDependencyItem { Identity = target?.Mod!=null ? Identity(target.Mod) : "package:" + key, Name = target?.Mod?.Name ?? key,
                    Mod = target?.Mod, Installed = target != null, Version = target?.Version ?? "", Required = match.Groups[3].Value,
                    Unknown = target == null && mods.Any(m => m.PackageName.Equals(match.Groups[2].Value, StringComparison.OrdinalIgnoreCase) && !m.SourceConfirmed) });
            }
            _chainForward[identity] = edges.GroupBy(i => i.Identity, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }
        foreach (var pair in _chainForward)
        foreach (var edge in pair.Value)
        {
            if (!_chainReverse.TryGetValue(edge.Identity, out var incoming)) _chainReverse[edge.Identity] = incoming = new();
            if (nodes.TryGetValue(pair.Key, out var source) && !incoming.Any(i => i.Identity.Equals(source.Identity, StringComparison.OrdinalIgnoreCase)))
                incoming.Add(new OverviewDependencyItem { Identity = source.Identity, Name = source.Name, Mod = source.Mod,
                    Installed = true, Version = source.Version, Optional = edge.Optional });
        }
    }
    internal IReadOnlyList<OverviewDependencyItem> Forward(OverviewMod mod) => _forward.TryGetValue(mod, out var result) ? result : Array.Empty<OverviewDependencyItem>();
    internal IReadOnlyList<OverviewDependencyItem> Reverse(OverviewMod mod) => Reverse(Identity(mod));
    internal IReadOnlyList<OverviewDependencyItem> Forward(string identity) => _chainForward.TryGetValue(identity, out var result) ? result : Array.Empty<OverviewDependencyItem>();
    internal IReadOnlyList<OverviewDependencyItem> Reverse(string identity) => _chainReverse.TryGetValue(identity, out var result) ? result : Array.Empty<OverviewDependencyItem>();
    internal IEnumerable<OverviewDependencyItem> Nodes=>_nodes.Values;
    internal IEnumerable<(OverviewDependencyItem Source,OverviewDependencyItem Target)> Relationships
    {
        get {foreach(var pair in _chainForward)if(_nodes.TryGetValue(pair.Key,out var source))foreach(var target in pair.Value)yield return(source,target);}
    }
    internal static string Identity(OverviewMod mod)=>mod.SourceConfirmed&&mod.IdentityConflict.Length==0&&ShopInstalled.Key(mod.SourceKey) ? "package:"+mod.SourceKey
        :mod.Guid.Length>0 ? "guid:"+mod.Guid :mod.Location.Length>0 ? "file:"+mod.Location
        :mod.PackageRoot.Length>0 ? "path:"+mod.PackageRoot :"local:"+RuntimeHelpers.GetHashCode(mod);
    internal static string VersionState(OverviewDependencyItem item)
    {
        if (!item.Installed || string.IsNullOrEmpty(item.Required)) return "";
        if (string.IsNullOrEmpty(item.Version)) return Loc.F("Version unknown (requires ≥ {0})", "版本待确认（需 ≥ {0}）", "版本待確認（需 ≥ {0}）", item.Required);
        if (Version.TryParse(item.Version, out var actual) && Version.TryParse(item.Required, out var required))
            return actual < required ? Loc.F("Outdated version ({0} < {1})", "版本不足（{0} < {1}）", "版本不足（{0} < {1}）", item.Version, item.Required) : "";
        return Loc.F("Version unconfirmed ({0}; requires ≥ {1})", "版本待确认（{0}；需 ≥ {1}）", "版本待確認（{0}；需 ≥ {1}）", item.Version, item.Required);
    }
}

