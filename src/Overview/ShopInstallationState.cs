#nullable enable
using System;
using System.Linq;
using Newtonsoft.Json;
namespace ModConfigEnhance.Overview;

/// <summary>UI-independent inventory lifecycle. A refresh keeps ready evidence until it fails;
/// a new context never inherits readiness. Completion callers must carry the original ticket.</summary>
internal sealed class ShopInstallationState
{
    internal enum Phase { Reading, Ready, Failed }
    internal Phase Status { get; private set; } = Phase.Reading;
    internal ShopInstalled.Snapshot Snapshot { get; private set; } = new();
    internal string Context { get; private set; } = "";
    internal string Error { get; private set; } = "";
    internal int Generation { get; private set; }
    private string _signature = "";
    internal bool CompleteCurrentPack { get; set; }
    internal void Reset(string context)
    {
        Context=context;Generation++;Status=Phase.Reading;Error="";CompleteCurrentPack=false;
        Snapshot=new ShopInstalled.Snapshot { Context=context };_signature="";
    }
    internal bool Retry()
    {
        Generation++;
        bool changed=Status!=Phase.Ready;
        if(changed){Status=Phase.Reading;Error="";}
        return changed;
    }
    internal bool Current(int generation,string context)=>generation==Generation&&context==Context;
    internal bool Complete(int generation,string context,ShopInstalled.Snapshot? snapshot,string error="")
    {
        if(!Current(generation,context)||snapshot!=null&&snapshot.Context!=Context)return false;
        if(snapshot==null||error.Length>0||snapshot.Failure.Length>0){
            string failure=error.Length>0?error:snapshot?.Failure??"Inventory scan failed.";
            bool changed=Status!=Phase.Failed||Error!=failure;
            if (snapshot != null) Snapshot=snapshot;
            Status=Phase.Failed;Error=failure;return changed;
        }
        string signature=Signature(snapshot);
        bool update=Status!=Phase.Ready||signature!=_signature;
        Snapshot=snapshot;_signature=signature;Status=Phase.Ready;Error="";
        return update;
    }
    internal string BlockReason(string key) => BlockReason(key, false);
    private string BlockReason(string key, bool uninstall)
    {
        if(Status!=Phase.Ready && !Snapshot.Packages.ContainsKey(key))return Status==Phase.Failed?"Installation status failed; retry: "+Error:"Reading installation status; operations are disabled.";
        if(!Snapshot.Packages.TryGetValue(key,out var rows))return "";
        if(uninstall && rows.Any(r=>r.Cached))return "Cached package uninstall requires its exact cache location.";
        if(rows.Select(r=>r.Version).Distinct().Count()!=1||!Version.TryParse(Snapshot.Version(key),out _))return "Installed version is ambiguous or unknown; verify package identity.";
        return "";
    }
    internal bool CanInstall(string key)=>BlockReason(key).Length==0;
    internal Func<string,string>? DependencyUninstallReason { get; set; }
    internal string UninstallBlockReason(string key)=>DependencyUninstallReason?.Invoke(key) is string protection && protection.Length>0 ? protection : ModConfigEnhance.Updating.ManagerIdentity.IsManagerPackage(key)||key.StartsWith("BepInEx-BepInExPack",StringComparison.OrdinalIgnoreCase)
        ? "Required BepInEx/manager host cannot be uninstalled independently." : BlockReason(key, true);
    internal bool CanUninstall(string key,bool owned)=>UninstallBlockReason(key).Length==0&&Snapshot.Has(key)&&
        (owned||Snapshot.Packages[key].All(r=>r.Area=="plugins"||r.Area=="patchers"||r.Area=="core"));
    // Serialize sorted tuples, not delimiter joins or object identity. Dictionary/list order is irrelevant.
    internal static string Signature(ShopInstalled.Snapshot snapshot)=>JsonConvert.SerializeObject(
        snapshot.Packages.SelectMany(p=>p.Value.Select(r=>new[]{p.Key,r.Key,r.Version,r.Path,r.Area,r.Disabled?"1":"0",r.Cached?"1":"0"}))
            .Select(JsonConvert.SerializeObject).Distinct(StringComparer.Ordinal).OrderBy(s=>s,StringComparer.Ordinal));
}
