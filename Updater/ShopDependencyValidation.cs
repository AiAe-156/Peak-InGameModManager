using Mono.Cecil;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Metadata-only validation of the actual next plugin tree; never loads plugin code.</summary>
internal static class ShopDependencyValidation
{
    internal sealed record Requirement(string Guid, string? Minimum, bool Optional);
    internal sealed record Plugin(string Guid, string Version, string Path, bool Enabled, List<Requirement> Dependencies);

    internal sealed class Result { internal List<string> Hard = new(), Warnings = new(); }
    internal static List<string> Check(string root, string stage, IReadOnlyList<PackFile> originals, IReadOnlyList<PackFile> desired)
    { var result = Analyze(root, stage, originals, desired); return result.Hard.Concat(result.Warnings).ToList(); }
    internal static Result Analyze(string root, string stage, IReadOnlyList<PackFile> originals, IReadOnlyList<PackFile> desired)
    {
        var removed = originals.Select(f => f.Path).ToHashSet(Files.Compare);
        var incoming = desired.Where(f => IsPluginAssembly(f.Path)).ToDictionary(f => f.Path, Files.Compare);
        var plugins = new List<Plugin>();
        string live = Path.Combine(root, "plugins");
        foreach (var file in PackMaintenance.Walk(root, live))
        {
            string relative = "plugins/" + file.Rel.Replace('\\', '/');
            if (!Packages.IsAssembly(relative) || removed.Contains(relative) || incoming.ContainsKey(relative)) continue;
            Read(Files.Child(root, relative), relative, plugins);
        }
        foreach (var file in incoming.Values)
        {
            string full = Files.Child(stage, file.Path);
            Files.RejectLinks(stage, full);
            if (!File.Exists(full) || Files.Hash(full) != file.Hash) throw new IOException("Staging file changed: " + file.Path);
            Read(full, file.Path, plugins);
        }
        var result = new Result();
        foreach (var group in plugins.GroupBy(p => p.Guid, Files.Compare).Where(g => g.Count() > 1))
            result.Hard.Add("Duplicate plugin GUID: " + group.Key + " / " + string.Join(", ", group.Select(p => p.Path)));
        var baseline = new List<Plugin>();
        foreach (var file in PackMaintenance.Walk(root, live))
            if (Packages.IsAssembly(file.Rel)) Read(Files.Child(live,file.Rel), "plugins/" + file.Rel.Replace((char)92,'/'), baseline);
        var oldIssues = new HashSet<string>(Validate(baseline), StringComparer.Ordinal);
        result.Warnings.AddRange(Validate(plugins).Where(i => !oldIssues.Contains(i) && !result.Hard.Contains(i)));
        var assemblyIssues = CheckAssemblyConsumers(root,stage,originals,desired);
        var required = new HashSet<string>(Files.Compare) { ManagerIdentity.Guid }; // 2026-11-12：旧 GUID 是简化版 MCE，普通模组，可移除
        bool changed;
        do { changed = false; foreach (var plugin in baseline.Where(p => required.Contains(p.Guid)))
            foreach (var dependency in plugin.Dependencies.Where(d => !d.Optional)) changed |= required.Add(dependency.Guid); } while(changed);
        foreach (var plugin in baseline.Where(p => required.Contains(p.Guid)))
        {
            var final = plugins.FirstOrDefault(p => Files.Compare.Equals(p.Guid,plugin.Guid) && p.Enabled);
            if (final == null || Validate(plugins).Any(i => i.StartsWith(final.Path + " (",StringComparison.Ordinal)))
                result.Hard.Add("Required MCE plugin chain cannot be removed or broken: " + plugin.Guid);
        }
        foreach (string issue in assemblyIssues)
        {
            if (baseline.Where(p => required.Contains(p.Guid)).Any(p => issue.StartsWith(p.Path + " requires assembly ", StringComparison.Ordinal))) result.Hard.Add(issue);
            else result.Warnings.Add(issue);
        }
        return result;
    }

    static List<string> CheckAssemblyConsumers(string root,string stage,IReadOnlyList<PackFile> originals,IReadOnlyList<PackFile> desired)
    {
        var issues=new List<string>();
        (string Name,Version Version,List<(string Name,Version Version)> References)? Metadata(string file)
        {
            try{using var a=AssemblyDefinition.ReadAssembly(file);return(a.Name.Name,a.Name.Version,a.MainModule.AssemblyReferences.Select(r=>(r.Name,r.Version)).ToList());}
            catch(BadImageFormatException){return null;}
        }
        var removed=originals.Where(f=>Packages.IsAssembly(f.Path)&&File.Exists(Files.Child(root,f.Path))).Select(f=>Metadata(Files.Child(root,f.Path))).Where(m=>m!=null).Select(m=>m!.Value.Name).ToHashSet(Files.Compare);
        // Validate new core/patcher references too, even on a clean install with no removed files.
        var incomingNames=desired.Where(f=>f.Path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).Select(f=>Metadata(Files.Child(stage,f.Path))).Where(m=>m!=null).Select(m=>m!.Value.Name);
        removed.UnionWith(incomingNames);
        if(removed.Count==0)return issues;
        var final=new List<(string Path,string Name,Version Version,List<(string Name,Version Version)> References)>();
        foreach(string area in new[]{"plugins","patchers","core"})foreach(var f in PackMaintenance.Walk(root,Path.Combine(root,area)))
        {
            string rel=area+"/"+f.Rel.Replace((char)92,'/');
            if(!rel.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||originals.Any(o=>Files.Compare.Equals(o.Path,rel))||desired.Any(d=>Files.Compare.Equals(d.Path,rel)))continue;
            var m=Metadata(Files.Child(root,rel));if(m!=null)final.Add((rel,m.Value.Name,m.Value.Version,m.Value.References));
        }
        foreach(var f in desired.Where(f=>f.Path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)))
        {var m=Metadata(Files.Child(stage,f.Path));if(m!=null)final.Add((f.Path,m.Value.Name,m.Value.Version,m.Value.References));}
        foreach(var owner in final)foreach(var reference in owner.References.Where(r=>removed.Contains(r.Name)))
            if(!final.Any(p=>Files.Compare.Equals(p.Name,reference.Name)&&p.Version>=reference.Version))issues.Add(owner.Path+" requires assembly "+reference.Name+" >= "+reference.Version+"; final environment cannot satisfy this hard reference.");
        return issues;
    }
    static bool IsPluginAssembly(string path) => path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) && Packages.IsAssembly(path);

    internal static List<string> Validate(IReadOnlyList<Plugin> plugins)
    {
        var issues = new List<string>();
        // Disabled copies still reserve identities, but cannot provide a loadable dependency.
        foreach (var group in plugins.GroupBy(p => p.Guid, Files.Compare).Where(g => g.Count() > 1))
            issues.Add("Duplicate plugin GUID: " + group.Key + " / " + string.Join(", ", group.Select(p => p.Path)));
        var providers = plugins.Where(p => p.Enabled).GroupBy(p => p.Guid, Files.Compare).ToDictionary(g => g.Key, g => g.ToList(), Files.Compare);
        foreach (var plugin in plugins.Where(p => p.Enabled))
        foreach (var dependency in plugin.Dependencies.Where(d => !d.Optional))
        {
            if (!providers.TryGetValue(dependency.Guid, out var matches))
                issues.Add(plugin.Path + " (" + plugin.Guid + ") requires hard dependency " + dependency.Guid + " (enabled)" + (dependency.Minimum == null ? "" : " >= " + dependency.Minimum));
            else if (dependency.Minimum != null && (!Version.TryParse(dependency.Minimum, out var minimum) || !matches.Any(p => Version.TryParse(p.Version, out var actual) && actual >= minimum)))
                issues.Add(plugin.Path + " (" + plugin.Guid + ") requires " + dependency.Guid + " >= " + dependency.Minimum + "; final version: " + string.Join(", ", matches.Select(p => p.Version)));
        }
        return issues;
    }

    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; }
    }

    static void Read(string file, string relative, List<Plugin> plugins)
    {
        try
        {
            // Same Cecil metadata inspection as Packages/PackMaintenance, without reflection loading.
            using var assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { ReadSymbols = false, ReadingMode = ReadingMode.Deferred });
            foreach (var type in Types(assembly.MainModule.Types))
            {
                var identities = type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInPlugin" && a.ConstructorArguments.Count == 3).ToList();
                if (identities.Count == 0) continue;
                var dependencies = new List<Requirement>();
                foreach (var attribute in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency"))
                {
                    if (attribute.ConstructorArguments.Count == 0 || attribute.ConstructorArguments[0].Value is not string guid) throw new IOException("Invalid dependency metadata: " + relative);
                    string? minimum = null; bool optional = false;
                    if (attribute.ConstructorArguments.Count > 1)
                    {
                        var value = attribute.ConstructorArguments[1].Value;
                        if (value is string version) minimum = version;
                        else optional = (Convert.ToInt32(value) & 2) != 0;
                    }
                    dependencies.Add(new(guid, minimum, optional));
                }
                foreach (var identity in identities)
                    plugins.Add(new(identity.ConstructorArguments[0].Value.ToString()!, identity.ConstructorArguments[2].Value.ToString()!, relative, relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), dependencies));
            }
        }
        catch (BadImageFormatException) { } // Native DLLs have no BepInPlugin metadata.
    }
}
