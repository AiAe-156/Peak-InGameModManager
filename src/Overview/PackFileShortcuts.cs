#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

internal static class PackFileShortcuts
{
    internal static void Open(string path, bool folder)
    {
        string full = Path.GetFullPath(path);
        if (folder)
        {
            if (!Directory.Exists(full)) full = Path.GetDirectoryName(full)!;
            ManagementUi.OpenFolder(full); return;
        }
        if (!File.Exists(full)) throw new FileNotFoundException("文件不存在：" + full);
        // Only explicit text types are opened, always with a fixed editor, never a package association.
        var start = TextEditorStart(full);
        if (start == null) { Reveal(full); return; }
        Process.Start(start)?.Dispose();
    }
    internal static ProcessStartInfo? TextEditorStart(string full)
    {
        if (!PackEditPolicy.IsText(full)) return null;
        string editor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        return new ProcessStartInfo(editor, "\"" + full + "\"") { UseShellExecute = false };
    }
    internal static void Reveal(string path)
    {
        string full = Path.GetFullPath(path);
        if (Directory.Exists(full)) { ManagementUi.OpenFolder(full); return; }
        if (!File.Exists(full)) throw new FileNotFoundException("文件不存在：" + full);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + full + "\"") { UseShellExecute = true })?.Dispose();
        else ManagementUi.OpenFolder(Path.GetDirectoryName(full)!);
    }
}
