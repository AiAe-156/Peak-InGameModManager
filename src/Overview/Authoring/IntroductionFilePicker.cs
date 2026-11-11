#nullable enable
using System;
using System.Runtime.InteropServices;

namespace ModConfigEnhance.Overview;

internal static class IntroductionFilePicker
{
    // OPENFILENAMEW uses pointer-sized handles and lCustData on both x86 and x64.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class OpenFileName
    {
        public uint lStructSize;
        public IntPtr hwndOwner, hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFilter = "";
        public IntPtr lpstrCustomFilter;
        public uint nMaxCustFilter, nFilterIndex = 1;
        public IntPtr lpstrFile;
        public uint nMaxFile = 32768;
        public IntPtr lpstrFileTitle;
        public uint nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrTitle = "";
        public uint Flags;
        public ushort nFileOffset, nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrDefExt;
        public IntPtr lCustData, lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTemplateName;
        public IntPtr pvReserved;
        public uint dwReserved, FlagsEx;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileNameW([In, Out] OpenFileName file);
    [DllImport("comdlg32.dll", ExactSpelling = true)] private static extern uint CommDlgExtendedError();
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    // true = dialog was supported (including user cancellation); false = caller should offer path input.
    internal static bool TrySelect(string type, out string? path, string? initialDirectory = null)
    {
        path = null;
        if(Environment.OSVersion.Platform != PlatformID.Win32NT)return false;
        IntPtr buffer=IntPtr.Zero;
        try
        {
            buffer=Marshal.AllocHGlobal(32768*2);Marshal.WriteInt16(buffer,0);
            string filter = type == "translation" ? "Text (*.txt)\0*.txt\0\0" : type == "mod" ? "Mod (*.zip;*.dll)\0*.zip;*.dll\0\0" : type == "pack" ? "ZIP (*.zip)\0*.zip\0\0" : type == "markdown" ? "Markdown (*.md;*.markdown;*.txt)\0*.md;*.markdown;*.txt\0\0" : type == "gif" ? "GIF (*.gif)\0*.gif\0\0" : type == "video" ? "Video (*.mp4;*.webm;*.mov;*.avi;*.m4v;*.ogv)\0*.mp4;*.webm;*.mov;*.avi;*.m4v;*.ogv\0\0" : "PNG / JPEG (*.png;*.jpg;*.jpeg)\0*.png;*.jpg;*.jpeg\0\0";
            string title = type == "translation" ? Loc.T("Select a filled-in translation file", "选择填好的翻译文件", "選擇填好的翻譯檔案") : type == "pack" ? Loc.T("Select modpack", "选择整合包", "選擇整合包") : Loc.T("Select local media", "选择本地素材", "選擇本機素材");
            var file = new OpenFileName { hwndOwner = GetActiveWindow(), lpstrFile=buffer, lpstrFilter = filter, lpstrTitle = title, lpstrInitialDir = initialDirectory, Flags = 0x00080000 | 0x00000008 | 0x00000800 | 0x00001000 | 0x00000004 };
            if(file.hwndOwner == IntPtr.Zero)file.hwndOwner = GetForegroundWindow();
            file.lStructSize = (uint)Marshal.SizeOf(typeof(OpenFileName));
            if(GetOpenFileNameW(file)){path=Marshal.PtrToStringUni(buffer);return true;}
            return CommDlgExtendedError()==0;
        }
        catch(DllNotFoundException){return false;}
        catch(EntryPointNotFoundException){return false;}
        catch(MarshalDirectiveException){return false;}
        catch(PlatformNotSupportedException){return false;}
        finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);}
    }
}
