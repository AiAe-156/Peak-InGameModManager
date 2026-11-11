using System.Text.Json.Nodes;
using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;

// Separate authority from Safe: only explicit files in a complete current-pack batch.
internal sealed partial class ShopQueue
{
    static bool IsCorePackage(ShopPackage p)=>(Files.Compare.Equals(p.ClaimRoot,"core")||p.ClaimRoot.StartsWith("core/",StringComparison.OrdinalIgnoreCase))||p.Files.Any(f=>f.Path.StartsWith("core/",StringComparison.OrdinalIgnoreCase));
    static bool ProtectedHost(ShopPackage p)=>PackageRules.IsLoader(p.Key)||ManagerIdentity.IsManagerPackage(p.Key)||p.Files.Any(f=>Path.GetFileName(f.Path).Equals("BepInEx.dll",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(f.Path).Equals("ModConfigEnhance.dll",StringComparison.OrdinalIgnoreCase))||p.PluginGuids.Contains("com.aiae.modconfigenhance",Files.Compare);
    void RequireContext(ShopTask t)
    {
        var index=storage.Read();
        if (t.Request.AppliedSnapshotPath.Length > 0)
        {
            string snapshot = t.Request.AppliedSnapshotPath; Files.RejectLinks(storage.Root, snapshot);
            if (!File.Exists(snapshot) || !Files.Compare.Equals(Files.Hash(snapshot), t.Request.AppliedSnapshotHash))
                throw new IOException("Applied profile changed; preview folder organization again.");
        }
        if(index.ActivePackId!=t.ActivePackId||!independent && index.Revision!=t.IndexRevision)throw new IOException("Current pack/index revision changed; preview again.");
        if(!independent&&t.PlanToken.Length>0&&Ledger().Revision!=t.LedgerRevision)throw new IOException("Current pack ownership revision changed; preview again.");
    }
    void RequireComplete(ShopTask t)
    {
        // Core package updates use the same exact-file transaction as other packages.
        // A modpack migration is unrelated to updating a verified installed package.
        RequireContext(t);
    }
    void ApproveCoreFile(ShopTask t,PackFile f)
    {
        if (t.Uninstall) RequireContext(t); else RequireComplete(t);
        if(!PackEnvironmentPolicy.TryNormalize(f.Path,out var path)||!path.StartsWith("core/",StringComparison.OrdinalIgnoreCase)||!PackEnvironmentPolicy.ManagedPath(path,2))throw new IOException("Invalid core batch path: "+f.Path);
        if(!CorePayload(path))throw new IOException("Unsupported executable/script in core batch: "+path);
        if(!t.ApprovedCoreFiles.Any(p=>Files.Compare.Equals(p.Path,path)&&p.Hash==f.Hash))t.ApprovedCoreFiles.Add(new(){Path=path,Hash=f.Hash});
    }
    void ApproveCoreOriginals(ShopTask t,IEnumerable<PackFile>? files=null)
    {foreach(var f in (files??t.Originals).Where(f=>f.Path.StartsWith("core/",StringComparison.OrdinalIgnoreCase)))ApproveCoreFile(t,f);}
    bool Allowed(ShopTask t,string path,PackIndex index)
    {
        if(!path.StartsWith("core/",StringComparison.OrdinalIgnoreCase))return Safe(path,index);
        // Complete validated state and exact approved evidence are both mandatory, including recovery.
        var active=index.Packs.SingleOrDefault(p=>p.Id==t.ActivePackId);
        return PackEnvironmentPolicy.ManagedPath(path,2)&&CorePayload(path)&&t.ApprovedCoreFiles.Any(f=>Files.Compare.Equals(f.Path,path)&&t.Originals.Concat(t.Desired).Any(p=>Files.Compare.Equals(p.Path,path)&&p.Hash==f.Hash));
    }
    static string CoreRoute(string path,ShopPackage claim)
    {
        if(!PackEnvironmentPolicy.TryNormalize(path,out var normalized))throw new IOException("Unsafe official archive path: "+path);
        string p=normalized.StartsWith("BepInEx/",StringComparison.OrdinalIgnoreCase)?normalized[8..]:normalized;
        if(p.StartsWith("core/",StringComparison.OrdinalIgnoreCase)||p.StartsWith("patchers/",StringComparison.OrdinalIgnoreCase)||p.StartsWith("config/",StringComparison.OrdinalIgnoreCase))return p;
        string root=claim.ClaimRoot;
        if(root.Length==0){var manifest=claim.Files.FirstOrDefault(f=>f.Path.StartsWith("core/",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(f.Path).Equals("manifest.json",StringComparison.OrdinalIgnoreCase));if(manifest==null)throw new IOException("Core mapping evidence missing: "+claim.Key);root=manifest.Path[..^14];}
        return root.TrimEnd('/')+"/"+p;
    }
    static bool CorePayload(string path)=>!new[]{".bat",".cmd",".ps1",".hta",".wsf",".vbs",".js",".lnk",".url",".exe",".mm.dll"}.Any(e=>path.EndsWith(e,StringComparison.OrdinalIgnoreCase))&&!path.Contains("/monomod/",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(path).Equals("Assembly-CSharp.dll",StringComparison.OrdinalIgnoreCase);
    static bool RuntimeFile(string path)=>new[]{".dll",".exe",".old",".so",".dylib",".mm"}.Any(e=>path.EndsWith(e,StringComparison.OrdinalIgnoreCase));
    async Task VerifyCoreClaims(ShopTask t,StoreClient store,CancellationToken token)
    {
        foreach(var claim in t.VerificationPackages)
        {
            if (t.Uninstall) RequireContext(t); else RequireComplete(t);string zip=Path.Combine(Work(t),"original-"+claim.Key+".zip");
            await store.Download(claim.Url,zip,(_,_,_)=>{},token);claim.ArchiveHash=Files.Hash(zip);
            string extracted=Path.Combine(Work(t),"original-"+claim.Key);PackImports.Extract(zip,extracted);
            var data=JsonNode.Parse(File.ReadAllText(Path.Combine(extracted,"manifest.json")));
            if(data?["name"]?.GetValue<string>()!=claim.Key[(claim.Key.IndexOf('-')+1)..]||data?["version_number"]?.GetValue<string>()!=claim.Version)throw new IOException("Original ZIP identity mismatch: "+claim.Key);
            var dependencies=(data?["dependencies"]?.AsArray()??new JsonArray()).Select(d=>d?.GetValue<string>()??"").OrderBy(d=>d,StringComparer.Ordinal);
            if(!dependencies.SequenceEqual(claim.Dependencies.OrderBy(d=>d,StringComparer.Ordinal)))throw new IOException("Original ZIP dependencies differ from consent metadata: "+claim.Key);
            var evidence=new List<PackFile>();
            foreach(var f in Files.Inventory(extracted))
            {
                string mapped=CoreRoute(f.Key,claim),live=Files.Child(storage.Root,mapped);Files.RejectLinks(storage.Root,live);
                if(!PackEnvironmentPolicy.ManagedPath(mapped,2))throw new IOException("Unsupported original ZIP mapping: "+mapped);
                if(File.Exists(live)&&Files.Hash(live)==f.Value)evidence.Add(new(){Path=mapped,Hash=f.Value});
                else if(RuntimeFile(mapped)||Path.GetFileName(mapped).Equals("manifest.json",StringComparison.OrdinalIgnoreCase))throw new IOException("Original official file missing or modified; preserved: "+mapped);
                else t.Details.Add("Modified ordinary material preserved, not claimed: "+mapped);
            }
            if(!evidence.Any(f=>RuntimeFile(f.Path)))throw new IOException("Official runtime evidence absent: "+claim.Key);
            foreach(var f in Files.Inventory(Files.Child(storage.Root,claim.ClaimRoot)))
                if(RuntimeFile(f.Key)&&!evidence.Any(e=>Files.Compare.Equals(e.Path,claim.ClaimRoot+"/"+f.Key)))throw new IOException("Unknown core runtime file; preserved: "+f.Key);
            claim.Files=evidence;
            foreach(var previous in t.PreviousPackages.Concat(t.Packages).Where(p=>Files.Compare.Equals(p.Key,claim.Key)&&p.Version==claim.Version&&p.VerificationRequired)){previous.Files=evidence;previous.ArchiveHash=claim.ArchiveHash;}
        }
    }
    void RevalidateCoreClaim(ShopPackage p,ShopTask t)
    {
        var matches=Manifests("core").Where(f=>Files.Compare.Equals(ManifestPackage(f).Key,p.Key)).ToArray();
        if(matches.Length!=1||ManifestPackage(matches[0]).Version!=p.Version)throw new IOException("Original core claim identity changed: "+p.Key);
        foreach(var f in p.Files)RequireUnchanged(f);
        if(p.Files.Count>0&&p.ClaimRoot.Length>0)foreach(var file in Files.Inventory(Files.Child(storage.Root,p.ClaimRoot)))
            if(RuntimeFile(file.Key)&&!p.Files.Any(f=>Files.Compare.Equals(f.Path,p.ClaimRoot+"/"+file.Key)))throw new IOException("Unknown core runtime appeared; preserved: "+file.Key);
        if(p.ArchiveHash.Length>0){string zip=Path.Combine(Work(t),"original-"+p.Key+".zip");Files.RejectLinks(Home,zip);if(Files.Hash(zip)!=p.ArchiveHash)throw new IOException("Approved original ZIP changed: "+p.Key);}
    }
    static void RejectSharedClaims(ShopTask t,ShopLedger ledger)
    {
        var owners=ledger.Packages.Where(p=>!t.Packages.Any(q=>Files.Compare.Equals(q.Key,p.Key))).Concat(t.Packages).ToArray();
        foreach(var group in owners.SelectMany(p=>p.Files.Select(f=>(p.Key,File:f))).GroupBy(x=>x.File.Path,Files.Compare))
            if(group.Select(x=>x.File.Hash).Distinct(Files.Compare).Count()>1)throw new IOException("Shared ownership hash conflict; reference preserved: "+group.Key);
    }
}
