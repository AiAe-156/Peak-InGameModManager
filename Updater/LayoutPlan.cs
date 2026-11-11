using System.Text.Json;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Preview result of a folder-organization request: directory renames plus the ledger changes they imply.</summary>
internal sealed class LayoutPlan
{
    public List<MovePair> Moves { get; set; } = new();
    public LayoutJournal Journal { get; set; } = new();
    public List<string> Details { get; set; } = new();
    public List<string> Blockers { get; set; } = new();
    public List<PackFile> OldPaths { get; set; } = new();
    public List<PackFile> NewPaths { get; set; } = new();
    /// <summary>Structured folder result for the confirmation dialog. The plugin words and draws it.</summary>
    public List<LayoutTreeNode> Tree { get; set; } = new();
    /// <summary>2.3.1: empty folders (relative to the root) the user ticked; recycled after the moves succeed.</summary>
    public List<string> RecycleAfter { get; set; } = new();
}

/// <summary>Builds the layout plan from what is actually on disk. Unregistered folders organize too; only an
/// existing ledger entry is updated, so a manually installed package is never silently claimed.</summary>
internal static class LayoutPlanner
{
    internal const string FileName = "layout-plan.json";
    internal static string PathFor(string workDirectory) => Path.Combine(workDirectory, FileName);
    internal static void Save(string workDirectory, LayoutPlan plan) => Files.Save(PathFor(workDirectory), plan);
    internal static LayoutPlan? Read(string workDirectory)
    {
        string path = PathFor(workDirectory);
        return File.Exists(path) ? Files.Read<LayoutPlan>(path) : null;
    }

    internal static LayoutPlan Build(string root, LayoutQueueRequest request, ShopLedger ledger, Action<string>? progress = null)
    {
        var plan = new LayoutPlan();
        // Progress goes to the worker log and, when the caller supplies a channel, into the queued task as well so
        // the game can show what the preview is doing instead of an unchanging "preparing" line.
        void Step(string message) { WorkerLog.Info(message); progress?.Invoke(message); }
        Step("整理预览：开始读取模组目录");
        var input = new InstallationLayoutInput { Mode = request.Mode, BepInExAbsolutePath = root, InventoryComplete = true };
        foreach (var group in request.Groups)
            input.Groups.Add(new InstallationLayoutGroup { Id = group.Id, Name = group.Name, StableFolderName = StableFolder(ledger, group.Id) });
        if (!input.Groups.Any(g => g.Id == "ungrouped")) input.Groups.Add(new InstallationLayoutGroup { Id = "ungrouped", Name = "Ungrouped", StableFolderName = StableFolder(ledger, "ungrouped") });

        string pluginsRoot = Path.Combine(root, "plugins");
        var roots = new List<string>();
        var containerChildren = new List<string>();
        // One walk per package root, reused for the occupancy facts below instead of enumerating the tree twice.
        var walkedFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        void AddUnit(string folder, string relative)
        {
            string name = Path.GetFileName(folder);
            var discovered = InstallMetadata.ReadTree(root, folder, plan.Details);
            if (discovered.Count == 0) { plan.Details.Add("跳过未识别插件目录：" + relative); return; }
            var owned = FindLedgerEntry(ledger, relative, discovered);
            string translated = discovered.Select(p => request.Mods.FirstOrDefault(m => Files.Compare.Equals(m.Guid, p.Guid))?.Name ?? "").FirstOrDefault(n => n.Length > 0) ?? "";
            var unit = new InstallationLayoutUnit
            {
                Id = relative, OriginalPath = relative, OriginalName = name,
                TranslatedName = translated,
                // A folder people can read. The planner's hashed form stays as the fallback for a name that cannot be a folder.
                StableOuterFolderName = owned?.LayoutOuterFolder ?? PreferredOuter(request.Mode, translated, name),
                OwnershipComplete = true, FileInventoryComplete = true, IsCoreOrPatcher = false,
                IsProtectedManager = discovered.Any(p => ManagerIdentity.IsManagerGuid(p.Guid)),
                HasLinks = HasLinks(folder) ? LayoutFact.Yes : LayoutFact.No, IsLocked = LayoutFact.No, IsShared = LayoutFact.No
            };
            foreach (var plugin in discovered)
                unit.Plugins.Add(new InstallationLayoutPlugin { Id = plugin.Guid, GroupId = request.Mods.FirstOrDefault(m => Files.Compare.Equals(m.Guid, plugin.Guid))?.GroupId is { Length: > 0 } g ? g : "ungrouped", IsLoaded = LayoutFact.No,
                    Depth = Slash(Path.GetRelativePath(folder, plugin.File)).Count(c => c == '/') });
            var walked = InstallArchive.Walk(folder).ToArray();
            walkedFiles[folder] = walked;
            foreach (string file in walked)
            {
                var item = new InstallationLayoutFile { RelativePath = Slash(Path.GetRelativePath(folder, file)), HasLinks = HasLinks(file) ? LayoutFact.Yes : LayoutFact.No };
                item.OwnerUnitIds.Add(relative);
                unit.Files.Add(item);
                // No hash here. Hashing the whole tree for a preview ran over every package, including the ones
                // that end up already in place; the hashes are collected when the plan is confirmed instead.
                plan.OldPaths.Add(new PackFile { Path = relative + "/" + item.RelativePath, Hash = "" });
            }
            if (unit.Files.Count == 0) { plan.Details.Add("跳过空目录：" + relative); return; }
            input.Units.Add(unit);
            roots.Add(folder);
        }
        foreach (string folder in Directory.Exists(pluginsRoot) ? Directory.GetDirectories(pluginsRoot).OrderBy(n => n, StringComparer.OrdinalIgnoreCase) : Enumerable.Empty<string>())
        {
            string name = Path.GetFileName(folder);
            if (name.StartsWith(".", StringComparison.Ordinal)) continue;
            string relative = "plugins/" + name;
            var group = ContainerGroup(folder, name, input.Groups) ?? InferredContainer(root, folder, request, input.Groups);
            if (group != null)
            {
                // A folder named after a group that holds packages instead of being one is that group's folder already:
                // its children are the units, so they stay put and strays are moved in beside them instead of into a new hashed folder.
                if (group.StableFolderName.Length == 0) group.StableFolderName = name;
                plan.Details.Add("分组目录：" + relative + "（" + group.Name + "）");
                foreach (string child in Directory.GetDirectories(folder).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    if (Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal)) continue;
                    containerChildren.Add(child);
                    AddUnit(child, relative + "/" + Path.GetFileName(child));
                }
                continue;
            }
            AddUnit(folder, relative);
        }

        // Occupancy facts: the candidate roots, every direct child of plugins/ and every child of a group folder (folders move as whole units).
        Step("整理预览：已读取 " + input.Units.Count + " 个模组目录，正在收集占位记录");
        foreach (string folder in roots)
        {
            input.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = Slash(Path.GetRelativePath(root, folder)), IsDirectory = true, HasLinks = HasLinks(folder) ? LayoutFact.Yes : LayoutFact.No });
            foreach (string file in walkedFiles.TryGetValue(folder, out var cached) ? cached : InstallArchive.Walk(folder))
                input.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = Slash(Path.GetRelativePath(root, file)), IsDirectory = false, HasLinks = LayoutFact.No });
        }
        foreach (string folder in Directory.Exists(pluginsRoot) ? Directory.GetDirectories(pluginsRoot) : Enumerable.Empty<string>())
            input.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = "plugins/" + Path.GetFileName(folder), IsDirectory = true, HasLinks = LayoutFact.No });
        foreach (string folder in containerChildren.Where(c => !roots.Any(r => Files.Compare.Equals(r, c))))
            input.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = Slash(Path.GetRelativePath(root, folder)), IsDirectory = true, HasLinks = LayoutFact.No });

        // A group that has no folder yet gets its plain name when that is a valid, unclaimed folder, matching folders people
        // name by hand; the planner's hashed form stays as the fallback for names that are invalid or already taken.
        foreach (var group in input.Groups.Where(g => g.StableFolderName.Length == 0))
        {
            string candidate = group.Name.Trim();
            bool valid = candidate.Length > 0 && candidate.Length <= 255 && candidate.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && !candidate.EndsWith(".", StringComparison.Ordinal) && candidate != "." && candidate != "..";
            if (!valid || Directory.Exists(Path.Combine(pluginsRoot, candidate))) continue;
            if (input.Groups.Any(g => !ReferenceEquals(g, group) && Files.Compare.Equals(g.StableFolderName, candidate))) continue;
            group.StableFolderName = candidate;
        }

        Step("整理预览：开始计算目标位置（" + input.Units.Count + " 个单元 / " + input.ExistingPaths.Count + " 条占位记录）");
        var result = InstallationLayoutPlanner.Plan(input);
        plan.Blockers.AddRange(result.Errors);
        foreach (var unit in result.Units)
        {
            if (unit.Disposition == InstallationLayoutDisposition.Move && Ungrouped(input, unit.UnitId))
            {
                // Organizing is by group; a package nobody has grouped has no place to go and stays where it is.
                plan.Details.Add(unit.OriginalPath + "：未分组，保持原位");
                continue;
            }
            if (unit.Disposition == InstallationLayoutDisposition.Move)
            {
                plan.Moves.Add(new MovePair { From = unit.OriginalPath, To = unit.DestinationPath });
                if (unit.Notes.Contains("ManagerMovedLast")) plan.Details.Add(unit.OriginalPath + "：管理器自身目录，最后移动");
                plan.Details.Add(unit.OriginalPath + " → " + unit.DestinationPath);
                var owned = FindLedgerEntry(ledger, unit.OriginalPath, null);
                var files = new List<PackFile>();
                foreach (var file in unit.Files)
                {
                    files.Add(new PackFile { Path = file.DestinationPath, Hash = plan.OldPaths.FirstOrDefault(p => Files.Compare.Equals(p.Path, file.OriginalPath))?.Hash ?? "" });
                    plan.NewPaths.Add(files[files.Count - 1]);
                }
                plan.Journal.Units.Add(new LayoutUnitChange
                {
                    Key = owned?.Key ?? "", OldRoot = unit.OriginalPath, NewRoot = unit.DestinationPath,
                    GroupId = owned == null ? "" : request.Mods.FirstOrDefault(m => owned.PluginGuids.Contains(m.Guid, Files.Compare))?.GroupId ?? "ungrouped",
                    GroupFolder = unit.GroupFolderName, OuterFolder = unit.OuterFolderName, Files = files
                });
                if (owned == null) plan.Details.Add(unit.OriginalPath + "：来源 " + SourceLabel(owned) + "（管理器未登记，仅移动目录）");
                else plan.Details.Add(unit.OriginalPath + "：来源 " + SourceLabel(owned));
            }
            else if (unit.Disposition == InstallationLayoutDisposition.Unchanged) plan.Details.Add(unit.OriginalPath + "：位置已符合，无需整理");
            else plan.Details.Add(unit.OriginalPath + "：跳过（" + string.Join(", ", unit.Reasons) + "）");
        }
        // The manager's own folder moves last: if anything before it fails, its running copy is still where the game loaded it.
        var managerMoves = plan.Moves.Where(m => result.Units.Any(u => u.Notes.Contains("ManagerMovedLast") && Files.Compare.Equals(u.OriginalPath, m.From))).ToList();
        foreach (var move in managerMoves) { plan.Moves.Remove(move); plan.Moves.Add(move); }
        foreach (var unit in result.Units)
        {
            // The worker reports the category and the folders; the plugin words and draws the tree.
            if (unit.GroupFolderName.Length == 0 && unit.OuterFolderName.Length == 0) continue;
            plan.Tree.Add(new LayoutTreeNode
            {
                Group = unit.GroupFolderName, Folder = unit.OuterFolderName, OriginalPath = unit.OriginalPath,
                Disposition = unit.Disposition, Source = SourceKind(FindLedgerEntry(ledger, unit.OriginalPath, null)),
                Reasons = string.Join(", ", unit.Reasons), Notes = unit.Notes.ToList(),
                Ungrouped = unit.Disposition == InstallationLayoutDisposition.Move && Ungrouped(input, unit.UnitId),
                Manager = unit.Notes.Contains("ManagerMovedLast")
            });
        }
        if (plan.Moves.Count == 0)
        {
            if (input.Units.Count == 0) plan.Blockers.Add("没有可整理的模组目录（未发现可识别的插件文件夹）。");
            else plan.Details.Add("所有可识别的模组目录已在目标位置。");
        }
        return plan;
    }

    static string StableFolder(ShopLedger ledger, string groupId)
        => ledger.Packages.FirstOrDefault(p => Files.Compare.Equals(p.LayoutGroupId, groupId) && p.LayoutGroupFolder.Length > 0)?.LayoutGroupFolder ?? "";

    /// <summary>The readable name a package folder should use. Only the translated-outside mode renames the package
    /// folder, and only a name that is not a usable folder segment falls back to the planner's hashed form.</summary>
    static string PreferredOuter(InstallationLayoutMode mode, string translated, string original)
        => mode != InstallationLayoutMode.GroupAndTranslatedOuterFolder ? ""
            : InstallationLayoutPlanner.Label(translated.Length > 0 ? translated : original);

    static LayoutSourceKind SourceKind(ShopPackage? owned)
        => owned == null || owned.External ? LayoutSourceKind.Unknown
            : owned.Key.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ? LayoutSourceKind.Local
            : LayoutSourceKind.Store;

    static string SourceLabel(ShopPackage? owned) => SourceKind(owned) switch
    {
        LayoutSourceKind.Local => "本地",
        LayoutSourceKind.Store => "商店已确认",
        _ => "待定"
    };

    static bool Ungrouped(InstallationLayoutInput input, string unitId)
    {
        var unit = input.Units.FirstOrDefault(u => Files.Compare.Equals(u.Id, unitId));
        return unit != null && unit.Plugins.Count > 0 && unit.Plugins.All(p => Files.Compare.Equals(p.GroupId, "ungrouped"));
    }

    /// <summary>A direct child of plugins/ named after a group (its recorded folder, else its name) that only holds package folders:
    /// no plugin assembly or manifest of its own. Such a folder is the group's folder, not a package.</summary>
    static InstallationLayoutGroup? ContainerGroup(string folder, string name, List<InstallationLayoutGroup> groups)
    {
        var group = groups.FirstOrDefault(g => Files.Compare.Equals(g.StableFolderName.Length > 0 ? g.StableFolderName : g.Name, name));
        if (group == null) return null;
        if (Directory.GetFiles(folder).Any(f => PluginFileNames.IsAssembly(f) || Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))) return null;
        return Directory.GetDirectories(folder).Length > 0 ? group : null;
    }

    /// <summary>A category folder named differently from its group ("0核心&基础模组" for group "0基础模组"): it holds no plugin
    /// of its own, only package folders, and most of those packages' own plugins belong to one group. Without this the whole
    /// category folder was taken for one package carrying plugins from several groups and skipped.</summary>
    static InstallationLayoutGroup? InferredContainer(string root, string folder, LayoutQueueRequest request, List<InstallationLayoutGroup> groups)
    {
        if (Directory.GetFiles(folder).Any(f => PluginFileNames.IsAssembly(f) || Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))) return null;
        var children = Directory.GetDirectories(folder).Where(c => !Path.GetFileName(c).StartsWith(".", StringComparison.Ordinal)).ToArray();
        if (children.Length < 2) return null;
        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); int packages = 0;
        foreach (string child in children)
        {
            var plugins = InstallMetadata.ReadTree(root, child);
            if (plugins.Count == 0) continue;
            packages++;
            int Depth(PluginInfo p) => Slash(Path.GetRelativePath(child, p.File)).Count(c => c == '/');
            int top = plugins.Min(Depth);
            foreach (string id in plugins.Where(p => Depth(p) == top)
                .Select(p => request.Mods.FirstOrDefault(m => Files.Compare.Equals(m.Guid, p.Guid))?.GroupId ?? "")
                .Where(g => g.Length > 0 && g != "ungrouped").Distinct(StringComparer.OrdinalIgnoreCase))
                votes[id] = votes.TryGetValue(id, out int n) ? n + 1 : 1;
        }
        if (packages < 2 || votes.Count == 0) return null;
        var best = votes.OrderByDescending(v => v.Value).First();
        if (best.Value * 2 <= packages) return null;
        var group = groups.FirstOrDefault(g => Files.Compare.Equals(g.Id, best.Key));
        // Another folder already stands for this group: two folders for one group would need merging, which is not this step.
        if (group == null || group.StableFolderName.Length > 0 && !Files.Compare.Equals(group.StableFolderName, Path.GetFileName(folder))) return null;
        return group;
    }

    static ShopPackage? FindLedgerEntry(ShopLedger ledger, string relativeRoot, IReadOnlyList<PluginInfo>? plugins)
    {
        var byRoot = ledger.Packages.FirstOrDefault(p => Files.Compare.Equals(p.ClaimRoot, relativeRoot));
        if (byRoot != null) return byRoot;
        if (plugins == null) return null;
        return ledger.Packages.FirstOrDefault(p => p.PluginGuids.Any(g => plugins.Any(x => Files.Compare.Equals(x.Guid, g))));
    }

    static bool HasLinks(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Directory.Exists(path) && HasLinkedChild(path); }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    static bool HasLinkedChild(string directory)
    {
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out var current))
            foreach (string child in Directory.GetDirectories(current))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) return true;
                pending.Push(child);
            }
        return false;
    }

    static string Slash(string path) => path.Replace('\\', '/');
}
