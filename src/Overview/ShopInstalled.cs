#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ModConfigEnhance.Updating;
namespace ModConfigEnhance.Overview;

/// <summary>Display-only evidence. Never confers ownership or permission to mutate a package.</summary>
internal static class ShopInstalled
{
    internal sealed class Evidence
    {
        internal string Key="",Version="",Path="",Area="",Name="";
        internal bool Disabled, Cached;
    }
    internal sealed class Snapshot
    {
        internal string Context="", Failure="";
        internal Dictionary<string,List<Evidence>> Packages=new(StringComparer.OrdinalIgnoreCase);
        internal List<Evidence> Unresolved=new();
        internal bool Has(string key)=>Packages.ContainsKey(key);
        internal string Version(string key)=>Packages.TryGetValue(key,out var rows)?string.Join(" / ",rows.Select(r=>r.Version.Length>0?r.Version:"?").Distinct()):"";
        internal bool Updates(string key,string latest)=>Packages.TryGetValue(key,out var rows)&&rows.All(r=>System.Version.TryParse(r.Version,out var v)&&System.Version.TryParse(latest,out var next)&&next>v);
    }
    internal static bool Valid(string value)=>Regex.IsMatch(value??"",@"^[A-Za-z0-9_]+$");
    internal static bool Key(string key)=>key.Split('-') is var parts&&parts.Length==2&&parts.All(Valid);
    internal static bool Runtime(string path)=>ModConfigEnhance.Updating.PluginFileNames.IsAssembly(path);
    internal static IEnumerable<string> Files(string root,Action<string>? failure=null)
    {
        var pending=new Stack<string>();pending.Push(root);
        while(pending.Count>0){string folder=pending.Pop();string[] files,dirs;
            try{if(!Directory.Exists(folder)||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;files=Directory.GetFiles(folder);dirs=Directory.GetDirectories(folder);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){failure?.Invoke(e.Message);continue;}
            foreach(var file in files){bool safe=false;try{safe=(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0;}catch(Exception e) when(e is IOException or UnauthorizedAccessException){failure?.Invoke(e.Message);}if(safe)yield return file;}
            foreach(var dir in dirs)if(!new[]{"backup","backups","备份","bin","obj","Demo","验证"}.Contains(System.IO.Path.GetFileName(dir),StringComparer.OrdinalIgnoreCase)&&!System.IO.Path.GetFileName(dir).StartsWith("待安装",StringComparison.OrdinalIgnoreCase))pending.Push(dir);
        }
    }
    internal static Evidence? Manifest(string file,string area,string confirmedKey="")
    {
        try{
            var data=JObject.Parse(File.ReadAllText(file));string name=data.Value<string>("name")??"",team=data.Value<string>("namespace")??data.Value<string>("author")??"",version=data.Value<string>("version_number")??"";
            string folder=System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file))??"";var parts=folder.Split('-');
            if(team.Length==0&&parts.Length==2&&parts[1]==name)team=parts[0];
            if(team.Length==0&&Key(confirmedKey)&&confirmedKey.Split('-')[1]==name)team=confirmedKey.Split('-')[0];
            if(!Valid(team)||!Valid(name)||!System.Version.TryParse(version,out _))return null;
            string key=team+"-"+name,full=data.Value<string>("FullName")??"";
            if(full.Length>0&&!full.Equals(key,StringComparison.OrdinalIgnoreCase))return null;
            if(parts.Length==2&&parts[1]==name&&!parts[0].Equals(team,StringComparison.OrdinalIgnoreCase))return null;
            return new Evidence{Key=key,Version=version,Path=System.IO.Path.GetDirectoryName(file)!,Area=area};
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException or InvalidCastException){return null;}
    }
    internal static Snapshot Build(string root,string context,string ledgerPath,IEnumerable<Evidence> confirmed)
    {
        var snapshot=new Snapshot{Context=context};var evidence=new List<Evidence>();
        var sources=confirmed.ToList();
        // One inventory walk per refresh, not per card. Stored packs and system directories are excluded.
        var roots=new List<(string path,string area)>{(System.IO.Path.Combine(root,"plugins"),"plugins"),(System.IO.Path.Combine(root,"core"),"core"),(System.IO.Path.Combine(root,"patchers"),"patchers")};
        if(Directory.Exists(root))foreach(var dir in Directory.GetDirectories(root))if(!new[]{"plugins","core","patchers","config",ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory,ModConfigEnhance.Updating.ManagerIdentity.LegacyRuntimeDirectory,"cache","logs","monomod","interop","unhollowed"}.Contains(System.IO.Path.GetFileName(dir),StringComparer.OrdinalIgnoreCase))roots.Add((dir,"cache"));
        var all=roots.SelectMany(r=>Files(r.path,error=>snapshot.Failure=error).Select(f=>(file:System.IO.Path.GetFullPath(f),area:r.area))).ToList();
        var paths=new HashSet<string>(all.Select(f=>System.IO.Path.GetFullPath(f.file)),StringComparer.OrdinalIgnoreCase);
        var manifests=all.Where(f=>System.IO.Path.GetFileName(f.file).Equals("manifest.json",StringComparison.OrdinalIgnoreCase)).Select(f=>System.IO.Path.GetDirectoryName(f.file)!).ToList();
        Evidence? Resolve(string file,string area){string folder=System.IO.Path.GetDirectoryName(file)!;var hints=sources.Where(s=>Key(s.Key)&&System.IO.Path.GetFullPath(s.Path).StartsWith(folder+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).Select(s=>s.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();return Manifest(file,area,hints.Count==1?hints[0]:"");}
        var invalidManifests=new HashSet<string>(all.Where(f=>System.IO.Path.GetFileName(f.file).Equals("manifest.json",StringComparison.OrdinalIgnoreCase)&&Resolve(f.file,f.area)==null).Select(f=>System.IO.Path.GetDirectoryName(f.file)!),StringComparer.OrdinalIgnoreCase);
        foreach(var item in all.Where(f=>System.IO.Path.GetFileName(f.file).Equals("manifest.json",StringComparison.OrdinalIgnoreCase))){var row=Resolve(item.file,item.area);if(row==null)continue;string prefix=row.Path+System.IO.Path.DirectorySeparatorChar;var payload=all.Where(f=>f.file.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Runtime(f.file)&&!manifests.Any(m=>m.Length>row.Path.Length&&f.file.StartsWith(m+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))).ToList();if(payload.Count==0)continue;row.Disabled=payload.All(f=>!f.file.EndsWith(".dll",StringComparison.OrdinalIgnoreCase));row.Cached=item.area=="cache";evidence.Add(row);}
        foreach(var row in sources)if(Key(row.Key)&&paths.Contains(System.IO.Path.GetFullPath(row.Path))&&Runtime(row.Path)&&!invalidManifests.Any(m=>System.IO.Path.GetFullPath(row.Path).StartsWith(m+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))){ 
            row.Path=System.IO.Path.GetFullPath(row.Path);var match=all.First(f=>f.file.Equals(row.Path,StringComparison.OrdinalIgnoreCase));row.Area=match.area;row.Cached=match.area=="cache";row.Disabled=!row.Path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase);
            // Explicit confirmed source wins for a custom directory but cannot relabel a contradictory manifest.
            if(!evidence.Any(e=>row.Path.StartsWith(e.Path+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&!e.Key.Equals(row.Key,StringComparison.OrdinalIgnoreCase))){
                var manifest=evidence.FirstOrDefault(e=>e.Key.Equals(row.Key,StringComparison.OrdinalIgnoreCase)&&row.Path.StartsWith(e.Path+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                if(manifest==null)evidence.Add(row);
            }
        }
        try{var ledger=File.Exists(ledgerPath)?JsonConvert.DeserializeObject<ShopLedger>(File.ReadAllText(ledgerPath)):null;
            foreach(var package in ledger?.Packages??new()){
                if(!Key(package.Key))continue;
                var actual=package.Files.Select(f=>f.Path).Where(p=>!System.IO.Path.IsPathRooted(p)&&!p.Replace('\\','/').Split('/').Contains("..")).Select(p=>System.IO.Path.GetFullPath(System.IO.Path.Combine(root,p))).Where(p=>paths.Contains(p)&&Runtime(p)).ToList();
                // A stale receipt without a real runtime payload is not installed.
                actual=actual.Where(p=>!invalidManifests.Any(m=>p.StartsWith(m+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))).ToList();
                if(actual.Count==0)continue;
                foreach(var path in actual)if(!evidence.Any(e=>(path.StartsWith(e.Path+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))){string area=all.First(f=>f.file.Equals(path,StringComparison.OrdinalIgnoreCase)).area;evidence.Add(new Evidence{Key=package.Key,Version=package.Version,Path=path,Area=area,Cached=area=="cache",Disabled=!path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)});}
            }
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException){snapshot.Failure=e.Message;}
        // Conflicting claims for the exact same payload are ambiguous, not two installed packages.
        var ambiguous=new HashSet<string>(evidence.GroupBy(e=>e.Path,StringComparer.OrdinalIgnoreCase).Where(g=>g.Select(e=>e.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1).Select(g=>g.Key),StringComparer.OrdinalIgnoreCase);
        foreach(var group in evidence.Where(e=>!ambiguous.Contains(e.Path)).GroupBy(e=>e.Key,StringComparer.OrdinalIgnoreCase))snapshot.Packages[group.Key]=group.GroupBy(e=>e.Version+"|"+e.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
        foreach(var item in all.Where(f=>System.IO.Path.GetFileName(f.file).Equals("manifest.json",StringComparison.OrdinalIgnoreCase)&&invalidManifests.Contains(System.IO.Path.GetDirectoryName(f.file)!)))
        {
            try {
                var data=JObject.Parse(File.ReadAllText(item.file));string name=data.Value<string>("name")??"",folder=System.IO.Path.GetDirectoryName(item.file)!;
                var payload=all.Where(f=>f.file.StartsWith(folder+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Runtime(f.file)
                    &&!manifests.Any(m=>m.Length>folder.Length&&f.file.StartsWith(m+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))).ToList();
                if(Valid(name)&&payload.Count>0&&!evidence.Any(e=>e.Path.Equals(folder,StringComparison.OrdinalIgnoreCase)))
                    snapshot.Unresolved.Add(new Evidence{Name=name,Version=data.Value<string>("version_number")??"",Path=folder,Area=item.area,Cached=item.area=="cache",Disabled=payload.All(f=>!f.file.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))});
            } catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) {snapshot.Failure=e.Message;}
        }
        return snapshot;
    }
}
