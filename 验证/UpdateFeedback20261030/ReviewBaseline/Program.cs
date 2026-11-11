// Independent adversarial review of the MCE source/update pipeline.
// Links the four production sources; no production file and no existing assertion is modified.
// Labels: PASS/FAIL = invariant that must hold. "FINDING:" = plan-required behaviour that currently
// deviates (a FAIL there is the reproduction of a defect). "OBSERVED:" = documented trade-off.
using ModConfigEnhance.Overview;
using Newtonsoft.Json;

int pass = 0, fail = 0;
var failures = new List<string>();
void Check(bool ok, string label)
{
    if (ok) { pass++; Console.WriteLine("PASS  " + label); }
    else { fail++; failures.Add(label); Console.WriteLine("FAIL  " + label); }
}
void Note(string text) => Console.WriteLine("NOTE  " + text);

string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../runs/" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
Loc.RootDirectory = Root; Directory.CreateDirectory(Root);
string fixture = Path.Combine(Root, "fixture"); Directory.CreateDirectory(fixture);
string dll = Path.Combine(fixture, "feature.dll"), manifest = Path.Combine(fixture, "manifest.json");
File.WriteAllText(dll, "dll-v1"); File.WriteAllText(manifest, "{\"name\":\"BetterPing\"}");

const string HomeA = "https://github.com/Alice/BetterPing";
ShopListing Listing(string team, string name, string? home = null) => new()
{
    Team = team, Name = name, Homepage = home ?? "https://github.com/" + team + "/" + name, Url = "https://thunderstore.io/c/peak/p/" + team + "/" + name + "/",
    Version = "2.0.0", CategoriesKnown = true, Versions = new() { new() { Version = "1.0.0" }, new() { Version = "2.0.0" } }
};
StorePackage Package(string team, string name, string? home = null) => new()
{
    Author = team, Name = name, Homepage = home ?? "https://github.com/" + team + "/" + name, Version = "2.0.0", Versions = new() { "1.0.0", "2.0.0" }
};
ShopCatalog.Items = new() { Listing("Alice", "BetterPing", HomeA) };

OverviewMod M(string guid, string name = "BetterPing", string author = "Alice") => new()
{
    Id = guid, Guid = guid, RawName = name, PackageName = name, PackageVersion = "1.0.0",
    PackageRoot = fixture, Location = dll, Author = author, AuthorSource = "manifest"
};

// ---------------------------------------------------------------- baseline
var baseline = M("baseline");
await ModSources.Discover(new[] { baseline }, CancellationToken.None);
Check(baseline.SourceConfirmed && baseline.SourceNamespace == "Alice" && baseline.SourceName == "BetterPing" && baseline.SourceKind == ModSourceKind.Store,
    "baseline: exact original name + manifest author confirms the store identity");

// ---------------------------------------------------------------- 1. explicitLocal travels inside a published pack
var bundled = M("bundled");
ModSources.Set(new[] { bundled }, new ModSourceRecord { kind = ModSourceKind.Local, ns = "Alice", name = "BetterPing", author = "Alice", homepage = HomeA });
ModSources.Apply(bundled);
Check(bundled.SourceKind == ModSourceKind.Local && !bundled.SourceConfirmed, "explicit local choice suppresses matching on the authoring machine");

string exportDir = Path.Combine(Root, "export-local");
ModSources.Export(exportDir, new[] { bundled });
var exported = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(Path.Combine(exportDir, "sources.json")))!["bundled"];
Check(exported.kind == ModSourceKind.Local, "export keeps the local kind");
Check(exported.instance.Length == 0 && exported.environment.Length == 0 && exported.installation.Length == 0,
    "export strips installation fingerprint from the local record");
Check(!exported.explicitLocal, "FINDING: export copies explicitLocal=true into the published pack (plan: built-in export must not create that choice)");

// Receiving machine: same GUID, same installation content, store identity present in the catalog.
string importRoot = Path.Combine(Root, "import-machine"); Directory.CreateDirectory(importRoot);
Loc.RootDirectory = importRoot; PublishedPack.Packs = new() { new() { Root = exportDir } }; ModSources.InvalidateRecords();
var received = M("bundled");
ModSources.Apply(received);
Check(!ModSources.ExplicitLocal(received), "FINDING: a published explicitLocal record is honoured on the receiving machine");
Check(!received.SourceConfirmed && received.SourceKind == ModSourceKind.Local,
    "FINDING: the bundled store mod arrives as Local and is never re-verified (plan U3 requires re-verification)");
ModSources.Discover(new[] { received }, CancellationToken.None).GetAwaiter().GetResult();
Check(!received.SourceConfirmed, "FINDING: even a forced discovery pass leaves the published local record unconfirmed");
string reexport = Path.Combine(Root, "export-second");
ModSources.Export(reexport, new[] { received });
var second = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(Path.Combine(reexport, "sources.json")))!["bundled"];
Check(second.explicitLocal, "OBSERVED: the leaked flag is stable across further export hops");

// The recipient can still override it, but only one mod at a time.
ModSources.Set(new[] { received }, null);
ModSources.Apply(received);
Note("recipient override: confirmed=" + received.SourceConfirmed + " ns=" + received.SourceNamespace + " kind=" + received.SourceKind + " explicitLocal=" + ModSources.ExplicitLocal(received) + " record=" + JsonConvert.SerializeObject(ModSources.Get("bundled")));
Check(!ModSources.ExplicitLocal(received) && received.SourceConfirmed,
    "the recipient can override the bundled local decision with \"restore automatic classification\"");
await ModSources.Discover(new[] { received }, CancellationToken.None);
Check(received.SourceNamespace == "Alice" && received.SourceConfirmed,
    "after the override a discovery pass restores the real store identity");

Loc.RootDirectory = Root; PublishedPack.Packs = new(); ModSources.InvalidateRecords();
string derivedPath = Path.Combine(Root, "runtime", "source-identities.json");

// Counterfactual: the same published record with the flag stripped (the plan-conforming export) re-verifies.
string fixedDir = Path.Combine(Root, "export-local-fixed"); Directory.CreateDirectory(fixedDir);
var fixedMap = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(Path.Combine(exportDir, "sources.json")))!;
fixedMap["bundled"].explicitLocal = false;
File.WriteAllText(Path.Combine(fixedDir, "sources.json"), JsonConvert.SerializeObject(fixedMap));
string fixedRoot = Path.Combine(Root, "import-fixed"); Directory.CreateDirectory(fixedRoot);
Loc.RootDirectory = fixedRoot; PublishedPack.Packs = new() { new() { Root = fixedDir } }; ModSources.InvalidateRecords();
var fixedMod = M("bundled");
await ModSources.Discover(new[] { fixedMod }, CancellationToken.None);
Check(fixedMod.SourceConfirmed && fixedMod.SourceNamespace == "Alice",
    "counterfactual: stripping explicitLocal on export lets the recipient re-verify the identity (the minimal fix)");
Loc.RootDirectory = Root; PublishedPack.Packs = new(); ModSources.InvalidateRecords();

// ---------------------------------------------------------------- 2. stale derived identity blocks a valid match
var stale = M("stale");
string staleToken = ModSources.InstallationToken(stale);
Directory.CreateDirectory(Path.GetDirectoryName(derivedPath)!);
File.WriteAllText(derivedPath, JsonConvert.SerializeObject(new Dictionary<string, ModSourceRecord>
{
    [staleToken] = new() { kind = ModSourceKind.Store, ns = "Ghost", name = "BetterPing" }
}));
ModSources.InvalidateRecords();
ModSources.Apply(stale);
Check(stale.SourceNamespace == "Ghost", "derived cache is applied for the exact installation fingerprint");
Check(stale.SourceConfirmed, "FINDING: a stale derived identity (package no longer in the catalog) blocks the valid auto-match");
File.Delete(derivedPath); ModSources.InvalidateRecords();
var recovered = M("stale");
await ModSources.Discover(new[] { recovered }, CancellationToken.None);
Check(recovered.SourceConfirmed && recovered.SourceNamespace == "Alice", "control: the same installation confirms once the stale derived entry is gone");
// The stale entry is never pruned by discovery, so it also survives a forced re-match.
File.WriteAllText(derivedPath, JsonConvert.SerializeObject(new Dictionary<string, ModSourceRecord>
{
    [staleToken] = new() { kind = ModSourceKind.Store, ns = "Ghost", name = "BetterPing" }
}));
ModSources.InvalidateRecords();
var forced = M("stale");
await ModSources.Discover(new[] { forced }, CancellationToken.None, true);
Check(forced.SourceConfirmed, "FINDING: a forced re-match still cannot clear the stale derived identity (nothing prunes it)");
File.Delete(derivedPath); ModSources.InvalidateRecords();

// ---------------------------------------------------------------- 3. negative cache never hides a new identity
var miss = M("miss", name: "NoMatch");
await ModSources.Discover(new[] { miss }, CancellationToken.None);
Check(!miss.SourceConfirmed && miss.SourceKind == ModSourceKind.Pending, "unknown original name stays pending");
ShopCatalog.Items = ShopCatalog.Items.Concat(new[] { Listing("Alice", "NoMatch") }).ToList();
ShopCatalog.CurrentRevision++;
await ModSources.Discover(new[] { miss }, CancellationToken.None);
Note("miss2: confirmed=" + miss.SourceConfirmed + " ns=" + miss.SourceNamespace + " items=" + ShopCatalog.Items.Count + " rev=" + ShopCatalog.CurrentRevision + " loads=" + ShopCatalog.Loads + " exact=" + (ModSources.Exact(ShopCatalog.Items.Select(p => new StorePackage { Author = p.Team, Name = p.Name, Version = p.Version, Versions = p.Versions.Select(v => v.Version).ToList() }).ToList(), miss) != null));
Check(miss.SourceConfirmed, "negative cache does not hide an identity that appears later in the same catalog revision");

// ---------------------------------------------------------------- 4. cancellation isolation
string before = File.Exists(derivedPath) ? File.ReadAllText(derivedPath) : "";
bool cancelled = false;
ShopCatalog.Loading = () => ModSources.Set(new[] { M("other") }, null);
try { await ModSources.Discover(new[] { M("cancel") }, CancellationToken.None, true); }
catch (OperationCanceledException) { cancelled = true; }
finally { ShopCatalog.Loading = null; }
Check(cancelled, "a user choice made during the catalog await cancels discovery");
Check((File.Exists(derivedPath) ? File.ReadAllText(derivedPath) : "") == before, "cancelled discovery writes no derived identities");

// ---------------------------------------------------------------- 5. group rename during a check
InfrastructurePackages.Items = new();
var g1 = M("g1", name: "NoMatch1"); g1.Rule.groupId = "g";
var g2 = M("g2", name: "NoMatch2"); g2.Rule.groupId = "g";
using var svc = new ThunderstoreUpdateService(new());
Exception? crash = null;
try { await svc.CheckAsync(new[] { g1, g2 }, CancellationToken.None, (done, _) => { if (done == 1) g2.Rule.groupId = "renamed"; }); }
catch (Exception e) { crash = e; }
Check(crash == null, "FINDING: renaming a group while a check runs aborts the round with " + (crash?.GetType().Name ?? "no error") + " (" + (crash?.Message ?? "") + ")");
Note("crash report: error=" + JsonConvert.SerializeObject(svc.LastReport.Error) + " completed=" + svc.LastReport.Completed + "/" + svc.LastReport.ModCount + " groups=" + string.Join(",", svc.LastReport.Groups.Select(g => g.Key + "=" + g.Value[0] + "/" + g.Value[1])));
Check(svc.LastReport.Error.Length == 0 && svc.LastReport.Completed == svc.LastReport.ModCount,
    "FINDING: progress accounting survives a concurrent group edit");

// ---------------------------------------------------------------- 6. summary wording on a global failure
ShopCatalog.Error = new IOException("offline");
using var svc2 = new ThunderstoreUpdateService(new());
try { await svc2.CheckAsync(new[] { M("f1"), M("f2") }, CancellationToken.None); } catch (IOException) { }
finally { ShopCatalog.Error = null; }
string summary = svc2.LastReport.Summary;
Note("global-failure summary = " + JsonConvert.SerializeObject(summary));
Check(summary == "Checked 0/2 mods; 0 mods have updates.\noffline",
    "FINDING: a global failure still reports the completed-round wording instead of a failure status");

// ---------------------------------------------------------------- 7. cached-category pass rewrites the finished message
var unconfirmed = M("unconfirmed", name: "NoMatch3");
using var svc3 = new ThunderstoreUpdateService(new());
await svc3.CheckAsync(new[] { unconfirmed }, CancellationToken.None);
Check(unconfirmed.CheckMessage == "Store source needs confirmation.", "check reports the pending-source message");
svc3.ApplyCachedCategories(new[] { unconfirmed });
Check(unconfirmed.CheckMessage == "Waiting for a confirmed update source",
    "FINDING: the cached-category pass replaces the finished check message with the pre-check wording");

// ---------------------------------------------------------------- 8. shared identity accounting
var s1 = M("s1"); var s2 = M("s2");
await ModSources.Discover(new[] { s1, s2 }, CancellationToken.None);
Note("pre-shared: s1=" + s1.SourceConfirmed + "/" + s1.SourceNamespace + "/" + s1.SourceName + " s2=" + s2.SourceConfirmed + "/" + s2.SourceNamespace + "/" + s2.SourceName + " items=" + ShopCatalog.Items.Count);
using var svc4 = new ThunderstoreUpdateService(new());
await svc4.CheckAsync(new[] { s1, s2 }, CancellationToken.None);
Note("shared: pkgs=" + svc4.LastReport.PackageCount + " K=" + svc4.LastReport.UpdateCount + " s1=" + s1.SourceKey + "/" + s1.HasUpdate + "/" + s1.CheckMessage + " s2=" + s2.SourceKey + "/" + s2.HasUpdate + "/" + s2.CheckMessage + " err=" + svc4.LastReport.Error);
Check(svc4.LastReport.PackageCount == 1 && svc4.LastReport.UpdateCount == 2 && s1.HasUpdate && s2.HasUpdate,
    "shared store identity: one package queried, two green dots, K == 2");
Check(ShopNetwork.Requests == 0, "catalog metadata alone needs no HTTP request");
Check(svc4.LastReport.Groups.TryGetValue("", out var ungrouped) && ungrouped[0] == 2 && ungrouped[1] == 2, "ungrouped totals match the entry count");

// ---------------------------------------------------------------- 9. confirming an absent identity
bool rejected = false;
try { ModSources.Confirm(M("ghostmod"), new StorePackage { Author = "Ghost", Name = "BetterPing", Version = "1.0.0", Versions = new() { "1.0.0" } }); }
catch (InvalidDataException) { rejected = true; }
Check(rejected, "confirming an identity that is absent from the catalog is rejected");

// ---------------------------------------------------------------- 10. user mapping whose manifest author differs from the store team
ShopCatalog.Items = ShopCatalog.Items.Concat(new[] { Listing("TeamX", "BetterPing") }).ToList();
var teamMod = M("teammod", author: "Alice");
ModSources.Confirm(teamMod, Package("TeamX", "BetterPing"));
ModSources.Apply(teamMod);
Check(teamMod.SourceConfirmed && teamMod.SourceNamespace == "TeamX", "user mapping to a different store team is honoured locally");
string teamExport = Path.Combine(Root, "export-team");
ModSources.Export(teamExport, new[] { teamMod });
var teamRecord = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(Path.Combine(teamExport, "sources.json")))!["teammod"];
Check(teamRecord.note != "user" && teamRecord.ns == "TeamX", "export keeps the identity but never transports user authority");
string teamRoot = Path.Combine(Root, "import-team"); Directory.CreateDirectory(teamRoot);
Loc.RootDirectory = teamRoot; PublishedPack.Packs = new() { new() { Root = teamExport } }; ModSources.InvalidateRecords();
var teamReceived = M("teammod", author: "Alice");
ModSources.Apply(teamReceived);
Check(!teamReceived.SourceConfirmed && teamReceived.SourceNamespace == "TeamX",
    "OBSERVED: after export/import the mapping is pre-filled but Pending because the manifest author differs from the store team");
Loc.RootDirectory = Root; PublishedPack.Packs = new(); ModSources.InvalidateRecords();

// ---------------------------------------------------------------- 10b. "restore automatic classification" after a manual mapping
// The catalog holds both Alice-BetterPing (the manifest's own repository) and TeamX-BetterPing.
var autoMod = M("auto-restore", author: "Alice");
ModSources.Confirm(autoMod, Package("TeamX", "BetterPing"));
ModSources.Apply(autoMod);
Check(autoMod.SourceConfirmed && autoMod.SourceNamespace == "TeamX", "manual mapping to TeamX is active");
await ModSources.Discover(new[] { autoMod }, CancellationToken.None);
Check(autoMod.SourceConfirmed && autoMod.SourceNamespace == "TeamX", "discovery keeps the manual mapping");
string autoToken = ModSources.InstallationToken(autoMod);
var derivedMap = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(derivedPath)) ?? new();
Check(derivedMap.TryGetValue(autoToken, out var leaked) && leaked.ns == "TeamX",
    "FINDING: a user-selected identity is copied into the derived cache (plan: user overrides are stored separately)");

ModSources.Set(new[] { autoMod }, null);            // SourceUi "auto": restore automatic classification
ModSources.Apply(autoMod);
Note("after auto: confirmed=" + autoMod.SourceConfirmed + " ns=" + autoMod.SourceNamespace + " name=" + autoMod.SourceName + " kind=" + autoMod.SourceKind);
Check(autoMod.SourceNamespace == "Alice" && autoMod.SourceConfirmed,
    "FINDING: \"restore automatic classification\" leaves the leaked TeamX identity pinned and Pending instead of re-matching Alice");
await ModSources.Discover(new[] { autoMod }, CancellationToken.None, true);
Check(autoMod.SourceNamespace == "Alice" && autoMod.SourceConfirmed,
    "FINDING: a forced re-match also cannot restore the automatic identity");

var stillLeaked = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(derivedPath)) ?? new();
Check(stillLeaked.ContainsKey(autoToken) && stillLeaked[autoToken].ns == "TeamX",
    "FINDING: nothing prunes the derived entry, so the wrong identity survives every later discovery pass");

// Control: the same mod re-matches automatically once the leaked derived entry is gone.
File.Delete(derivedPath); ModSources.InvalidateRecords();
var autoControl = M("auto-restore", author: "Alice");
await ModSources.Discover(new[] { autoControl }, CancellationToken.None);
Check(autoControl.SourceConfirmed && autoControl.SourceNamespace == "Alice",
    "control: without the leaked derived entry the same mod re-matches Alice automatically");

// ---------------------------------------------------------------- 10d. real-world monorepo veto (PEAKLib family)
// Data taken from the live catalog: six PEAKLib packages share github.com/PEAKModding/PEAKLib.
var libHome = "https://github.com/PEAKModding/PEAKLib";
var libPackages = new[] { "PEAKLib_Core", "PEAKLib_UI", "PEAKLib_Items", "PEAKLib_Stats", "ModConfig" }
    .Select(n => Package("PEAKModding", n, libHome)).ToArray();
var libIndex = new StoreIdentityIndex(libPackages);
var libMod = M("peaklib-core", name: "PEAKLib_Core", author: "PEAKModding");
libMod.HomepageUrl = libHome;
Check(libIndex.Identity("PEAKModding", "PEAKLib_Core") != null, "the PEAKLib identity exists exactly once in the catalog");
Check(libIndex.Resolve(libMod) == null,
    "FINDING: an exact identity with a trusted author is still vetoed because the repository hosts several packages (real install: 4 of 61 mods)");
Check(libIndex.Reason(libPackages[0], libMod).Contains("branches"), "the reason string explains the veto to the user");

// ---------------------------------------------------------------- 11. installation fingerprint coverage
var fp = M("fingerprint");
ModSources.Confirm(fp, Package("Alice", "BetterPing"));
ModSources.Apply(fp);
Check(ModSources.UserChoice(fp, "Alice", "BetterPing"), "mapping binds to the current installation");
File.AppendAllText(dll, "-v2");
var fpChanged = M("fingerprint");
ModSources.Apply(fpChanged);
Check(!ModSources.UserChoice(fpChanged, "Alice", "BetterPing"), "changing the DLL invalidates the mapping");
File.WriteAllText(dll, "dll-v1"); File.WriteAllText(manifest, "{\"name\":\"BetterPing\",\"version_number\":\"1.0.0\"}");
var fpManifest = M("fingerprint");
ModSources.Apply(fpManifest);
Check(!ModSources.UserChoice(fpManifest, "Alice", "BetterPing"), "changing the manifest invalidates the mapping");

// ---------------------------------------------------------------- 12. derived cache is single-context
string multiContext = Path.Combine(Root, "runtime", "source-identities.json");
var contextMap = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(multiContext)) ?? new();
Check(contextMap.Keys.All(k => k.StartsWith(ModSources.Context + "|", StringComparison.Ordinal)),
    "derived cache holds only the current context");
PackLibrary.Managed = true; PackLibrary.Workspace = Path.Combine(Root, "pack-b"); PackLibrary.Active = new() { RevisionStateHash = "rev-b" };
await ModSources.Discover(new[] { M("ctx") }, CancellationToken.None);
var afterSwitch = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(multiContext)) ?? new();
Check(afterSwitch.Keys.All(k => k.StartsWith(ModSources.Context + "|", StringComparison.Ordinal)),
    "FINDING: switching pack environment rewrites the derived cache with only the new context, dropping the previous pack's identities");
PackLibrary.Managed = false; PackLibrary.Active = null;

Check(ShopNetwork.Requests == 0, "the whole review ran without a single HTTP request");
Console.WriteLine();
Console.WriteLine($"TOTAL {pass + fail} assertions: {pass} passed, {fail} failed; sandbox {Root}");
foreach (string failure in failures) Console.WriteLine("  FAILED: " + failure);
return fail == 0 ? 0 : 1;
