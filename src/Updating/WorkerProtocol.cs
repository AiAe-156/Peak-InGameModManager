#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;

[assembly: AssemblyMetadata("MCE.WorkerProtocol", "2")]
namespace ModConfigEnhance.Updating;

/// <summary>Local release integrity and capability declaration, NOT a security signature.</summary>
public static class WorkerProtocol
{
    public const int Current = 2;
    public const string ResourceCapability = "resource-transactions-v3-independent-operations";
    public const string ResourceCapabilityFile = "resource-capabilities.txt";
    public static void RequireResourceTools(string directory)
    {
        ValidateTools(directory);
        string capability = Path.Combine(directory, ResourceCapabilityFile);
        RejectLinks(Path.GetFullPath(directory), capability);
        if (!File.Exists(capability) || File.ReadAllText(capability).Trim() != ResourceCapability)
            throw new IOException("Worker resource transaction capability missing; reinstall the complete matching MCE release.");
    }
    public static void RequireResourceWorker(UpdateSnapshot snapshot, bool running)
    {
        if (running && snapshot.ResourceCapability != ResourceCapability)
            throw new IOException("An older worker instance is still running. Wait for it to exit before submitting resource operations; no command was published.");
    }
    public const string ManifestName = "worker-protocol.manifest";
    public const string Executable = "MCE.UpdateWorker.exe";
    public static string Hash(string file) { using var sha = SHA256.Create(); using var input = File.OpenRead(file); return Hex(sha.ComputeHash(input)); }
    static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    static string Digest(string value) { using var sha = SHA256.Create(); return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
    // Legacy callers without a selected boundary retain the conservative disk-root check.
    public static void RejectLinks(string path) => RejectLinks(System.IO.Path.GetPathRoot(Path.GetFullPath(path))!, path);
    public static void RejectLinks(string boundary, string path)
    {
        if (!Path.IsPathFullyQualified(boundary) || !Path.IsPathFullyQualified(path)) throw new IOException("Worker boundary/path must be absolute.");
        string root = Path.GetFullPath(boundary).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(path);
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Worker path is outside the selected boundary: " + path);
        // Trust only ancestors ABOVE the explicit installation/tool boundary, never the boundary itself.
        for (string? current = full; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked worker path rejected: " + current);
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase) || root.Length == 0 && current == System.IO.Path.GetPathRoot(full)) break;
        }
    }
    static SortedDictionary<string, string> Inventory(string directory, string boundary)
    {
        RejectLinks(boundary, directory);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.Count > 0)
        {
            string current = pending.Pop(); RejectLinks(boundary, current);
            foreach (string child in Directory.GetDirectories(current)) { RejectLinks(boundary, child); pending.Push(child); }
            foreach (string file in Directory.GetFiles(current))
            {
                RejectLinks(boundary, file);
                string path = Path.GetRelativePath(directory, file).Replace((char)92, '/');
                if (path == ManifestName) continue;
                if (path.Split('/').Any(p => new[] { "bin", "obj", "tests", "test", ".git" }.Contains(p, StringComparer.OrdinalIgnoreCase))
                    || new[] { ".pdb", ".cs", ".csproj", ".log", ".tmp", ".partial" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    throw new IOException("Build/test residue is not a worker release dependency: " + path);
                if (path.Any(c => char.IsControl(c) || c == '|' || c == ':') || path.Split('/').Any(p => p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ")))
                    throw new IOException("Invalid tool path: " + path);
                result.Add(path, Hash(file));
            }
        }
        foreach (string required in new[] { Executable, "Mono.Cecil.LICENSE.txt", "Runtime-LICENSE.TXT", "Runtime-THIRD-PARTY-NOTICES.TXT" })
            if (!result.ContainsKey(required)) throw new IOException("Incomplete worker release: " + required);
        return result;
    }
    static string Payload(SortedDictionary<string, string> files) => string.Join("\n", files.Select(p => p.Key + "|" + p.Value));
    public static void WriteManifest(string directory)
    {
        File.WriteAllText(Path.Combine(directory, ResourceCapabilityFile), ResourceCapability, new UTF8Encoding(false));
        var files = Inventory(directory, Path.GetFullPath(directory)); string payload = Payload(files);
        File.WriteAllText(Path.Combine(directory, ManifestName), "MCE-WORKER|2\nbuild|" + Digest(payload) + "\n" + payload + "\n", new UTF8Encoding(false));
    }
    public static string ValidateTools(string directory, int requiredProtocol = Current, string? boundary = null)
    {
        boundary ??= Path.GetFullPath(directory);
        if (requiredProtocol < 1 || requiredProtocol > Current) throw new IOException("Unsupported required worker protocol: " + requiredProtocol);
        string manifest = Path.Combine(directory, ManifestName); RejectLinks(boundary, manifest);
        string[] lines = File.ReadAllLines(manifest);
        if (lines.Length < 3 || lines[0] != "MCE-WORKER|2") throw new IOException("Worker protocol2 declaration missing or incompatible; reinstall the complete MCE release.");
        var files = Inventory(directory, boundary); string payload = Payload(files); string build = Digest(payload);
        if (lines[1] != "build|" + build || string.Join("\n", lines.Skip(2)) != payload)
            throw new IOException("Worker release manifest/dependencies damaged. No cached tool was overwritten.");
        return build;
    }
    /// <summary>Validate source every time, then stage the ENTIRE release and publish by directory rename.
    /// A damaged/running cache is never repaired in place. Source is the local installed release, not a trust authority.</summary>
    public static string PrepareTools(string source, string toolsRoot, int requiredProtocol = Current, string? sourceBoundary = null, string? cacheBoundary = null)
    {
        sourceBoundary ??= Path.GetFullPath(source); cacheBoundary ??= Path.GetFullPath(toolsRoot);
        string build = ValidateTools(source, requiredProtocol, sourceBoundary); RejectLinks(cacheBoundary, toolsRoot); Directory.CreateDirectory(toolsRoot);
        string prefix = "p2-" + build;
        foreach (string candidate in Directory.GetDirectories(toolsRoot, prefix + "*"))
        {
            if (Path.GetFileName(candidate).Contains(".stage-")) continue;
            try { if (ValidateTools(candidate, requiredProtocol, cacheBoundary) == build) return candidate; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        string stage = Path.Combine(toolsRoot, prefix + ".stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            RejectLinks(sourceBoundary, file); string target = Path.Combine(stage, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, false);
        }
        if (ValidateTools(stage, requiredProtocol, cacheBoundary) != build || ValidateTools(source, requiredProtocol, sourceBoundary) != build) throw new IOException("Worker source changed during copy; incomplete stage retained.");
        string published = Path.Combine(toolsRoot, prefix + "-" + Guid.NewGuid().ToString("N")); Directory.Move(stage, published); return published;
    }
    public static void RequireProtocol(int schema, int minimum)
    {
        if (schema < 1 || schema > 2 || minimum < 1 || minimum > Current || schema == 2 && minimum != Current)
            throw new IOException("Pack index requires an incompatible worker protocol. No commands were consumed.");
    }
}
