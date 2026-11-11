using System.Text.Json;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

// Portable description is data, never a destination or a command. It is outside the payload.
internal sealed class PackImportEnvironment
{
    internal const string FileName = "MCEEnvironment.json";
    public int SchemaVersion { get; set; }
    public int EnvironmentVersion { get; set; }
    public int MinimumWorkerProtocol { get; set; }
    public List<PackFile> Files { get; set; } = new();
    public ShopLedger Ledger { get; set; } = null!;

    internal static PackImportEnvironment Read(string path)
    {
        var data = MCE.UpdateWorker.Files.Read<PackImportEnvironment>(path);
        if (data.SchemaVersion != 2 || data.EnvironmentVersion != 2 || data.MinimumWorkerProtocol != 2 || data.Files == null || data.Ledger == null)
            throw new IOException("独立环境描述必须明确为 schema2/environment2/protocol2，并附组件记录（可明确为空）。");
        return data;
    }
    internal void Validate(string content)
    {
        var actual = PackStorage.Inventory(content);
        var declared = new Dictionary<string, string>(MCE.UpdateWorker.Files.Compare);
        foreach (var file in Files)
            if (file == null || !PackEnvironmentPolicy.TryNormalize(file.Path, out string path) || !PackStorage.ManagedPath(path, 2) || !declared.TryAdd(path, file.Hash))
                throw new IOException("独立环境清单路径无效或重复。");
        if (actual.Count != declared.Count || actual.Any(f => !declared.TryGetValue(f.Path, out var hash) || !FilesCompare(hash, f.Hash)))
            throw new IOException("独立环境描述与最终 payload 不一致，不能借用本机 core 或宿主补齐。");
        // Validate the same evidence before committing; PackRevisionState.Write validates it again after payload commit.
        if (Ledger.SchemaVersion != 1 || Ledger.Revision < 0 || Ledger.Packages == null) throw new IOException("组件记录无效。");
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in Ledger.Packages)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.Key) || !keys.Add(package.Key) || package.Files == null || package.Dependencies == null || package.PluginGuids == null) throw new IOException("组件记录无效。");
            foreach (var file in package.Files)
                if (!PackEnvironmentPolicy.TryNormalize(file.Path, out var path) || !owned.Add(path) || !declared.TryGetValue(path, out var hash) || !FilesCompare(hash, file.Hash)) throw new IOException("组件归属文件不在目标内容中：" + file.Path);
        }
    }
    static bool FilesCompare(string a, string b) => StringComparer.OrdinalIgnoreCase.Equals(a, b);
}
