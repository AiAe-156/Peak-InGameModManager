#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 日志导出的「另存为」对话框（GetSaveFileNameW）。
///
/// 2.4.3：原先把文件名缓冲写成结构里的 <c>[MarshalAs(LPWStr)] StringBuilder</c> 字段。StringBuilder 只能作为
/// P/Invoke 参数，不能当结构字段；.NET 会抛 MarshalDirectiveException，Unity 的 Mono 却在 <c>Marshal.SizeOf</c>
/// 里直接中止整个进程（2026-11-10 实机崩溃，output_log 栈顶 Marshal:SizeOf ← LogSavePicker:Select）。
/// 改为与 <see cref="IntroductionFilePicker"/> 同一写法：缓冲用 <c>AllocHGlobal</c> 自己管，字段类型照 OPENFILENAMEW。
/// 交给系统对话框的结构里不得再出现 StringBuilder（回归 <c>interop-structs</c> 会查）。
/// </summary>
internal static class LogSavePicker
{
    private const int BufferChars = 32768;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class SaveFileName
    {
        public uint lStructSize;
        public IntPtr hwndOwner, hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFilter = "ZIP (*.zip)\0*.zip\0\0";
        public IntPtr lpstrCustomFilter;
        public uint nMaxCustFilter, nFilterIndex = 1;
        public IntPtr lpstrFile;
        public uint nMaxFile = BufferChars;
        public IntPtr lpstrFileTitle;
        public uint nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrTitle = "";
        public uint Flags;
        public ushort nFileOffset, nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrDefExt = "zip";
        public IntPtr lCustData, lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTemplateName;
        public IntPtr pvReserved;
        public uint dwReserved, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileNameW([In, Out] SaveFileName file);
    [DllImport("comdlg32.dll", ExactSpelling = true)] private static extern uint CommDlgExtendedError();
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// true = 对话框可用（<paramref name="path"/> 为 null 表示用户取消）；false = 对话框不可用，调用方改用固定目录。
    /// </summary>
    internal static bool TrySelect(string name, out string? path, out string? failure)
    {
        path = null;
        failure = null;
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            failure = "not Windows";
            return false;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(BufferChars * 2);
            char[] initial = (name.Length >= BufferChars ? name.Substring(0, BufferChars - 1) : name).ToCharArray();
            Marshal.Copy(initial, 0, buffer, initial.Length);
            Marshal.WriteInt16(buffer, initial.Length * 2, 0);
            // OFN_EXPLORER | OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            var file = new SaveFileName
            {
                hwndOwner = GetActiveWindow(),
                lpstrFile = buffer,
                lpstrTitle = Loc.T("Export log ZIP", "导出日志 ZIP", "匯出日誌 ZIP"),
                Flags = 0x00080000 | 0x00000002 | 0x00000800 | 0x00000008,
            };
            if (file.hwndOwner == IntPtr.Zero)
            {
                file.hwndOwner = GetForegroundWindow();
            }

            file.lStructSize = (uint)Marshal.SizeOf(typeof(SaveFileName));
            if (GetSaveFileNameW(file))
            {
                path = Marshal.PtrToStringUni(buffer);
                return true;
            }

            uint error = CommDlgExtendedError();
            if (error == 0)
            {
                return true; // 用户取消
            }

            failure = "CommDlgExtendedError 0x" + error.ToString("X");
            return false;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException or PlatformNotSupportedException or TypeLoadException)
        {
            failure = e.GetType().Name + ": " + e.Message;
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>对话框不可用时的固定导出位置：<c>BepInEx\config\ModConfigEnhance\export\</c>。</summary>
    internal static string FallbackPath(string configRoot, string name) => Path.Combine(Updating.MceDirectories.LogOutput(configRoot), name);
}
