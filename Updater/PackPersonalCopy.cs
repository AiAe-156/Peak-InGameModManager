using ModConfigEnhance.Updating;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCE.UpdateWorker;

internal static class PackPersonalCopy
{
    internal static PackRecord Save(PackStorage storage, PackRequest request)
    {
        var index = storage.Read();
        var original = index.Packs.SingleOrDefault(p => p.Id == request.PackId) ?? throw new IOException("所选整合包已不存在，请先存入再编辑副本。");
        original = original.UnsavedEnvironment ?? original.PendingUpdate ?? original;
        storage.Validate(original);
        string drafts = Path.GetFullPath(request.Source);
        if (!Files.Within(Path.Combine(storage.Home, ".editor-drafts"), drafts)) throw new IOException("编辑草稿路径越界。");
        Files.RejectLinks(storage.Home, drafts);
        var changes = Files.Inventory(drafts);
        if (changes.Count == 0) throw new IOException("没有修改需要保存。");
        string sourceContent = storage.Content(original);
        var documents = changes.Keys.Where(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && original.Files.Any(f => Files.Compare.Equals(f.Path, p.Replace('\\', '/'))))
            .ToDictionary(p => p.Replace('\\', '/'), p => {
                string path = Files.Child(drafts, p);
                if (new FileInfo(path).Length > PackEditPolicy.TextLimit) throw new IOException("文本修改超过 2 MB。");
                return File.ReadAllText(path);
            }, Files.Compare);
        // Validate every draft before creating the new pack directory or copying its content.
        foreach (var item in changes)
        {
            string rel = item.Key.Replace('\\', '/'), source = Files.Child(drafts, item.Key);
            bool existing = original.Files.Any(f => Files.Compare.Equals(f.Path, rel));
            if (PackEditPolicy.IsAsset(rel))
            {
                if (!documents.Any(d => PackEditPolicy.AssetForDocument(rel, d.Key, d.Value))) throw new IOException("素材必须由同目录文档引用并位于 media 或 fonts 目录：" + rel);
                if (new FileInfo(source).Length > PackEditPolicy.AssetLimit) throw new IOException("素材超过 1 GB：" + rel);
            }
            else
            {
                if (!existing || !PackEditPolicy.CanEdit(rel)) throw new IOException("不允许修改包结构文件或新增非素材文件：" + rel);
                if (new FileInfo(source).Length > PackEditPolicy.TextLimit) throw new IOException("文本修改超过 2 MB。");
                if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) JsonNode.Parse(File.ReadAllText(source));
            }
        }
        var authors = original.Files.Where(f => Path.GetFileName(f.Path).Equals("release.json", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path.Replace('\\', '/')[..f.Path.Replace('\\', '/').LastIndexOf('/')])
            .Where(p => ManagerIdentity.IsPackFolder(Path.GetFileName(p))
                || File.Exists(Files.Child(sourceContent, p + "/profiles.json")) || File.Exists(Files.Child(sourceContent, p + "/signature.json")))
            .Distinct(Files.Compare).ToList();
        if (original.AuthorRoot.Length > 0 && !authors.Contains(original.AuthorRoot, Files.Compare)) authors.Add(original.AuthorRoot);
        foreach (string author in authors) PackSignature.Verify(Files.Child(sourceContent, author));
        var copy = JsonSerializer.Deserialize<PackRecord>(JsonSerializer.Serialize(original, Files.Json), Files.Json)!;
        copy.Id = Guid.NewGuid().ToString("N"); copy.PublicationId = ""; copy.PendingUpdate = null; copy.UnsavedEnvironment = null; copy.Report = null;
        copy.Content = "revisions/" + Guid.NewGuid().ToString("N"); copy.ModifiedUtc = DateTime.UtcNow.ToString("O");
        copy.Name = string.IsNullOrWhiteSpace(request.SnapshotName) ? original.Name + "（个人副本）" : request.SnapshotName.Trim();
        string content = storage.Content(copy);
        PackStorage.CopyTree(sourceContent, content);
        foreach (var item in changes)
        {
            string target = Files.Child(content, item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + ".edit-" + Guid.NewGuid().ToString("N");
            Files.CopyAtomic(Files.Child(drafts, item.Key), temporary); FrameworkCompat.MoveOverwrite(temporary, target);
        }
        foreach (string relative in authors)
        {
            string author = Files.Child(content, relative);
            if (changes.Keys.Any(p => p.Replace('\\', '/').StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase)))
            {
                string signature = Path.Combine(author, "signature.json"), release = Path.Combine(author, "release.json");
                if (File.Exists(signature)) File.Delete(signature); // personal revision only
                if (File.Exists(release))
                {
                    var data = JsonNode.Parse(File.ReadAllText(release))!.AsObject();
                    data["signed"] = false; data["packId"] = copy.Id; data["name"] = copy.Name;
                    File.WriteAllText(release, data.ToJsonString());
                }
            }
            PackSignature.Verify(author);
        }
        CopyWorkspace(storage, original, copy);
        copy.Files = PackStorage.Inventory(content);
        if (PackEnvironmentPolicy.IsComplete(original))
        {
            copy.RevisionStateHash = "";
            var ledger = new PackRevisionState(storage).Read(original)!; // original revision, NOT mutable workspace
            PackCompleteEnvironment.ReconcileEditedLedger(ledger, copy);
            new PackRevisionState(storage).Write(copy, ledger);
            Files.Save(PackCompleteEnvironment.LedgerPath(storage, copy.Id), ledger);
        }
        storage.Validate(copy);
        index.Packs.Add(copy); storage.Save(index); return copy;
    }

    static void CopyWorkspace(PackStorage storage, PackRecord original, PackRecord copy)
    {
        string source = storage.Workspace(original.Id), target = storage.Workspace(copy.Id);
        var files = Files.Inventory(source);
        foreach (var file in files.Keys)
        {
            string rel = file.Replace('\\', '/');
            // Only durable user data; pending apply/switch commands and runtime caches are not inherited.
            if (!(rel.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("language/", StringComparison.OrdinalIgnoreCase)
                || rel.Equals("runtime/applied-profile.json", StringComparison.OrdinalIgnoreCase) || !PackEnvironmentPolicy.IsComplete(original) && rel.Equals("shop-installed.json",StringComparison.OrdinalIgnoreCase))) continue;
            if (Path.GetFileName(rel).StartsWith("pending-", StringComparison.OrdinalIgnoreCase)) continue;
            string input = Files.Child(source, file), output = Files.Child(target, file);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (!rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { Files.CopyAtomic(input, output); continue; }
            var data = JsonNode.Parse(File.ReadAllText(input));
            void Rewrite(JsonNode node)
            {
                if (node is JsonObject obj)
                    foreach (var pair in obj.ToArray())
                    {
                        if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                        {
                            if (text == original.Id) obj[pair.Key] = copy.Id;
                            else if (Path.IsPathRooted(text) && Files.Within(storage.DirectoryFor(original.Id), text))
                                obj[pair.Key] = Files.Child(storage.DirectoryFor(copy.Id), Path.GetRelativePath(storage.DirectoryFor(original.Id), text));
                        }
                        else if (pair.Value != null) Rewrite(pair.Value);
                    }
                else if (node is JsonArray array) foreach (var value in array) if (value != null) Rewrite(value);
            }
            if (data != null) Rewrite(data);
            File.WriteAllText(output, data?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");
        }
    }
}
