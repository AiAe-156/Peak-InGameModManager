using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;

internal sealed partial class ShopQueue
{
    readonly PackStorage storage;
    readonly string? analysisHome;
    readonly bool analysisOnly;
    readonly bool independent;
    internal static readonly object MetadataGate = new();
    string Home => analysisHome ?? ManagerIdentity.Runtime(storage.Root,"shop");
    string TaskPath => Path.Combine(Home,"task.json");
    string LedgerPath => LedgerPathFor(storage.Read());
    string LedgerPathFor(PackIndex index) => index.ActivePackId.Length>0 ? Path.Combine(storage.Workspace(index.ActivePackId),"shop-installed.json") : ManagerIdentity.Runtime(storage.Root,"shop","installed-unmanaged.json");
    internal static HttpMessageHandler? TestHandler = null;
    internal static Action<string>? StepProbe = null;
    internal ShopQueue(string root, string? preparationHome = null, bool executeIndependent = false) { analysisHome=preparationHome; analysisOnly=preparationHome!=null && !executeIndependent; independent=executeIndependent; storage=new(root);Files.RejectLinks(Directory.GetParent(root)!.FullName,Home);var task=Read();if(task?.State is "downloading" or "resolving"){task.State="failed";task.Error="Preparation interrupted. Installed files were not changed; preview again.";Save(task);}else if(task!=null&&PackStorage.Id(task.Id)&&!File.Exists(Path.Combine(Work(task),"task.json")))Save(task); }
    internal ShopTask? Read()=>File.Exists(TaskPath)?Files.Read<ShopTask>(TaskPath):null;
    readonly object stateGate = new();
    internal void CancelPreparation()
    {
        lock(stateGate) {
        var task = Read();
        if (task?.State is "requested" or "resolving" or "downloading" or "preview" or "ready" or "uninstall-preview" or "waiting-exit")
        { task.State = "cancelled"; task.Message = "Cancelled before commit; installed files unchanged."; Save(task); }
    }
    }
    internal bool BlocksWrites=>Read()?.BlocksWrites==true;
    internal bool Watching=>Read()?.State is "requested" or "waiting-exit" or "committing" or "recovery-required";
    string Work(ShopTask t)=>Files.Child(Home,"tasks/"+t.Id);
    ShopLedger Ledger()
    {
        var index=storage.Read();string path=LedgerPathFor(index);
        Files.RejectLinks(storage.Root,path);
        var active=index.Packs.SingleOrDefault(p=>p.Id==index.ActivePackId);
        bool complete=active!=null&&PackEnvironmentPolicy.IsComplete(active);
        // Saved revision-state describes saved payload, not necessarily the current unsaved environment.
        // Only an explicit recovery transaction/user choice may restore that older ownership state.
        if(!File.Exists(path))return complete
            ? throw new IOException("Current complete environment workspace ledger missing; installed files and ownership were preserved. Restore ownership explicitly before retrying; saved revision-state cannot substitute for live ownership.")
            : new();
        try
        {
            var ledger=Files.Read<ShopLedger>(path);
            if(ledger.SchemaVersion!=1||ledger.Revision<0||ledger.Packages==null)throw new IOException("Invalid workspace ownership ledger; preserved. Restore ownership explicitly before retrying.");
            return ledger;
        }
        catch(JsonException e){throw new IOException("Workspace ownership ledger is corrupt; preserved. Restore ownership explicitly before retrying.",e);}
    }
    static bool Part(string s)=>Regex.IsMatch(s,"^[A-Za-z0-9_]+$");
    static string Key(ShopRequest r)=>r.Namespace+"-"+r.Package;
    static readonly AsyncLocal<CancellationToken> preparationToken = new();
    void Save(ShopTask t)
    {
        lock(stateGate) {
        preparationToken.Value.ThrowIfCancellationRequested();
        Files.Save(TaskPath,t);
        if(PackStorage.Id(t.Id))Files.Save(Path.Combine(Work(t),"task.json"),t);
    }
    }
    internal void Handle(ShopRequest request)
    {
        var old=Read();
        if(request.Operation=="recover") {if(old?.State!="recovery-required"||old.Id!=request.PreviewId)throw new IOException("Recovery request expired.");return;}
        if(old?.BlocksWrites==true)throw new IOException("A store operation is still running.");
        if(request.Operation is "approve-download" or "confirm-install" or "confirm-uninstall")
        {
            if(old==null||old.Id!=request.PreviewId||old.PlanToken!=request.PlanToken||old.PlanToken!=Token(old))throw new IOException("Store preview expired or changed.");
            if(DateTime.UtcNow-DateTime.Parse(old.CreatedUtc).ToUniversalTime()>TimeSpan.FromMinutes(30))throw new IOException("Preview expired; check again.");
            RequireContext(old);
            RequireComponentUninstall(old);
            if (request.IgnoreDependencyWarnings && request.DependencyWarningToken.Length > 0)
            {
                if (!old.Uninstall || request.DependencyWarningToken != DependencyWarningToken(old.DependencyWarnings)) throw new IOException("Dependency warning list changed; review again.");
                old.Request.IgnoreDependencyWarnings = true; old.Request.DependencyWarningToken = request.DependencyWarningToken;
                old.PlanToken = Token(old);
            }
            if(old.Blockers.Count>0)throw new IOException(string.Join("\n",old.Blockers));
            RevalidateClaims(old);
            var currentIssues=CheckConsumers(old,old.Uninstall||request.Operation=="confirm-uninstall");
            if (!analysisOnly) RequireDependencyConsent(old, currentIssues.Concat(old.DependencyWarnings));
            if(request.Operation=="approve-download"&&old.State=="preview"){
                if(request.NewGroupName.Length>80||request.NewGroupName.Any(char.IsControl))throw new IOException("Invalid group name.");
                old.Request.GroupId=request.GroupId;old.Request.NewGroupName=request.NewGroupName;old.Request.NewGroupColor=request.NewGroupColor;
                old.PlanToken=Token(old);old.State="requested";
            }
            else if(request.Operation=="confirm-install"&&old.State=="ready"||request.Operation=="confirm-uninstall"&&old.State=="uninstall-preview"){
                if(request.Operation=="confirm-install"){if(request.NewGroupName.Length>80||request.NewGroupName.Any(char.IsControl))throw new IOException("Invalid group name.");old.Request.GroupId=request.GroupId;old.Request.NewGroupName=request.NewGroupName;old.Request.NewGroupColor=request.NewGroupColor;old.PlanToken=Token(old);}
                old.State="waiting-exit";
            }
            else throw new IOException("Operation does not match the preview.");
            old.Request.Operation=request.Operation;old.Request.SourceProcessId=request.SourceProcessId;Save(old);return;
        }
        if(request.Operation is not ("preview-install" or "preview-uninstall")||!Part(request.Namespace)||!Part(request.Package)||!Version.TryParse(request.Version,out _))throw new IOException("Invalid store package request.");
        if(request.NewGroupName.Length>80||request.NewGroupName.Any(char.IsControl))throw new IOException("Invalid group name.");
        Save(new ShopTask{OperationId=request.OperationId,Id=Guid.NewGuid().ToString("N"),State="requested",Request=request,CreatedUtc=DateTime.UtcNow.ToString("O"),ActivePackId=storage.Read().ActivePackId,IndexRevision=storage.Read().Revision,Message="Preparing store plan..."});
    }
    internal async Task Pump(UpdateNetworkOptions network, CancellationToken token = default)
    {
        preparationToken.Value = token;
        var t=Read();if(t==null)return;
        if(!PackStorage.Id(t.Id))throw new IOException("Invalid store task identity.");
        Files.RejectLinks(storage.Root,Home);Files.RejectLinks(storage.Root,Work(t));
        try
        {
            if(t.State=="requested")
            {
                if(t.Request.Operation=="preview-install")await Resolve(t,network,token);
                else if(t.Request.Operation=="approve-download")await Download(t,network,token);
                else if(t.Request.Operation=="preview-uninstall")await PreviewUninstall(t,network,token);
                return;
            }
            if(t.State is not ("waiting-exit" or "committing" or "recovery-required"))return;
            if(Files.GameRunning(new UpdateJob{SourceProcessId=t.Request.SourceProcessId}))return;
            using var writes = await InstallationWriteLease.WaitAsync(storage.Root, token);
            token.ThrowIfCancellationRequested();
            string journal=Path.Combine(Work(t),"transaction.json");
            if(t.State is "committing" or "recovery-required")
            {
                if(!File.Exists(journal))throw new IOException("Recovery journal missing; no further writes allowed.");
                var p=Files.Read<ShopTransaction>(journal);
                if(p.Phase=="installed"){t.State="completed";t.Message="Installation committed; restart PEAK.";}
                else {Rollback(t,p);t.State="failed";t.Message="Original environment restored. Preview again to retry.";}
                Save(t);return;
            }
            lock(MetadataGate) lock(stateGate) { token.ThrowIfCancellationRequested(); Commit(t);t.OrphanCandidates=FindOrphanCandidates(Ledger());t.State="completed";t.Message="Store operation completed. Start PEAK to use the result.";Save(t); }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception e)
        {
            t.Error=e.ToString();string journal=Path.Combine(Work(t),"transaction.json");
            if(File.Exists(journal))
            {
                t.State="recovery-required";
                try{using var recoveryWrites = await InstallationWriteLease.WaitAsync(storage.Root, CancellationToken.None); if(!Files.GameRunning(new UpdateJob{SourceProcessId=t.Request.SourceProcessId})){lock(MetadataGate)Rollback(t,Files.Read<ShopTransaction>(journal));t.State="failed";t.Message="Failed; original environment restored.";}}
                catch(Exception recovery){t.Error+="\nRecovery: "+recovery;}
            }
            else {t.State="failed";t.Message="No installed files were changed.";}
            Save(t);
        }
    }
    async Task Resolve(ShopTask t,UpdateNetworkOptions network,CancellationToken token)
    {
        RequireContext(t);
        using var store=new StoreClient(network,TestHandler);var chosen=new Dictionary<string,StoreClient.StoreVersion>(Files.Compare);
        var minima=new Dictionary<string,string>(Files.Compare);var queue=new Queue<(string Key,string Version)>();queue.Enqueue((Key(t.Request),t.Request.Version));int steps=0;
        while(queue.TryDequeue(out var demand))
        {
            if(++steps>1024||chosen.Count>128)throw new IOException("Dependency graph exceeds safety limit.");
            if(PackageRules.IsLoader(demand.Key)){if(Files.Compare.Equals(demand.Key,Key(t.Request)))throw new IOException("BepInEx loader replacement requires full migration; game-root loader files are outside this batch.");t.Details.Add("Loader already supplied by this installation: "+demand.Key);continue;}
            if(minima.TryGetValue(demand.Key,out var prior)&&Version.Parse(prior)>=Version.Parse(demand.Version))continue;
            minima[demand.Key]=demand.Version;
            int split=demand.Key.IndexOf('-');if(split<1)throw new IOException("Invalid dependency identity.");
            string resolvedVersion=demand.Version;
            if(!Files.Compare.Equals(demand.Key,Key(t.Request)))
            {
                var existing=Ledger().Packages.SingleOrDefault(p=>Files.Compare.Equals(p.Key,demand.Key))??FindExternal(demand.Key,"0.0.0",true);
                if(existing!=null&&Version.Parse(existing.Version)>=Version.Parse(demand.Version)&&(existing.VerificationRequired||existing.Files.All(f=>File.Exists(Files.Child(storage.Root,f.Path))&&Files.Hash(Files.Child(storage.Root,f.Path))==f.Hash)))resolvedVersion=existing.Version;
            }
            var value=await store.Resolve(new UpdateJob{Namespace=demand.Key[..split],Package=demand.Key[(split+1)..]},resolvedVersion,token);chosen[demand.Key]=value;
            foreach(string dependency in value.Dependencies){int at=dependency.LastIndexOf('-');if(at<1||!Version.TryParse(dependency[(at+1)..],out _))throw new IOException("Invalid dependency: "+dependency);queue.Enqueue((dependency[..at],dependency[(at+1)..]));}
        }
        if(!chosen.TryGetValue(Key(t.Request),out var rootChoice)||rootChoice.Version!=t.Request.Version) t.Blockers.Add("Selected root version conflicts with a dependency request.");
        var ledger=Ledger();t.LedgerRevision=ledger.Revision;
        foreach(var entry in chosen)
        {
            var installed=ledger.Packages.FirstOrDefault(p=>Files.Compare.Equals(p.Key,entry.Key));
            bool root=Files.Compare.Equals(entry.Key,Key(t.Request));
            var external=installed==null?FindExternal(entry.Key,"0.0.0",true):null;
            var previous=installed??external;
            if(root)t.Action=ComponentActions.Select(previous?.Version??"",t.Request.Version);
            bool reuse=!root&&previous!=null&&(previous.VerificationRequired||previous.Files.Count>0)&&Version.TryParse(previous.Version,out var v)&&v>=Version.Parse(entry.Value.Version)&&(previous.VerificationRequired||previous.Files.All(f=>File.Exists(Files.Child(storage.Root,f.Path))&&Files.Hash(Files.Child(storage.Root,f.Path))==f.Hash));
            if(previous?.VerificationRequired==true){
                RequireComplete(t);int split=entry.Key.IndexOf('-');
                var original=await store.Resolve(new UpdateJob{Namespace=entry.Key[..split],Package=entry.Key[(split+1)..]},previous.Version,token);
                previous.Url=original.DownloadUrl;previous.Dependencies=original.Dependencies.ToList();t.VerificationPackages.Add(previous);
                t.Details.Add("Consent required: download official original version for exact core verification: "+entry.Key+" "+previous.Version+" "+previous.Url);
            }
            if(reuse){if(previous!.Files.Any(f=>Packages.IsAssembly(f.Path)&&!f.Path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)))throw new IOException("Required dependency is disabled: "+entry.Key);previous.Reuse=true;t.Packages.Add(previous);continue;}
            if(previous!=null)t.PreviousPackages.Add(previous);
            if(ManagerIdentity.IsManagerPackage(entry.Key)&&!reuse)t.Blockers.Add("MCE must use its existing self-update flow.");
            t.Packages.Add(new ShopPackage{Key=entry.Key,Version=reuse?installed!.Version:entry.Value.Version,Url=entry.Value.DownloadUrl,Dependencies=entry.Value.Dependencies.ToList(),Reuse=reuse,InstalledAsDependency=entry.Key!=Key(t.Request)&&(installed?.InstalledAsDependency??true),Files=reuse?installed!.Files:new(),PluginGuids=reuse?installed!.PluginGuids:new()});
        }
        if(t.Packages.Concat(t.PreviousPackages).Any(IsCorePackage))RequireComplete(t);
        if (!analysisOnly) AddDependencyIssues(t,CheckConsumers(t,false));
        t.PlanToken=Token(t);t.State="preview";t.Message="Review dependencies before approving downloads.";Save(t);
    }
    ShopPackage? FindExternal(string key,string minimum,bool forUninstall=false)=>ClaimExternal(key,minimum,forUninstall);
    static string Token(ShopTask t)
    {
        var payload = JsonNode.Parse(JsonSerializer.Serialize(new{t.Id,t.Uninstall,t.ActivePackId,t.IndexRevision,t.LedgerRevision,t.VerificationPackages,t.ApprovedCoreFiles,t.Request.Namespace,t.Request.Package,t.Request.Version,t.Request.GroupId,t.Request.NewGroupName,t.Request.NewGroupColor,t.Request.RemoveConfiguration,t.Action,t.Packages,t.PreviousPackages,t.CoreRequirements,t.Desired,t.Originals},Files.Json))!;
        // Empty new optional layout fields must not invalidate protocol2 journals created before this release.
        void Prune(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (string name in new[] { "LayoutGroupId", "LayoutGroupFolder", "LayoutOuterFolder" })
                    if (obj[name]?.GetValue<string>() == "") obj.Remove(name);
                foreach (var child in obj.Select(p => p.Value).Where(v => v != null).ToArray()) Prune(child!);
            }
            else if (node is JsonArray array) foreach (var child in array.Where(v => v != null)) Prune(child!);
        }
        Prune(payload);
        if (t.Request.IgnoreDependencyWarnings) payload["IgnoreDependencyWarnings"] = true;
        if (t.Request.DependencyWarningToken.Length > 0) payload["DependencyWarningToken"] = t.Request.DependencyWarningToken;
        if (t.Request.AppliedSnapshotPath.Length > 0)
        { payload["AppliedSnapshotPath"] = t.Request.AppliedSnapshotPath; payload["AppliedSnapshotHash"] = t.Request.AppliedSnapshotHash; }
        return FrameworkCompat.Hex(FrameworkCompat.HashData(Encoding.UTF8.GetBytes(payload.ToJsonString(Files.Json))));
    }
    async Task Download(ShopTask t,UpdateNetworkOptions network,CancellationToken token)
    {
        if(t.PlanToken!=Token(t))throw new IOException("Approved download plan changed.");
        RequireContext(t);
        using var store=new StoreClient(network,TestHandler);var ledger=Ledger();var index=storage.Read();
        await VerifyCoreClaims(t,store,token);
        if(t.Uninstall){
            var target=t.Packages.Single();ApproveCoreOriginals(t,target.Files);
            foreach(var file in target.Files){if(file.Path.StartsWith("config/",StringComparison.OrdinalIgnoreCase))continue;RequireUnchanged(file);if(!ledger.Packages.Where(p=>!Files.Compare.Equals(p.Key,target.Key)).Any(p=>p.Files.Any(f=>Files.Compare.Equals(f.Path,file.Path))))t.Originals.Add(file);}
            AddDependencyIssues(t,CheckConsumers(t,true));AddDependencyIssues(t,CheckFinalPlugins(t));t.State="uninstall-preview";t.PlanToken=Token(t);t.Message="Review verified official ownership; configuration and extra files retained.";Save(t);return;
        }
        string stage=Path.Combine(Work(t),"content");
        foreach(var package in t.Packages.Where(p=>!p.Reuse))
        {
            t.State="downloading";t.Message="Downloading "+package.Key;Save(t);
            string zip=Path.Combine(Work(t),package.Key+".zip");Directory.CreateDirectory(Work(t));
            DateTime progressAt=DateTime.MinValue;
            await store.Download(package.Url,zip,(done,total,message)=>{t.DownloadedBytes=done;t.TotalBytes=total;if(DateTime.UtcNow-progressAt>TimeSpan.FromMilliseconds(400)){progressAt=DateTime.UtcNow;Save(t);}},token);
            token.ThrowIfCancellationRequested();
            package.ArchiveHash=Files.Hash(zip);
            string extracted=Path.Combine(Work(t),"extract-"+package.Key);PackImports.Extract(zip,extracted);
            string manifest=Path.Combine(extracted,"manifest.json");var data=JsonNode.Parse(File.ReadAllText(manifest));
            if(data?["name"]?.GetValue<string>()!=package.Key[(package.Key.IndexOf('-')+1)..]||data?["version_number"]?.GetValue<string>()!=package.Version)throw new IOException("Downloaded manifest identity mismatch: "+package.Key);
            var downloadedDependencies=(data?["dependencies"]?.AsArray()??new JsonArray()).Select(d=>d?.GetValue<string>()??"").OrderBy(s=>s,StringComparer.Ordinal).ToArray();
            if(!downloadedDependencies.SequenceEqual(package.Dependencies.OrderBy(s=>s,StringComparer.Ordinal)))throw new IOException("Downloaded dependencies differ from approved metadata: "+package.Key);
            foreach(var input in Files.Inventory(extracted))
            {
                string relative=RoutePackage(input.Key,package,t);string source=Files.Child(extracted,input.Key);
                if(relative.StartsWith("core/",StringComparison.OrdinalIgnoreCase)){RequireComplete(t);ApproveCoreFile(t,new PackFile{Path=relative,Hash=input.Value});}
                if(!(relative.StartsWith("core/",StringComparison.OrdinalIgnoreCase)?t.ApprovedCoreFiles.Any(f=>Files.Compare.Equals(f.Path,relative)&&f.Hash==input.Value):Safe(relative,index)))throw new IOException("Unsupported or protected package content: "+relative);
                string target=Files.Child(stage,relative);if(File.Exists(target)&&Files.Hash(target)!=input.Value)throw new IOException("Conflicting dependency files: "+relative);
                if(!File.Exists(target))Files.CopyAtomic(source,target);
                package.Files.Add(new PackFile{Path=relative,Hash=input.Value});
                if(Packages.IsAssembly(relative))package.PluginGuids.AddRange(Packages.ReadPlugins(source).Select(p=>p.Guid));
            }
        }
        foreach(var package in t.Packages.Where(p=>!p.Reuse))
        {
            var old=t.PreviousPackages.FirstOrDefault(p=>Files.Compare.Equals(p.Key,package.Key));
            if(old!=null)foreach(var file in old.Files)
            {if(file.Path.StartsWith("config/",StringComparison.OrdinalIgnoreCase))continue;
                if(ledger.Packages.Where(p=>!Files.Compare.Equals(p.Key,package.Key)).Any(p=>p.Files.Any(f=>Files.Compare.Equals(f.Path,file.Path)))) { var replacement=package.Files.FirstOrDefault(f=>Files.Compare.Equals(f.Path,file.Path));if(replacement==null||replacement.Hash!=file.Hash)t.Blockers.Add("Shared file cannot be changed: "+file.Path);continue; }
                RequireUnchanged(file);if(!t.Originals.Any(f=>Files.Compare.Equals(f.Path,file.Path)))t.Originals.Add(file);}
            foreach(var file in package.Files.ToArray())
            {
                string live=Files.Child(storage.Root,file.Path);
                if(file.Path.StartsWith("config/",StringComparison.OrdinalIgnoreCase)&&File.Exists(live)){package.Files.Remove(file);t.Details.Add("Existing configuration retained: "+file.Path);continue;}
                if(File.Exists(live)&&!t.Originals.Any(f=>Files.Compare.Equals(f.Path,file.Path)))
                {
                    // Adopt only identical files with an existing explicit ledger owner; never unknown files.
                    if(ledger.Packages.Any(p=>p.Files.Any(f=>Files.Compare.Equals(f.Path,file.Path)&&f.Hash==file.Hash))&&Files.Hash(live)==file.Hash){t.Details.Add("Shared same-hash reference retained: "+file.Path);continue;}
                    t.Blockers.Add("Unowned installed file would be overwritten: "+file.Path);
                }
                if(!t.Desired.Any(f=>Files.Compare.Equals(f.Path,file.Path)))t.Desired.Add(file);
            }
        }
        // Prevent upgrading a library beyond or below a version required by recorded installed packages.
        foreach(var owner in ledger.Packages.Where(p=>!t.Packages.Any(q=>Files.Compare.Equals(q.Key,p.Key))))
            foreach(string dep in owner.Dependencies){int at=dep.LastIndexOf('-');if(at<1)continue;var selected=t.Packages.FirstOrDefault(p=>Files.Compare.Equals(p.Key,dep[..at]));if(!analysisOnly&&selected!=null&&Version.Parse(selected.Version)<Version.Parse(dep[(at+1)..]))t.Blockers.Add(owner.Key+" requires "+dep);}
        ApproveCoreOriginals(t);
        RejectSharedClaims(t,ledger);
        if (!analysisOnly) AddDependencyIssues(t,CheckConsumers(t,false));
        AddDependencyIssues(t,CheckFinalPlugins(t));
        t.State="ready";t.PlanToken=Token(t);t.Message="Files checked. Confirm installation after exiting PEAK.";Save(t);
    }
    internal static string Route(string path,string key)
    {
        string p=path.Replace('\\','/');if(p.StartsWith("BepInEx/",StringComparison.OrdinalIgnoreCase))p=p[8..];
        string lower=p.ToLowerInvariant(),leaf=Path.GetFileName(p);
        if(lower.StartsWith("core/"))return p;
        if(leaf.Equals("BepInEx.dll",StringComparison.OrdinalIgnoreCase)||leaf.Equals("0Harmony.dll",StringComparison.OrdinalIgnoreCase))return "core/"+leaf;
        if(lower.StartsWith("plugins/")){p=p[8..];return "plugins/"+key+"/"+p;}
        if(lower.StartsWith("patchers/")||lower.StartsWith("config/"))return p;
        return "plugins/"+key+"/"+p;
    }
    static bool Safe(string path,PackIndex index)
    {
        string lower=path.ToLowerInvariant();if(!PackStorage.ManagedPath(path)||index.HostRoots.Any(r=>Files.Compare.Equals(path,r)||path.StartsWith(r.TrimEnd('/')+"/",StringComparison.OrdinalIgnoreCase)))return false;
        if(lower.StartsWith("config/")&&PackStorage.GlobalConfig(path[7..]))return false;
        return !new[]{".bat",".cmd",".ps1",".hta",".wsf",".vbs",".js",".lnk",".url",".mm.dll"}.Any(lower.EndsWith)&&!lower.Contains("/monomod/")&&!lower.Contains("/peak_data/")&&!lower.EndsWith("/winhttp.dll")&&!lower.EndsWith("/doorstop_config.ini")&&!Path.GetFileName(lower).Equals("bepinex.dll")&&!lower.EndsWith("/assembly-csharp.dll")&&(!lower.EndsWith(".exe")||lower.Contains("/tools/updater/"));
    }
    List<string> CheckFinalPlugins(ShopTask t)
    {
        var result = ShopDependencyValidation.Analyze(storage.Root,Path.Combine(Work(t),"content"),t.Originals,t.Desired);
        if (result.Hard.Count > 0) throw new IOException(string.Join(Environment.NewLine,result.Hard));
        return result.Warnings;
    }
    void RequireUnchanged(PackFile f){string path=Files.Child(storage.Root,f.Path);Files.RejectLinks(storage.Root,path);if(!File.Exists(path)||Files.Hash(path)!=f.Hash)throw new IOException("Installed file changed; preserved: "+f.Path);}
    internal static List<ShopPackage> FindOrphanCandidates(ShopLedger ledger)
    {
        return ledger.Packages.Where(candidate=>candidate.InstalledAsDependency&&!candidate.External&&!ledger.Packages.Where(owner=>!Files.Compare.Equals(owner.Key,candidate.Key)).Any(owner=>owner.Dependencies.Any(d=>d.StartsWith(candidate.Key+"-",StringComparison.OrdinalIgnoreCase)))).ToList();
    }
    async Task PreviewUninstall(ShopTask t,UpdateNetworkOptions network,CancellationToken token)
    {
        t.Uninstall = true;
        RequireContext(t);
        RequireComponentUninstall(Key(t.Request));
        var ledger=Ledger();t.LedgerRevision=ledger.Revision;var target=ledger.Packages.SingleOrDefault(p=>Files.Compare.Equals(p.Key,Key(t.Request)))??FindExternal(Key(t.Request),t.Request.Version,true)??throw new IOException("No verifiable package manifest and independent package ownership were found. No files were removed.");
        if(ProtectedHost(target))throw new IOException("Required BepInEx/manager host cannot be uninstalled independently: "+target.Key);
        // Uninstall is bounded by explicit ownership/protection, not pack migration status.
        // Installing/replacing core content retains the complete-environment requirement.
        if(target.VerificationRequired){
            t.Uninstall=true;using var store=new StoreClient(network,TestHandler);int split=target.Key.IndexOf('-');
            var original=await store.Resolve(new UpdateJob{Namespace=target.Key[..split],Package=target.Key[(split+1)..]},target.Version,token);
            target.Url=original.DownloadUrl;target.Dependencies=original.Dependencies.ToList();t.Packages.Add(target);t.VerificationPackages.Add(target);
            AddDependencyIssues(t,CheckConsumers(t,true));t.State="preview";t.Message="Approve original official ZIP download to verify exact core ownership before uninstall.";t.PlanToken=Token(t);Save(t);return;
        }
        t.Packages.Add(target);ApproveCoreOriginals(t,target.Files);
        if(target.External)t.Details.Add("Existing package identified from its manifest; exact files and dependencies are checked before uninstall.");
        foreach(var owner in ledger.Packages.Where(p=>p.Key!=target.Key))if(owner.Dependencies.Any(d=>d.StartsWith(target.Key+"-",StringComparison.OrdinalIgnoreCase)))t.DependencyWarnings.Add("Required by: "+owner.Key);
        // Include dependency declarations of pre-existing manually installed packages.
        foreach(string file in MutableManifests())
        {var data=JsonNode.Parse(File.ReadAllText(file));if(target.Files.Any(f=>Files.Compare.Equals(Files.Child(storage.Root,f.Path),file)))continue;foreach(var dep in data?["dependencies"]?.AsArray()??new JsonArray())if(dep?.GetValue<string>().StartsWith(target.Key+"-",StringComparison.OrdinalIgnoreCase)==true)t.DependencyWarnings.Add("Required by installed manifest: "+file);}
        var live=PackTransactions.Live(storage,storage.Read());
        foreach(var issue in PackMaintenance.CheckDependencies(storage.Root,new PackRecord{Files=live},target.Files.Select(f=>f.Path).ToList(),storage.Read()))t.DependencyWarnings.Add(issue.Message);
        AddDependencyIssues(t,CheckConsumers(t,true));
        foreach(var file in target.Files){if(file.Path.StartsWith("config/",StringComparison.OrdinalIgnoreCase))continue;if(!(file.Path.StartsWith("core/",StringComparison.OrdinalIgnoreCase)?t.ApprovedCoreFiles.Any(f=>Files.Compare.Equals(f.Path,file.Path)&&f.Hash==file.Hash):Safe(file.Path,storage.Read()))){t.Blockers.Add("Protected: "+file.Path);continue;}RequireUnchanged(file);if(!ledger.Packages.Where(p=>p.Key!=target.Key).Any(p=>p.Files.Any(f=>Files.Compare.Equals(f.Path,file.Path))))t.Originals.Add(file);}
        AddDependencyIssues(t,CheckFinalPlugins(t));
        t.State="uninstall-preview";t.PlanToken=Token(t);t.Message="Review exact uninstall files; configuration is always retained.";Save(t);
    }
    internal sealed class ShopTransaction
    {
        public string Phase{get;set;}="prepared";
        public ShopLedger Before{get;set;}=new();public ShopLedger After{get;set;}=new();
        public PackIndex PackBefore{get;set;}=new();public PackIndex PackAfter{get;set;}=new();
        public List<string> Placed{get;set;}=new();public List<string> Removed{get;set;}=new();
    }
    void Commit(ShopTask t)
    {
        if (analysisOnly) throw new IOException("Preparation-only queue cannot commit installation files.");
        RequireContext(t);
        if(t.PlanToken!=Token(t)||storage.Read().ActivePackId!=t.ActivePackId||!independent && Ledger().Revision!=t.LedgerRevision)throw new IOException("Environment or installation plan changed; preview again.");
        RequireComponentUninstall(t);
        RevalidateClaims(t);
        var consumers=CheckConsumers(t,t.Request.Operation=="confirm-uninstall");
        RequireDependencyConsent(t,consumers);
        if(t.Request.Operation=="confirm-uninstall")
        {
            var issues=PackMaintenance.CheckDependencies(storage.Root,new PackRecord{Files=PackTransactions.Live(storage,storage.Read())},t.Originals.Select(f=>f.Path).ToList(),storage.Read());
            RequireDependencyConsent(t,issues.Select(i=>i.Message));
        }
        // Re-read staged and retained assemblies after exit; the preview is not a safety cache.
        var pluginIssues=CheckFinalPlugins(t);
        RequireDependencyConsent(t,pluginIssues);
        foreach(var dependency in t.Packages.Where(p=>p.Reuse))foreach(var file in dependency.Files)RequireUnchanged(file);
        var p=new ShopTransaction{Before=Ledger(),PackBefore=storage.Read()};p.After=JsonSerializer.Deserialize<ShopLedger>(JsonSerializer.Serialize(p.Before,Files.Json),Files.Json)!;p.PackAfter=JsonSerializer.Deserialize<PackIndex>(JsonSerializer.Serialize(p.PackBefore,Files.Json),Files.Json)!;
        foreach(var f in t.Originals){if(!Allowed(t,f.Path,p.PackBefore))throw new IOException("Protected target");RequireUnchanged(f);Files.CopyAtomic(Files.Child(storage.Root,f.Path),Files.Child(Path.Combine(Work(t),"backup"),f.Path));}
        foreach(var f in t.Desired){Files.RejectLinks(storage.Root,Files.Child(storage.Root,f.Path));Files.RejectLinks(Home,Files.Child(Path.Combine(Work(t),"content"),f.Path));if(!Allowed(t,f.Path,p.PackBefore))throw new IOException("Protected target");if(!t.Originals.Any(o=>Files.Compare.Equals(o.Path,f.Path))&&File.Exists(Files.Child(storage.Root,f.Path)))throw new IOException("New file appeared: "+f.Path);if(Files.Hash(Files.Child(Path.Combine(Work(t),"content"),f.Path))!=f.Hash)throw new IOException("Staging file changed");}
        foreach(var package in t.Packages.Where(p=>!p.Reuse||p.VerificationRequired||t.Request.Operation=="confirm-uninstall")){p.After.Packages.RemoveAll(x=>Files.Compare.Equals(x.Key,package.Key));if(t.Request.Operation!="confirm-uninstall"){var recorded=JsonSerializer.Deserialize<ShopPackage>(JsonSerializer.Serialize(package,Files.Json),Files.Json)!;recorded.VerificationRequired=false;recorded.External=false;p.After.Packages.Add(recorded);}}p.After.Revision++;
        var changed=t.Originals.Select(f=>f.Path).ToHashSet(Files.Compare);p.PackAfter.ActiveFiles.RemoveAll(f=>changed.Contains(f.Path));if(p.PackAfter.ActivePackId.Length>0)p.PackAfter.ActiveFiles.AddRange(t.Desired.Where(f=>!p.PackAfter.ActiveFiles.Any(a=>Files.Compare.Equals(a.Path,f.Path))));
        if(p.PackAfter.ActivePackId.Length>0&&t.Request.Operation!="confirm-uninstall")foreach(var f in t.Packages.SelectMany(p=>p.Files))
            if(!p.PackAfter.ActiveFiles.Any(a=>Files.Compare.Equals(a.Path,f.Path)))p.PackAfter.ActiveFiles.Add(f);
        string journal=Path.Combine(Work(t),"transaction.json");t.State="committing";Save(t);Files.Save(journal,p);
        foreach(var f in t.Originals){Stopped(t);Files.RejectLinks(storage.Root,Files.Child(storage.Root,f.Path));Files.MoveAside(Files.Child(storage.Root,f.Path),Files.Child(Path.Combine(Work(t),"retired"),f.Path));p.Removed.Add(f.Path);Files.Save(journal,p);StepProbe?.Invoke("removed:"+f.Path);}
        // Record the full intended write set before bounded parallel copies; rollback handles absent/partial targets.
        p.Placed=t.Desired.Select(f=>f.Path).ToList();Files.Save(journal,p);
        Parallel.ForEach(t.Desired,new ParallelOptions{MaxDegreeOfParallelism=4},f=>{Stopped(t);Files.RejectLinks(storage.Root,Files.Child(storage.Root,f.Path));Files.CopyAtomic(Files.Child(Path.Combine(Work(t),"content"),f.Path),Files.Child(storage.Root,f.Path));StepProbe?.Invoke("placed:"+f.Path);});
        foreach(var f in t.Desired)if(!File.Exists(Files.Child(storage.Root,f.Path))||Files.Hash(Files.Child(storage.Root,f.Path))!=f.Hash)throw new IOException("Installed file verification failed: "+f.Path);
        Stopped(t);Files.RejectLinks(storage.Root,LedgerPath);Files.Save(LedgerPath,p.After);storage.Save(p.PackAfter);p.Phase="installed";Files.Save(journal,p);
    }
    void Stopped(ShopTask t){if(Files.GameRunning(new UpdateJob{SourceProcessId=t.Request.SourceProcessId}))throw new IOException("PEAK restarted; writes stopped until exit.");}
    void Rollback(ShopTask t,ShopTransaction p)
    {
        Stopped(t);if(t.PlanToken!=Token(t))throw new IOException("Recovery plan changed; no writes allowed.");if(storage.Read().ActivePackId!=t.ActivePackId)throw new IOException("Recovery package changed; manual recovery required.");var index=p.PackBefore;foreach(string path in p.Placed.AsEnumerable().Reverse()){Stopped(t);Files.RejectLinks(storage.Root,Files.Child(storage.Root,path));if(!Allowed(t,path,index))throw new IOException("Invalid recovery target");string live=Files.Child(storage.Root,path);var desired=t.Desired.Single(f=>Files.Compare.Equals(f.Path,path));if(File.Exists(live)){var original=t.Originals.FirstOrDefault(f=>Files.Compare.Equals(f.Path,path));if(original!=null&&Files.Hash(live)==original.Hash)continue;if(Files.Hash(live)!=desired.Hash)throw new IOException("Changed recovery file retained: "+path);Files.MoveAside(live,Files.Child(Path.Combine(Work(t),"withdrawn"),path));}}
        foreach(var f in t.Originals){Stopped(t);Files.RejectLinks(storage.Root,Files.Child(storage.Root,f.Path));Files.RejectLinks(Home,Files.Child(Path.Combine(Work(t),"backup"),f.Path));if(!Allowed(t,f.Path,index))throw new IOException("Invalid recovery original");string live=Files.Child(storage.Root,f.Path);if(File.Exists(live)){if(Files.Hash(live)!=f.Hash)throw new IOException("Changed original retained: "+f.Path);continue;}string backup=Files.Child(Path.Combine(Work(t),"backup"),f.Path);if(Files.Hash(backup)!=f.Hash)throw new IOException("Backup damaged");Files.CopyAtomic(backup,live);}
        Stopped(t);Files.RejectLinks(storage.Root,LedgerPath);Files.Save(LedgerPath,p.Before);storage.Save(p.PackBefore);p.Phase="rolled-back";Files.Save(Path.Combine(Work(t),"transaction.json"),p);
    }
}
