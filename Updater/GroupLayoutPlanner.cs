using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>2.3.0 folder organization: collects what is under plugins/ and turns <see cref="GroupFolderPlanner"/>'s
/// result into a <see cref="LayoutPlan"/>. Moves are whole-folder renames in order (category folders first, nested
/// packages before the packages they leave, the manager last); a later move may run on a path an earlier one produced.</summary>
internal static class GroupLayoutPlanner
{
    internal static LayoutPlan Build(string root, LayoutQueueRequest request, ShopLedger ledger, Action<string>? progress = null)
    {
        void Step(string message) { WorkerLog.Info(message); progress?.Invoke(message); }
        Step("整理预览：开始读取模组目录");
        string plugins = Path.Combine(root, "plugins");
        var input = new FolderLayoutInput { BepInExAbsolutePath = root, UngroupedFolderName = request.UngroupedFolder.Length > 0 ? request.UngroupedFolder : "Ungrouped" };
        foreach (var group in request.Groups) input.Groups.Add(new FolderGroup { Id = group.Id, Name = group.Name, ParentId = group.ParentId ?? "" });
        foreach (var row in request.Rows) input.Rows.Add(new FolderRow { Guids = row.Guids.ToList(), GroupId = row.GroupId, PackageRoot = Relative(root, row.PackageRoot), Version = row.Version, Loaded = row.Loaded });
        if (Directory.Exists(plugins)) Collect(root, plugins, input.Entries);
        Step("整理预览：已读取 " + input.Entries.Count(e => e.IsDirectory) + " 个目录，正在计算目标位置");
        var result = GroupFolderPlanner.Plan(input);

        var plan = new LayoutPlan();
        plan.Details.AddRange(result.Details);
        plan.Tree.AddRange(result.Nodes.Select(n => { n.Source = SourceKind(Find(ledger, n.OriginalPath)); return n; }));
        foreach (var move in result.Moves) plan.Moves.Add(new MovePair { From = move.From, To = move.To });
        foreach (var change in result.Packages)
        {
            var owned = Find(ledger, change.OriginalPath);
            var files = new List<PackFile>();
            foreach (string inside in change.Files)
            {
                files.Add(new PackFile { Path = change.FinalPath + "/" + inside, Hash = "" });
                plan.OldPaths.Add(new PackFile { Path = change.OriginalPath + "/" + inside, Hash = "" });
                plan.NewPaths.Add(files[files.Count - 1]);
            }
            string groupFolder = GroupFolderPlanner.Parent(change.FinalPath);
            plan.Journal.Units.Add(new LayoutUnitChange
            {
                Key = owned?.Key ?? "", OldRoot = change.OriginalPath, NewRoot = change.FinalPath, GroupId = change.GroupId,
                GroupFolder = groupFolder.Length > "plugins".Length ? groupFolder.Substring("plugins/".Length) : "", OuterFolder = GroupFolderPlanner.Leaf(change.FinalPath), Files = files
            });
        }
        if (plan.Moves.Count == 0) plan.Details.Add("所有模组目录已在目标位置。");
        Step("整理预览：" + plan.Moves.Count + " 处移动");
        return plan;
    }

    static string Relative(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/'); }
        catch (Exception) { return ""; }
    }

    /// <summary>Every folder and file under plugins/ with the facts the planner needs: manifests, plugin assemblies
    /// (BepInPlugin ids read with Cecil), links. MMHOOK and MCEPack_* are skipped whole.</summary>
    static void Collect(string root, string plugins, List<FolderEntry> entries)
    {
        var pending = new Stack<string>(); pending.Push(plugins);
        while (pending.TryPop(out var dir))
        {
            string[] children, files;
            try { children = Directory.GetDirectories(dir); files = Directory.GetFiles(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string child in children)
            {
                string rel = Slash(Path.GetRelativePath(root, child));
                if (GroupFolderPlanner.NeverTouch(rel) || Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal)) continue;
                var entry = new FolderEntry { Path = rel, IsDirectory = true, HasLink = IsLink(child) };
                // A mod MCE disabled keeps its manifest as manifest.json.old (ModToggleService); it is still a package.
                string manifest = Path.Combine(child, "manifest.json");
                if (!File.Exists(manifest) && File.Exists(manifest + ".old")) manifest += ".old";
                if (File.Exists(manifest)) ReadManifest(manifest, entry);
                entries.Add(entry);
                if (!entry.HasLink) pending.Push(child);
            }
            foreach (string file in files)
            {
                string rel = Slash(Path.GetRelativePath(root, file));
                if (GroupFolderPlanner.NeverTouch(rel)) continue;
                var entry = new FolderEntry { Path = rel, HasLink = IsLink(file), IsAssembly = PluginFileNames.IsAssembly(file) };
                if (entry.IsAssembly) entry.PluginGuids = InstallMetadata.Read(root, file).Select(p => p.Guid).ToList();
                entries.Add(entry);
            }
        }
    }

    static void ReadManifest(string path, FolderEntry entry)
    {
        entry.HasManifest = true;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var rootElement = document.RootElement;
            if (rootElement.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String) entry.ManifestName = name.GetString() ?? "";
            if (rootElement.TryGetProperty("version_number", out var version) && version.ValueKind == System.Text.Json.JsonValueKind.String) entry.ManifestVersion = version.GetString() ?? "";
            if (rootElement.TryGetProperty("dependencies", out var deps) && deps.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var dep in deps.EnumerateArray()) if (dep.ValueKind == System.Text.Json.JsonValueKind.String) entry.Dependencies.Add(dep.GetString() ?? "");
        }
        catch (Exception) { }
    }

    static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    static ShopPackage? Find(ShopLedger ledger, string relativeRoot) => ledger.Packages.FirstOrDefault(p => Files.Compare.Equals(p.ClaimRoot, relativeRoot));

    static LayoutSourceKind SourceKind(ShopPackage? owned)
        => owned == null || owned.External ? LayoutSourceKind.Unknown
            : owned.Key.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ? LayoutSourceKind.Local
            : LayoutSourceKind.Store;

    static string Slash(string path) => path.Replace('\\', '/');
}
