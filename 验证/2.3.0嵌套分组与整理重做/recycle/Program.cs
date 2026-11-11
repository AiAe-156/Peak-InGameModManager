using System;
using System.IO;
using System.Runtime.InteropServices;

// Same struct and flags as src/Overview/RecycleBin.cs, on a scratch folder in %TEMP%.
string folder = Path.Combine(Path.GetTempPath(), "mce-recycle-probe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "README.md"), "probe");
var op = new ShFileOp { Function = 3, From = folder + "\0\0", Flags = 0x40 | 0x10 | 0x4 | 0x400 };
int code = Native.SHFileOperation(ref op);
Console.WriteLine("code=" + code + " aborted=" + op.Aborted + " stillExists=" + Directory.Exists(folder) + " name=" + Path.GetFileName(folder));
return code == 0 && !Directory.Exists(folder) ? 0 : 1;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct ShFileOp { public IntPtr Window; public uint Function; [MarshalAs(UnmanagedType.LPWStr)] public string From; [MarshalAs(UnmanagedType.LPWStr)] public string? To; public ushort Flags; [MarshalAs(UnmanagedType.Bool)] public bool Aborted; public IntPtr NameMappings; [MarshalAs(UnmanagedType.LPWStr)] public string? Title; }
static class Native { [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")] public static extern int SHFileOperation(ref ShFileOp op); }
