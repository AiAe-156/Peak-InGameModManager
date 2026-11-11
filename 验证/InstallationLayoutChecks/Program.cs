using ModConfigEnhance.Updating;
using D = ModConfigEnhance.Updating.InstallationLayoutDisposition;
using M = ModConfigEnhance.Updating.InstallationLayoutMode;
using F = ModConfigEnhance.Updating.LayoutFact;

static class Program
{
    static int count;
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); count++; Console.WriteLine("PASS " + name); }
    static InstallationLayoutInput Input()
    {
        var input = new InstallationLayoutInput { Mode = M.GroupAndTranslatedOuterFolder, BepInExAbsolutePath = @"F:\Sandbox\BepInEx", InventoryComplete = true };
        input.Groups.Add(new InstallationLayoutGroup { Id = "gameplay", Name = "玩法" });
        input.Units.Add(Unit("author-package", "plugins/Original"));
        return input;
    }
    static InstallationLayoutUnit Unit(string id, string path)
    {
        var u = new InstallationLayoutUnit { Id = id, OriginalPath = path, OriginalName = "Original", TranslatedName = "译名", OwnershipComplete = true, FileInventoryComplete = true, HasLinks = F.No, IsLocked = F.No, IsShared = F.No };
        u.Plugins.Add(new InstallationLayoutPlugin { Id = id + ".plugin", GroupId = "gameplay", IsLoaded = F.No });
        var file = new InstallationLayoutFile { RelativePath = @"internal\Actual.dll", HasLinks = F.No };
        file.OwnerUnitIds.Add(id); u.Files.Add(file);
        return u;
    }
    static InstallationLayoutPreview First(InstallationLayoutInput i) => InstallationLayoutPlanner.Plan(i).Units[0];
    /// <summary>Informational facts (2.2.13): the folder still moves as one; the code is only shown.</summary>
    static void Moved(Action<InstallationLayoutInput> mutate, string note)
    { var i = Input(); mutate(i); var p = First(i); Check(p.Disposition == D.Move && p.Notes.Contains(note) && p.Reasons.Count == 0, "moves with note " + note); }
    static void Deferred(Action<InstallationLayoutInput> mutate, string reason)
    { var i = Input(); mutate(i); var p = First(i); Check(p.Disposition == D.Deferred && p.Reasons.Contains(reason), reason); }
    static void Main()
    {
        var i = Input(); var p = First(i);
        Check(p.Disposition == D.Move && p.DestinationPath.StartsWith("plugins/玩法--"), "translated move");
        Check(p.Files[0].DestinationPath.EndsWith(@"/internal\Actual.dll"), "internal spelling preserved");
        Check(p.Files[0].OriginalPath == @"plugins/Original/internal\Actual.dll", "original preview");
        Check(i.Units[0].OriginalPath == "plugins/Original" && i.Groups[0].StableFolderName == "", "no mutation");
        Check(First(i).DestinationPath == p.DestinationPath, "repeat deterministic");
        i.Mode = M.GroupOnly; Check(First(i).DestinationPath.EndsWith("/Original"), "group only preserves outer");
        i.Mode = M.Disabled; Check(First(i).Disposition == D.Disabled && First(i).DestinationPath == "plugins/Original", "disabled no moves");
        i = Input(); i.Units[0].OriginalPath = p.DestinationPath; Check(First(i).Disposition == D.Unchanged, "idempotent");
        i = Input(); i.Groups[0].StableFolderName = p.GroupFolderName; i.Units[0].StableOuterFolderName = p.OuterFolderName;
        i.Groups[0].Name = "renamed"; i.Units[0].TranslatedName = "new language";
        Check(First(i).DestinationPath == p.DestinationPath, "persisted folders freeze names");
        i = Input(); i.Units[0].TranslatedName = ""; Check(First(i).OuterFolderName.StartsWith("Original--"), "missing translation fallback");
        i = Input(); i.Units[0].Plugins.Add(new InstallationLayoutPlugin { Id = "second", GroupId = "gameplay", IsLoaded = F.No });
        Check(First(i).Disposition == D.Move && InstallationLayoutPlanner.Plan(i).Units.Count == 1, "multi plugin atomic");
        Deferred(x => x.Units[0].Plugins.Add(new InstallationLayoutPlugin { Id = "second", GroupId = "other", IsLoaded = F.No }), "MissingOrConflictingGroup");
        Deferred(x => x.Units[0].Plugins[0].IsLoaded = F.Yes, "LoadedOrUnknownPlugins");
        Deferred(x => x.Units[0].Plugins[0].IsLoaded = F.Unknown, "LoadedOrUnknownPlugins");
        Deferred(x => x.Units[0].IsLocked = F.Yes, "PackageLocked");
        Deferred(x => x.Units[0].IsLocked = F.Unknown, "LockFactsUnknown");
        Deferred(x => x.Units[0].IsShared = F.Yes, "SharedPackage");
        Deferred(x => x.Units[0].IsShared = F.Unknown, "SharingFactsUnknown");
        Deferred(x => x.Units[0].OwnershipComplete = false, "OwnershipOrFilesIncomplete");
        Deferred(x => x.Units[0].FileInventoryComplete = false, "OwnershipOrFilesIncomplete");
        Deferred(x => x.InventoryComplete = false, "InventoryIncomplete");
        Deferred(x => x.Units[0].HasLinks = F.Unknown, "LinkFactsUnknown");
        Deferred(x => x.Units[0].Files[0].OwnerUnitIds.Add("other"), "SharedOrUnknownFileOwner");
        Deferred(x => x.Units[0].Files[0].OwnerUnitIds.Clear(), "SharedOrUnknownFileOwner");
        Deferred(x => x.Units[0].IsCoreOrPatcher = true, "CoreOrPatcherExcluded");
        Moved(x => x.Units[0].IsProtectedManager = true, "ManagerMovedLast");
        Moved(x => x.Units[0].Plugins[0].Id = "com.aiae.modconfigenhance", "ManagerMovedLast");
        Moved(x => x.Units[0].Plugins.Add(new InstallationLayoutPlugin { Id = x.Units[0].Plugins[0].Id, GroupId = "gameplay", IsLoaded = F.No }), "DuplicatePluginId");
        Moved(x => x.Units[0].Plugins.Add(new InstallationLayoutPlugin { Id = "bundled", GroupId = "other", IsLoaded = F.No, Depth = 2 }), "BundledOtherGroup");
        Deferred(x => x.MaximumFullPathLength = 64, "PathLengthExceeded");
        Deferred(x => x.Units.Add(Unit("other", "plugins/Original/Child")), "OverlappingPackageRoots");
        Moved(x => { var other = Unit("other", "plugins/Other"); other.Plugins[0].Id = x.Units[0].Plugins[0].Id; x.Units.Add(other); }, "PluginOwnedByMultiplePackages");
        foreach (string path in new[] { "../Escape", "/absolute", @"C:\absolute", @"\host\share", "plugins/../Bad", "plugins/A.", "plugins/A ", "plugins/CON", "plugins/aux.dll", "plugins/COM¹", "plugins/A:ads", "plugins/a//b", "core/Package", "patchers/Package", "plugins/nu\0l" })
        { i = Input(); i.Units[0].OriginalPath = path; Check(First(i).Disposition == D.Rejected, "reject source " + path); }
        foreach (string path in new[] { "../evil.dll", @"a\..\evil.dll", "/evil.dll", "C:evil.dll", "NUL.dll", "folder./a", "a?/b" })
        { i = Input(); i.Units[0].Files[0].RelativePath = path; Check(First(i).Disposition == D.Rejected, "reject internal " + path); }
        foreach (string name in new[] { "CON", "PRN.txt", "AUX", "NUL", "COM1", "LPT9", "COM²", "CONIN$", "bad<>:\"/|?*", "... ", "   ", new string('a', 400) })
        { i = Input(); i.Units[0].TranslatedName = name; var folder = First(i).OuterFolderName; Check(First(i).Disposition == D.Move && folder.Length < 100 && !folder.Contains('/') && !folder.EndsWith('.') && !folder.EndsWith(' '), "sanitize " + name.Substring(0, Math.Min(20, name.Length))); }
        Deferred(x => x.Units[0].StableOuterFolderName = "CON", "InvalidStableFolderName");
        Deferred(x => x.Groups[0].StableFolderName = "../escape", "InvalidStableFolderName");
        foreach (bool fileLink in new[] { false, true })
        { i = Input(); if (fileLink) i.Units[0].Files[0].HasLinks = F.Yes; else i.Units[0].HasLinks = F.Yes; Check(First(i).Disposition == D.Rejected, "reject exposed link " + fileLink); }
        i = Input(); i.Units.Add(Unit("other", "plugins/Other"));
        var plan = InstallationLayoutPlanner.Plan(i); Check(plan.Units.All(u => u.Disposition == D.Move) && plan.Units[0].OuterFolderName != plan.Units[1].OuterFolderName, "same label stable identity suffix");
        var before = string.Join("|", plan.Units.Select(u => u.UnitId + u.DestinationPath)); i.Units.Reverse();
        Check(before == string.Join("|", InstallationLayoutPlanner.Plan(i).Units.Select(u => u.UnitId + u.DestinationPath)), "order independent");
        i = Input(); i.Units[0].StableOuterFolderName = "Same"; var u2 = Unit("other", "plugins/Other"); u2.StableOuterFolderName = "same"; i.Units.Add(u2);
        Check(InstallationLayoutPlanner.Plan(i).Units.All(u => u.Disposition == D.Deferred && u.Reasons.Contains("DestinationCollision")), "case insensitive collision no winner");
        Deferred(x => x.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = First(x).DestinationPath + "/existing.txt", HasLinks = F.No }), "DestinationOccupied");
        Deferred(x => x.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = First(x).DestinationPath, IsDirectory = true, HasLinks = F.No }), "DestinationOccupied");
        Deferred(x => x.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = "plugins", IsDirectory = true, HasLinks = F.Yes }), "InventoryLinkRejected");
        Deferred(x => x.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = "plugins", IsDirectory = true }), "InventoryLinkFactsUnknown");
        i = Input(); i.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = "plugins", IsDirectory = true, HasLinks = F.No }); Check(First(i).Disposition == D.Move, "safe existing ancestor directory");
        i = Input(); i.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = "../escape" }); Check(First(i).Disposition == D.Rejected, "invalid inventory fail closed");
        i = Input(); i.BepInExAbsolutePath = "//?/C:/BepInEx"; Check(First(i).Disposition == D.Rejected, "device root rejected");
        i = Input(); i.Units.Add(Unit("AUTHOR-PACKAGE", "plugins/Other")); Check(InstallationLayoutPlanner.Plan(i).Units.All(u => u.Disposition == D.Rejected), "duplicate identity rejected");
        i = Input(); i.Mode = (M)99; Check(First(i).Disposition == D.Rejected, "invalid mode");
        i = Input(); i.Groups.Add(new InstallationLayoutGroup { Id = "GAMEPLAY" }); Check(First(i).Disposition == D.Rejected, "ambiguous group identity");
        Deferred(x => { x.Groups[0].StableFolderName = "same"; x.Groups.Add(new InstallationLayoutGroup { Id = "other", StableFolderName = "SAME" }); }, "GroupFolderCollision");
        Deferred(x => { var f = new InstallationLayoutFile { RelativePath = "internal/Actual.dll/child", HasLinks = F.No }; f.OwnerUnitIds.Add(x.Units[0].Id); x.Units[0].Files.Add(f); }, "OverlappingInternalFiles");
        Deferred(x => { var f = new InstallationLayoutFile { RelativePath = "INTERNAL/ACTUAL.DLL", HasLinks = F.No }; f.OwnerUnitIds.Add(x.Units[0].Id); x.Units[0].Files.Add(f); }, "DuplicateInternalPath");
        i = Input(); p = First(i); int exact = i.BepInExAbsolutePath.Length + 1 + p.Files[0].DestinationPath.Length; i.MaximumFullPathLength = exact;
        Check(First(i).Disposition == D.Move, "path length exact boundary"); i.MaximumFullPathLength--; Check(First(i).Reasons.Contains("PathLengthExceeded"), "path length boundary exceeded");
        Deferred(x => { x.Groups[0].StableFolderName = "Original"; }, "SourceDestinationOverlap");
        Deferred(x => x.ExistingPaths.Add(new InstallationLayoutExistingPath { Path = "plugins/Original/unowned.txt", HasLinks = F.No }), "UnownedSourceInventoryFile");
        i = Input(); i.Units.Clear(); for (int n = 0; n < 10001; n++) i.Units.Add(Unit("u" + n, "plugins/u" + n));
        Check(InstallationLayoutPlanner.Plan(i).Errors.Contains("InputLimitExceeded"), "bounded unit count");
        Console.WriteLine($"{count} assertions passed");
    }
}
