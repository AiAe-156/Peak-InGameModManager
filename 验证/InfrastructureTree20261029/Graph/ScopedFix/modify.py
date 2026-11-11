from pathlib import Path
p=Path('src/Updating/ComponentDependencyGraph.cs');s=p.read_text(encoding='utf-8');s=s.replace('    static readonly StringComparer Compare', '''    public sealed class FailureScope
    {
        public string Node = "", Path = "", Reason = "";
    }
    public readonly List<FailureScope> FailureScopes = new List<FailureScope>();
    public void AddFailure(string reason, string node = "", string path = "")
    {
        Failures.Add(reason);
        FailureScopes.Add(new FailureScope { Node = node, Path = path, Reason = reason });
    }
    static readonly StringComparer Compare''');s=s.replace('bool unknown = Failures.Count > 0 || pending.Count == 0;', '''bool unknown = Failures.Count > FailureScopes.Count || FailureScopes.Any(f => f.Node.Length == 0) || pending.Count == 0;''');s=s.replace('string current = pending.Dequeue();','''string current = pending.Dequeue();
            if (FailureScopes.Any(f => Compare.Equals(f.Node, current))) unknown = true;''');s=s.replace('        return new Capability { CanUninstall = true };','''        bool scopedUnknown = FailureScopes.Any(f => Compare.Equals(f.Node, target!.Id));
        return new Capability { CanUninstall = true, Unknown = scopedUnknown,
            Reason = scopedUnknown ? "Evidence incomplete; verification-only uninstall, independent ownership and reverse-dependency checks required. " + string.Join("; ", FailureScopes.Where(f => Compare.Equals(f.Node, target!.Id)).Select(f => f.Reason)) : "" };''');p.write_text(s,encoding='utf-8')
p=Path('src/Updating/ComponentDependencyCollector.cs');s=p.read_text(encoding='utf-8');s=s.replace('public string PackageKey = "", Path = "";', 'public string PackageKey = "", Path = "", Hash = "";');s=s.replace('Name, Token;', 'Name, Token, Culture;');s=s.replace('Name = name.Name; Version = name.Version;', 'Name = name.Name; Version = name.Version; Culture = name.Culture ?? "";');s=s.replace('Version.Equals(other.Version) && StringComparer.OrdinalIgnoreCase.Equals(Token, other.Token);','other.Version >= Version && StringComparer.OrdinalIgnoreCase.Equals(Token, other.Token)\n            && StringComparer.OrdinalIgnoreCase.Equals(Culture, other.Culture);');s=s.replace(' + ", PublicKeyToken="', ' + ", Culture=" + Culture + ", PublicKeyToken="');s=s.replace('internal ComponentDependencyGraph.Component Owner = null!;', 'internal ComponentDependencyGraph.Component Owner = null!;\n        internal string File = "";');s=s.replace('graph.Failures.Add(e.Message);','graph.AddFailure(e.Message);',1)
a=s.index('        var manifests =');b=s.index('        var metadata =',a)
s=s[:a]+'''        var claims = new Dictionary<string, HashSet<string>>(compare);
        var badClaims = new List<(string Path, string Key, string Reason)>();
        foreach (var claim in confirmedFiles ?? Array.Empty<ConfirmedFile>())
        {
            if (!ValidKey(claim.PackageKey)) { graph.AddFailure("Invalid confirmed package key: " + claim.PackageKey); continue; }
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, claim.Path));
            if (!files.Contains(path, compare)) continue;
            if (!claims.TryGetValue(path, out var owners)) claims[path] = owners = new HashSet<string>(compare);
            owners.Add(claim.PackageKey);
            try
            {
                using var stream = File.OpenRead(path);
                using var sha = System.Security.Cryptography.SHA256.Create();
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                if (claim.Hash.Length > 0 && !compare.Equals(claim.Hash, actual))
                    badClaims.Add((path, claim.PackageKey, "Confirmed file hash mismatch: " + path));
            }
            catch (Exception e) { badClaims.Add((path, claim.PackageKey, "Cannot verify confirmed file: " + path + ": " + e.Message)); }
        }
        var manifests = new Dictionary<string, Manifest>(compare);
        var badManifests = new List<(string Path, string Reason)>();
        foreach (string file in files.Where(f => compare.Equals(System.IO.Path.GetFileName(f), "manifest.json")).OrderBy(f => f.Length))
        {
            string directory = System.IO.Path.GetDirectoryName(file)!;
            // A resource/document manifest beneath an established package is not a new package.
            if (manifests.Keys.Any(d => directory.StartsWith(d + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                && !ValidKey(System.IO.Path.GetFileName(directory))) continue;
            try
            {
                var manifest = readManifest(file);
                if (!ValidKey(manifest.Key)) throw new IOException("Unconfirmed package identity: " + file);
                manifests[directory] = manifest;
            }
            catch (Exception e) { badManifests.Add((file, "Manifest evidence: " + file + ": " + e.Message)); }
        }
        ComponentDependencyGraph.Component Component(string key, string relative)
        {
            string id = key.Length > 0 ? key : "file:" + relative;
            var owner = graph.Components.FirstOrDefault(c => compare.Equals(c.Id, id));
            if (owner == null) { owner = new ComponentDependencyGraph.Component { Id = id, PackageKey = key }; graph.Components.Add(owner); }
            if (!owner.Locations.Contains(relative, compare)) owner.Locations.Add(relative);
            return owner;
        }
        List<ComponentDependencyGraph.Component> Owners(string file)
        {
            var nearest = manifests.Keys.Where(d => file.StartsWith(d + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.Length).FirstOrDefault();
            string key = nearest == null ? "" : manifests[nearest].Key;
            string relative = file.Substring(loaderRoot.Length + 1).Replace('\\', '/');
            var keys = claims.TryGetValue(file, out var found) ? new HashSet<string>(found, compare) : new HashSet<string>(compare);
            if (key.Length > 0) keys.Add(key);
            return keys.Count == 0 ? new List<ComponentDependencyGraph.Component> { Component("", relative) }
                : keys.Select(k => Component(k, relative)).ToList();
        }
        foreach (string file in files) Owners(file);
        foreach (var bad in badClaims) graph.AddFailure(bad.Reason, bad.Key, bad.Path);
        foreach (var bad in badManifests) foreach (var owner in Owners(bad.Path)) graph.AddFailure(bad.Reason, owner.Id, bad.Path);
''' + s[b:]
s=s.replace('var owner = Owner(file);', 'var owners = Owners(file);\n            var owner = owners[0];');s=s.replace('new Metadata { Owner = owner }','new Metadata { Owner = owner, File = file }');s=s.replace('owner.Assemblies.Add(assembly.Name.Name);','foreach (var o in owners) o.Assemblies.Add(assembly.Name.Name);');s=s.replace('owner.PluginGuids.Add((string)identity.ConstructorArguments[0].Value);','foreach (var o in owners) o.PluginGuids.Add((string)identity.ConstructorArguments[0].Value);')
s=s.replace('if (!NativePe(file)) graph.Failures.Add("Unreadable component metadata: " + file);','if (!NativePe(file)) foreach (var o in owners) graph.AddFailure("Unreadable component metadata: " + file, o.Id, file);')
s=s.replace('catch (Exception e) { graph.Failures.Add(file + ": " + e.Message); }','catch (Exception e) { foreach (var o in owners) graph.AddFailure(file + ": " + e.Message, o.Id, file); }')
s=s.replace('var owner = Owner(System.IO.Path.Combine(manifest.Key, "manifest.json"));','var owners = Owners(System.IO.Path.Combine(manifest.Key, "manifest.json"));')
s=s.replace('graph.Failures.Add("Invalid manifest dependency: " + dependency);','foreach (var o in owners) graph.AddFailure("Invalid manifest dependency: " + dependency, o.Id, manifest.Key);')
s=s.replace('Edge(owner.Id, dependency.Substring(0, at)', 'foreach (var owner in owners) Edge(owner.Id, dependency.Substring(0, at)')
s=s.replace('foreach (var data in metadata) foreach (var requirement in data.Requirements)','foreach (var data in metadata) foreach (var owner in Owners(data.File)) foreach (var requirement in data.Requirements)')
s=s.replace('Edge(data.Owner.Id,', 'Edge(owner.Id,')
s=s.replace('Edge(owner.Id, "missing:" + requirement.Target, evidence, requirement.Hard);','''Edge(owner.Id, "missing:" + requirement.Target, evidence, requirement.Hard);
                foreach (var failure in graph.FailureScopes.Where(f => f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith(".dll.old", StringComparison.OrdinalIgnoreCase)).ToList())
                    Edge(owner.Id, failure.Node, "possible provider for unresolved " + requirement.Target + " at " + failure.Path, requirement.Hard);''')
p.write_text(s,encoding='utf-8')
p=Path('Updater/ShopComponentProtection.cs');s=p.read_text(encoding='utf-8');s=s.replace('Path = f.Path','Path = f.Path, Hash = f.Hash');p.write_text(s,encoding='utf-8')
