using BepInEx;
using MCE.UpdateWorker;
using ModConfigEnhance;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
using System.IO.Compression;

internal static class PublicationDefaultsAudit
{
    internal static async Task Run(string project)
    {
        string fixture = Path.Combine(project, "验证", "RealPackRoundTrip", "defaults-audit-" + Guid.NewGuid().ToString("N"));
        Paths.BepInExRootPath = Path.Combine(fixture, "publisher", "BepInEx");
        Paths.PluginPath = Path.Combine(Paths.BepInExRootPath, "plugins"); Paths.ConfigPath = Path.Combine(Paths.BepInExRootPath, "config");
        Loc.RootDirectory = Path.Combine(Paths.ConfigPath, "ModConfigEnhance");
        Directory.CreateDirectory(Paths.PluginPath); Directory.CreateDirectory(Loc.RootDirectory);
        string tools = CompleteAuditFixture.Setup(fixture);
        var repo = new OverviewRepository();
        var profile = new OverviewProfile { id = "default-test", name = "Startup config", directory = Path.Combine(Loc.RootDirectory, "profiles", "test") };
        profile.config.values.Add(new() { pluginGuid = "fixture.startup", configFile = "Startup.cfg", section = "General", key = "Enabled", serializedValue = "true" });
        Directory.CreateDirectory(profile.directory); File.WriteAllText(Path.Combine(profile.directory, "profile.json"), JsonConvert.SerializeObject(profile)); repo.Profiles.Items.Add(profile);
        Directory.CreateDirectory(Paths.ConfigPath); File.WriteAllText(Path.Combine(Paths.ConfigPath, "Extra.cfg"), "[Extra]\nCount = 7\n");
        int checks = 0;
        void Check(bool good, string message) { if (!good) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        Check(WorkerProtocol.ValidateTools(tools).Length == 64, "isolated synthetic Tools manifest validates (not executed)");
        foreach (var mode in new[] { PublishMode.Local, PublishMode.Thunderstore })
        {
            var request = new PublishRequest { Mode = mode, Name = "StartupDefaults", DisplayName = "StartupDefaults", Description = "Isolated config audit", DefaultProfile = profile.id,
                Profiles = new() { profile.id }, IncludeBootstrap = false, Sign = true, SigningKeyPath = Path.Combine(fixture, "keys", mode + ".json"), Icon = Path.Combine(project, "icon.png"), OutputDirectory = Path.Combine(fixture, "output", mode.ToString()), ExtraConfigFiles = new() { "Extra.cfg" } };
            request.InlineRoots.Add(Path.Combine(Paths.PluginPath, "AuditHost"));
            var preview = PackPublisher.PreviewFiles(repo, request, PackPublisher.Inventory(repo));
            Check(preview.Contains("BepInEx/config/Startup.cfg") && preview.Contains("BepInEx/config/Extra.cfg"), mode + " preview includes startup and extra configs");
            string zipPath = await PackPublisher.PublishAsync(repo, request);
            using var zip = ZipFile.OpenRead(zipPath);
            Check((zip.GetEntry(PackPublisher.EnvironmentFile) != null) == (mode == PublishMode.Local), mode + " preserves Local complete / Thunderstore legacy descriptor contract");
            var entry = zip.GetEntry("BepInEx/config/Startup.cfg"); Check(entry != null, mode + " production publication ZIP contains startup config");
            string config; using (var reader = new StreamReader(entry!.Open())) config = reader.ReadToEnd();
            Check(config == "[General]\nEnabled = true\n", mode + " cfg contains selected default values before any main-menu application");
            Check(zip.GetEntry("BepInEx/config/Extra.cfg") != null, mode + " explicit extra config included");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var storage = new PackStorage(Path.Combine(fixture, "recipient-" + mode + "-" + attempt, "BepInEx"));
                if (mode == PublishMode.Thunderstore)
                {
                    Directory.CreateDirectory(Path.Combine(storage.Root, "core"));
                    foreach (string name in new[] { "BepInEx", "BepInEx.Preloader" })
                    {
                        using var loader = Mono.Cecil.AssemblyDefinition.CreateAssembly(new(name, new Version(5, 4, 23, 5)), name, Mono.Cecil.ModuleKind.Dll);
                        loader.Write(Path.Combine(storage.Root, "core", name + ".dll"));
                    }
                }
                if (mode == PublishMode.Local) Check(!Directory.Exists(Path.Combine(storage.Root, "core")) && !Directory.Exists(Path.Combine(storage.Root, "plugins")), "Local recipient has no live core or host fallback");
                var imported = await PackImports.Preview(storage, new PackRequest { Source = zipPath }, new());
                if (imported.Proposed.Problems.Count > 0) Console.WriteLine(string.Join("; ", imported.Proposed.Problems));
                Check(imported.Proposed.Problems.Count == 0 && imported.PendingDownloads.Count == 0, mode + " production import accepts generated ZIP offline");
                PackImports.Commit(storage, imported);
                var stored = storage.Read().Packs.Single();
                storage.Validate(stored);
                Check(mode == PublishMode.Local
                    ? stored.EnvironmentVersion == 2 && new PackRevisionState(storage).Read(stored) != null
                    : stored.EnvironmentVersion == 1 && new PackRevisionState(storage).Read(stored) == null, mode + " retains complete/legacy format and validates revision state");
                Check(File.ReadAllText(Path.Combine(storage.Content(stored), "config", "Startup.cfg")).Replace("\r\n", "\n").TrimEnd() == config.TrimEnd(), mode + " imported cfg retains exact sections/keys/values (importer normalizes line endings)");
                string authorRoot = stored.AuthorRoot;
                string document = stored.Files.First(f => f.Path.EndsWith("/introduction.md", StringComparison.OrdinalIgnoreCase)).Path;
                if (attempt == 1) { stored.AuthorRoot = ""; var index = storage.Read(); index.Packs[0] = stored; storage.Save(index); }
                string drafts = Path.Combine(storage.Home, ".editor-drafts", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(drafts, document))!); File.WriteAllText(Path.Combine(drafts, document), "Personal edited introduction");
                var copy = PackPersonalCopy.Save(storage, new() { PackId = stored.Id, Source = drafts });
                PublishedPack.Verify(Path.Combine(storage.Content(copy), authorRoot), false);
                Check(PublishedPack.Discover(storage.Content(copy)).Single().Valid, mode + " personal copy passes production author-data discovery (empty AuthorRoot=" + (attempt == 1) + ")");
                PublishedPack.Verify(Path.Combine(storage.Content(stored), authorRoot), true);
                Check(File.Exists(Path.Combine(storage.Content(stored), authorRoot, "signature.json")), mode + " original signed publication remains valid");
            }
        }
        Console.WriteLine($"PASS: {checks} production publication/default config checks. Fixtures: {fixture}");
    }
}
