#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ModConfigEnhance.Updating
{
    /// <summary>Pure, conservative directory-organization preview. No filesystem, process or network IO.</summary>
    public static class InstallationLayoutPlanner
    {
        private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;
        private const int MaxUnits = 10000, MaxFiles = 100000, MaxText = 4096;

        public static InstallationLayoutPlan Plan(InstallationLayoutInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var errors = new List<string>();
            if (!Enum.IsDefined(typeof(InstallationLayoutMode), input.Mode)) errors.Add("InvalidMode");
            if (!ValidRoot(input.BepInExAbsolutePath)) errors.Add("InvalidBepInExRoot");
            if (input.MaximumFullPathLength < 64 || input.MaximumFullPathLength > 32760) errors.Add("InvalidPathLimit");
            if (input.Units.Count > MaxUnits || input.Groups.Count > MaxUnits || input.ExistingPaths.Count > MaxFiles ||
                input.Units.Sum(u => u == null ? 0L : u.Files.Count) > MaxFiles ||
                input.Units.Sum(u => u == null ? 0L : u.Plugins.Count) > MaxUnits)
                return new InstallationLayoutPlan(Array.Empty<InstallationLayoutPreview>(), new[] { "InputLimitExceeded" });
            if (input.Units.Any(u => u == null) || input.Groups.Any(g => g == null) || input.ExistingPaths.Any(p => p == null))
                return new InstallationLayoutPlan(Array.Empty<InstallationLayoutPreview>(), new[] { "NullInputEntry" });
            var duplicateIds = new HashSet<string>(input.Units.GroupBy(u => u.Id ?? "", IgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key), IgnoreCase);
            var groups = new Dictionary<string, InstallationLayoutGroup>(IgnoreCase);
            foreach (var group in input.Groups.OrderBy(g => g.Id, StringComparer.Ordinal))
            {
                if (!ValidId(group.Id) || groups.ContainsKey(group.Id)) { errors.Add("InvalidOrDuplicateGroupId"); continue; }
                groups.Add(group.Id, group);
            }
            foreach (var path in input.ExistingPaths)
                if (!ValidRelative(path.Path)) errors.Add("InvalidInventoryPath");
            var results = new List<InstallationLayoutPreview>();
            foreach (var unit in input.Units.OrderBy(u => u.Id, IgnoreCase).ThenBy(u => u.Id, StringComparer.Ordinal).ThenBy(u => u.OriginalPath, StringComparer.Ordinal))
            {
                string original = Normalize(unit.OriginalPath);
                var preview = new InstallationLayoutPreview(unit.Id ?? "", original);
                results.Add(preview);
                var reasons = new List<string>();
                bool rejected = false;
                if (!ValidId(unit.Id) || duplicateIds.Contains(unit.Id ?? "")) { reasons.Add("InvalidOrDuplicateUnitId"); rejected = true; }
                if (!ValidRelative(unit.OriginalPath) || !original.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))
                { reasons.Add("SourceOutsidePluginDirectory"); rejected = true; }
                if (unit.Files.Any(f => f == null || !ValidRelative(f.RelativePath))) { reasons.Add("InvalidInternalPath"); rejected = true; }
                if (unit.HasLinks == LayoutFact.Yes || unit.Files.Any(f => f != null && f.HasLinks == LayoutFact.Yes))
                { reasons.Add("LinkRejected"); rejected = true; }
                if (errors.Count > 0) { reasons.Add("InvalidPlanInput"); rejected = true; }
                if (rejected) { Finish(preview, InstallationLayoutDisposition.Rejected, reasons); continue; }
                if (input.Mode == InstallationLayoutMode.Disabled) { Finish(preview, InstallationLayoutDisposition.Disabled, new[] { "OrganizationDisabled" }); continue; }
                if (unit.IsCoreOrPatcher) reasons.Add("CoreOrPatcherExcluded");
                // The manager moves like any package: it runs from its cached tools, and the move happens after the game exits.
                var notes = new List<string>();
                if (unit.IsProtectedManager || unit.Plugins.Any(p => p != null && ManagerIdentity.IsManagerGuid(p.Id))) notes.Add("ManagerMovedLast");
                if (!input.InventoryComplete) reasons.Add("InventoryIncomplete");
                if (!unit.OwnershipComplete || !unit.FileInventoryComplete || unit.Files.Count == 0) reasons.Add("OwnershipOrFilesIncomplete");
                if (unit.HasLinks != LayoutFact.No || unit.Files.Any(f => f.HasLinks != LayoutFact.No)) reasons.Add("LinkFactsUnknown");
                if (unit.IsLocked != LayoutFact.No) reasons.Add(unit.IsLocked == LayoutFact.Yes ? "PackageLocked" : "LockFactsUnknown");
                if (unit.IsShared != LayoutFact.No) reasons.Add(unit.IsShared == LayoutFact.Yes ? "SharedPackage" : "SharingFactsUnknown");
                if (unit.Files.Any(f => f.OwnerUnitIds.Count != 1 || !IgnoreCase.Equals(f.OwnerUnitIds[0], unit.Id))) reasons.Add("SharedOrUnknownFileOwner");
                if (unit.Files.GroupBy(f => Normalize(f.RelativePath), IgnoreCase).Any(g => g.Count() > 1)) reasons.Add("DuplicateInternalPath");
                var internalPaths = unit.Files.Select(f => Normalize(f.RelativePath)).OrderBy(p => p, IgnoreCase).ToArray();
                for (int n = 1; n < internalPaths.Length; n++)
                    if (Within(internalPaths[n], internalPaths[n - 1])) reasons.Add("OverlappingInternalFiles");
                if (unit.Plugins.Count == 0 || unit.Plugins.Any(p => p == null || !ValidId(p.Id) || p.IsLoaded != LayoutFact.No)) reasons.Add("LoadedOrUnknownPlugins");
                // Several copies of one plugin inside the folder (a ".dll.old" beside the ".dll") travel together; moving changes nothing about them.
                if (unit.Plugins.Where(p => p != null).GroupBy(p => p.Id, IgnoreCase).Any(g => g.Count() > 1)) notes.Add("DuplicatePluginId");
                // The package's own (shallowest) plugins decide where it goes; a library bundled deeper inside travels with it.
                var present = unit.Plugins.Where(p => p != null).ToArray();
                int top = present.Length == 0 ? 0 : present.Min(p => p.Depth);
                var groupIds = present.Where(p => p.Depth == top).Select(p => p.GroupId ?? "").Distinct(IgnoreCase).ToArray();
                if (groupIds.Length == 1 && present.Any(p => !IgnoreCase.Equals(p.GroupId ?? "", groupIds[0]))) notes.Add("BundledOtherGroup");
                InstallationLayoutGroup? selected = null;
                if (groupIds.Length != 1 || !groups.TryGetValue(groupIds[0], out selected)) reasons.Add("MissingOrConflictingGroup");
                string groupFolder = selected == null ? "" : Folder(selected.StableFolderName, selected.Name, selected.Id);
                string outer = input.Mode == InstallationLayoutMode.GroupOnly ? Leaf(original) :
                    Folder(unit.StableOuterFolderName, string.IsNullOrWhiteSpace(unit.TranslatedName) ? (string.IsNullOrWhiteSpace(unit.OriginalName) ? Leaf(original) : unit.OriginalName) : unit.TranslatedName, unit.Id!);
                if (!ValidSegment(groupFolder) || !ValidSegment(outer)) reasons.Add("InvalidStableFolderName");
                if (selected != null && groups.Values.Any(g => !IgnoreCase.Equals(g.Id, selected.Id) && IgnoreCase.Equals(groupFolder, Folder(g.StableFolderName, g.Name, g.Id)))) reasons.Add("GroupFolderCollision");
                preview.GroupFolderName = groupFolder;
                preview.OuterFolderName = outer;
                preview.DestinationPath = "plugins/" + groupFolder + "/" + outer;
                if (!IgnoreCase.Equals(original, preview.DestinationPath) && Overlap(original, preview.DestinationPath)) reasons.Add("SourceDestinationOverlap");
                preview.Files = Array.AsReadOnly(unit.Files.Select(f => new InstallationLayoutFilePreview(original + "/" + f.RelativePath, preview.DestinationPath + "/" + f.RelativePath)).ToArray());
                if (TooLong(input, original) || TooLong(input, preview.DestinationPath) || preview.Files.Any(f => TooLong(input, f.OriginalPath) || TooLong(input, f.DestinationPath))) reasons.Add("PathLengthExceeded");
                foreach (var other in input.Units)
                {
                    if (ReferenceEquals(unit, other)) continue;
                    if (Overlap(original, Normalize(other.OriginalPath))) reasons.Add("OverlappingPackageRoots");
                    if (Overlap(preview.DestinationPath, Normalize(other.OriginalPath))) reasons.Add("DestinationOccupiedByPackage");
                    // The same plugin installed in another folder too: moving either folder does not change that, so it is only shown.
                    if (unit.Plugins.Any(p => p != null && other.Plugins.Any(q => q != null && IgnoreCase.Equals(p.Id, q.Id)))) notes.Add("PluginOwnedByMultiplePackages");
                }
                foreach (var path in input.ExistingPaths)
                {
                    string occupied = Normalize(path.Path);
                    bool relevant = Overlap(occupied, original) || Overlap(occupied, preview.DestinationPath);
                    if (relevant && path.HasLinks != LayoutFact.No) reasons.Add(path.HasLinks == LayoutFact.Yes ? "InventoryLinkRejected" : "InventoryLinkFactsUnknown");
                    if (Within(occupied, original))
                    {
                        if (!path.IsDirectory && !unit.Files.Any(f => IgnoreCase.Equals(occupied, original + "/" + Normalize(f.RelativePath)))) reasons.Add("UnownedSourceInventoryFile");
                        continue;
                    }
                    if (Within(occupied, preview.DestinationPath) || (!path.IsDirectory && Within(preview.DestinationPath, occupied))) reasons.Add("DestinationOccupied");
                }
                preview.Notes = Array.AsReadOnly(notes.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());
                Finish(preview, reasons.Count > 0 ? InstallationLayoutDisposition.Deferred :
                    IgnoreCase.Equals(original, preview.DestinationPath) ? InstallationLayoutDisposition.Unchanged : InstallationLayoutDisposition.Move, reasons);
            }
            foreach (var result in results.Where(r => r.Disposition == InstallationLayoutDisposition.Move || r.Disposition == InstallationLayoutDisposition.Unchanged).ToArray())
                if (results.Any(other => !ReferenceEquals(result, other) && other.GroupFolderName.Length > 0 && Overlap(result.DestinationPath, other.DestinationPath)))
                    Finish(result, InstallationLayoutDisposition.Deferred, result.Reasons.Concat(new[] { "DestinationCollision" }));
            return new InstallationLayoutPlan(results.AsReadOnly(), errors.Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal).ToArray());
        }
        private static void Finish(InstallationLayoutPreview preview, InstallationLayoutDisposition disposition, IEnumerable<string> reasons)
        { preview.Disposition = disposition; preview.Reasons = Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToArray()); }
        private static string Normalize(string? path) { return (path ?? "").Replace((char)92, '/'); }
        private static string Leaf(string path) { return path.Substring(path.LastIndexOf('/') + 1); }
        private static bool Within(string path, string parent) { return IgnoreCase.Equals(path, parent) || path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase); }
        private static bool Overlap(string a, string b) { return Within(a, b) || Within(b, a); }
        private static bool ValidId(string? id) { return !string.IsNullOrWhiteSpace(id) && id!.Length <= 256 && !id.Any(char.IsControl) && id == id.Trim(); }
        private static bool TooLong(InstallationLayoutInput input, string relative)
        { return input.BepInExAbsolutePath.TrimEnd((char)92, '/').Length + 1L + relative.Length > input.MaximumFullPathLength; }
        private static bool ValidRoot(string? path)
        {
            if (string.IsNullOrEmpty(path) || path!.Length > MaxText) return false;
            string p = Normalize(path).TrimEnd('/');
            return p.Length > 3 && ((p[0] >= 'A' && p[0] <= 'Z') || (p[0] >= 'a' && p[0] <= 'z')) && p[1] == ':' && p[2] == '/' && ValidRelative(p.Substring(3));
        }
        private static bool ValidRelative(string? path)
        {
            if (string.IsNullOrEmpty(path) || path!.Length > MaxText) return false;
            return Normalize(path).Split('/').All(ValidSegment);
        }
        private static bool ValidSegment(string? segment)
        {
            if (string.IsNullOrEmpty(segment) || segment!.Length > 255 || segment == "." || segment == ".." || segment.EndsWith(" ", StringComparison.Ordinal) || segment.EndsWith(".", StringComparison.Ordinal)) return false;
            if (segment.Any(c => c < 32 || c == 127 || c == 92 || "<>:\"/|?*".IndexOf(c) >= 0)) return false;
            string stem = segment.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CONIN$" || stem == "CONOUT$") return false;
            if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".IndexOf(stem[3]) >= 0) return false;
            return true;
        }
        /// <summary>The readable folder label a name yields, or empty when it cannot be a usable path segment.
        /// Callers prefer this over the collision hash so a folder people move is the name they recognize.</summary>
        public static string Label(string? name)
        {
            var text = new StringBuilder();
            foreach (char c in (name ?? "").Take(MaxText))
            {
                if (text.Length >= 60) break;
                text.Append(c < 32 || c == 127 || c == 92 || "<>:\"/|?*".IndexOf(c) >= 0 ? '_' : c);
            }
            string label = text.ToString().Trim().TrimEnd('.', ' ');
            if (label.Length > 0 && char.IsHighSurrogate(label[label.Length - 1])) label = label.Substring(0, label.Length - 1);
            return label.Length > 0 && ValidSegment(label) ? label : "";
        }
        private static string Folder(string stable, string name, string id)
        {
            if (!string.IsNullOrEmpty(stable)) return stable;
            var text = new StringBuilder();
            foreach (char c in (name ?? "").Take(MaxText))
            {
                if (text.Length >= 60) break;
                text.Append(c < 32 || c == 127 || c == 92 || "<>:\"/|?*".IndexOf(c) >= 0 ? '_' : c);
            }
            string label = text.ToString().Trim().TrimEnd('.', ' ');
            if (label.Length > 0 && char.IsHighSurrogate(label[label.Length - 1])) label = label.Substring(0, label.Length - 1);
            if (label.Length == 0) label = "unnamed";
            if (!ValidSegment(label)) label = "_" + label;
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((id ?? "").ToUpperInvariant()));
                var suffix = new StringBuilder();
                for (int i = 0; i < 8; i++) suffix.Append(bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return label + "--" + suffix;
            }
        }
    }
}
