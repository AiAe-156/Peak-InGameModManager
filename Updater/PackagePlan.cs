using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal static class Packages
{
    internal sealed record Plugin(string Guid, string Version, string File, string Name = "");
    internal static bool IsAssembly(string path) => PluginFileNames.IsAssembly(path);
    internal static bool Documentation(string path)
    {
        string name = Path.GetFileName(path);
        string extension = Path.GetExtension(name);
        if (extension.Length != 0 && !new[] { ".md", ".txt", ".rst" }.Contains(extension, Files.Compare)) return false;
        return name.StartsWith("README", StringComparison.OrdinalIgnoreCase) || name.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LICENCE", StringComparison.OrdinalIgnoreCase);
    }
    internal static List<Plugin> ReadPlugins(string file)
    {
        var result = new List<Plugin>();
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { ReadSymbols = false, ReadingMode = ReadingMode.Deferred });
            foreach (var type in assembly.MainModule.Types)
            foreach (var attribute in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInPlugin" && a.ConstructorArguments.Count == 3))
                result.Add(new(attribute.ConstructorArguments[0].Value.ToString()!, attribute.ConstructorArguments[2].Value.ToString()!, file, attribute.ConstructorArguments[1].Value?.ToString() ?? ""));
        }
        catch (BadImageFormatException) { }
        return result;
    }
    internal static void SyncCatalogue(string root, UpdateJob job, Transaction transaction)
    {
        string path = Path.Combine(MceDirectories.Runtime(MceDirectories.RootFromBepInEx(root)), "store-catalog.json");
        if (!File.Exists(path)) return;
        var data = JsonNode.Parse(File.ReadAllText(path)) as JsonArray ?? throw new IOException("商店缓存格式异常。");
        var package = data.OfType<JsonObject>().FirstOrDefault(p => string.Equals(p["Author"]?.GetValue<string>(), job.Namespace, StringComparison.OrdinalIgnoreCase) && string.Equals(p["Name"]?.GetValue<string>(), job.Package, StringComparison.OrdinalIgnoreCase));
        if (package == null) return;
        var versions = package["Versions"] as JsonArray;
        if (versions == null) { versions = new JsonArray(); package["Versions"] = versions; }
        if (!versions.Any(v => v?.GetValue<string>() == transaction.NewVersion)) versions.Add(transaction.NewVersion);
        if (!Version.TryParse(package["Version"]?.GetValue<string>(), out var previous) || Version.Parse(transaction.NewVersion) > previous) package["Version"] = transaction.NewVersion;
        var dependencies = package["Dependencies"] as JsonObject;
        if (dependencies == null) { dependencies = new JsonObject(); package["Dependencies"] = dependencies; }
        dependencies[transaction.NewVersion] = new JsonArray(transaction.Dependencies.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray());
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        Files.Save(path, data); File.SetLastWriteTimeUtc(path, timestamp);
    }
}

/// <summary>Journal of one job. Older journals (no Installer tag) still deserialize: Candidates and Ledger only appear there.</summary>
internal sealed class Transaction
{
    public string Root { get; set; } = "";
    public string PackageRoot { get; set; } = "";
    public string JobId { get; set; } = "";
    public string OldVersion { get; set; } = "";
    public string NewVersion { get; set; } = "";
    public string NewTree { get; set; } = "";
    public string Phase { get; set; } = "prepared";
    public bool CanPlaceLive { get; set; }
    public bool RemoveAfterRollback { get; set; }
    public bool RollbackRequested { get; set; }
    public bool StoreVerified { get; set; } = true;
    public int SourceProcessId { get; set; }
    public List<FileChange> Originals { get; set; } = [];
    public List<FileChange> Desired { get; set; } = [];
    public List<FileChange> Candidates { get; set; } = [];
    public List<FileChange> Parameters { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
    public UpdateLedgerState? Ledger { get; set; }
    public string Installer { get; set; } = "";
    public bool RequiresExit { get; set; }
    public List<FileChange> Retired { get; set; } = [];
    public List<string> Extras { get; set; } = [];
    /// <summary>Directory renames of a folder-organization job; rollback moves them back in reverse order.</summary>
    public List<MovePair> Moves { get; set; } = [];
    public LayoutJournal? Layout { get; set; }
    /// <summary>2.3.1: folders (relative to the root) a folder-organization commit created; only these are removed again
    /// when it rolls back. Null on journals written before this existed: rollback then removes no folder at all.</summary>
    public List<string>? CreatedDirectories { get; set; }
    /// <summary>2.3.1: empty folders a move replaced (an empty shell reused as the target); rollback puts them back.</summary>
    public List<string>? ReplacedEmptyDirectories { get; set; }
    /// <summary>2.3.1: empty folders the user ticked in the review; recycled after every move succeeded.</summary>
    public List<string> RecycleAfter { get; set; } = [];
}

internal sealed class MovePair
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool Done { get; set; }
}

/// <summary>What a move means for the shop ledger: only entries that already exist are updated.</summary>
internal sealed class LayoutJournal
{
    public List<LayoutUnitChange> Units { get; set; } = new();
}

/// <summary>A moved file keeps its path inside the package; only the package root changes.</summary>
internal static class LayoutPaths
{
    /// <summary>Where a moved file was before the move. Only the file name used to be kept, so any file inside a
    /// subfolder ("plugins/X.dll", as in most store packages) was looked up in the wrong place and the confirm failed.</summary>
    internal static string OldPath(LayoutUnitChange unit, PackFile file)
    {
        string path = file.Path.Replace('\\', '/'), newRoot = unit.NewRoot.Replace('\\', '/').TrimEnd('/');
        string inside = path.StartsWith(newRoot + "/", StringComparison.OrdinalIgnoreCase) ? path.Substring(newRoot.Length + 1) : System.IO.Path.GetFileName(path);
        return unit.OldRoot.Replace('\\', '/').TrimEnd('/') + "/" + inside;
    }
}

internal sealed class LayoutUnitChange
{
    public string Key { get; set; } = "";
    public string OldRoot { get; set; } = "";
    public string NewRoot { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string GroupFolder { get; set; } = "";
    public string OuterFolder { get; set; } = "";
    public List<PackFile> Files { get; set; } = new();
}
internal sealed class FileChange
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Backup { get; set; } = "";
    public bool Plugin { get; set; }
    public bool Written { get; set; }
}
internal sealed class UpdateLedgerState
{
    public string Path { get; set; } = "";
    public string ActivePackId { get; set; } = "";
    public ShopLedger Before { get; set; } = new();
    public ShopLedger After { get; set; } = new();
}
