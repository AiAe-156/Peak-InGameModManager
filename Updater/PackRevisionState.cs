using System.Security.Cryptography;
using System.Text;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

// Immutable version-paired ownership state; workspace is never a fallback source.
internal sealed class PackRevisionState
{
    private readonly PackStorage store;
    internal PackRevisionState(PackStorage store) { this.store = store; }

    internal string PathFor(PackRecord pack)
    {
        store.Content(pack); // validates both pack identity and the revision path
        string revision = pack.Content.Replace('\\', '/').Split('/')[1];
        string path = Files.Child(store.DirectoryFor(pack.Id), "revision-state/" + revision + "/shop-installed.json");
        Files.RejectLinks(store.Home, path);
        return path;
    }

    internal static ShopLedger EmptyLedger() => new();

    // Returns null for legacy, even if a workspace ledger happens to exist.
    internal ShopLedger? Read(PackRecord pack)
    {
        PackStorage.ValidateRecord(pack);
        if (!PackEnvironmentPolicy.IsComplete(pack)) return null;
        store.ValidatePayload(pack);
        string path = PathFor(pack);
        if (string.IsNullOrEmpty(pack.RevisionStateHash) || !File.Exists(path)
            || !string.Equals(Files.Hash(path), pack.RevisionStateHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("完整环境的版本状态缺失或摘要不符。");
        var state = Files.Read<RevisionState>(path);
        if (state.SchemaVersion != 2 || state.PackId != pack.Id || state.Content != pack.Content.Replace('\\', '/')
            || state.PayloadHash != PayloadHash(pack)) throw new IOException("版本状态与整合包内容不匹配。");
        ValidateLedger(pack, state.Ledger);
        return state.Ledger;
    }

    // Call after payload + Files are final, before PackStorage.Save. Never rewrites an existing revision state.
    internal void Write(PackRecord pack, ShopLedger ledger)
    {
        PackStorage.ValidateRecord(pack);
        if (!PackEnvironmentPolicy.IsComplete(pack)) throw new IOException("旧环境不能推断或写入完整版本状态。");
        store.ValidatePayload(pack);
        ValidateLedger(pack, ledger);
        string path = PathFor(pack);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("版本状态已存在；请创建新 revision。");
        Files.Save(path, new RevisionState { SchemaVersion = 2, PackId = pack.Id, Content = pack.Content.Replace('\\', '/'), PayloadHash = PayloadHash(pack), Ledger = ledger });
        pack.RevisionStateHash = Files.Hash(path);
    }

    // Only copy exact ownership evidence; changed/removed owned files fail validation, not silently pruned.
    internal void Clone(PackRecord source, PackRecord destination)
    {
        var ledger = Read(source);
        if (ledger == null)
        {
            if (PackEnvironmentPolicy.IsComplete(destination)) throw new IOException("旧版本没有可验证状态；迁移须显式提供 ledger。");
            PackStorage.ValidateRecord(destination);
            return;
        }
        if (Files.Compare.Equals(PathFor(source), PathFor(destination))) throw new IOException("不能覆盖同一版本状态。");
        Write(destination, ledger);
    }

    internal void ValidateLedger(PackRecord pack, ShopLedger ledger)
    {
        if (ledger == null || ledger.SchemaVersion != 1 || ledger.Revision < 0 || ledger.Packages == null)
            throw new IOException("商店归属状态无效。");
        var payload = new Dictionary<string, string>(Files.Compare);
        foreach (var file in pack.Files)
        {
            if (file == null || !PackEnvironmentPolicy.TryNormalize(file.Path, out string path) || !payload.TryAdd(path, file.Hash))
                throw new IOException("Invalid payload manifest.");
        }
        var keys = new HashSet<string>(Files.Compare);
        var owned = new Dictionary<string, string>(Files.Compare);
        foreach (var package in ledger.Packages)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.Key) || !keys.Add(package.Key) || package.Files == null
                || package.Dependencies == null || package.PluginGuids == null) throw new IOException("Invalid package ownership.");
            var packagePaths = new HashSet<string>(Files.Compare);
            foreach (var file in package.Files)
            {
                if (file == null || !PackEnvironmentPolicy.TryNormalize(file.Path, out string path)
                    || !PackStorage.ManagedPath(path, pack) || !packagePaths.Add(path)
                    || (owned.TryGetValue(path, out string? ownedHash) && !string.Equals(ownedHash, file.Hash, StringComparison.OrdinalIgnoreCase))
                    || !payload.TryGetValue(path, out string? hash) || !string.Equals(hash, file.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Owned file absent, duplicated or changed: " + file?.Path);
                // Different packages may retain references to the same verified bytes.
                owned[path] = file.Hash;
                string source = Files.Child(store.Content(pack), path);
                Files.RejectLinks(store.Home, source);
                if (!File.Exists(source) || !string.Equals(Files.Hash(source), file.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Owned file changed: " + path);
            }
        }
    }
    private static string PayloadHash(PackRecord pack)
    {
        string manifest = string.Join("\n", pack.Files.Select(f => f.Path.Replace((char)92, '/').ToLowerInvariant() + ":" + f.Hash.ToUpperInvariant()).OrderBy(s => s, StringComparer.Ordinal));
        return FrameworkCompat.Hex(FrameworkCompat.HashData(Encoding.UTF8.GetBytes(manifest)));
    }
    internal sealed class RevisionState
    {
        public int SchemaVersion { get; set; }
        public string PackId { get; set; } = "";
        public string Content { get; set; } = "";
        public string PayloadHash { get; set; } = "";
        public ShopLedger Ledger { get; set; } = null!;
    }
}
