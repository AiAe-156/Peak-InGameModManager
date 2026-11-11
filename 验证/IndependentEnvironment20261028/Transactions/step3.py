from pathlib import Path
p=Path('Updater/PackQueue.cs');s=p.read_text(encoding='utf-8-sig')
s=s.replace('"preview-adopt" or "confirm-adopt"','"preview-adopt" or "confirm-adopt" or "preview-adopt-environment" or "confirm-adopt-environment" or "preview-upgrade-environment" or "confirm-upgrade-environment"')
s=s.replace('"preview-switch" or "preview-adopt" or "personal-copy"','"preview-switch" or "preview-adopt" or "preview-adopt-environment" or "preview-upgrade-environment" or "personal-copy"')
s=s.replace('"preview-switch" or "preview-adopt" or "confirm-adopt"','"preview-switch" or "preview-adopt" or "confirm-adopt" or "preview-adopt-environment" or "confirm-adopt-environment" or "preview-upgrade-environment" or "confirm-upgrade-environment"')
s=s.replace('task.Request.Operation is "preview-switch" or "preview-adopt")','task.Request.Operation is "preview-switch" or "preview-adopt" or "preview-adopt-environment" or "preview-upgrade-environment")')
s=s.replace('request.Operation is "confirm-import" or "confirm-adopt" ?','request.Operation is "confirm-import" or "confirm-adopt" ?') # complete adopt waits for exit
s=s.replace('    static string CompletedMessage(PackTask task) => task.Request.Operation == "confirm-adopt"','    static string CompletedMessage(PackTask task) => task.Request.Operation == "confirm-upgrade-environment"\n        ? "当前包已明确升级为完整环境；旧 revision 保留，真实文件未被替换。"\n        : task.Request.Operation is "confirm-adopt" or "confirm-adopt-environment"')
p.write_text(s,encoding='utf-8')
p=Path('Updater/PackPersonalCopy.cs');s=p.read_text(encoding='utf-8-sig')
a='        copy.Files = PackStorage.Inventory(content); storage.Validate(copy);'
b='''        copy.Files = PackStorage.Inventory(content);
        if (PackEnvironmentPolicy.IsComplete(original))
        {
            copy.RevisionStateHash = "";
            var ledger = new PackRevisionState(storage).Read(original)!; // original revision, NOT mutable workspace
            PackCompleteEnvironment.ReconcileEditedLedger(ledger, copy);
            new PackRevisionState(storage).Write(copy, ledger);
            Files.Save(PackCompleteEnvironment.LedgerPath(storage, copy.Id), ledger);
        }
        storage.Validate(copy);'''
assert s.count(a)==1;s=s.replace(a,b)
s=s.replace('|| rel.Equals("shop-installed.json",StringComparison.OrdinalIgnoreCase)', '|| !PackEnvironmentPolicy.IsComplete(original) && rel.Equals("shop-installed.json",StringComparison.OrdinalIgnoreCase)')
p.write_text(s,encoding='utf-8')
p=Path('Updater/PackMaintenance.cs');s=p.read_text(encoding='utf-8-sig')
s=s.replace('        if (pack.PendingUpdate != null) { Block(p, "pending-update",','        if (pack.UnsavedEnvironment != null) { Block(p, "unsaved-environment", "此包有未保存运行环境，请先明确保存或放弃后再修改。"); return; }\n        if (pack.PendingUpdate != null) { Block(p, "pending-update",')
s=s.replace('var provided = kept.SelectMany(f => f.Guids).Concat(index.HostPlugins.Select(h => h.Guid)).ToHashSet(Files.Compare);','var provided = kept.SelectMany(f => f.Guids).Concat(PackEnvironmentPolicy.IsComplete(pack) ? Array.Empty<string>() : index.HostPlugins.Select(h => h.Guid)).ToHashSet(Files.Compare);')
s=s.replace('return plan.Id == name && plan.Phase is "installed" or "rolled-back";','return plan.SchemaVersion is 1 or 2 && plan.Id == name && plan.Phase is "installed" or "rolled-back";')
s=s.replace('if (record != null && record.Content.Length > 0) { try { result.Add(Path.GetFullPath(storage.Content(record))); } catch (IOException) { } }','''if (record != null && record.Content.Length > 0)
                {
                    try
                    {
                        result.Add(Path.GetFullPath(storage.Content(record)));
                        if (PackEnvironmentPolicy.IsComplete(record)) result.Add(Path.GetDirectoryName(new PackRevisionState(storage).PathFor(record))!);
                    }
                    catch (IOException) { }
                }''')
s=s.replace('var oldRecord = (Content: pack.Content, Files: pack.Files, Modified: pack.ModifiedUtc, Report: pack.Report);','''var oldRecord = (Content: pack.Content, Files: pack.Files, Modified: pack.ModifiedUtc, Report: pack.Report, StateHash: pack.RevisionStateHash);
        var ledger = new PackRevisionState(storage).Read(pack); // state paired to source revision, never workspace''')
s=s.replace('            storage.Validate(pack);\n            if (storage.Read().Revision != index.Revision)', '''            if (ledger != null)
            {
                pack.RevisionStateHash = "";
                PackCompleteEnvironment.ReconcileEditedLedger(ledger, pack);
                new PackRevisionState(storage).Write(pack, ledger);
            }
            storage.Validate(pack);
            if (storage.Read().Revision != index.Revision)''')
s=s.replace('pack.ModifiedUtc = oldRecord.Modified; pack.Report = oldRecord.Report;','pack.ModifiedUtc = oldRecord.Modified; pack.Report = oldRecord.Report; pack.RevisionStateHash = oldRecord.StateHash;')
# Keep failed immutable paired revision rather than deleting only payload and leaving state orphaned.
s=s.replace('            try { if (Directory.Exists(newContent) && Files.Within(storage.DirectoryFor(pack.Id), newContent)) Directory.Delete(newContent, true); } catch (IOException) { }','            // Failed new payload/state remain unreferenced for diagnosis; never damage the original pair.')
p.write_text(s,encoding='utf-8')
