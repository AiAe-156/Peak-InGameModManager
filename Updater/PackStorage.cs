using System.Security.Cryptography;
using System.Text;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed class PackStorage
{
    internal readonly string Root, Home, IndexPath, TaskPath;
    internal PackStorage(string root)
    {
        Root = root; Home = Path.Combine(root, "MCE", "packs");
        IndexPath = Path.Combine(Home, "index.json"); TaskPath = Path.Combine(root, "MCE", "pack-task.json");
        Files.RejectLinks(Directory.GetParent(root)!.FullName, Home);
    }
    // Structure/protocol only: execution must explicitly Validate its target/plan.
    // Inactive payload damage must not prevent journal recovery or index inspection.
    internal PackIndex Read()
    {
        if (!File.Exists(IndexPath)) return new();
        var index = Files.Read<PackIndex>(IndexPath);
        ValidateIndex(index);
        return index;
    }
    internal void Save(PackIndex index)
    {
        if (index.Packs == null || index.Packs.Any(p => p == null)) throw new IOException("整合包列表无效。");
        // Promote only when an explicitly complete record is written; never upgrade legacy implicitly.
        if (Records(index).Any(p => p.EnvironmentVersion == PackEnvironmentPolicy.CompleteVersion))
        { index.SchemaVersion = 2; index.MinimumWorkerProtocol = 2; }
        // Read a fresh disk snapshot, never trust mutable caller references or a cached hash.
        // The old index is structure-only so unrelated damaged payloads cannot block recovery.
        var previous = File.Exists(IndexPath) ? Read() : null;
        if (previous?.SchemaVersion == 2) { index.SchemaVersion = 2; index.MinimumWorkerProtocol = 2; }
        ValidateIndex(index);
        var unchanged = previous == null ? new HashSet<string>(StringComparer.Ordinal)
            : Records(previous).Where(PackEnvironmentPolicy.IsComplete).Select(RecordSignature).ToHashSet(StringComparer.Ordinal);
        foreach (var pack in Records(index).Where(PackEnvironmentPolicy.IsComplete))
            if (!unchanged.Contains(RecordSignature(pack))) Validate(pack);
        // Nothing is committed (including lastvalid) until all new/changed records pass.
        if (previous != null)
        { Files.Save(IndexPath + ".lastvalid", previous); index.Revision = Math.Max(index.Revision, previous.Revision); }
        index.Revision++; Files.Save(IndexPath, index);
    }
    // Deliberately excludes child references and index bookkeeping, but includes the entire
    // record value. In particular Files must be compared even when RevisionStateHash matches.
    private static string RecordSignature(PackRecord pack) => System.Text.Json.JsonSerializer.Serialize(new
    {
        pack.Id, pack.Content, pack.EnvironmentVersion, pack.MinimumWorkerProtocol, pack.RevisionStateHash,
        pack.Files, pack.Problems, pack.Requirements, pack.CoreRequirements, pack.PublicationId,
        pack.Name, pack.Version, pack.AuthorRoot, pack.ModifiedUtc, pack.Language, pack.Report
    });
    private static IEnumerable<PackRecord> Records(PackIndex index)
    {
        foreach (var pack in index.Packs)
        {
            yield return pack;
            if (pack.PendingUpdate != null) yield return pack.PendingUpdate;
            if (pack.UnsavedEnvironment != null) yield return pack.UnsavedEnvironment;
        }
    }
    private void ValidateIndex(PackIndex index)
    {
        if (index.SchemaVersion is not (1 or 2) || index.MinimumWorkerProtocol < 1 || index.MinimumWorkerProtocol > 2
            || index.SchemaVersion == 2 && index.MinimumWorkerProtocol != 2
            || index.Packs == null || index.ActiveFiles == null || index.HostRoots == null || index.HostPlugins == null
            || index.Packs.Any(p => p == null || !Id(p.Id)) || index.Packs.GroupBy(p => p.Id, Files.Compare).Any(g => g.Count() > 1)
            || index.ActivePackId == null || index.ActivePackId.Length > 0 && index.Packs.All(p => p.Id != index.ActivePackId))
            throw new IOException("整合包索引损坏或助手协议不兼容，已保留原文件。");
        foreach (var parent in index.Packs)
            foreach (var child in new[] { parent.PendingUpdate, parent.UnsavedEnvironment })
                if (child != null && (child.Id != parent.Id || child.PendingUpdate != null || child.UnsavedEnvironment != null))
                    throw new IOException("内容版本身份无效。");
        foreach (var pack in Records(index))
        {
            ValidateRecord(pack);
            if (pack.Content.Length > 0 || PackEnvironmentPolicy.IsComplete(pack)) Content(pack);
            if (PackEnvironmentPolicy.IsComplete(pack))
            {
                if (index.SchemaVersion != 2 || index.MinimumWorkerProtocol != 2) throw new IOException("完整环境需要 schema2 / protocol2。");
            }
        }
    }
    internal static void ValidateRecord(PackRecord pack)
    {
        if (pack == null || !Id(pack.Id) || pack.Files == null || pack.Problems == null || pack.Requirements == null || pack.CoreRequirements == null || pack.Content == null
            || pack.RevisionStateHash == null
            || pack.EnvironmentVersion is not (1 or 2) || pack.MinimumWorkerProtocol < 1 || pack.MinimumWorkerProtocol > 2
            || pack.EnvironmentVersion == 2 && !PackEnvironmentPolicy.IsComplete(pack)
            || pack.EnvironmentVersion == 1 && !string.IsNullOrEmpty(pack.RevisionStateHash))
            throw new IOException("整合包记录不完整或环境协议不兼容。");
        ValidateManifest(pack);
    }
    private static HashSet<string> ValidateManifest(PackRecord pack)
    {
        var seen = new HashSet<string>(Files.Compare);
        foreach (var file in pack.Files)
            if (file == null || !ManagedPath(file.Path, pack) || !PackEnvironmentPolicy.TryNormalize(file.Path, out var normalized)
                || !seen.Add(normalized) || file.Hash == null || file.Hash.Length != 64 || !file.Hash.All(Uri.IsHexDigit))
                throw new IOException("整合包文件清单无效：" + file?.Path);
        return seen;
    }
    internal string DirectoryFor(string id) => Id(id) ? Files.Child(Home, id) : throw new IOException("整合包编号无效。");
    internal string Content(PackRecord pack)
    {
        string[] parts = pack.Content.Replace('\\', '/').Split('/');
        if (parts.Length != 2 || parts[0] != "revisions" || !Id(parts[1])) throw new IOException("整合包内容版本路径无效。");
        return Files.Child(DirectoryFor(pack.Id), pack.Content);
    }
    internal string Workspace(string id) => Path.Combine(DirectoryFor(id), "workspace");
    internal static bool Id(string id) => id != null && id.Length == 32 && id.All(Uri.IsHexDigit);
    internal static string StableId(string publicationId)
    { using var sha = SHA256.Create(); return FrameworkCompat.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(publicationId.Trim().ToLowerInvariant())))[..32].ToLowerInvariant(); }
    internal static bool GlobalConfig(string relative) => PackEnvironmentPolicy.LegacyGlobalConfig(relative);
    // Existing callers remain legacy: this overload must never grant core access.
    internal static bool ManagedPath(string path) => PackEnvironmentPolicy.ManagedPath(path, 1);
    internal static bool ManagedPath(string path, int environmentVersion) => PackEnvironmentPolicy.ManagedPath(path, environmentVersion);
    internal static bool ManagedPath(string path, PackRecord pack) => PackEnvironmentPolicy.ManagedPath(path, pack);
    internal static void CopyTree(string source, string destination)
    {
        var inventory = Files.Inventory(source);
        if (inventory.Count > 50000 || inventory.Keys.Sum(p => new FileInfo(Files.Child(source, p)).Length) > 8L * 1024 * 1024 * 1024) throw new IOException("目录文件数或大小超过整合包导入限额。");
        foreach (var item in inventory)
        { string target = Files.Child(destination, item.Key); Files.RejectLinks(destination, target); Directory.CreateDirectory(Path.GetDirectoryName(target)!); Files.CopyAtomic(Files.Child(source, item.Key), target); }
    }
    internal static List<PackFile> Inventory(string content) => Files.Inventory(content).Select(p => new PackFile { Path = p.Key.Replace('\\', '/'), Hash = p.Value }).ToList();
    internal void Validate(PackRecord pack)
    {
        ValidateRecord(pack);
        // State.Read already validates the complete payload before checking the envelope.
        if (PackEnvironmentPolicy.IsComplete(pack)) new PackRevisionState(this).Read(pack);
        else ValidatePayload(pack);
    }
    internal void ValidatePayload(PackRecord pack)
    {
        ValidateRecord(pack);
        if (pack.Problems.Count > 0) throw new IOException(string.Join("\n", pack.Problems));
        string content = Content(pack);
        Files.RejectLinks(Home, content);
        if (!Directory.Exists(content)) throw new IOException("库存内容目录缺失。");
        var seen = ValidateManifest(pack);
        // Inventory hashes each file once and rejects links; reuse those hashes for both
        // exact-set and manifest verification instead of hashing the whole tree twice.
        var actual = Files.Inventory(content).ToDictionary(p => p.Key.Replace('\\', '/'), p => p.Value, Files.Compare);
        if (!actual.Keys.ToHashSet(Files.Compare).SetEquals(seen)) throw new IOException("库存包含未登记文件或文件缺失，请重新导入。");
        foreach (var file in pack.Files)
        {
            PackEnvironmentPolicy.TryNormalize(file.Path, out var normalized);
            if (!string.Equals(actual[normalized], file.Hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("库存文件已改变或缺失：" + file.Path);
        }
    }
}
