#nullable enable
using System.Runtime.InteropServices;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Sends folders to the Windows Recycle Bin (2.3.1). Folder organization recycles the empty folders the user
/// ticked in the review, after every move succeeded. A folder is taken only when, at that moment, it is under plugins/,
/// holds no plugin assembly, no manifest and no link; anything else is skipped and reported, never forced. The shell
/// operation is the one Explorer uses, so the user can restore the folder from the Recycle Bin.</summary>
internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr Window;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Title;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOp operation);

    private const uint FoDelete = 3;
    private const ushort FofAllowUndo = 0x40, FofNoConfirmation = 0x10, FofSilent = 0x4, FofNoErrorUi = 0x400;

    /// <summary>Test hook: replaces the shell call (fixtures run on scratch folders and must not fill the real bin).</summary>
    internal static Action<string>? SendOverride { get; set; }

    /// <summary>Why this folder may not be recycled now, or null when it may.</summary>
    internal static string? Refusal(string root, string relative)
    {
        string plugins = Path.GetFullPath(Path.Combine(root, "plugins")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full;
        try { full = Path.GetFullPath(Files.Child(root, relative)).TrimEnd(Path.DirectorySeparatorChar); }
        catch (Exception) { return "invalid path"; }
        if (!full.StartsWith(plugins, StringComparison.OrdinalIgnoreCase)) return "outside plugins";
        if (GroupFolderPlanner.NeverTouch(relative)) return "protected";
        if (!Directory.Exists(full)) return "missing";
        try { Files.RejectLinks(root, full); } catch (IOException) { return "link"; }
        foreach (string entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return "link";
            string name = Path.GetFileName(entry);
            if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || name.Equals("manifest.json.old", StringComparison.OrdinalIgnoreCase)
                || PluginFileNames.IsAssembly(entry)) return "has mod files";
        }
        return null;
    }

    /// <summary>Recycles each folder that is still safe to recycle; returns one line per folder that was skipped.</summary>
    internal static List<string> SendAll(string root, IEnumerable<string> relatives)
    {
        var skipped = new List<string>();
        // Deepest first, so a ticked folder inside another ticked folder is handled before its parent goes.
        foreach (string relative in relatives.Distinct(Files.Compare).OrderByDescending(r => r.Count(c => c == '/')))
        {
            string? refusal = Refusal(root, relative);
            if (refusal == "missing") continue;
            if (refusal != null) { skipped.Add(relative + "（" + refusal + "）"); continue; }
            string full = Path.GetFullPath(Files.Child(root, relative)).TrimEnd(Path.DirectorySeparatorChar);
            try
            {
                if (SendOverride != null) SendOverride(full);
                else
                {
                    var operation = new ShFileOp { Function = FoDelete, From = full + "\0\0", Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi };
                    int code = SHFileOperation(ref operation);
                    if (code != 0 || operation.Aborted) { skipped.Add(relative + "（code " + code.ToString("X") + "）"); continue; }
                }
                if (Directory.Exists(full)) skipped.Add(relative + "（still there）");
                else WorkerLog.Info("recycled empty folder " + relative);
            }
            catch (Exception error) { skipped.Add(relative + "（" + error.Message + "）"); }
        }
        return skipped;
    }
}
