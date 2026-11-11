#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Standalone, net48-compatible preparation only. Does not touch the installation,
/// resolve assemblies, execute imported code, contact a store, or commit ownership.</summary>
public static class ModImportPreview
{
    private static readonly StringComparer Compare = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] Areas = { "plugins", "core", "patchers", "config" };
    private static readonly HashSet<string> Protected = new HashSet<string>(new[] {
        "BepInEx.dll", "BepInEx.Core.dll", "BepInEx.Preloader.dll", "BepInEx.Unity.Mono.dll",
        "BepInEx.Unity.IL2CPP.dll", "BepInEx.Harmony.dll", "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll",
        "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll", "MonoMod.RuntimeDetour.dll", "MonoMod.Utils.dll",
        "0Harmony.dll", "mscorlib.dll", "netstandard.dll", "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll",
        "UnityPlayer.dll", "GameAssembly.dll", "winhttp.dll", "version.dll", "doorstop_config.ini",
        ".doorstop_version", "PEAK.exe", "steam_api64.dll", "steam_api.dll", "boot.config"
    }, Compare);

    public static ModImportPlan Prepare(ModImportRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var issues = new List<ModImportIssue>();
        var plugins = new List<ModImportPlugin>();
        var files = new List<ModImportFile>();
        string id = Guid.NewGuid().ToString("N"), source = "", staging = "", kind = "", package = request.PackageFolder;
        try
        {
            var limits = request.Limits ?? throw new IOException("Missing import limits.");
            CheckLimits(limits);
            SafeRelative(package, limits);
            if (package.IndexOfAny(new[] { '/', '\\' }) >= 0 || Areas.Contains(package, Compare) || Compare.Equals(package, "BepInEx"))
                throw new IOException("PackageFolder must be one independent package folder.");
            source = Path.GetFullPath(request.SourcePath);
            RejectLinks(source);
            string parent = Path.GetFullPath(request.StagingParent);
            RejectLinks(parent);
            if (Compare.Equals(source, parent) || Within(source, parent) || Within(parent, source))
                throw new IOException("Source and staging parent must be disjoint.");
            // Caller supplies private scratch. A new random child avoids overwriting any prior import.
            Directory.CreateDirectory(parent);
            staging = Path.Combine(parent, "mod-import-" + id);
            Directory.CreateDirectory(staging);
            string raw = Path.Combine(staging, "raw");
            Directory.CreateDirectory(raw);
            if (Directory.Exists(source))
            { kind = "directory"; CopyDirectory(source, raw, limits); }
            else if (Compare.Equals(Path.GetExtension(source), ".zip"))
            { kind = "zip"; Extract(source, raw, limits); }
            else if (Compare.Equals(Path.GetExtension(source), ".dll"))
            {
                kind = "dll";
                var budget = new Budget(limits);
                string name = Path.GetFileName(source);
                budget.Add(name, new FileInfo(source).Length, false);
                CopyFile(source, Child(raw, name), limits.MaximumFileBytes);
            }
            else throw new IOException("Only local DLL, ZIP or directory imports are supported.");

            var inventory = Inventory(raw, limits);
            string prefix = WrapperPrefix(inventory);
            var targets = new HashSet<string>(Compare);
            bool patcher = false;
            foreach (string original in inventory)
            {
                string relative = original.Substring(prefix.Length);
                string[] segments = relative.Split('/');
                string area = "plugins", payload = relative;
                if (Areas.Contains(segments[0], Compare))
                { area = segments[0].ToLowerInvariant(); payload = string.Join("/", segments.Skip(1)); }
                if (payload.Length == 0) throw new IOException("Empty package payload path.");
                foreach (string segment in segments)
                {
                    if (Compare.Equals(segment, "pathcer") || Compare.Equals(segment, "pathcers"))
                        issues.Add(new ModImportIssue("misspelled-patchers", original, "Rename '" + segment + "' to 'patchers' and preview again; no automatic rerouting."));
                    if (Compare.Equals(segment, "BepInEx") || Compare.Equals(segment, "PEAK_Data") || Compare.Equals(segment, "Managed") || Compare.Equals(segment, "doorstop_libs"))
                        issues.Add(new ModImportIssue("forbidden-directory", original, "Game/loader directories are not importable here."));
                }
                string name = Path.GetFileName(relative);
                if (Protected.Contains(name) || name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("BepInEx.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    issues.Add(new ModImportIssue("protected-file", original, "Game and loader files cannot be imported as a mod."));
                string target = area + "/" + (area == "config" ? "" : package + "/") + payload;
                SafeRelative(target, limits);
                if (!targets.Add(target)) issues.Add(new ModImportIssue("target-collision", original, "Multiple files map to " + target));
                string path = Child(raw, original);
                bool managed = false, native = false;
                if (Compare.Equals(Path.GetExtension(path), ".dll"))
                {
                    try
                    {
                        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadingMode = ReadingMode.Deferred, ReadSymbols = false }))
                        {
                            managed = true;
                            foreach (var type in AllTypes(assembly.MainModule.Types))
                            {
                                var attrs = type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInPlugin").ToArray();
                                foreach (var attr in attrs)
                                {
                                    if (attr.ConstructorArguments.Count != 3) throw new IOException("Malformed BepInPlugin metadata.");
                                    string guid = Text(attr, 0), pluginName = Text(attr, 1), version = Text(attr, 2);
                                    Version? parsed;
                                    if (string.IsNullOrWhiteSpace(guid) || string.IsNullOrWhiteSpace(pluginName) || !Version.TryParse(version, out parsed))
                                        throw new IOException("Invalid plugin identity or version.");
                                    var deps = new List<ModImportDependency>();
                                    foreach (var dep in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency"))
                                    {
                                        if (dep.ConstructorArguments.Count < 1) throw new IOException("Malformed dependency metadata.");
                                        string minimum = ""; bool soft = false;
                                        if (dep.ConstructorArguments.Count > 1)
                                        {
                                            var arg = dep.ConstructorArguments[1];
                                            if (arg.Type.FullName == "System.String") minimum = Convert.ToString(arg.Value) ?? "";
                                            else soft = (Convert.ToInt32(arg.Value) & 2) != 0;
                                        }
                                        foreach (var prop in dep.Properties)
                                        {
                                            if (prop.Name == "Flags") soft = (Convert.ToInt32(prop.Argument.Value) & 2) != 0;
                                            if (prop.Name == "MinimumVersion") minimum = Convert.ToString(prop.Argument.Value) ?? "";
                                        }
                                        deps.Add(new ModImportDependency(Text(dep, 0), minimum, soft));
                                    }
                                    plugins.Add(new ModImportPlugin(guid, pluginName, version, type.FullName, original, deps));
                                    if (area != "plugins") issues.Add(new ModImportIssue("plugin-wrong-area", original, "BepInPlugin assembly must be under plugins."));
                                }
                                if (area == "patchers" && type.Methods.Any(m => m.IsPublic && m.IsStatic && m.Name == "Patch"
                                    && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1
                                    && m.Parameters[0].ParameterType.FullName == "Mono.Cecil.AssemblyDefinition")
                                    && type.Properties.Any(p => p.Name == "TargetDLLs" && p.GetMethod != null && p.GetMethod.IsPublic && p.GetMethod.IsStatic
                                        && p.PropertyType.FullName == "System.Collections.Generic.IEnumerable`1<System.String>")) patcher = true;
                            }
                        }
                    }
                    catch (BadImageFormatException)
                    {
                        native = IsNativePe(path);
                        if (!native) issues.Add(new ModImportIssue("invalid-dll", original, "Not a managed assembly or native PE DLL."));
                    }
                    catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is IndexOutOfRangeException)
                    { issues.Add(new ModImportIssue("invalid-metadata", original, ex.Message)); }
                }
                files.Add(new ModImportFile(original, "raw/" + original, target, area, new FileInfo(path).Length, Hash(path), managed, native));
            }
            if (plugins.Count == 0 && !patcher)
                issues.Add(new ModImportIssue("no-plugin", "", "No BepInPlugin or static patcher entry point found. Bare native/library DLLs cannot be installed independently."));
            ValidateDependencies(plugins, request.InstalledPlugins, issues);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is InvalidDataException)
        { issues.Add(new ModImportIssue("unsafe-input", "", ex.Message)); }
        return new ModImportPlan(id, source, kind, staging, package, files, plugins, issues);
    }

    /// <summary>Must be called by the parent transaction immediately before consuming staging.
    /// Checks exact inventory (including additions), links, lengths and immutable content hashes.
    /// This is a check, not a lock; executor still needs exclusive handles/revalidation at commit.</summary>
    public static IReadOnlyList<ModImportIssue> Revalidate(ModImportPlan plan)
    {
        var issues = new List<ModImportIssue>();
        try
        {
            RejectLinks(plan.StagingDirectory);
            // staging is reserved for raw content only; no journals or extra files are accepted.
            var expected = new HashSet<string>(plan.Files.Select(f => f.StagedRelativePath), StringComparer.Ordinal);
            var actual = Inventory(plan.StagingDirectory, new ModImportLimits());
            if (!expected.SetEquals(actual)) throw new IOException("Staging inventory changed; prepare a new preview.");
            foreach (var file in plan.Files)
            {
                string path = Child(plan.StagingDirectory, file.StagedRelativePath);
                if (new FileInfo(path).Length != file.Length || !StringComparer.Ordinal.Equals(Hash(path), file.Sha256))
                    issues.Add(new ModImportIssue("staging-changed", file.StagedRelativePath, "Staged file changed; prepare a new preview."));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        { issues.Add(new ModImportIssue("staging-changed", "", ex.Message)); }
        return issues.AsReadOnly();
    }

    private static void ValidateDependencies(List<ModImportPlugin> plugins, IReadOnlyList<ModImportInstalledPlugin> installed, List<ModImportIssue> issues)
    {
        var versions = new Dictionary<string, string>(Compare);
        foreach (var plugin in installed ?? Array.Empty<ModImportInstalledPlugin>()) versions[plugin.Guid] = plugin.Version;
        var guids = new HashSet<string>(Compare);
        foreach (var plugin in plugins)
        {
            if (!guids.Add(plugin.Guid)) issues.Add(new ModImportIssue("duplicate-guid", plugin.OriginalRelativePath, "Multiple imported plugin types claim " + plugin.Guid));
            versions[plugin.Guid] = plugin.Version;
        }
        foreach (var plugin in plugins)
            foreach (var dep in plugin.Dependencies)
            {
                string? found; Version? have, need;
                bool satisfied = versions.TryGetValue(dep.Guid, out found);
                if (dep.MinimumVersion.Length > 0) satisfied = satisfied && Version.TryParse(found, out have)
                    && Version.TryParse(dep.MinimumVersion, out need) && have >= need;
                if (!satisfied) issues.Add(new ModImportIssue(dep.Soft ? "soft-dependency" : "hard-dependency", plugin.OriginalRelativePath,
                    "Requires " + dep.Guid + (dep.MinimumVersion.Length > 0 ? " >= " + dep.MinimumVersion : "") + "; not present in import/installed snapshot.", !dep.Soft));
            }
    }
    private static string Text(CustomAttribute attr, int index) => Convert.ToString(attr.ConstructorArguments[index].Value) ?? "";
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    { foreach (var type in roots) { yield return type; foreach (var child in AllTypes(type.NestedTypes)) yield return child; } }
    private static string WrapperPrefix(List<string> files)
    {
        string prefix = "";
        while (files.Count > 0)
        {
            var rest = files.Select(f => f.Substring(prefix.Length)).ToArray();
            string first = rest[0].Split('/')[0];
            if (Areas.Contains(first, Compare) || new[] { "pathcer", "pathcers", "PEAK_Data", "Managed", "doorstop_libs" }.Contains(first, Compare)) break;
            if (!rest.All(f => f.StartsWith(first + "/", StringComparison.Ordinal))) break;
            prefix += first + "/";
            if (Compare.Equals(first, "BepInEx")) break;
        }
        return prefix;
    }
    private sealed class Budget
    {
        private readonly ModImportLimits limits;
        private readonly Dictionary<string, bool> paths = new Dictionary<string, bool>(Compare);
        private readonly HashSet<string> declared = new HashSet<string>(Compare);
        private long bytes; private int entries;
        internal Budget(ModImportLimits limits) { this.limits = limits; }
        internal void Add(string path, long length, bool directory)
        {
            SafeRelative(path, limits);
            if (!declared.Add(path)) throw new IOException("Duplicate entry: " + path);
            if (++entries > limits.MaximumEntries || length < 0 || length > limits.MaximumFileBytes || length > limits.MaximumTotalBytes - bytes)
                throw new IOException("Import exceeds file count or uncompressed byte limits.");
            bytes += length;
            var segments = path.Split('/'); string current = "";
            for (int i = 0; i < segments.Length; i++)
            {
                current += (i == 0 ? "" : "/") + segments[i];
                bool isDirectory = i < segments.Length - 1 || directory;
                bool prior;
                if (paths.TryGetValue(current, out prior))
                {
                    string exact = paths.Keys.First(k => Compare.Equals(k, current));
                    if (exact != current || !prior || !isDirectory)
                        throw new IOException("Duplicate, case-conflicting or file/directory-conflicting path: " + path);
                }
                else paths.Add(current, isDirectory);
            }
        }
    }
    private static void CheckLimits(ModImportLimits l)
    {
        if (l.MaximumEntries <= 0 || l.MaximumEntries > 100000 || l.MaximumFileBytes <= 0 || l.MaximumTotalBytes <= 0
            || l.MaximumCompressionRatio <= 0 || l.MaximumRelativePathLength <= 0 || l.MaximumDepth <= 0 || l.MaximumDepth > 64)
            throw new IOException("Invalid import safety limits.");
    }
    private static void SafeRelative(string path, ModImportLimits limits)
    {
        if (string.IsNullOrEmpty(path) || path.Length > limits.MaximumRelativePathLength || path[0] == '/' || path.IndexOf('\\') >= 0)
            throw new IOException("Unsafe relative path: " + path);
        var parts = path.Split('/');
        if (parts.Length > limits.MaximumDepth) throw new IOException("Path depth exceeds limit.");
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal)
                || part.Any(c => c < 32 || "<>:\"|?*".IndexOf(c) >= 0)) throw new IOException("Unsafe Windows path: " + path);
            string stem = part.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$" }.Contains(stem, Compare)
                || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && "123456789\u00b9\u00b2\u00b3".IndexOf(stem[3]) >= 0) throw new IOException("Reserved Windows path: " + path);
        }
    }
    private static bool Within(string root, string path) => path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Child(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!Within(Path.GetFullPath(root), path)) throw new IOException("Path escapes staging.");
        RejectLinks(path); return path;
    }
    private static void RejectLinks(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            // GetAttributes also sees dangling reparse points on Windows.
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Links/reparse points are forbidden: " + current); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static void CopyDirectory(string source, string raw, ModImportLimits limits)
    {
        var budget = new Budget(limits);
        Walk(source, "", (path, rel, dir) => {
            budget.Add(rel, dir ? 0 : new FileInfo(path).Length, dir);
            if (dir) Directory.CreateDirectory(Child(raw, rel));
            else CopyFile(path, Child(raw, rel), limits.MaximumFileBytes);
        }, limits.MaximumDepth);
    }
    private static void Walk(string root, string prefix, Action<string, string, bool> visit, int depth)
    {
        if (depth < 0) throw new IOException("Directory depth exceeds limit.");
        RejectLinks(root);
        foreach (string path in Directory.EnumerateFileSystemEntries(root))
        {
            RejectLinks(path);
            string relative = prefix + Path.GetFileName(path);
            bool dir = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
            visit(path, relative, dir);
            if (dir) Walk(path, relative + "/", visit, depth - 1);
        }
    }
    private static List<string> Inventory(string root, ModImportLimits limits)
    {
        var result = new List<string>(); var budget = new Budget(limits);
        Walk(root, "", (path, rel, dir) => { budget.Add(rel, dir ? 0 : new FileInfo(path).Length, dir); if (!dir) result.Add(rel); }, limits.MaximumDepth);
        result.Sort(StringComparer.Ordinal); return result;
    }
    private static void Extract(string source, string raw, ModImportLimits limits)
    {
        if (new FileInfo(source).Length > limits.MaximumTotalBytes) throw new IOException("ZIP input exceeds byte limit.");
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
        {
            var budget = new Budget(limits);
            // Validate ALL entries before writing any payload.
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                bool dir = name.EndsWith("/", StringComparison.Ordinal);
                int mode = (entry.ExternalAttributes >> 16) & 0xF000;
                if ((mode != 0 && mode != 0x8000 && mode != 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new IOException("ZIP links/special entries are forbidden: " + name);
                budget.Add(dir ? name.Substring(0, name.Length - 1) : name, entry.Length, dir);
                if (dir && entry.Length != 0 || entry.Length > 0 && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > limits.MaximumCompressionRatio))
                    throw new IOException("ZIP compression ratio exceeds safety limit: " + name);
            }
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(Child(raw, name.TrimEnd('/'))); continue; }
                string path = Child(raw, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = entry.Open()) using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { long copied = CopyBounded(stream, output, Math.Min(limits.MaximumFileBytes, entry.Length)); if (copied != entry.Length) throw new IOException("ZIP entry length changed."); }
            }
        }
    }
    private static void CopyFile(string source, string target, long maximum)
    {
        RejectLinks(source); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            long length = input.Length;
            if (CopyBounded(input, output, Math.Min(maximum, length)) != length) throw new IOException("Source changed while staging.");
        }
    }
    private static long CopyBounded(Stream input, Stream output, long maximum)
    {
        byte[] buffer = new byte[81920]; long copied = 0; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        { if (count > maximum - copied) throw new IOException("Uncompressed stream exceeds safety limit."); output.Write(buffer, 0, count); copied += count; }
        return copied;
    }
    private static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
    private static bool IsNativePe(string path)
    {
        using (var input = File.OpenRead(path)) using (var reader = new BinaryReader(input))
        {
            if (input.Length < 64 || reader.ReadUInt16() != 0x5A4D) return false;
            input.Position = 60; int pe = reader.ReadInt32();
            if (pe < 64 || pe > input.Length - 24) return false;
            input.Position = pe;
            if (reader.ReadUInt32() != 0x00004550) return false;
            input.Position = pe + 22;
            return (reader.ReadUInt16() & 0x2000) != 0;
        }
    }
}
