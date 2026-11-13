using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
string patch = "BepInEx/patchers/0.com.github.MonoDetour.BepInEx5.dll";
Check(PublicationFilePolicy.StartupAttachment(patch), "unowned flat patcher is automatically included");
Check(PublicationFilePolicy.StartupAttachment("BepInEx\\MonoMod\\Patch\\file.dll"), "separator/case normalization");
Check(!PublicationFilePolicy.StartupAttachment("BepInEx/plugins/Patch/file.dll"), "ordinary plugins are not startup attachments");
Check(!PublicationFilePolicy.ExcludedStartupAttachment(patch, Array.Empty<string>()), "no exclusions includes the patcher");
Check(PublicationFilePolicy.ExcludedStartupAttachment(patch, new[] { patch.ToUpperInvariant() }), "explicit file exclusion");
Check(PublicationFilePolicy.ExcludedStartupAttachment("BepInEx/patchers/Pack/sub/file.dll", new[] { "BepInEx\\patchers\\Pack\\" }), "directory exclusion includes descendants");
Check(!PublicationFilePolicy.ExcludedStartupAttachment("BepInEx/patchers/Package/file.dll", new[] { "BepInEx/patchers/Pack" }), "directory prefix does not exclude neighboring package");
Check(!PublicationFilePolicy.ExcludedStartupAttachment("BepInEx/plugins/Pack/file.dll", new[] { "BepInEx/plugins/Pack" }), "exclusions cannot silently remove ordinary plugins");
Check(PublicationFilePolicy.ExcludedStartupAttachment("BepInEx/monomod/Pack/file.dll", new[] { "BepInEx/monomod/Pack" }), "monomod exclusion");
Check(PublicationFilePolicy.Keep(patch), "flat MonoDetour patcher passes existing export filter");
Check(PackEnvironmentPolicy.ManagedPath("monomod/Pack/file.dll", 2), "complete package accepts monomod payload");
Check(!PackEnvironmentPolicy.ManagedPath("monomod/Pack/file.dll", 1), "legacy package scope remains unchanged");
foreach (string invalid in new[] { "monomod/../core/evil.dll", "monomod2/file.dll", "monomod/file.dll:ads", "monomod//file.dll" })
    Check(!PackEnvironmentPolicy.ManagedPath(invalid, 2), "invalid path rejected: " + invalid);
Console.WriteLine($"PASS {checks} startup publication checks (production file/path policies)");
