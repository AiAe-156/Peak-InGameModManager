#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Updating
{
    /// <summary>One file or folder under plugins/, as collected by the caller. Paths are relative to BepInEx, '/' separated.</summary>
    public sealed class FolderEntry
    {
        public string Path = "";
        public bool IsDirectory;
        public bool HasLink;
        /// <summary>Folders: a manifest.json (or manifest.json.old, a mod MCE disabled) sits directly inside.</summary>
        public bool HasManifest;
        public string ManifestName = "", ManifestVersion = "";
        public List<string> Dependencies = new List<string>();
        /// <summary>Files: a plugin assembly (".dll" or a disabled ".dll.*").</summary>
        public bool IsAssembly;
        /// <summary>Files: BepInPlugin ids read from the assembly.</summary>
        public List<string> PluginGuids = new List<string>();
    }

    /// <summary>One overview row: the group the user sees it in.</summary>
    public sealed class FolderRow { public List<string> Guids = new List<string>(); public string GroupId = "", PackageRoot = "", Version = ""; public bool Loaded; }
    public sealed class FolderGroup { public string Id = "", Name = "", ParentId = ""; }

    public sealed class FolderLayoutInput
    {
        public string BepInExAbsolutePath = "";
        public int MaximumFullPathLength = 240;
        public string UngroupedFolderName = "Ungrouped";
        public string ManagerGuid = ManagerIdentity.Guid;
        public List<FolderGroup> Groups = new List<FolderGroup>();
        public List<FolderEntry> Entries = new List<FolderEntry>();
        public List<FolderRow> Rows = new List<FolderRow>();
    }

    public sealed class FolderMove { public string From = "", To = ""; public bool Container; }
    /// <summary>A package whose location changes (moved itself, or carried by a renamed category folder).</summary>
    public sealed class FolderPackageChange { public string OriginalPath = "", FinalPath = "", GroupId = ""; public List<string> Files = new List<string>(); }

    public sealed class FolderLayoutResult
    {
        public readonly List<FolderMove> Moves = new List<FolderMove>();
        public readonly List<FolderPackageChange> Packages = new List<FolderPackageChange>();
        public readonly List<LayoutTreeNode> Nodes = new List<LayoutTreeNode>();
        public readonly List<string> Details = new List<string>();
    }

    /// <summary>What each folder under plugins/ is. A package is a folder with its own manifest.json (or, outside any
    /// package, a folder holding plugin assemblies directly); a folder inside a package is part of it, except a nested
    /// package with its own manifest — unless that is the package's own layout (inside its plugins/, BepInEx/ or
    /// patchers/, or the same manifest name). A container holds packages but is none; an empty folder holds neither.</summary>
    public sealed class FolderClassification
    {
        public readonly HashSet<string> Packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Containers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Top-most folders that hold no package (and so no plugin assembly).</summary>
        public readonly HashSet<string> Empties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Owning package of every folder and file ("" outside packages).</summary>
        public readonly Dictionary<string, string> Owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Package → the package it is nested in ("" at top level).</summary>
        public readonly Dictionary<string, string> OuterOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Folder organization (2.3.0): moves whole package folders, names unchanged, into folders that mirror the
    /// (nested) group tree, and extracts nested packages. Pure: the caller collects the facts and executes the moves.</summary>
    public static class GroupFolderPlanner
    {
        public const int MaxGroupDepth = 3;
        private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
        private static readonly string[] Packaging = { "plugins", "BepInEx", "patchers" };

        public static string Normalize(string? path) => (path ?? "").Replace('\\', '/').Trim('/');
        public static string Parent(string path) { int at = path.LastIndexOf('/'); return at < 0 ? "" : path.Substring(0, at); }
        public static string Leaf(string path) => path.Substring(path.LastIndexOf('/') + 1);
        private static int Depth(string path) => path.Count(c => c == '/');
        private static bool Within(string path, string parent) => Paths.Equals(path, parent) || path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>Folders the organizer never touches: generated hook assemblies and the manager's own pack data.</summary>
        public static bool NeverTouch(string path)
        {
            var parts = Normalize(path).Split('/');
            if (parts.Length < 2 || !parts[0].Equals("plugins", StringComparison.OrdinalIgnoreCase)) return false;
            return parts[1].Equals("MMHOOK", StringComparison.OrdinalIgnoreCase) || ManagerIdentity.IsPackFolderPrefixed(parts[1]);
        }

        /// <summary>A group name as a folder name: characters Windows does not allow are dropped (not replaced).</summary>
        public static string FolderName(string? name)
        {
            var text = new string((name ?? "").Where(c => c >= 32 && c != 127 && "\\/:*?\"<>|".IndexOf(c) < 0).ToArray()).Trim().TrimEnd('.', ' ');
            if (text.Length == 0) return "_";
            string stem = text.Split('.')[0].Trim().ToUpperInvariant();
            bool device = stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]);
            return device ? "_" + text : text;
        }

        public static FolderClassification Classify(IEnumerable<FolderEntry> entries)
        {
            var c = new FolderClassification();
            var list = entries.Where(e => e != null).Select(e => { e.Path = Normalize(e.Path); return e; })
                .Where(e => e.Path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) && !NeverTouch(e.Path)).ToList();
            var byPath = new Dictionary<string, FolderEntry>(Paths);
            foreach (var e in list) byPath[e.Path] = e;
            var assemblyDirs = new HashSet<string>(list.Where(e => !e.IsDirectory && e.IsAssembly).Select(e => Parent(e.Path)), Paths);
            var candidates = new List<string>();
            string OwnerOfParent(string path) { string parent = Parent(path); return parent.Equals("plugins", StringComparison.OrdinalIgnoreCase) ? "" : c.Owner.TryGetValue(parent, out var o) ? o : ""; }
            foreach (var dir in list.Where(e => e.IsDirectory).OrderBy(e => Depth(e.Path)).ThenBy(e => e.Path, Paths))
            {
                string owner = OwnerOfParent(dir.Path);
                if (dir.HasManifest)
                {
                    if (owner.Length > 0 && IsPackaging(dir.Path, owner, byPath)) { c.Owner[dir.Path] = owner; continue; }
                    c.Packages.Add(dir.Path); c.Owner[dir.Path] = dir.Path; c.OuterOf[dir.Path] = owner; continue;
                }
                if (owner.Length == 0 && assemblyDirs.Contains(dir.Path)) { c.Packages.Add(dir.Path); c.Owner[dir.Path] = dir.Path; c.OuterOf[dir.Path] = ""; continue; }
                c.Owner[dir.Path] = owner;
                if (owner.Length == 0) candidates.Add(dir.Path);
            }
            foreach (var file in list.Where(e => !e.IsDirectory)) c.Owner[file.Path] = OwnerOfParent(file.Path);
            var empty = new HashSet<string>(Paths);
            foreach (string candidate in candidates)
                if (c.Packages.Any(p => p.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase))) c.Containers.Add(candidate); else empty.Add(candidate);
            foreach (string folder in empty) if (!empty.Contains(Parent(folder))) c.Empties.Add(folder);
            return c;
        }

        private static bool IsPackaging(string inner, string outer, Dictionary<string, FolderEntry> byPath)
        {
            string first = inner.Substring(outer.Length + 1).Split('/')[0];
            if (Packaging.Any(p => p.Equals(first, StringComparison.OrdinalIgnoreCase))) return true;
            string a = byPath.TryGetValue(inner, out var ie) ? ie.ManifestName : "", b = byPath.TryGetValue(outer, out var oe) ? oe.ManifestName : "";
            return a.Length > 0 && Paths.Equals(a, b);
        }

        /// <summary>Group chain (root first), honouring the same rules as the plugin's GroupTree: a missing parent, a
        /// cycle or a chain deeper than three levels is read as top level.</summary>
        private static List<FolderGroup> Chain(Dictionary<string, FolderGroup> groups, string id)
        {
            var chain = new List<FolderGroup>();
            if (!groups.TryGetValue(id ?? "", out var group)) return chain;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var cursor = group; cursor != null; )
            {
                if (!seen.Add(cursor.Id) || chain.Count >= MaxGroupDepth) return new List<FolderGroup> { group };
                chain.Insert(0, cursor);
                cursor = cursor.ParentId.Length > 0 && cursor.ParentId != cursor.Id && groups.TryGetValue(cursor.ParentId, out var parent) ? parent : null;
            }
            return chain;
        }

        /// <summary>The working tree while moves are planned: every path, rewritten as folders move.</summary>
        private sealed class Simulation
        {
            internal readonly HashSet<string> Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, string> Current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal bool Occupied(string path) => Paths.Contains(path);
            internal void Move(string from, string to)
            {
                var moved = Paths.Where(p => Within(p, from)).ToList();
                foreach (string p in moved) Paths.Remove(p);
                foreach (string p in moved) Paths.Add(to + p.Substring(from.Length));
                for (string parent = Parent(to); parent.Length > 0; parent = Parent(parent)) Paths.Add(parent);
                foreach (string key in Current.Keys.ToList())
                    if (Within(Current[key], from)) Current[key] = to + Current[key].Substring(from.Length);
            }
        }

        public static FolderLayoutResult Plan(FolderLayoutInput input)
        {
            var result = new FolderLayoutResult();
            var entries = input.Entries.Where(e => e != null).ToList();
            var cls = Classify(entries);
            var byPath = new Dictionary<string, FolderEntry>(Paths);
            foreach (var e in entries) byPath[e.Path] = e;
            var groups = new Dictionary<string, FolderGroup>(StringComparer.Ordinal);
            foreach (var g in input.Groups) if (g != null && g.Id.Length > 0 && !groups.ContainsKey(g.Id)) groups.Add(g.Id, g);
            string GroupPath(string id) => "plugins/" + string.Join("/", Chain(groups, id).Select(g => FolderName(g.Name)));
            string ungroupedPath = "plugins/" + FolderName(input.UngroupedFolderName);

            // Package facts.
            var files = new Dictionary<string, List<FolderEntry>>(Paths);
            foreach (string p in cls.Packages) files[p] = new List<FolderEntry>();
            foreach (var e in entries.Where(e => !e.IsDirectory && !NeverTouch(e.Path)))
                if (cls.Owner.TryGetValue(e.Path, out var owner) && owner.Length > 0) files[owner].Add(e);
            bool HasLink(string package) => byPath.TryGetValue(package, out var d) && d.HasLink
                || entries.Any(e => e.HasLink && cls.Owner.TryGetValue(e.Path, out var o) && Paths.Equals(o, package));
            var guids = cls.Packages.ToDictionary(p => p, p => new HashSet<string>(files[p].SelectMany(f => f.PluginGuids), StringComparer.OrdinalIgnoreCase), Paths);
            // A row's folder may be a folder inside the package's own layout (the overview stops at the nearest manifest,
            // and some packages carry a second manifest in their plugins/): map it to the package that owns that folder.
            string PackageOf(string folder) => folder.Length > 0 && cls.Owner.TryGetValue(folder, out var owner) && owner.Length > 0 ? owner : folder;
            var rows = input.Rows.Where(r => r != null).Select(r => { r.PackageRoot = PackageOf(Normalize(r.PackageRoot)); return r; }).ToList();
            var rowsOf = cls.Packages.ToDictionary(p => p, p => rows.Where(r => Paths.Equals(r.PackageRoot, p) || r.Guids.Any(g => guids[p].Contains(g)) && (r.PackageRoot.Length == 0 || Paths.Equals(r.PackageRoot, p))).ToList(), Paths);

            // Which group each package follows.
            var groupOf = new Dictionary<string, string>(Paths);
            var notes = cls.Packages.ToDictionary(p => p, p => new List<string>(), Paths);
            var known = new HashSet<string>(Paths);
            foreach (string p in cls.Packages)
            {
                var own = rowsOf[p];
                if (own.Count == 0) continue;
                var chosen = own.FirstOrDefault(r => r.Loaded) ?? own.GroupBy(r => r.GroupId).OrderByDescending(g => g.Count()).First().First();
                groupOf[p] = groups.ContainsKey(chosen.GroupId) ? chosen.GroupId : "";
                known.Add(p);
            }
            foreach (string p in cls.Packages.Where(p => !known.Contains(p)).OrderBy(p => p, Paths))
            {
                string name = byPath.TryGetValue(p, out var d) ? d.ManifestName : "";
                string? twin = name.Length == 0 ? null : known.FirstOrDefault(k => byPath.TryGetValue(k, out var kd) && Paths.Equals(kd.ManifestName, name));
                if (twin != null) { groupOf[p] = groupOf[twin]; notes[p].Add("GroupFromSameName"); continue; }
                string? dependent = name.Length == 0 ? null : known.OrderBy(k => k, Paths).FirstOrDefault(k => byPath.TryGetValue(k, out var kd) && kd.Dependencies.Any(dep => DependsOn(dep, name, Leaf(p))));
                if (dependent != null) { groupOf[p] = groupOf[dependent]; notes[p].Add("GroupFromDependent"); continue; }
                notes[p].Add("NoGroupKnown");
            }

            var sim = new Simulation();
            foreach (var e in entries) sim.Paths.Add(e.Path);
            foreach (string p in cls.Packages) sim.Current[p] = p;
            foreach (string c in cls.Containers) sim.Current[c] = c;
            // Empty folders are tracked too: one inside a category folder that gets renamed is somewhere else afterwards,
            // and that later path is where the Recycle Bin step must look.
            foreach (string e in cls.Empties) sim.Current[e] = e;

            // Category folders that stand for a group are renamed to the group's folder (shallow targets first).
            var mapped = new Dictionary<string, string>(Paths);
            var takenGroups = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<(string Container, string Group, bool Named, int Votes)>();
            foreach (string c in cls.Containers.Where(c => !byPath.TryGetValue(c, out var d) || !d.HasLink).Where(c => !entries.Any(e => e.HasLink && Within(e.Path, c))))
            {
                var named = groups.Values.FirstOrDefault(g => Paths.Equals(FolderName(g.Name), Leaf(c)));
                if (named != null) { candidates.Add((c, named.Id, true, int.MaxValue)); continue; }
                // A container stands for a group only when it holds at least two packages directly and most of them are
                // in that group; a lone wrapper folder around one mod is a shell to be emptied, not a category.
                var direct = cls.Packages.Where(p => Paths.Equals(Parent(p), c) && groupOf.TryGetValue(p, out var g) && g.Length > 0).Select(p => groupOf[p]).ToList();
                int directKnown = cls.Packages.Count(p => Paths.Equals(Parent(p), c) && known.Contains(p));
                if (direct.Count == 0 || directKnown < 2) continue;
                var best = direct.GroupBy(g => g).OrderByDescending(g => g.Count()).First();
                if (best.Count() * 2 > directKnown) candidates.Add((c, best.Key, false, best.Count()));
            }
            foreach (var candidate in candidates.OrderByDescending(x => x.Named).ThenBy(x => Depth(x.Container)).ThenByDescending(x => x.Votes).ThenBy(x => x.Container, Paths))
            {
                if (mapped.ContainsKey(candidate.Container) || !takenGroups.Add(candidate.Group)) continue;
                mapped[candidate.Container] = candidate.Group;
            }
            foreach (var pair in mapped.OrderBy(m => Depth(GroupPath(m.Value))).ThenBy(m => Depth(m.Key)).ThenBy(m => m.Key, Paths))
            {
                string from = sim.Current[pair.Key], to = GroupPath(pair.Value);
                if (Paths.Equals(from, to)) { result.Details.Add("分组目录：" + from); continue; }
                if (Within(to, from) || Within(from, to) || sim.Occupied(to)) { result.Details.Add("分组目录未改名（目标已被占用）：" + from + " → " + to); continue; }
                result.Moves.Add(new FolderMove { From = from, To = to, Container = true });
                result.Details.Add("分组目录：" + from + " → " + to);
                sim.Move(from, to);
                result.Nodes.Add(new LayoutTreeNode { Kind = LayoutNodeKind.Container, GroupId = pair.Value, OriginalPath = pair.Key, FinalPath = to, Folder = Leaf(to),
                    Group = Parent(to).Length > "plugins".Length ? Parent(to).Substring("plugins/".Length) : "", Disposition = InstallationLayoutDisposition.Move });
            }

            // Where every package should end up.
            string manager = cls.Packages.FirstOrDefault(p => guids[p].Contains(input.ManagerGuid) || rowsOf[p].Any(r => r.Guids.Contains(input.ManagerGuid, StringComparer.OrdinalIgnoreCase))) ?? "";
            var reasons = cls.Packages.ToDictionary(p => p, p => new List<string>(), Paths);
            var target = new Dictionary<string, string>(Paths);
            foreach (string p in cls.Packages)
            {
                if (notes[p].Contains("NoGroupKnown")) continue;
                if (HasLink(p)) { reasons[p].Add("LinkRejected"); continue; }
                string g = groupOf[p];
                target[p] = (g.Length > 0 ? GroupPath(g) : ungroupedPath) + "/" + Leaf(p);
            }
            var movers = target.Where(t => !Paths.Equals(sim.Current[t.Key], t.Value)).Select(t => t.Key).ToList();
            var moverSet = new HashSet<string>(movers, Paths);
            var stays = cls.Packages.Where(p => !moverSet.Contains(p)).ToList();
            var claimed = new HashSet<string>(stays.Select(p => sim.Current[p]), Paths);
            var final = new Dictionary<string, string>(Paths);
            bool FreeAt(string dest)
            {
                if (claimed.Contains(dest)) return false;
                if (!sim.Occupied(dest)) return true;
                // Occupied only by a package that is about to move away (or by something inside one) is free.
                return movers.Any(m => Within(dest, sim.Current[m])) && !claimed.Any(c => Within(dest, c));
            }
            bool InsidePackage(string folder) => claimed.Concat(stays.Select(s => sim.Current[s])).Any(c => Within(folder, c));
            string VersionOf(string p) => rowsOf[p].FirstOrDefault(r => r.Version.Length > 0)?.Version ?? (byPath.TryGetValue(p, out var d) ? d.ManifestVersion : "");
            foreach (string p in movers.OrderByDescending(m => rowsOf[m].Any(r => r.Loaded)).ThenBy(m => m, Paths))
            {
                string dest = target[p];
                if (InsidePackage(Parent(dest))) { reasons[p].Add("DestinationInsidePackage"); continue; }
                if (!FreeAt(dest))
                {
                    string version = FolderName(VersionOf(p));
                    string suffixed = version == "_" ? "" : dest + " " + version;
                    if (suffixed.Length == 0 || !FreeAt(suffixed)) { reasons[p].Add("DestinationOccupied"); continue; }
                    dest = suffixed; notes[p].Add("NameSuffixed");
                }
                long longest = files[p].Select(f => (long)f.Path.Length - p.Length).DefaultIfEmpty(0).Max();
                if (input.BepInExAbsolutePath.TrimEnd('\\', '/').Length + 1L + dest.Length + longest > input.MaximumFullPathLength) { reasons[p].Add("PathLengthExceeded"); continue; }
                final[p] = dest; claimed.Add(dest);
            }

            // Execute in a safe order: nested packages before the packages they leave, the manager last.
            foreach (string p in final.Keys.OrderBy(m => Paths.Equals(m, manager) ? 1 : 0).ThenByDescending(m => Depth(sim.Current[m])).ThenBy(m => m, Paths).ToList())
            {
                string from = sim.Current[p], to = final[p];
                if (Paths.Equals(from, to)) continue;
                result.Moves.Add(new FolderMove { From = from, To = to });
                if (cls.OuterOf.TryGetValue(p, out var outer) && outer.Length > 0) notes[p].Add("NestedExtracted");
                sim.Move(from, to);
            }
            if (manager.Length > 0) notes[manager].Add("ManagerMovedLast");

            foreach (string p in cls.Packages.OrderBy(p => p, Paths))
            {
                string end = sim.Current[p];
                bool moved = !Paths.Equals(end, p);
                if (moved)
                {
                    if (!final.ContainsKey(p)) notes[p].Add("CarriedByFolderRename");
                    result.Packages.Add(new FolderPackageChange { OriginalPath = p, FinalPath = end, GroupId = groupOf.TryGetValue(p, out var gid) ? gid : "",
                        Files = files[p].Select(f => f.Path.Substring(p.Length + 1)).ToList() });
                    result.Details.Add(p + " → " + end);
                }
                else if (reasons[p].Count > 0) result.Details.Add(p + "：跳过（" + string.Join(", ", reasons[p]) + "）");
                else result.Details.Add(p + "：位置已符合，无需整理");
                bool ungrouped = known.Contains(p) && groupOf[p].Length == 0 || !known.Contains(p) && !notes[p].Contains("NoGroupKnown") && groupOf.TryGetValue(p, out var inherited) && inherited.Length == 0;
                string parent = Parent(end);
                result.Nodes.Add(new LayoutTreeNode
                {
                    Kind = LayoutNodeKind.Package, OriginalPath = p, FinalPath = end, Folder = Leaf(end),
                    GroupId = groupOf.TryGetValue(p, out var g) ? g : "", Group = parent.Length > "plugins".Length ? parent.Substring("plugins/".Length) : "",
                    Disposition = reasons[p].Count > 0 ? InstallationLayoutDisposition.Deferred : moved ? InstallationLayoutDisposition.Move : InstallationLayoutDisposition.Unchanged,
                    Reasons = string.Join(", ", reasons[p]), Notes = notes[p].Distinct().ToList(), Ungrouped = ungrouped,
                    Manager = Paths.Equals(p, manager), ModCount = rowsOf[p].Count
                });
            }

            foreach (string folder in cls.Empties.OrderBy(f => f, Paths))
                // 2.3.1: an empty folder the moves fill (an existing, still empty group folder) is not "empty" in the review.
                if (!cls.Packages.Any(p => Within(sim.Current[p], sim.Current[folder])))
                result.Nodes.Add(new LayoutTreeNode { Kind = LayoutNodeKind.Empty, OriginalPath = folder, FinalPath = sim.Current.TryGetValue(folder, out var now) ? now : folder,
                    Folder = Leaf(folder), Group = Parent(folder).Length > "plugins".Length ? Parent(folder).Substring("plugins/".Length) : "",
                    FileCount = entries.Count(e => !e.IsDirectory && Within(e.Path, folder)), Disposition = InstallationLayoutDisposition.Unchanged });
            // Category folders left with nothing in them once the moves run; they can be deleted after organizing.
            foreach (string c in cls.Containers.Where(c => !mapped.ContainsKey(c) || !result.Moves.Any(m => m.Container && Paths.Equals(m.To, GroupPath(mapped[c])))).OrderBy(c => c, Paths))
            {
                string now = sim.Current[c];
                if (cls.Packages.Any(p => Within(sim.Current[p], now)) || entries.Any(e => !e.IsDirectory && cls.Owner.TryGetValue(e.Path, out var o) && o.Length == 0 && Within(e.Path, c))) continue;
                result.Nodes.Add(new LayoutTreeNode { Kind = LayoutNodeKind.Empty, OriginalPath = c, FinalPath = now, Folder = Leaf(now),
                    Group = Parent(now).Length > "plugins".Length ? Parent(now).Substring("plugins/".Length) : "", Notes = new List<string> { "EmptyAfterMove" }, Disposition = InstallationLayoutDisposition.Unchanged });
            }
            return result;
        }

        /// <summary>A Thunderstore dependency string ("Team-Name-1.2.3") names this package by its manifest name or its folder name.</summary>
        private static bool DependsOn(string dependency, string manifestName, string folder)
        {
            var parts = (dependency ?? "").Split('-');
            if (parts.Length >= 3 && Paths.Equals(parts[parts.Length - 2], manifestName)) return true;
            if (parts.Length >= 2 && Paths.Equals(parts[0] + "-" + parts[1], folder)) return true;
            return false;
        }
    }
}
