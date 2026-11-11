from pathlib import Path
import sys
root = Path(sys.argv[1])
def sub(path, old, new, count=1):
    p = root / path; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (path, old[:70], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')

c = 'src/Updating/InstallationLayoutContracts.cs'
sub(c, '''        public InstallationLayoutDisposition Disposition { get; set; }
        public LayoutSourceKind Source { get; set; }
        public string Reasons { get; set; } = "";
    }''', '''        public InstallationLayoutDisposition Disposition { get; set; }
        public LayoutSourceKind Source { get; set; }
        public string Reasons { get; set; } = "";
        /// <summary>Informational codes that do not stop the move (see <see cref="InstallationLayoutPreview.Notes"/>).</summary>
        public List<string> Notes { get; set; } = new List<string>();
        /// <summary>Nobody grouped it: it stays where it is.</summary>
        public bool Ungrouped { get; set; }
        /// <summary>The manager's own folder: moved last, after the game exits.</summary>
        public bool Manager { get; set; }
    }''')
sub(c, '''        public string Id = "", GroupId = "";
        public LayoutFact IsLoaded;''', '''        public string Id = "", GroupId = "";
        public LayoutFact IsLoaded;
        /// <summary>Folder depth of the plugin's file inside the package; the shallowest plugins decide the group.</summary>
        public int Depth;''')
sub(c, '''        public IReadOnlyList<string> Reasons { get; internal set; } = Array.Empty<string>();''', '''        public IReadOnlyList<string> Reasons { get; internal set; } = Array.Empty<string>();
        /// <summary>Facts worth showing that do not stop the move: the whole folder moves as one, so duplicates
        /// inside it, a copy elsewhere, or a bundled library from another group are unaffected.</summary>
        public IReadOnlyList<string> Notes { get; internal set; } = Array.Empty<string>();''')

p = 'src/Updating/InstallationLayoutPlanner.cs'
sub(p, '''                if (unit.IsProtectedManager || unit.Plugins.Any(p => p != null && IgnoreCase.Equals(p.Id, "com.aiae.modconfigenhance"))) reasons.Add("ProtectedManagerExcluded");''',
'''                // The manager moves like any package: it runs from its cached tools, and the move happens after the game exits.
                var notes = new List<string>();
                if (unit.IsProtectedManager || unit.Plugins.Any(p => p != null && IgnoreCase.Equals(p.Id, "com.aiae.modconfigenhance"))) notes.Add("ManagerMovedLast");''')
sub(p, '''                if (unit.Plugins.Where(p => p != null).GroupBy(p => p.Id, IgnoreCase).Any(g => g.Count() > 1)) reasons.Add("DuplicatePluginId");
                var groupIds = unit.Plugins.Where(p => p != null).Select(p => p.GroupId ?? "").Distinct(IgnoreCase).ToArray();
                InstallationLayoutGroup? selected = null;
                if (groupIds.Length != 1 || !groups.TryGetValue(groupIds[0], out selected)) reasons.Add("MissingOrConflictingGroup");''',
'''                // Several copies of one plugin inside the folder (a ".dll.old" beside the ".dll") travel together; moving changes nothing about them.
                if (unit.Plugins.Where(p => p != null).GroupBy(p => p.Id, IgnoreCase).Any(g => g.Count() > 1)) notes.Add("DuplicatePluginId");
                // The package's own (shallowest) plugins decide where it goes; a library bundled deeper inside travels with it.
                var present = unit.Plugins.Where(p => p != null).ToArray();
                int top = present.Length == 0 ? 0 : present.Min(p => p.Depth);
                var groupIds = present.Where(p => p.Depth == top).Select(p => p.GroupId ?? "").Distinct(IgnoreCase).ToArray();
                if (groupIds.Length == 1 && present.Any(p => !IgnoreCase.Equals(p.GroupId ?? "", groupIds[0]))) notes.Add("BundledOtherGroup");
                InstallationLayoutGroup? selected = null;
                if (groupIds.Length != 1 || !groups.TryGetValue(groupIds[0], out selected)) reasons.Add("MissingOrConflictingGroup");''')
sub(p, '''                    if (unit.Plugins.Any(p => p != null && other.Plugins.Any(q => q != null && IgnoreCase.Equals(p.Id, q.Id)))) reasons.Add("PluginOwnedByMultiplePackages");''',
'''                    // The same plugin installed in another folder too: moving either folder does not change that, so it is only shown.
                    if (unit.Plugins.Any(p => p != null && other.Plugins.Any(q => q != null && IgnoreCase.Equals(p.Id, q.Id)))) notes.Add("PluginOwnedByMultiplePackages");''')
sub(p, '''                Finish(preview, reasons.Count > 0 ? InstallationLayoutDisposition.Deferred :''',
'''                preview.Notes = Array.AsReadOnly(notes.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());
                Finish(preview, reasons.Count > 0 ? InstallationLayoutDisposition.Deferred :''')
print('ok')
