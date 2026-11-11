#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Updating;

/// <summary>Import is a single package, even when it contains multiple plugin assemblies.
/// Target paths are relative to BepInEx. This preview never authorizes a live write.</summary>
public sealed class ModImportRequest
{
    public string SourcePath { get; set; } = "";
    /// <summary>Private scratch directory, NOT BepInEx or any live installation directory.</summary>
    public string StagingParent { get; set; } = "";
    public string PackageFolder { get; set; } = "";
    public ModImportLimits Limits { get; set; } = new ModImportLimits();
    public IReadOnlyList<ModImportInstalledPlugin> InstalledPlugins { get; set; } = Array.Empty<ModImportInstalledPlugin>();
}
public sealed class ModImportLimits
{
    public int MaximumEntries { get; set; } = 10000;
    public long MaximumFileBytes { get; set; } = 128L * 1024 * 1024;
    public long MaximumTotalBytes { get; set; } = 512L * 1024 * 1024;
    public int MaximumCompressionRatio { get; set; } = 200;
    public int MaximumRelativePathLength { get; set; } = 220;
    public int MaximumDepth { get; set; } = 32;
}
public sealed class ModImportInstalledPlugin
{
    public string Guid { get; set; } = "";
    public string Version { get; set; } = "";
}
public sealed class ModImportIssue
{
    public string Code { get; }
    public string Path { get; }
    public string Message { get; }
    public bool Blocking { get; }
    public ModImportIssue(string code, string path, string message, bool blocking = true)
    { Code = code; Path = path; Message = message; Blocking = blocking; }
}
public sealed class ModImportDependency
{
    public string Guid { get; }
    public string MinimumVersion { get; }
    public bool Soft { get; }
    public ModImportDependency(string guid, string minimumVersion, bool soft)
    { Guid = guid; MinimumVersion = minimumVersion; Soft = soft; }
}
public sealed class ModImportPlugin
{
    public string Guid { get; }
    public string Name { get; }
    public string Version { get; }
    public string TypeName { get; }
    public string OriginalRelativePath { get; }
    public IReadOnlyList<ModImportDependency> Dependencies { get; }
    public ModImportPlugin(string guid, string name, string version, string typeName, string path, IEnumerable<ModImportDependency> dependencies)
    { Guid = guid; Name = name; Version = version; TypeName = typeName; OriginalRelativePath = path;
      Dependencies = Array.AsReadOnly(dependencies.ToArray()); }
}
public sealed class ModImportFile
{
    public string OriginalRelativePath { get; }
    public string StagedRelativePath { get; }
    public string TargetRelativePath { get; }
    public string Area { get; }
    public long Length { get; }
    public string Sha256 { get; }
    public bool ManagedAssembly { get; }
    public bool NativeDll { get; }
    public ModImportFile(string original, string staged, string target, string area, long length, string hash, bool managed, bool native)
    { OriginalRelativePath = original; StagedRelativePath = staged; TargetRelativePath = target; Area = area;
      Length = length; Sha256 = hash; ManagedAssembly = managed; NativeDll = native; }
}
public sealed class ModImportPlan
{
    public string Id { get; }
    public string OriginalSource { get; }
    public string SourceKind { get; }
    public string Source => "local";
    public string StagingDirectory { get; }
    public string PackageFolder { get; }
    public IReadOnlyList<ModImportFile> Files { get; }
    public IReadOnlyList<ModImportPlugin> Plugins { get; }
    public IReadOnlyList<ModImportIssue> Issues { get; }
    public bool CanProceed => Files.Count > 0 && !Issues.Any(i => i.Blocking);
    public ModImportPlan(string id, string source, string kind, string staging, string package,
        IEnumerable<ModImportFile> files, IEnumerable<ModImportPlugin> plugins, IEnumerable<ModImportIssue> issues)
    { Id = id; OriginalSource = source; SourceKind = kind; StagingDirectory = staging; PackageFolder = package;
      Files = Array.AsReadOnly(files.ToArray()); Plugins = Array.AsReadOnly(plugins.ToArray()); Issues = Array.AsReadOnly(issues.ToArray()); }
}
/// <summary>Candidate discovery is not source verification; neither changes Plan.Source.
/// A future transaction must verify store archive bytes separately before claiming store ownership.</summary>
public sealed class ModImportStoreCandidate
{
    public string PackageKey { get; }
    public string Version { get; }
    public string Evidence { get; }
    public bool Verified => false;
    public ModImportStoreCandidate(string packageKey, string version, string evidence)
    { PackageKey = packageKey; Version = version; Evidence = evidence; }
}
