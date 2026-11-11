#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
namespace ModConfigEnhance.Overview;
internal static partial class PackPublisher
{
    internal static List<string> MissingTargets(OverviewRepository repo,PublishRequest request,IReadOnlyList<OverviewMod> inventory) => repo.Profiles.ListProfiles().Where(p=>request.Profiles.Contains(p.id)).SelectMany(p=>p.mods).Where(m=>!inventory.Any(i=>i.Guid.Equals(m.guid,StringComparison.OrdinalIgnoreCase))).GroupBy(m=>m.guid,StringComparer.OrdinalIgnoreCase).Select(g=>(g.Any(m=>m.enabled)?"Missing enabled mod / 缺少启用模组：":"Disabled mod not bundled / 停用模组未附带：")+g.Key).ToList();
    internal static string PublicationRelative(string source)
    {
        string relative = Relative(Paths.BepInExRootPath,source).Replace('\\','/');
        return new[]{"plugins/","patchers/","monomod/","core/"}.Any(p=>relative.StartsWith(p,StringComparison.OrdinalIgnoreCase)) ? relative : "plugins/"+relative;
    }
    internal static List<OverviewMod> SelectInventory(OverviewRepository repo, PublishRequest request, IReadOnlyList<OverviewMod> inventory)
    {
        var profiles=repo.Profiles.ListProfiles().Where(p=>request.Profiles.Contains(p.id)).ToList();
        foreach(var profile in profiles)
        foreach(var duplicate in profile.mods.GroupBy(m=>m.guid,StringComparer.OrdinalIgnoreCase).Where(g=>g.Select(m=>m.enabled).Distinct().Count()>1))
            throw new InvalidOperationException("方案内同一模组的启停目标冲突："+duplicate.Key);
        foreach(var profile in profiles)
        foreach(var duplicate in profile.config.values.GroupBy(v=>v.Identity).Where(g=>g.Select(v=>v.serializedValue).Distinct(StringComparer.Ordinal).Count()>1))
            throw new InvalidOperationException("方案内同一配置项存在冲突值："+duplicate.Key);
        var needed=new HashSet<string>(profiles.SelectMany(p=>p.mods).Select(m=>m.guid).Where(g=>g.Length>0),StringComparer.OrdinalIgnoreCase) { Plugin.PluginGuid };
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Inside(string file,string root)
        {file=Path.GetFullPath(file);root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);return file.Equals(root,StringComparison.OrdinalIgnoreCase)||file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        foreach(var mod in inventory) if(request.ExtraFiles.Any(p=>Inside(mod.PackageRoot,Path.Combine(GameRoot,p))) || request.InlineRoots.Contains(mod.PackageRoot) || request.Dependencies.ContainsKey(mod.PackageRoot)) roots.Add(mod.PackageRoot);
        bool changed;
        do
        {
            changed=false;
            foreach(var mod in inventory.Where(m=>needed.Contains(m.Guid)||roots.Contains(m.PackageRoot)).ToList())
            {
                changed|=roots.Add(mod.PackageRoot);
                foreach(var member in inventory.Where(m=>m.PackageRoot.Equals(mod.PackageRoot,StringComparison.OrdinalIgnoreCase))) changed|=needed.Add(member.Guid);
                foreach(var dep in mod.RuntimeDependencies.Where(d=>!d.Optional&&!d.Id.StartsWith("BepInEx",StringComparison.OrdinalIgnoreCase))) changed|=needed.Add(dep.Id);
                foreach(string dependency in mod.PackageDependencies)
                { int dash=dependency.LastIndexOf('-'); if(dash<=0) continue; string key=dependency.Substring(0,dash); foreach(var provider in inventory.Where(m=>m.SourceKey.Equals(key,StringComparison.OrdinalIgnoreCase))) changed|=roots.Add(provider.PackageRoot); }
            }
        } while(changed);
        var selected=inventory.Where(m=>roots.Contains(m.PackageRoot)).ToList();
        if(CompleteMode(request.Mode))
        {
            var unknown=new List<string>();
            foreach(string route in new[]{"patchers","monomod"})
            foreach(string file in OverviewRepository.SafeFiles(Path.Combine(Paths.BepInExRootPath,route)))
                if(Keep(Relative(Paths.BepInExRootPath,file))&&!inventory.Any(m=>Inside(file,m.PackageRoot))&&!request.ExtraFiles.Any(p=>Inside(file,Path.Combine(GameRoot,p)))&&!request.ExcludedExtras.Keys.Any(p=>Inside(file,Path.Combine(GameRoot,p)))) unknown.Add(Relative(Paths.BepInExRootPath,file));
            if(unknown.Count>0) throw new InvalidOperationException("启动补丁/资源归属待确认，请在附加文件中明确选择：\n"+string.Join("\n",unknown));
        }
        foreach(var duplicate in selected.Where(m=>!m.Guid.StartsWith("package:",StringComparison.OrdinalIgnoreCase)).GroupBy(m=>m.Guid,StringComparer.OrdinalIgnoreCase).Where(g=>g.Select(m=>m.PackageRoot).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1))
            throw new InvalidOperationException("发布包含重复模组，请确认保留的副本："+duplicate.Key+"\n"+string.Join("\n",duplicate.Select(m=>m.PackageRoot)));
        return selected;
    }
}
