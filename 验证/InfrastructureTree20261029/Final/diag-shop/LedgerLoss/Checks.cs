using MCE.UpdateWorker;
using ModConfigEnhance.Updating;

internal static class LedgerLossChecks
{
    internal static async Task<int> Run(string fixtures)
    {
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS LedgerLoss: "+message);checks++;}
        async Task<ShopTask> Preview(ShopQueue queue,bool uninstall=false,string version="1.1.0")
        {
            queue.Handle(new(){Operation=uninstall?"preview-uninstall":"preview-install",Namespace="Team",Package="Plain",Version=version});
            await queue.Pump(new());return queue.Read()!;
        }
        async Task<ShopTask> Install(ShopQueue queue,string version)
        {
            var task=await Preview(queue,version:version);
            queue.Handle(new(){Operation="approve-download",PreviewId=task.Id,PlanToken=task.PlanToken});await queue.Pump(new());task=queue.Read()!;
            if(task.State!="ready")throw new Exception(task.Error);
            queue.Handle(new(){Operation="confirm-install",PreviewId=task.Id,PlanToken=task.PlanToken});await queue.Pump(new());return queue.Read()!;
        }
        // Exclude only shop tasks/download caches. Preserve exact bytes/existence of all live payload,
        // index/.lastvalid, saved revisions/states, and workspace (including damaged ledger evidence).
        Dictionary<string,string> Evidence(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories)
            .Where(p=>!Path.GetRelativePath(root,p).Replace('\\','/').StartsWith("MCE/shop/",StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p=>Path.GetRelativePath(root,p),p=>Convert.ToBase64String(File.ReadAllBytes(p)));
        bool Same(Dictionary<string,string> a,Dictionary<string,string> b)=>a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out var value)&&value==p.Value);

        foreach(string damage in new[]{"missing","invalid-json","null","invalid-schema"})
        {
            string root=Path.Combine(fixtures,damage,"BepInEx");Directory.CreateDirectory(root);MceSeed.Add(root);
            var storage=new PackStorage(root);
            var pack=new PackRecord{Id=Guid.NewGuid().ToString("N"),Content="revisions/"+Guid.NewGuid().ToString("N"),EnvironmentVersion=2,MinimumWorkerProtocol=2};
            Directory.CreateDirectory(storage.Content(pack));new PackRevisionState(storage).Write(pack,new());
            string ledgerPath=Path.Combine(storage.Workspace(pack.Id),"shop-installed.json");Files.Save(ledgerPath,new ShopLedger());
            storage.Save(new PackIndex{ActivePackId=pack.Id,Packs=new(){pack}});
            var queue=new ShopQueue(root);
            Check((await Install(queue,"1.0.0")).State=="completed",damage+": install initial 1.0");
            // Save 1.0 as a new immutable revision, then leave the 1.1 upgrade unsaved.
            var index=storage.Read();pack=index.Packs.Single();pack.Content="revisions/"+Guid.NewGuid().ToString("N");pack.RevisionStateHash="";
            foreach(var file in index.ActiveFiles)Files.CopyAtomic(Files.Child(root,file.Path),Files.Child(storage.Content(pack),file.Path));
            pack.Files=PackStorage.Inventory(storage.Content(pack));new PackRevisionState(storage).Write(pack,Files.Read<ShopLedger>(ledgerPath));storage.Save(index);
            Check((await Install(queue,"1.1.0")).State=="completed",damage+": unsaved upgrade 1.1");
            Check(new PackRevisionState(storage).Read(pack)!.Packages.Single().Version=="1.0.0"&&Files.Read<ShopLedger>(ledgerPath).Packages.Single().Version=="1.1.0",damage+": saved/live ownership deliberately differs");
            if(damage=="missing")File.Move(ledgerPath,ledgerPath+".lost-evidence");
            else File.WriteAllText(ledgerPath,damage=="invalid-json"?"{ damaged ledger":damage=="null"?"null":"{\"SchemaVersion\":99,\"Packages\":[]}");
            var before=Evidence(root);
            foreach(bool uninstall in new[]{false,true})
            {
                var task=await Preview(queue,uninstall);
                Check(task.State=="failed"&&task.Error.Contains(damage=="missing"?"workspace ledger missing":"ledger",StringComparison.OrdinalIgnoreCase),damage+": "+(uninstall?"uninstall":"install")+" preview explicitly refuses ledger loss/corruption");
                Check(Same(before,Evidence(root)),damage+": rejected preview preserves live/index/saved-state/workspace bytes and existence");
                Check(!Directory.GetFiles(Path.Combine(root,"MCE/shop"),"transaction.json",SearchOption.AllDirectories).Any(p=>p.Contains(task.Id)),damage+": no write transaction created");
            }
        }
        foreach(bool managed in new[]{false,true})
        {
            string root=Path.Combine(fixtures,managed?"legacy":"unmanaged","BepInEx");Directory.CreateDirectory(root);MceSeed.Add(root);var storage=new PackStorage(root);
            if(managed){var pack=new PackRecord{Id=Guid.NewGuid().ToString("N"),Content="revisions/"+Guid.NewGuid().ToString("N")};Directory.CreateDirectory(storage.Content(pack));storage.Save(new PackIndex{ActivePackId=pack.Id,Packs=new(){pack}});}
            var queue=new ShopQueue(root);string label=managed?"legacy":"unmanaged";
            Check((await Install(queue,"1.0.0")).State=="completed",label+": missing ledger retains explicit empty initial state and ordinary install");
            var task=await Preview(queue,true,"1.0.0");Check(task.State=="uninstall-preview",label+": normal ordinary uninstall preview");
            queue.Handle(new(){Operation="confirm-uninstall",PreviewId=task.Id,PlanToken=task.PlanToken});await queue.Pump(new());
            Check(queue.Read()!.State=="completed",label+": normal uninstall commits");
        }
        return checks;
    }
}
