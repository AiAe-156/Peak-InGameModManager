using ModConfigEnhance.Overview;
using Newtonsoft.Json;
int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
string root=Path.Combine(AppContext.BaseDirectory,"sandbox-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var profile=new OverviewProfile{id="preset",name="Low effects",state=new(){pack=new(){name="Original pack"}},config=new(){values=new(){new(){pluginGuid="test.mod",configFile="test.cfg",section="Main",key="Enabled",type="System.Boolean",serializedValue="true"}}},mods=new(){new(){guid="test.mod",enabled=false}}};
OverviewProfileNames.Normalize(profile,true);Check(profile.name=="Low effects"&&profile.state.pack.name=="Original pack","preset name independent of pack name");
string output=ConfigurationPresetFile.Export(new[]{profile},root);string file=Directory.GetFiles(output).Single();
var preset=ConfigurationPresetFile.Read(file);Check(preset.name==profile.name&&preset.config.values.Single().serializedValue=="true","configuration roundtrip preserves values");
string json=File.ReadAllText(file);Check(!json.Contains("Original pack")&&!json.Contains("introDocument")&&!json.Contains("resourceDirectory"),"pure config export excludes pack information/resources");
Check(preset.mods.Single().enabled==false,"disabled mod target retained");
profile.config.values[0].configFile="../bad.cfg";string bad=Path.Combine(root,"bad.json");File.WriteAllText(bad,JsonConvert.SerializeObject(new ConfigurationPresetFile{name="Bad",config=profile.config}));
try{ConfigurationPresetFile.Read(bad);throw new Exception("Unsafe path accepted");}catch(InvalidDataException){checks++;}
string profiles=Path.Combine(root,"workspace","profiles"),resources=Path.Combine(root,"workspace","resources");Directory.CreateDirectory(profiles);Directory.CreateDirectory(resources);
PackWorkspaceInformation.Write(profiles,profile.state,resources);var pack=PackWorkspaceInformation.Read(profiles)!;Check(pack.pack.name=="Original pack","pack info has independent saved identity");
PackWorkspaceInformation.Write(profiles,profile.state,root);try{PackWorkspaceInformation.Read(profiles);throw new Exception("Boundary accepted");}catch(InvalidDataException){checks++;}
Console.WriteLine($"PASS {checks} production preset/workspace checks");
namespace ModConfigEnhance{internal static class Loc{internal static string T(string a,string b,string c)=>a;}}
namespace ModConfigEnhance.Overview{
internal sealed class OverviewState{public OverviewPack pack=new();public string packNameOverride="";public List<OverviewGroup> groups=new();public List<OverviewModRule> mods=new();}
internal sealed class OverviewPack{public string name="";}
internal sealed class OverviewGroup{public string id="",name="",color="",description="",parentId="";public int order;}internal sealed class OverviewModRule{public string id="",groupId="";public int order;}
internal static class OverviewProfileStore{internal static T DeserializeJson<T>(string s)=>JsonConvert.DeserializeObject<T>(s)!;}
internal static class OverviewProfileExport{internal static string PortableId(string s)=>s;}
internal static class PublicationFilePolicy{internal static bool ProfileConfigPath(string s)=>!s.StartsWith("ModConfigEnhance/");}
internal static class OverviewRepository{internal static void AtomicWrite(string file,string data){Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,data);}}
}
