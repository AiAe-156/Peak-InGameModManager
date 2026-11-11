#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>One cached display graph for hover and details. Never an uninstall/ownership graph.</summary>
internal static class SharedRelationshipSnapshot
{
    private sealed class Snapshot
    {
        internal ComponentDependencyGraph? Raw;
        internal ComponentDependencyGraph Graph=new();
        internal Dictionary<string,string> Aliases=new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<OverviewMod,string> Roots=new();
        internal Dictionary<string,OverviewDependencyItem> Items=new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string,string> Required=new(StringComparer.OrdinalIgnoreCase);
        internal IReadOnlyList<InfrastructureOverviewModel> Components=Array.Empty<InfrastructureOverviewModel>();
    }
    private static readonly object Gate=new();
    private static readonly ConditionalWeakTable<ComponentDependencyGraph,Snapshot> Snapshots=new();
    private static Snapshot? _current;
    private static OverviewDependencies? _ordinary;
    private static IReadOnlyList<InfrastructureOverviewModel> _components=Array.Empty<InfrastructureOverviewModel>();
    private static string _context="",_ordinaryStamp="",_stamp="";
    private static int _inventory=-1, _content=-1;
    private static IReadOnlyList<OverviewMod>? _modsInput;
    private static IReadOnlyList<InfrastructureOverviewModel>? _componentsInput;

    internal static ComponentDependencyGraph Get(IReadOnlyList<OverviewMod> mods,
        IReadOnlyList<InfrastructureOverviewModel> components,ComponentDependencyGraph? rawGraph)
    {
        lock(Gate)
        {
            if(rawGraph!=null&&Snapshots.TryGetValue(rawGraph,out var prior))rawGraph=prior.Raw;
            string context=SourceUi.DisplayDependencyContext;int inventory=OverviewPageCache.Inventory;
            int content=OverviewPageCache.Content;
            bool sameComponents=components.Count==0 || ReferenceEquals(components,_componentsInput);
            if(_current!=null && ReferenceEquals(_modsInput,mods) && sameComponents
                && ReferenceEquals(_current.Raw,rawGraph) && _inventory==inventory && _content==content && _context==context)
                return _current.Graph;
            _modsInput=mods; if(components.Count>0)_componentsInput=components; _content=content;
            if(_context!=context){_components=Array.Empty<InfrastructureOverviewModel>();_ordinary=null;}
            if(components.Count>0)_components=components.ToArray();
            string ordinaryStamp=ModStamp(mods);
            if(_ordinary==null||_inventory!=inventory||_context!=context||_ordinaryStamp!=ordinaryStamp)
            {
                _ordinary=new OverviewDependencies(mods);_ordinaryStamp=ordinaryStamp;
            }
            var stamp=new StringBuilder(ordinaryStamp);
            foreach(var component in _components)
            {
                stamp.Append('|').Append(component.Key).Append('|').Append(component.Version).Append('|').Append(component.IdentityConfirmed);
                foreach(var location in component.Locations)stamp.Append('|').Append(location.Area).Append('|').Append(location.Path);
                foreach(string dependency in component.Dependencies)stamp.Append('|').Append(dependency);
            }
            string signature=stamp.ToString();
            if(_current!=null&&ReferenceEquals(_current.Raw,rawGraph)&&_stamp==signature&&_inventory==inventory&&_context==context)return _current.Graph;
            _current=Build(mods,_components,rawGraph,_ordinary);_stamp=signature;_inventory=inventory;_context=context;
            Snapshots.Add(_current.Graph,_current);
            return _current.Graph;
        }
    }
    internal static void InvalidateDisplay()
    { lock(Gate) { _modsInput=null; _current=null; } }
    private static string ModStamp(IReadOnlyList<OverviewMod> mods)
    {
        var stamp=new StringBuilder();
        foreach(var mod in mods)
        {
            stamp.Append(RuntimeHelpers.GetHashCode(mod)).Append('|').Append(mod.Guid).Append('|').Append(mod.SourceConfirmed)
                .Append('|').Append(mod.SourceKey).Append('|').Append(mod.IdentityConflict).Append('|').Append(mod.Location)
                .Append('|').Append(mod.PackageRoot).Append('|').Append(mod.PackageVersion).Append('|').Append(mod.PluginVersion)
                .Append('|').Append(mod.Name).Append('|').Append(mod.RawName).Append('|').Append(mod.DependencyMetadataKnown);
            foreach(string guid in mod.PluginGuids)stamp.Append("|g:").Append(guid);
            foreach(string path in mod.AssemblyPaths)stamp.Append("|f:").Append(path);
            foreach(string declaration in mod.PackageDependencies)stamp.Append("|p:").Append(declaration);
            foreach(var dependency in mod.RuntimeDependencies)stamp.Append("|d:").Append(dependency.Id).Append('|').Append(dependency.MinimumVersion).Append('|').Append(dependency.Optional);
            stamp.Append('\n');
        }
        return stamp.ToString();
    }
    internal static string RootId(OverviewMod mod,ComponentDependencyGraph graph)
        =>Snapshots.TryGetValue(graph,out var snapshot)&&snapshot.Roots.TryGetValue(mod,out string? id)?id:OverviewDependencies.Identity(mod);
    internal static string Canonical(ComponentDependencyGraph graph,string id)
        =>Snapshots.TryGetValue(graph,out var snapshot)?Resolve(snapshot,id):id;
    internal static IReadOnlyList<InfrastructureOverviewModel> Components(ComponentDependencyGraph graph)
        =>Snapshots.TryGetValue(graph,out var snapshot)?snapshot.Components:Array.Empty<InfrastructureOverviewModel>();
    internal static OverviewDependencyItem? Metadata(ComponentDependencyGraph graph,string id)
        =>Snapshots.TryGetValue(graph,out var snapshot)&&snapshot.Items.TryGetValue(Resolve(snapshot,id),out var item)?item:null;
    internal static OverviewDependencyItem Item(ComponentDependencyGraph graph,ComponentRelationshipTree.Row row,bool reverse)
    {
        var item=Metadata(graph,row.Id);
        string? requirement=null;
        if(Snapshots.TryGetValue(graph,out var snapshot))snapshot.Required.TryGetValue(EdgeKey(reverse?row.Id:row.ParentId,reverse?row.ParentId:row.Id),out requirement);
        return new OverviewDependencyItem {Identity=row.Id,Name=item?.Name??row.Id,Mod=item?.Mod,
            Installed=!row.Missing,Unknown=item?.Unknown??false,Version=item?.Version??"",Required=requirement??"",Optional=row.Optional};
    }
    internal static bool IsFramework(string id)
    {
        string value=id.Replace('\\','/');
        foreach(string prefix in new[]{"package:","missing:","guid:"})if(value.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))value=value.Substring(prefix.Length);
        if(value.Equals("BepInEx-BepInExPack",StringComparison.OrdinalIgnoreCase)||value.Equals("BepInEx-BepInExPack_PEAK",StringComparison.OrdinalIgnoreCase)
            ||value.Equals("BepInEx",StringComparison.OrdinalIgnoreCase)||value.Equals("BepInEx.dll",StringComparison.OrdinalIgnoreCase))return true;
        return (value.StartsWith("file:core/",StringComparison.OrdinalIgnoreCase)||value.StartsWith("shared:core/",StringComparison.OrdinalIgnoreCase)
            ||value.IndexOf("/BepInEx/core/",StringComparison.OrdinalIgnoreCase)>=0)&&value.EndsWith("/BepInEx.dll",StringComparison.OrdinalIgnoreCase);
    }
    private static string Resolve(Snapshot snapshot,string id)
    {
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(seen.Add(id)&&snapshot.Aliases.TryGetValue(id,out string? alias)&&!id.Equals(alias,StringComparison.OrdinalIgnoreCase))id=alias;
        return id;
    }
    private static string FullPath(string path)
    {
        try
        {
            if(path.StartsWith("file:",StringComparison.OrdinalIgnoreCase)||path.StartsWith("shared:",StringComparison.OrdinalIgnoreCase))path=path.Substring(path.IndexOf(':')+1);
            return Path.GetFullPath(Path.IsPathRooted(path)?path:Path.Combine(BepInEx.Paths.BepInExRootPath,path.Replace('/',Path.DirectorySeparatorChar))).TrimEnd('\\','/');
        }
        catch(ArgumentException){return "";}
        catch(NotSupportedException){return "";}
        catch(IOException){return "";}
    }
    private static string EdgeKey(string from,string to)=>from.Length+":"+from+to;
    private static Snapshot Build(IReadOnlyList<OverviewMod> mods,IReadOnlyList<InfrastructureOverviewModel> components,
        ComponentDependencyGraph? raw,OverviewDependencies ordinary)
    {
        var snapshot=new Snapshot {Raw=raw,Components=components.ToArray()};
        var guidMods=mods.SelectMany(m=>m.PluginGuids.Concat(new[]{m.Guid}).Where(g=>g.Length>0).Select(g=>(Guid:g,Mod:m)))
            .GroupBy(p=>p.Guid,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.Select(p=>p.Mod).Distinct().ToArray(),StringComparer.OrdinalIgnoreCase);
        var pathMods=mods.SelectMany(m=>m.AssemblyPaths.Concat(new[]{m.Location}).Where(p=>p.Length>0).Select(p=>(Path:FullPath(p),Mod:m)))
            .Where(p=>p.Path.Length>0).GroupBy(p=>p.Path,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.Select(p=>p.Mod).Distinct().ToArray(),StringComparer.OrdinalIgnoreCase);
        foreach(var mod in mods)
        {
            string id=OverviewDependencies.Identity(mod);
            if(id.StartsWith("package:",StringComparison.Ordinal))id=id.Substring(8);
            snapshot.Roots[mod]=id;snapshot.Aliases[OverviewDependencies.Identity(mod)]=id;
            foreach(string guid in mod.PluginGuids.Concat(new[]{mod.Guid}).Where(g=>g.Length>0))
            {snapshot.Aliases["guid:"+guid]=id;snapshot.Aliases[guid]=id;}
        }
        foreach(var dependency in mods.SelectMany(m=>m.RuntimeDependencies).Where(d=>d.Id.Length>0))
            snapshot.Aliases["missing:"+dependency.Id]="guid:"+dependency.Id;
        foreach(var component in components)snapshot.Aliases["package:"+component.Key]=component.Key;
        if(raw!=null)
        {
            foreach(var node in raw.Components)
            {
                if(IsFramework(node.PackageKey)){snapshot.Aliases[node.Id]=node.PackageKey;continue;}
                if(IsFramework(node.Id))continue;
                var matches=node.PluginGuids.Where(guidMods.ContainsKey).SelectMany(g=>guidMods[g]).Distinct().ToArray();
                if(matches.Length==0)matches=node.Locations.Select(FullPath).Where(pathMods.ContainsKey).SelectMany(p=>pathMods[p]).Distinct().ToArray();
                var location=components.FirstOrDefault(c=>node.Locations.Any(p=>c.Locations.Any(l=>
                    FullPath(p).StartsWith(FullPath(l.Path)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))));
                string id=ShopInstalled.Key(node.PackageKey)?node.PackageKey:location?.Key
                    ??(matches.Select(m=>Resolve(snapshot,snapshot.Roots[m])).Distinct(StringComparer.OrdinalIgnoreCase).Count()==1
                        ?Resolve(snapshot,snapshot.Roots[matches[0]]):Resolve(snapshot,node.Id));
                snapshot.Aliases[node.Id]=id;
                if(ShopInstalled.Key(node.PackageKey))snapshot.Aliases["package:"+node.PackageKey]=id;
                foreach(string guid in node.PluginGuids.Where(g=>g.Length>0))
                {snapshot.Aliases["guid:"+guid]=id;snapshot.Aliases[guid]=id;snapshot.Aliases["missing:"+guid]=id;}
                foreach(var mod in matches)
                {
                    snapshot.Aliases[snapshot.Roots[mod]]=id;snapshot.Roots[mod]=id;
                    snapshot.Aliases[OverviewDependencies.Identity(mod)]=id;
                    foreach(string guid in mod.PluginGuids.Concat(new[]{mod.Guid}).Where(g=>g.Length>0))
                    {snapshot.Aliases["guid:"+guid]=id;snapshot.Aliases[guid]=id;}
                }
            }
        }
        string Id(string id)
        {
            string resolved=Resolve(snapshot,id);
            if(resolved.StartsWith("package:",StringComparison.OrdinalIgnoreCase)&&ShopInstalled.Key(resolved.Substring(8)))return resolved.Substring(8);
            return resolved;
        }
        ComponentDependencyGraph.Component Node(string id)
        {
            id=Id(id);var node=snapshot.Graph.Components.FirstOrDefault(c=>c.Id.Equals(id,StringComparison.OrdinalIgnoreCase));
            if(node==null){node=new(){Id=id,PackageKey=ShopInstalled.Key(id)?id:""};snapshot.Graph.Components.Add(node);}return node;
        }
        void Metadata(string id,OverviewDependencyItem item)
        {
            id=Id(id);if(IsFramework(id))return;
            if(!snapshot.Items.TryGetValue(id,out var prior)||prior.Mod==null&&item.Mod!=null||!prior.Installed&&item.Installed)
                snapshot.Items[id]=new(){Identity=id,Name=item.Name,Version=item.Version,Mod=item.Mod,Installed=item.Installed,Unknown=item.Unknown};
            if(item.Installed)Node(id);
        }
        void Edge(string from,string to,bool hard,string evidence,string required="")
        {
            from=Id(from);to=Id(to);
            if(IsFramework(from)||IsFramework(to)||from.Equals(to,StringComparison.OrdinalIgnoreCase))return;
            snapshot.Graph.Edges.Add(new(){From=from,To=to,Hard=hard,Evidence=evidence});
            if(required.Length>0)
            {
                string key=EdgeKey(from,to);
                if(!snapshot.Required.TryGetValue(key,out string? previous)||Version.TryParse(required,out var next)
                    &&(!Version.TryParse(previous,out var old)||next>old))snapshot.Required[key]=required;
            }
        }
        if(raw!=null)
        {
            foreach(var original in raw.Components)
            {
                string id=Id(original.Id);if(IsFramework(id))continue;
                var node=Node(id);node.PluginGuids.AddRange(original.PluginGuids);node.Locations.AddRange(original.Locations);node.Assemblies.AddRange(original.Assemblies);
            }
            var assemblyProviders = raw.Components.SelectMany(c => c.Assemblies.Distinct(StringComparer.OrdinalIgnoreCase).Select(a => (Assembly: a, Owner: c.Id)))
                .GroupBy(p => p.Assembly, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Owner).Distinct(StringComparer.OrdinalIgnoreCase).Count(), StringComparer.OrdinalIgnoreCase);
            var installedNodes = new HashSet<string>(raw.Components.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
            foreach(var edge in raw.Edges)
            {
                // Assembly resolution may have several physical providers because mods bundle
                // Harmony/Cecil/etc. Those are library references, not dependencies on every
                // mod that happens to ship that DLL. Keep backend safety evidence untouched.
                const string assemblyPrefix = "assembly ref ";
                if (edge.Evidence.StartsWith(assemblyPrefix, StringComparison.Ordinal))
                {
                    string assembly = edge.Evidence.Substring(assemblyPrefix.Length);
                    if (!assemblyProviders.TryGetValue(assembly, out int providers) || providers <= 1) { Edge(edge.From, edge.To, edge.Hard, edge.Evidence); continue; }
                    string target = "assembly:" + assembly;
                    bool installed = installedNodes.Contains(edge.To);
                    Metadata(target, new() { Name = assembly + ".dll", Installed = installed });
                    Edge(edge.From, target, edge.Hard, edge.Evidence);
                }
                else if (!edge.Evidence.StartsWith("possible provider for unresolved ", StringComparison.Ordinal))
                    Edge(edge.From,edge.To,edge.Hard,edge.Evidence);
            }
        }
        foreach(var mod in mods)
        {
            string id=Id(snapshot.Roots[mod]);snapshot.Roots[mod]=id;
            Metadata(id,new(){Name=mod.Name,Mod=mod,Installed=true,Version=mod.PackageVersion.Length>0?mod.PackageVersion:mod.PluginVersion});
            Node(id).PluginGuids.AddRange(mod.PluginGuids.Concat(new[]{mod.Guid}).Where(g=>g.Length>0));
        }
        foreach(var component in components)Metadata(component.Key,new(){Name=component.Name,Version=component.Version,Installed=true,Unknown=!component.IdentityConfirmed});
        foreach(var item in ordinary.Nodes)Metadata(item.Identity,item);
        foreach(var relation in ordinary.Relationships)
        {
            Metadata(relation.Target.Identity,relation.Target);
            Edge(relation.Source.Identity,relation.Target.Identity,!relation.Target.Optional,"registered dependency",relation.Target.Required);
        }
        foreach(var component in components)
        foreach(string declaration in component.Dependencies)
        {
            var match=Regex.Match(declaration,@"^([A-Za-z0-9_]+-[A-Za-z0-9_]+)-(\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?)$");
            if(match.Success)Edge(component.Key,"package:"+match.Groups[1].Value,true,"component manifest",match.Groups[2].Value);
        }
        // The marker also makes accidental policy queries on this display projection fail closed.
        snapshot.Graph.AddFailure("Display-only relationship snapshot; file operations require the original backend graph.");
        return snapshot;
    }
}
