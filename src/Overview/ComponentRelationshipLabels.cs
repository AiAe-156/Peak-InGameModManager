#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

internal sealed class ComponentRelationshipLabels
{
    private sealed class Lookup
    {
        internal IReadOnlyList<OverviewMod>? ModsInput;
        internal IReadOnlyList<InfrastructureOverviewModel>? ComponentsInput;
        internal readonly Dictionary<string,OverviewMod?> Mods=new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string,InfrastructureOverviewModel?> Components=new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string,string> Canonical=new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string,(int Order,OverviewMod Mod)> Direct=new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string,(int Order,OverviewMod Mod)> Packages=new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string,(int Order,OverviewMod Mod)> Guids=new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string,ModConfigEnhance.Updating.ComponentDependencyGraph.Component> Nodes=new(StringComparer.OrdinalIgnoreCase);
        internal bool Ready;
    }
    private static readonly ConditionalWeakTable<ComponentDependencyGraph,Lookup> Lookups=new();
    private readonly Lookup _lookup;
    private readonly ComponentDependencyGraph _graph;
    private readonly IReadOnlyList<OverviewMod> _mods;
    private readonly IReadOnlyList<InfrastructureOverviewModel> _components;
    internal ComponentRelationshipLabels(ComponentDependencyGraph graph, IReadOnlyList<OverviewMod> mods,
        IReadOnlyList<InfrastructureOverviewModel> components)
    {
        _graph=graph;_mods=mods;_components=components.Count>0?components:SharedRelationshipSnapshot.Components(graph);
        var shared=Lookups.GetValue(graph,_=>new Lookup {ModsInput=_mods,ComponentsInput=_components});
        // Details may supply a different display model over the same backend graph.
        // Never reuse positive/negative identity results across those inputs.
        _lookup=ReferenceEquals(shared.ModsInput,_mods)&&ReferenceEquals(shared.ComponentsInput,_components)
            ?shared:new Lookup {ModsInput=_mods,ComponentsInput=_components};
        if(!_lookup.Ready)BuildIndex();
    }
    private void BuildIndex()
    {
        for(int i=0;i<_mods.Count;i++)
        {
            var mod=_mods[i];var value=(i,mod);
            if(mod.Guid.Length>0)
            {
                if(!_lookup.Direct.ContainsKey(mod.Guid))_lookup.Direct[mod.Guid]=value;
                if(!_lookup.Direct.ContainsKey("guid:"+mod.Guid))_lookup.Direct["guid:"+mod.Guid]=value;
            }
            foreach(string guid in mod.PluginGuids.Concat(new[]{mod.Guid}))
                if(guid.Length>0&&!_lookup.Guids.ContainsKey(guid))_lookup.Guids[guid]=value;
            if(mod.SourceConfirmed&&mod.IdentityConflict.Length==0&&ShopInstalled.Key(mod.SourceKey)&&!_lookup.Packages.ContainsKey(mod.SourceKey))
                _lookup.Packages[mod.SourceKey]=value;
        }
        foreach(var node in _graph.Components)if(!_lookup.Nodes.ContainsKey(node.Id))_lookup.Nodes[node.Id]=node;
        foreach(var component in _components)if(!_lookup.Components.ContainsKey(component.Key))_lookup.Components[component.Key]=component;
        _lookup.Ready=true;
    }

    internal string Canonical(string id)
    {
        if(_lookup.Canonical.TryGetValue(id,out var cached))return cached;
        string result=ResolveCanonical(id);_lookup.Canonical[id]=result;return result;
    }
    private string ResolveCanonical(string id)
    {
        string shared=SharedRelationshipSnapshot.Canonical(_graph,id);
        if(!shared.Equals(id,StringComparison.OrdinalIgnoreCase))return shared;
        if (!id.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return id;
        string relative = id.Substring(5).Replace('/', Path.DirectorySeparatorChar);
        string path = Path.Combine(BepInEx.Paths.BepInExRootPath, relative);
        // Unconfirmed folders also group their file nodes for display, under their local file: ID.
        // This projection is never supplied to the backend's ownership or permission checks.
        var package = _components.FirstOrDefault(c => c.Locations.Any(l =>
            path.StartsWith(l.Path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        return package?.Key ?? id;
    }
    internal OverviewMod? Mod(string id)
    {
        if(_lookup.Mods.TryGetValue(id,out var cached))return cached;
        var result=ResolveMod(id);_lookup.Mods[id]=result;return result;
    }
    private OverviewMod? ResolveMod(string id)
    {
        var registered=SharedRelationshipSnapshot.Metadata(_graph,id)?.Mod;
        if(registered!=null)return registered;
        _lookup.Nodes.TryGetValue(id,out var node);
        int order=int.MaxValue;OverviewMod? best=null;
        void Candidate((int Order,OverviewMod Mod) value) { if(value.Order<order) { order=value.Order;best=value.Mod; } }
        if(_lookup.Direct.TryGetValue(id,out var direct))Candidate(direct);
        if(_lookup.Packages.TryGetValue(node?.PackageKey??id,out var package))Candidate(package);
        if(node!=null)foreach(string guid in node.PluginGuids)if(_lookup.Guids.TryGetValue(guid,out var found))Candidate(found);
        return best;
    }
    internal InfrastructureOverviewModel? Component(string id)
    {
        if(_lookup.Components.TryGetValue(id,out var cached))return cached;
        var result=_components.FirstOrDefault(c=>c.Key.Equals(id,StringComparison.OrdinalIgnoreCase));
        _lookup.Components[id]=result;return result;
    }
    internal string Name(string id)
    {
        var mod = Mod(id); if (mod != null) return OverviewLanguageBridge.GetDisplayName(mod);
        var component = Component(id); if (component != null) return component.Name;
        var metadata=SharedRelationshipSnapshot.Metadata(_graph,id);if(metadata!=null)return metadata.Name;
        if (id.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || id.StartsWith("shared:", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(id.Substring(id.IndexOf(':') + 1).Replace('/', Path.DirectorySeparatorChar));
        return id.StartsWith("missing:", StringComparison.OrdinalIgnoreCase) ? id.Substring(8) : id;
    }
    internal string Version(string id)
    {
        var mod = Mod(id); return mod != null ? (mod.PackageVersion.Length > 0 ? mod.PackageVersion : mod.PluginVersion) : Component(id)?.Version ?? SharedRelationshipSnapshot.Metadata(_graph,id)?.Version ?? "";
    }
    internal string Original(string id)
    {
        var mod = Mod(id);
        return mod != null ? (string.IsNullOrWhiteSpace(mod.RawName) ? mod.Name : mod.RawName) : Component(id)?.Name ?? Name(id);
    }
    internal string State(ComponentRelationshipTree.Row row)
    {
        var notes = new List<string>(); var mod = Mod(row.Id);
        if (row.Missing) notes.Add(Loc.T("Not found", "未找到", "找不到"));
        else if (mod != null) {
            notes.Add(mod.Loaded ? Loc.T("Loaded", "已加载", "已加載") : Loc.T("Not loaded", "未加载", "未加載"));
            if (!mod.TargetEnabled) notes.Add(Loc.T("Disabled", "已停用", "已停用"));
        }
        else if (Component(row.Id) is { } component) {
            notes.Add(Loc.T("Installed", "已安装", "已安裝"));
            if (!component.IdentityConfirmed) notes.Add(Loc.T("Identity unconfirmed", "身份未确认", "身分未確認"));
        }
        if (row.Optional) notes.Add(Loc.T("Optional", "可选", "可選"));
        if (row.Cycle) notes.Add(Loc.T("Circular dependency", "循环依赖", "循環依賴"));
        else if (row.Shared) notes.Add(Loc.T("Shared reference", "共享引用", "共用參照"));
        return string.Join(" · ", notes);
    }
}
