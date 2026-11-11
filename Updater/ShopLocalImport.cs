using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed partial class ShopQueue
{
    internal void FailPreparation(Exception error)
    {
        var task = Read();
        if (task == null || task.State is not ("preparing-local" or "preparing-layout")) return;
        task.State = "failed"; task.Error = error.ToString(); task.Message = "Preparation failed. Installed files unchanged."; Save(task);
    }
    // Local identities deliberately do not impersonate a Thunderstore namespace/package.
    internal void HandleLocalImport(LocalImportRequest request)
    {
        var previous = Read();
        if (request.Operation == "confirm")
        {
            if (previous == null || previous.Id != request.PreviewId || previous.PlanToken != request.PlanToken
                || previous.PlanToken != Token(previous) || previous.State != "ready" || previous.Request.Operation != "local-preview")
                throw new IOException("Local import preview expired or changed; analyze again.");
            if (DateTime.UtcNow - DateTime.Parse(previous.CreatedUtc).ToUniversalTime() > TimeSpan.FromMinutes(30))
                throw new IOException("Local import preview expired; analyze again.");
            RequireContext(previous);
            if (previous.Blockers.Count > 0) throw new IOException(string.Join("\n", previous.Blockers));
            foreach (var file in previous.Desired)
            {
                string source = Files.Child(Path.Combine(Work(previous), "content"), file.Path);
                Files.RejectLinks(Home, source);
                if (Files.Hash(source) != file.Hash) throw new IOException("Import staging changed: " + file.Path);
            }
            previous.Request.Operation = "local-confirm";
            previous.Request.SourceProcessId = request.SourceProcessId;
            previous.State = "waiting-exit"; previous.Message = "Local import confirmed. Waiting for PEAK to exit.";
            Save(previous); return;
        }
        if (request.Operation == "cancel")
        {
            if (previous == null || previous.Id != request.PreviewId || previous.Request.Operation != "local-preview" || previous.BlocksWrites)
                throw new IOException("Only an unconfirmed local preview can be cancelled.");
            previous.State = "cancelled"; previous.Message = "Import cancelled; installed files unchanged."; Save(previous); return;
        }
        if (request.Operation != "preview") throw new IOException("Unsupported local import operation.");
        if (previous?.BlocksWrites == true) throw new IOException("Finish the active installation or recovery first.");
        Files.ValidateRoot(storage.Root);
        var index = storage.Read(); ShopLedger ledger; try { ledger = Ledger(); } catch (IOException) { ledger = new ShopLedger(); }
        var task = new ShopTask {
            OperationId = request.OperationId, Id = Guid.NewGuid().ToString("N"), State = "preparing-local", ActivePackId = index.ActivePackId,
            IndexRevision = index.Revision, LedgerRevision = ledger.Revision, CreatedUtc = DateTime.UtcNow.ToString("O"),
            Request = new ShopRequest { Operation = "local-preview", SourceProcessId = request.SourceProcessId },
            Message = "Reading local package without executing its code."
        };
        Save(task); // Invalidate an earlier ready preview before analysis can fail.
        string stageParent = Path.Combine(Work(task), "source");
        var plan = ModImportPreview.Prepare(new ModImportRequest {
            SourcePath = request.SourcePath, PackageFolder = request.PackageFolder, StagingParent = stageParent
        });
        task.Blockers.AddRange(plan.Issues.Where(i => i.Blocking && i.Code != "hard-dependency").Select(i => i.Message + " " + i.Path));
        task.Details.Add("Source: local. Store identity is unverified; no automatic store updates were enabled.");
        task.Details.Add("Original source: " + plan.OriginalSource);
        foreach (var plugin in plan.Plugins)
        {
            task.Details.Add(plugin.Name + " · " + plugin.Version + " · " + plugin.Guid);
            foreach (var dependency in plugin.Dependencies)
                task.Details.Add((dependency.Soft ? "Optional: " : "Required: ") + dependency.Guid + " " + dependency.MinimumVersion);
        }
        var package = new ShopPackage {
            Key = "local:" + task.Id, Version = "0.0.0", ClaimRoot = "plugins/" + plan.PackageFolder,
            PluginGuids = plan.Plugins.Select(p => p.Guid).Distinct(Files.Compare).ToList()
        };
        task.Packages.Add(package);
        if (plan.StagingDirectory.Length == 0 || plan.Files.Count == 0)
        { task.State = "ready"; task.PlanToken = Token(task); Save(task); return; }
        foreach (string area in plan.Files.Select(f => f.Area).Distinct(Files.Compare).Where(a => a != "config"))
        {
            string folder = Files.Child(storage.Root, area + "/" + plan.PackageFolder); Files.RejectLinks(storage.Root, folder);
            if (Directory.Exists(folder) || File.Exists(folder)) task.Details.Add("Destination folder already exists; files inside it are backed up before being replaced: " + folder);
        }
        var areas = plan.Files.Select(f => f.Area).Where(a => a != "config").Distinct(Files.Compare).ToArray();
        package.ClaimRoot = areas.Length == 1 ? areas[0] + "/" + plan.PackageFolder : "";
        var stagingIssues = ModImportPreview.Revalidate(plan);
        if (stagingIssues.Count > 0) throw new IOException(string.Join("\n", stagingIssues.Select(i => i.Message)));
        foreach (var file in plan.Files)
        {
            var entry = new PackFile { Path = file.TargetRelativePath, Hash = file.Sha256.ToUpperInvariant() };
            string target = Files.Child(storage.Root, entry.Path); Files.RejectLinks(storage.Root, target);
            if (entry.Path.StartsWith("config/", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            { task.Details.Add("Existing configuration retained: " + entry.Path); continue; }
            try
            {
                if (entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Executable files are not supported by local mod import: " + entry.Path);
                if (entry.Path.StartsWith("core/", StringComparison.OrdinalIgnoreCase)) ApproveCoreFile(task, entry);
                else if (!Safe(entry.Path, index)) throw new IOException("Unsupported or protected content: " + entry.Path);
                if (Path.GetFileName(entry.Path).Equals("ModConfigEnhance.dll", StringComparison.OrdinalIgnoreCase)
                    || package.PluginGuids.Contains("com.aiae.modconfigenhance", Files.Compare))
                    throw new IOException("Use the dedicated manager update flow.");
                if (File.Exists(target)) task.Details.Add("Existing file will be backed up and replaced: " + entry.Path);
                string staged = Files.Child(Path.Combine(Work(task), "content"), entry.Path);
                Files.CopyAtomic(Files.Child(plan.StagingDirectory, file.StagedRelativePath), staged);
                if (!Files.Compare.Equals(Files.Hash(staged), file.Sha256)) throw new IOException("Import source changed while staging.");
                task.Desired.Add(entry); package.Files.Add(entry);
            }
            catch (IOException error) { task.Blockers.Add(error.Message); }
        }
        // The final environment check, rather than missing-installed facts in a source-only preview,
        // is authoritative for existing GUIDs and hard dependencies.
        if (package.Files.Count == 0) task.Blockers.Add("No installable payload remains.");
        var finalPlugins = ShopDependencyValidation.Analyze(storage.Root, Path.Combine(Work(task), "content"), task.Originals, task.Desired);
        task.Blockers.AddRange(finalPlugins.Hard);
        foreach (string warning in finalPlugins.Warnings) task.Details.Add("Note: " + warning);
        task.Blockers = task.Blockers.Distinct().ToList();
        task.State = "ready"; task.Message = "Review local plugin identities, dependencies and exact destinations before confirmation.";
        task.PlanToken = Token(task); Save(task);
    }
    /// <summary>The confirmed import continues as a queue job; the preview task only records the hand-off.</summary>
    internal string ContentDirectory(ShopTask task) => Path.Combine(Work(task), "content");
    internal ShopTask? ReadTask() => Read();
    internal string WorkDirectory(ShopTask task) => Work(task);
    internal void WriteTask(ShopTask task) => Save(task);
    internal string TaskToken(ShopTask task) => Token(task);
    /// <summary>Ledger reads for preview paths never fail a preview: a missing or unreadable ledger just means "nothing registered".</summary>
    internal ShopLedger ReadLedgerTolerant()
    {
        try { return Ledger(); }
        catch (IOException) { return new ShopLedger(); }
    }
    internal void CompleteLayoutHandoff(ShopTask task, string jobId, int count)
    {
        task.State = "completed"; task.Message = "Queued as folder-organization job " + jobId.Substring(0, 8) + " (" + count + " folders); progress is in the update queue."; Save(task);
    }
    internal void CompleteLocalImportHandoff(ShopTask task, string jobId)
    {
        task.State = "completed"; task.Message = "Queued as install job " + jobId.Substring(0, 8) + "; progress is in the update queue."; Save(task);
    }
}
