#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;
/// <summary>Read-only inventory of loader libraries and preload packages, separate from profile mod targets.</summary>
internal static class InfrastructurePackages
{
    internal sealed class Package
    {
        internal string Team="",Name="",Version="",Path="",Area="",Latest="",Author="",Description="";
        internal string[] Dependencies=Array.Empty<string>();
        // File matching is inventory evidence only, never an installation/ownership claim.
        internal bool IdentityConfirmed=true, ManifestVerified=true;
        internal string LocalId="", Homepage="";
        internal JObject? ManifestEvidence;
        /// <summary>2.4.4: lower-version manifest.json copies of this same package elsewhere (core/patchers/plugins); never used for the version.</summary>
        internal List<string> StaleManifests=new();
        /// <summary>2.3.4: the runtime files (assemblies, any .dll.* copy) this package owns, for enablement renames.</summary>
        internal string[] RuntimeFiles=Array.Empty<string>();
        internal string Key=>IdentityConfirmed ? Team+"-"+Name : LocalId;
    }
    internal static IReadOnlyList<Package> Results {get;private set;}=Array.Empty<Package>();
    internal static List<Package> Scan()=>Scan(BepInEx.Paths.BepInExRootPath);
    internal static List<Package> Scan(string loaderRoot)
    {
        var result=new List<Package>();
        var payloads=new Dictionary<Package,string[]>();
        var unidentified=new List<(string Area,string Path,string[] Files,bool HasManifest)>();
        foreach(string area in new[]{"core","patchers"})
        {
            string root=System.IO.Path.Combine(loaderRoot,area);if(!Directory.Exists(root))continue;
            var files=ShopInstalled.Files(root).ToArray();
            var manifests=files.Where(p=>System.IO.Path.GetFileName(p).Equals("manifest.json",StringComparison.OrdinalIgnoreCase)).ToArray();
            string? Boundary(string path)=>manifests.Select(System.IO.Path.GetDirectoryName)
                .Where(p=>p!=null&&path.StartsWith(p+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p=>p!.Length).FirstOrDefault();
            var confirmed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string file in manifests)
            {
                var item=ShopInstalled.Manifest(file,area);
                if(item==null)continue;
                string prefix=item.Path+System.IO.Path.DirectorySeparatorChar;
                // A parent manifest must not borrow a nested package's payload.
                var runtime=files.Where(p=>p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&ShopInstalled.Runtime(p)
                    &&!manifests.Any(m=>m!=file&&System.IO.Path.GetDirectoryName(m)!.Length>item.Path.Length
                        &&p.StartsWith(System.IO.Path.GetDirectoryName(m)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))).ToArray();
                if(runtime.Length==0)
                {
                    // 2.3.3: MonoDetour's packages keep their manifest in a folder but their assemblies loose in core/ and
                    // patchers/ (BepInEx loads by file). A known package whose folder holds no assembly is still installed when
                    // its assemblies sit directly in the area root; dropping it is why the components list shrank to three.
                    runtime=LooseRuntime(root,item.Key,files);
                    if(runtime.Length==0)continue;
                }
                int split=item.Key.IndexOf('-');
                string[] dependencies=Array.Empty<string>();
                string author=item.Key.Substring(0,split), description="", homepage=""; JObject? evidence=null;
                try { var manifest=JObject.Parse(File.ReadAllText(file)); evidence=manifest; homepage=(string?)manifest["website_url"]??""; dependencies=(manifest["dependencies"] as JArray)?.Values<string>().Where(d=>!string.IsNullOrWhiteSpace(d)).Select(d=>d!).ToArray()??Array.Empty<string>(); author=(string?)manifest["author"]??author; description=(string?)manifest["description"]??""; }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException or ArgumentException or InvalidCastException or FormatException) { }
                var package=new Package{Team=item.Key.Substring(0,split),Name=item.Key.Substring(split+1),Version=item.Version,Path=item.Path,Area=area,Dependencies=dependencies,Author=author,Description=description,Homepage=homepage,ManifestEvidence=evidence};
                package.RuntimeFiles=runtime;result.Add(package);payloads[package]=runtime;confirmed.Add(item.Path);
            }
            // Direct loader files are BepInEx's own baseline, not additional package folders.
            // For everything below a folder, retain the nearest manifest boundary, even when invalid.
            string Folder(string file)
            {
                string? boundary=Boundary(file);
                if(boundary!=null)return boundary;
                string relative=file.Substring(root.TrimEnd('\\','/').Length+1);
                int slash=relative.IndexOf(System.IO.Path.DirectorySeparatorChar);
                return slash<0 ? root : System.IO.Path.Combine(root,relative.Substring(0,slash));
            }
            foreach(var group in files.Where(ShopInstalled.Runtime).GroupBy(Folder,StringComparer.OrdinalIgnoreCase)
                .Where(g=>!g.Key.Equals(root,StringComparison.OrdinalIgnoreCase)&&!confirmed.Contains(g.Key)))
                unidentified.Add((area,group.Key,group.ToArray(),manifests.Any(m=>System.IO.Path.GetDirectoryName(m)!.Equals(group.Key,StringComparison.OrdinalIgnoreCase))));
        }
        // Only missing manifests can be associated by payload. An invalid/contradictory manifest
        // remains unconfirmed. Every runtime file must match the same unique package and version.
        foreach(var item in unidentified)
        {
            var matches=item.HasManifest ? new List<Package>() : payloads.Keys.Where(package=>item.Files.All(file=>
                payloads[package].Any(candidate=>RuntimeName(candidate).Equals(RuntimeName(file),StringComparison.OrdinalIgnoreCase)
                    &&SameFileContent(file,candidate)))).ToList();
            var identities=matches.GroupBy(p=>p.Key+"|"+p.Version,StringComparer.OrdinalIgnoreCase).ToArray();
            if(identities.Length==1)
            {
                var source=identities[0].First();
                result.Add(new Package {Team=source.Team,Name=source.Name,Version=source.Version,Path=item.Path,Area=item.Area,
                    Author=source.Author,Description=source.Description,Dependencies=source.Dependencies.ToArray(),ManifestVerified=false,RuntimeFiles=item.Files});
            }
            else result.Add(new Package {Name=System.IO.Path.GetFileName(item.Path),Path=item.Path,Area=item.Area,
                LocalId="file:"+item.Area+"/"+item.Path.Substring(System.IO.Path.Combine(loaderRoot,item.Area).TrimEnd('\\','/').Length+1).Replace('\\','/'),
                IdentityConfirmed=false,ManifestVerified=false,RuntimeFiles=item.Files});
        }
        ReconcileManifestCopies(loaderRoot,result);
        return result;
    }
    /// <summary>
    /// 2.4.4: one store package can leave manifest.json in several places (MonoDetour: core\MonoDetour-MonoDetour from a manual
    /// install, plugins\MonoDetour-MonoDetour from the store). The store update writes only one of them, so reading the stale one made
    /// the component offer the same update forever. Every copy of a key in core/patchers/plugins is compared by
    /// <see cref="ManifestCopies"/>; the highest version wins and the rest are reported as stale.
    /// </summary>
    private static void ReconcileManifestCopies(string loaderRoot,List<Package> result)
    {
        var keys=new HashSet<string>(result.Where(p=>p.IdentityConfirmed).Select(p=>p.Key),StringComparer.OrdinalIgnoreCase);
        if(keys.Count==0)return;
        var copies=new List<ManifestCopies.Copy>();
        foreach(string area in new[]{"core","patchers","plugins"})
        {
            string root=System.IO.Path.Combine(loaderRoot,area);if(!Directory.Exists(root))continue;
            foreach(string file in ShopInstalled.Files(root))
            {
                if(!System.IO.Path.GetFileName(file).Equals("manifest.json",StringComparison.OrdinalIgnoreCase))continue;
                var copy=ManifestCopies.Read(file,File.ReadAllText);
                if(copy!=null&&keys.Contains(copy.Key))copies.Add(copy);
            }
        }
        foreach(var verdict in ManifestCopies.Resolve(copies))
            foreach(var package in result.Where(p=>p.IdentityConfirmed&&p.Key.Equals(verdict.Key,StringComparison.OrdinalIgnoreCase)))
            {
                if(ManifestCopies.Compare(verdict.Version,package.Version)>0)package.Version=verdict.Version;
                package.StaleManifests=verdict.Stale.Select(c=>c.ManifestPath).ToList();
            }
    }
    /// <summary>Assemblies a package installs straight into the area root, by the names the package itself ships. Only
    /// known layouts are listed: guessing ownership of a loose DLL from its name would misattribute BepInEx's own files.</summary>
    private static readonly Dictionary<string,string[]> LooseAssemblies=new(StringComparer.OrdinalIgnoreCase)
    {
        ["MonoDetour-MonoDetour"]=new[]{"com.github.MonoDetour.dll","com.github.MonoDetour.Reflection.dll","com.github.MonoDetour.Bindings.Reorg.dll"},
        ["MonoDetour-MonoDetour_BepInEx_5"]=new[]{"0.com.github.MonoDetour.BepInEx.5.dll","com.github.MonoDetour.Interop.HarmonyX.dll"},
    };
    private static string[] LooseRuntime(string areaRoot,string key,IEnumerable<string> files)
    {
        if(!LooseAssemblies.TryGetValue(key,out var names))return Array.Empty<string>();
        string root=areaRoot.TrimEnd('\\','/');
        return files.Where(p=>string.Equals(System.IO.Path.GetDirectoryName(p),root,StringComparison.OrdinalIgnoreCase)
            &&names.Contains(System.IO.Path.GetFileName(p),StringComparer.OrdinalIgnoreCase)&&ShopInstalled.Runtime(p)).ToArray();
    }
    private static string RuntimeName(string file)
    {
        string name=System.IO.Path.GetFileName(file);
        int extension=name.IndexOf(".dll",StringComparison.OrdinalIgnoreCase);
        return extension<0 ? name : name.Substring(0,extension+4);
    }
    private static bool SameFileContent(string left,string right)
    {
        try
        {
            using var a=File.OpenRead(left);using var b=File.OpenRead(right);
            if(a.Length!=b.Length)return false;
            var leftBuffer=new byte[8192];var rightBuffer=new byte[8192];
            while(true)
            {
                int read=a.Read(leftBuffer,0,leftBuffer.Length);
                if(read==0)return b.Position==b.Length;
                int offset=0;
                while(offset<read){int next=b.Read(rightBuffer,offset,read-offset);if(next==0)return false;offset+=next;}
                for(int i=0;i<read;i++)if(leftBuffer[i]!=rightBuffer[i])return false;
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return false;}
    }
    internal static void SetResults(List<Package> packages)=>Results=packages;
    internal static string Summary()
    {
        var updates=Results.Where(p=>p.IdentityConfirmed&&ThunderstoreUpdateService.IsNewer(p.Latest,p.Version))
            .GroupBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
        return updates.Count==0?"":Loc.T("Core / preloader updates (preview current-pack batch): ","核心／启动补丁有更新（预览当前包批次）：","核心／啟動補丁有更新（預覽目前套件批次）：")+string.Join("; ",updates.Select(p=>p.Key+" "+p.Version+" → "+p.Latest));
    }
}
