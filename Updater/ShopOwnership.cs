using System.Text.Json.Nodes;
using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;

internal sealed partial class ShopQueue
{
    IEnumerable<string> Manifests(string area)
    {
        string root=Path.Combine(storage.Root,area);
        return PackMaintenance.Walk(storage.Root,root).Where(f=>Path.GetFileName(f.Rel).Equals("manifest.json",StringComparison.OrdinalIgnoreCase)).Select(f=>Files.Child(root,f.Rel));
    }
    IEnumerable<string> MutableManifests()=>Manifests("plugins").Concat(Manifests("patchers"));
    static ShopPackage ManifestPackage(string file)
    {
        var data=JsonNode.Parse(File.ReadAllText(file))??throw new IOException("Invalid manifest: "+file);
        string name=data["name"]?.GetValue<string>()??"",ns=data["namespace"]?.GetValue<string>()??data["author"]?.GetValue<string>()??"";
        string folder=Path.GetFileName(Path.GetDirectoryName(file)!);
        if(ns.Length==0&&folder.EndsWith("-"+name,StringComparison.OrdinalIgnoreCase))ns=folder[..^(name.Length+1)];
        if(!Part(ns)||!Part(name)||!Version.TryParse(data["version_number"]?.GetValue<string>(),out _))throw new IOException("Unverifiable manifest: "+file);
        string key=ns+"-"+name;
        if(data["FullName"] is JsonNode full&&!Files.Compare.Equals(full.GetValue<string>(),key))throw new IOException("Conflicting manifest identity: "+file);
        return new ShopPackage{Key=key,Version=data["version_number"]!.GetValue<string>(),Dependencies=(data["dependencies"]?.AsArray()??new JsonArray()).Select(d=>d!.GetValue<string>()).ToList()};
    }
    List<ShopPackage> CoreManifests()=>Manifests("core").Select(ManifestPackage).ToList();
    bool TryCore(string key,string minimum,ShopTask task)
    {
        var matches=CoreManifests().Where(p=>Files.Compare.Equals(p.Key,key)).ToList();
        if(matches.Count==0)return false;
        var core=matches[0];
        task.CoreRequirements.Add(new ShopPackage{Key=key,Version=minimum});
        string requirement="Core dependency "+key+"; local "+string.Join(" / ",matches.Select(p=>p.Version))+"; minimum required "+minimum;
        if(matches.Count!=1||Version.Parse(core.Version)<Version.Parse(minimum))task.Blockers.Add(requirement+". Blocked: MCE never downloads or writes core; install the required core separately.");
        else task.Details.Add(requirement+". Existing higher/equal core reused without byte comparison or download.");
        return true;
    }
    ShopPackage? ClaimExternal(string key,string minimum,bool allowDisabled)
    {
        var all=MutableManifests().Concat(Manifests("core")).ToList();
        var candidates=all.Where(f=>{
            try{return Files.Compare.Equals(ManifestPackage(f).Key,key);}
            catch{return Files.Compare.Equals(Path.GetFileName(Path.GetDirectoryName(f)!),key);}
        }).ToList();
        if(candidates.Count==0)return null;
        if(candidates.Count!=1)throw new IOException("Duplicate package identity; ownership refused: "+key);
        string manifest=candidates[0],directory=Path.GetDirectoryName(manifest)!;
        Files.RejectLinks(storage.Root,directory);
        string rel=Path.GetRelativePath(storage.Root,directory).Replace('\\','/');
        if(rel.StartsWith("core/",StringComparison.OrdinalIgnoreCase)||Files.Compare.Equals(rel,"core"))
        {
            var core=ManifestPackage(manifest);
            if(!Files.Compare.Equals(core.Key,key))throw new IOException("Conflicting core identity: "+key);
            core.External=true;core.VerificationRequired=true;core.ClaimRoot=rel;
            return core; // Deliberately no directory inventory ownership before official ZIP consent.
        }
        // A claim must be a standalone immediate child, never root, a collection, or a nested shared tree.
        if(rel.Split('/').Length!=2||!(rel.StartsWith("plugins/",StringComparison.OrdinalIgnoreCase)||rel.StartsWith("patchers/",StringComparison.OrdinalIgnoreCase)))throw new IOException("Independent package directory required: "+key);
        var inventory=Files.Inventory(directory);
        if(inventory.Keys.Count(p=>Path.GetFileName(p).Equals("manifest.json",StringComparison.OrdinalIgnoreCase))!=1)throw new IOException("Shared/multiple-manifest directory refused: "+directory);
        var package=ManifestPackage(manifest);
        if(!Files.Compare.Equals(package.Key,key))throw new IOException("Unknown package ownership: "+directory);
        if(Version.Parse(package.Version)<Version.Parse(minimum))return null;
        var dlls=inventory.Keys.Where(Packages.IsAssembly).ToList();
        if(!allowDisabled&&dlls.Any(p=>!p.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)))throw new IOException("Required dependency is disabled: "+key);
        package.External=true;package.Reuse=true;
        package.Files=inventory.Select(f=>new PackFile{Path=rel+"/"+f.Key.Replace('\\','/'),Hash=f.Value}).ToList();
        package.PluginGuids=dlls.SelectMany(p=>Packages.ReadPlugins(Files.Child(directory,p))).Select(p=>p.Guid).Distinct(Files.Compare).ToList();
        if(Ledger().Packages.Where(p=>!Files.Compare.Equals(p.Key,key)).Any(p=>p.Files.Any(f=>package.Files.Any(q=>Files.Compare.Equals(q.Path,f.Path)))))throw new IOException("Shared ledger ownership refused: "+key);
        return package;
    }
    void RevalidateClaims(ShopTask task)
    {
        foreach(var previous in task.PreviousPackages.Concat(task.Packages).Where(p=>p.External))
        {
            if(previous.VerificationRequired){RevalidateCoreClaim(previous,task);continue;}
            var current=ClaimExternal(previous.Key,"0.0.0",true)??throw new IOException("Claim disappeared: "+previous.Key);
            if(current.Version!=previous.Version||current.Files.Count!=previous.Files.Count||current.Files.Any(f=>!previous.Files.Any(p=>Files.Compare.Equals(p.Path,f.Path)&&p.Hash==f.Hash)))throw new IOException("Claim changed since preview; no files changed: "+previous.Key);
        }
        foreach(var p in task.PreviousPackages)foreach(var f in p.Files)RequireUnchanged(f);
        foreach(var p in task.Packages.Where(p=>!task.Uninstall&&task.State!="uninstall-preview"&&task.Request.Operation!="confirm-uninstall"&&!p.Reuse&&p.ArchiveHash.Length>0)){
            string zip=Path.Combine(Work(task),p.Key+".zip");Files.RejectLinks(Home,zip);if(Files.Hash(zip)!=p.ArchiveHash)throw new IOException("Approved target ZIP changed: "+p.Key);
        }
        foreach(var requirement in task.CoreRequirements)
        {
            var matches=CoreManifests().Where(p=>Files.Compare.Equals(p.Key,requirement.Key)).ToList();
            if(matches.Count!=1||Version.Parse(matches[0].Version)<Version.Parse(requirement.Version))throw new IOException("Core dependency "+requirement.Key+"; local "+(matches.Count==0?"missing":string.Join(" / ",matches.Select(p=>p.Version)))+"; minimum required "+requirement.Version+". Core is never downloaded or written by MCE.");
        }
    }
    List<string> CheckConsumers(ShopTask task,bool uninstall)
    {
        var issues=new List<string>();
        var consumers=Ledger().Packages.Where(p=>!task.Packages.Any(q=>Files.Compare.Equals(q.Key,p.Key))).ToList();
        foreach(string file in MutableManifests().Concat(Manifests("core")))
        {
            ShopPackage p;
            try{p=ManifestPackage(file);}catch(IOException){var data=JsonNode.Parse(File.ReadAllText(file));p=new ShopPackage{Key=file,Dependencies=(data?["dependencies"]?.AsArray()??new JsonArray()).Select(d=>d!.GetValue<string>()).ToList()};}
            if(!task.Packages.Any(q=>Files.Compare.Equals(q.Key,p.Key)))consumers.Add(p);
        }
        foreach(var owner in consumers)foreach(var dep in owner.Dependencies)
        {
            int at=dep.LastIndexOf('-');if(at<1||!Version.TryParse(dep[(at+1)..],out var minimum))throw new IOException("Invalid installed dependency: "+dep);
            var selected=task.Packages.FirstOrDefault(p=>Files.Compare.Equals(p.Key,dep[..at]));
            if(selected!=null&&(uninstall||Version.Parse(selected.Version)<minimum))issues.Add(owner.Key+" requires "+dep+"; selected "+(uninstall?"uninstall":selected.Version));
        }
        // Dependencies are checked against the final selected batch, not the old core manifest.
        foreach(var package in task.Packages.Where(_=>!uninstall))foreach(var dep in package.Dependencies)
        {
            int at=dep.LastIndexOf('-');if(at<1||!Version.TryParse(dep[(at+1)..],out var minimum))throw new IOException("Invalid dependency: "+dep);
            if(PackageRules.IsLoader(dep[..at]))continue;
            var selected=task.Packages.FirstOrDefault(p=>Files.Compare.Equals(p.Key,dep[..at]));
            if(selected!=null){if(uninstall||!Version.TryParse(selected.Version,out var actual)||actual<minimum)issues.Add(package.Key+" requires "+dep);continue;}
            var existing=consumers.Where(p=>Files.Compare.Equals(p.Key,dep[..at])).ToList();
            if(existing.Count==0||existing.Any(p=>!Version.TryParse(p.Version,out var actual)||actual<minimum))issues.Add(package.Key+" requires "+dep+" in final current-pack environment.");
        }
        return issues.Distinct().ToList();
    }
    internal static (bool Found,string Error) CheckUpdateCore(string root,string key,string minimum)
    {
        var queue=new ShopQueue(root);var task=new ShopTask();
        bool found=queue.TryCore(key,minimum,task);
        return(found,string.Join("\n",task.Blockers));
    }
    string RoutePackage(string path,ShopPackage package,ShopTask task)
    {
        string route=Route(path,package.Key);
        var previous=task.PreviousPackages.FirstOrDefault(p=>Files.Compare.Equals(p.Key,package.Key));
        if(previous!=null&&IsCorePackage(previous)&&(previous.ClaimRoot.Length>0||previous.Files.Any(f=>f.Path.StartsWith("core/",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(f.Path).Equals("manifest.json",StringComparison.OrdinalIgnoreCase))))return CoreRoute(path,previous);
        string? manifest=previous?.Files.FirstOrDefault(f=>f.Path.StartsWith("patchers/",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(f.Path).Equals("manifest.json",StringComparison.OrdinalIgnoreCase))?.Path;
        if(manifest!=null&&route.StartsWith("plugins/"+package.Key+"/",StringComparison.OrdinalIgnoreCase))return manifest[..^("manifest.json".Length)]+route[("plugins/"+package.Key+"/").Length..];
        return route;
    }
}
