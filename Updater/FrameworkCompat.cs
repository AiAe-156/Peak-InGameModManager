global using System.Net.Http;
#if NETFRAMEWORK
global using Path = MCE.UpdateWorker.FrameworkPath;
#endif
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
#endif

namespace MCE.UpdateWorker
{
    // Deliberately shared with net10 test harnesses: identical hashes and atomic replacement semantics.
    internal static class FrameworkCompat
    {
        internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
        internal static byte[] HashData(byte[] bytes) { using var sha = SHA256.Create(); return sha.ComputeHash(bytes); }
        internal static byte[] HashData(Stream input) { using var sha = SHA256.Create(); return sha.ComputeHash(input); }
        internal static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
        internal static int ProcessId { get { using var process = Process.GetCurrentProcess(); return process.Id; } }
        internal static string ProcessPath { get { using var process = Process.GetCurrentProcess(); return process.MainModule?.FileName ?? throw new IOException("Cannot locate worker executable."); } }
        internal static void MoveOverwrite(string source, string target)
        {
            // Never delete the destination first: a failed rename must retain the old transaction record.
            if (File.Exists(target)) File.Replace(source, target, null); else File.Move(source, target);
        }
        internal static DirectoryInfo ResolveDirectory(DirectoryInfo directory)
        {
#if NETFRAMEWORK
            using var handle = CreateFile(directory.FullName, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var text = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(handle, text, (uint)text.Capacity, 0);
            if (length == 0 || length >= text.Capacity) throw new IOException("Cannot resolve installation alias: " + directory.FullName);
            string path = text.ToString();
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path.Substring(4);
            return new DirectoryInfo(path);
#else
            return (DirectoryInfo)(directory.ResolveLinkTarget(true) ?? throw new IOException("Cannot resolve installation alias."));
#endif
        }
#if NETFRAMEWORK
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringBuilder path, uint size, uint flags);
#endif
        internal static async Task<string> ReadTextAsync(this HttpContent content, CancellationToken cancellation)
        {
#if NETFRAMEWORK
            using (cancellation.Register(() => content.Dispose()))
            { cancellation.ThrowIfCancellationRequested(); var value = await content.ReadAsStringAsync(); cancellation.ThrowIfCancellationRequested(); return value; }
#else
            return await content.ReadAsStringAsync(cancellation);
#endif
        }
        internal static async Task<Stream> ReadStreamAsync(this HttpContent content, CancellationToken cancellation)
        {
#if NETFRAMEWORK
            using (cancellation.Register(() => content.Dispose()))
            { cancellation.ThrowIfCancellationRequested(); var value = await content.ReadAsStreamAsync(); cancellation.ThrowIfCancellationRequested(); return value; }
#else
            return await content.ReadAsStreamAsync(cancellation);
#endif
        }
    }
#if NETFRAMEWORK
    // Windows ordinal path semantics (not URI semantics: percent/hash characters are ordinary filenames).
    internal static class FrameworkPath
    {
        internal static char DirectorySeparatorChar => System.IO.Path.DirectorySeparatorChar;
        internal static char AltDirectorySeparatorChar => System.IO.Path.AltDirectorySeparatorChar;
        internal static string Combine(params string[] paths) => System.IO.Path.Combine(paths);
        internal static string GetFullPath(string path) => System.IO.Path.GetFullPath(path);
        internal static string GetDirectoryName(string path) => System.IO.Path.GetDirectoryName(path)!;
        internal static string GetFileName(string path) => System.IO.Path.GetFileName(path);
        internal static string GetFileNameWithoutExtension(string path) => System.IO.Path.GetFileNameWithoutExtension(path);
        internal static string GetExtension(string path) => System.IO.Path.GetExtension(path);
        internal static char[] GetInvalidFileNameChars() => System.IO.Path.GetInvalidFileNameChars();
        internal static bool IsPathRooted(string path) => System.IO.Path.IsPathRooted(path);
        internal static bool IsPathFullyQualified(string path) => path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/') || path.Length >= 2 && (path[0] == '\\' || path[0] == '/') && (path[1] == '\\' || path[1] == '/');
        internal static string GetRelativePath(string root, string path)
        {
            string from = GetFullPath(root).TrimEnd('\\', '/'), to = GetFullPath(path).TrimEnd('\\', '/');
            if (!string.Equals(System.IO.Path.GetPathRoot(from), System.IO.Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase)) return to;
            string[] a = from.Split('\\'), b = to.Split('\\'); int common = 0;
            while (common < a.Length && common < b.Length && string.Equals(a[common], b[common], StringComparison.OrdinalIgnoreCase)) common++;
            string relative = string.Join("\\", Enumerable.Repeat("..", a.Length - common).Concat(b.Skip(common)));
            return relative.Length == 0 ? "." : relative;
        }
    }
    internal static class FrameworkExtensions
    {
        internal static bool Contains(this string value, string part, StringComparison comparison) => value.IndexOf(part, comparison) >= 0;
        internal static bool Contains(this string value, char part) => value.IndexOf(part) >= 0;
        internal static bool EndsWith(this string value, char suffix) => value.Length > 0 && value[value.Length - 1] == suffix;
        internal static bool StartsWith(this string value, char prefix) => value.Length > 0 && value[0] == prefix;
        internal static string[] Split(this string value, char separator, int count) => value.Split(new[] { separator }, count);
        internal static HashSet<T> ToHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null) => new(source, comparer);
        internal static bool Remove<K,V>(this Dictionary<K,V> dictionary, K key, out V value) { if (!dictionary.TryGetValue(key, out value!)) return false; return dictionary.Remove(key); }
        internal static bool TryDequeue<T>(this Queue<T> queue, out T value) { if (queue.Count > 0) { value = queue.Dequeue(); return true; } value = default!; return false; }
        internal static bool TryAdd<K,V>(this Dictionary<K,V> dictionary, K key, V value) { if (dictionary.ContainsKey(key)) return false; dictionary.Add(key,value); return true; }
        internal static bool TryPop<T>(this Stack<T> stack, out T value) { if (stack.Count > 0) { value = stack.Pop(); return true; } value = default!; return false; }
    }
#endif
}

#if NETFRAMEWORK
namespace System
{
    internal readonly struct Index
    {
        readonly int value;
        public Index(int value, bool fromEnd = false) { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); this.value = fromEnd ? ~value : value; }
        public int Value => value < 0 ? ~value : value;
        public bool IsFromEnd => value < 0;
        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;
        public static Index Start => new(0);
        public static Index End => new(0,true);
        public static implicit operator Index(int value) => new(value);
    }
    internal readonly struct Range
    {
        public Index Start { get; }
        public Index End { get; }
        public Range(Index start, Index end) { Start=start; End=end; }
        public static Range StartAt(Index start) => new(start,Index.End);
        public static Range EndAt(Index end) => new(Index.Start,end);
        public static Range All => new(Index.Start,Index.End);
        public (int Offset,int Length) GetOffsetAndLength(int length) { int start=Start.GetOffset(length), end=End.GetOffset(length); if ((uint)end > (uint)length || (uint)start > (uint)end) throw new ArgumentOutOfRangeException(nameof(length)); return (start,end-start); }
    }
}
#endif
