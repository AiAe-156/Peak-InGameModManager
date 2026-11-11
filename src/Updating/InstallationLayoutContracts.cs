#nullable enable
using System;
using System.Collections.Generic;

namespace ModConfigEnhance.Updating
{
    public enum InstallationLayoutMode { Disabled, GroupOnly, GroupAndTranslatedOuterFolder }
    public enum LayoutFact { Unknown, No, Yes }
    public enum InstallationLayoutDisposition { Disabled, Unchanged, Move, Deferred, Rejected }

    /// <summary>Where the package came from, as far as the ledger knows. The confirmation dialog names these in
    /// the user's own language, so the worker reports the category rather than a sentence.</summary>
    public enum LayoutSourceKind { Unknown, Local, Store }
    /// <summary>2.3.0: what a tree node stands for.</summary>
    public enum LayoutNodeKind { Package, Container, Empty }

    /// <summary>One resulting package folder in the plugins tree. Flat: a node names the group folder it lands in
    /// (empty when it stays beside plugins/), so the caller can draw the tree and word the notes locally.</summary>
    public sealed class LayoutTreeNode
    {
        public string Group { get; set; } = "";
        public string Folder { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public InstallationLayoutDisposition Disposition { get; set; }
        public LayoutSourceKind Source { get; set; }
        public string Reasons { get; set; } = "";
        /// <summary>Informational codes that do not stop the move (see <see cref="InstallationLayoutPreview.Notes"/>).</summary>
        public List<string> Notes { get; set; } = new List<string>();
        /// <summary>Nobody grouped it: it stays where it is.</summary>
        public bool Ungrouped { get; set; }
        /// <summary>The manager's own folder: moved last, after the game exits.</summary>
        public bool Manager { get; set; }
        /// <summary>2.3.0 fields. Older previews leave them at their defaults.</summary>
        public LayoutNodeKind Kind { get; set; }
        public string GroupId { get; set; } = "";
        public string FinalPath { get; set; } = "";
        public int ModCount { get; set; }
        public int FileCount { get; set; }
    }

    /// <summary>Bounded, caller-collected snapshot. All paths are relative to BepInEx, never absolute.
    /// No planner result authorizes writes: the executor must revalidate the snapshot after exit.</summary>
    public sealed class InstallationLayoutInput
    {
        public InstallationLayoutMode Mode;
        public string BepInExAbsolutePath = "";
        public int MaximumFullPathLength = 240;
        public bool InventoryComplete;
        public readonly List<InstallationLayoutGroup> Groups = new List<InstallationLayoutGroup>();
        public readonly List<InstallationLayoutUnit> Units = new List<InstallationLayoutUnit>();
        public readonly List<InstallationLayoutExistingPath> ExistingPaths = new List<InstallationLayoutExistingPath>();
    }

    public sealed class InstallationLayoutGroup
    {
        public string Id = "", Name = "";
        // Persist the emitted folder name alongside the ID to freeze it across renames/languages.
        public string StableFolderName = "";
    }

    /// <summary>A whole installation package directory, not a plugin DLL. Multi-plugin units never split.</summary>
    public sealed class InstallationLayoutUnit
    {
        public string Id = "", OriginalPath = "", OriginalName = "", TranslatedName = "";
        public string StableOuterFolderName = "";
        public bool OwnershipComplete, FileInventoryComplete;
        public bool IsCoreOrPatcher, IsProtectedManager;
        public LayoutFact HasLinks, IsLocked, IsShared;
        public readonly List<InstallationLayoutPlugin> Plugins = new List<InstallationLayoutPlugin>();
        public readonly List<InstallationLayoutFile> Files = new List<InstallationLayoutFile>();
    }

    public sealed class InstallationLayoutPlugin
    {
        public string Id = "", GroupId = "";
        public LayoutFact IsLoaded;
        /// <summary>Folder depth of the plugin's file inside the package; the shallowest plugins decide the group.</summary>
        public int Depth;
    }

    public sealed class InstallationLayoutFile
    {
        // Relative to the package directory. The planner never renames any part of this path.
        public string RelativePath = "";
        public LayoutFact HasLinks;
        public readonly List<string> OwnerUnitIds = new List<string>();
    }

    public sealed class InstallationLayoutExistingPath
    {
        public string Path = "";
        public bool IsDirectory;
        public LayoutFact HasLinks;
    }

    public sealed class InstallationLayoutFilePreview
    {
        public string OriginalPath { get; }
        public string DestinationPath { get; }
        internal InstallationLayoutFilePreview(string original, string destination)
        { OriginalPath = original; DestinationPath = destination; }
    }

    public sealed class InstallationLayoutPreview
    {
        public string UnitId { get; }
        public string OriginalPath { get; }
        public string DestinationPath { get; internal set; }
        public string GroupFolderName { get; internal set; } = "";
        public string OuterFolderName { get; internal set; } = "";
        public InstallationLayoutDisposition Disposition { get; internal set; }
        public IReadOnlyList<string> Reasons { get; internal set; } = Array.Empty<string>();
        /// <summary>Facts worth showing that do not stop the move: the whole folder moves as one, so duplicates
        /// inside it, a copy elsewhere, or a bundled library from another group are unaffected.</summary>
        public IReadOnlyList<string> Notes { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<InstallationLayoutFilePreview> Files { get; internal set; } = Array.Empty<InstallationLayoutFilePreview>();
        internal InstallationLayoutPreview(string id, string path)
        { UnitId = id; OriginalPath = path; DestinationPath = path; }
    }

    public sealed class InstallationLayoutPlan
    {
        public IReadOnlyList<InstallationLayoutPreview> Units { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool HasMoves { get; }
        internal InstallationLayoutPlan(IReadOnlyList<InstallationLayoutPreview> units, IReadOnlyList<string> errors)
        {
            Units = units; Errors = errors;
            foreach (var unit in units) if (unit.Disposition == InstallationLayoutDisposition.Move) HasMoves = true;
        }
    }
}
