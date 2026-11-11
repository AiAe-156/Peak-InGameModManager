using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
int checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
string root=Path.Combine(AppContext.BaseDirectory,"fixtures-"+Guid.NewGuid().ToString("N"),"BepInEx");Directory.CreateDirectory(root);
// Final collector resolves platform refs only from this fixture game's PEAK_Data/Managed (never the real game).
string managedFixture=Path.Combine(Path.GetDirectoryName(root)!,"PEAK_Data","Managed");Directory.CreateDirectory(managedFixture);
using(var platform=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("mscorlib",new Version(4,0,0,0)),"mscorlib",ModuleKind.Dll))platform.Write(Path.Combine(managedFixture,"mscorlib.dll"));
string Manifest(string key,string area,string[]? deps=null){string dir=Path.Combine(root,area,key);Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"manifest.json"),JsonSerializer.Serialize(new{name=key.Split('-')[1],@namespace=key.Split('-')[0],version_number="1.0.0",dependencies=deps??Array.Empty<string>()}));return dir;}
void Dll(string dir,string name,string? guid=null,(string,bool)[]? deps=null,string[]? refs=null,bool disabled=false)
{
    using var a=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name,new Version(1,0)),name,ModuleKind.Dll);a.MainModule.AssemblyReferences.Clear();
    var t=new TypeDefinition("Fixture","Plugin",Mono.Cecil.TypeAttributes.Public|Mono.Cecil.TypeAttributes.Class,a.MainModule.TypeSystem.Object);a.MainModule.Types.Add(t);
    MethodReference Attr(string type,params TypeReference[] args){var ctor=new MethodReference(".ctor",a.MainModule.TypeSystem.Void,new TypeReference("BepInEx",type,a.MainModule,a.MainModule.TypeSystem.CoreLibrary)){HasThis=true};foreach(var arg in args)ctor.Parameters.Add(new ParameterDefinition(arg));return ctor;}
    if(guid!=null){var at=new CustomAttribute(Attr("BepInPlugin",a.MainModule.TypeSystem.String,a.MainModule.TypeSystem.String,a.MainModule.TypeSystem.String));foreach(string v in new[]{guid,name,"1.0.0"})at.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.TypeSystem.String,v));t.CustomAttributes.Add(at);}
    foreach(var (dependency,soft) in deps??Array.Empty<(string,bool)>()){var at=new CustomAttribute(Attr("BepInDependency",a.MainModule.TypeSystem.String,a.MainModule.TypeSystem.Int32));at.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.TypeSystem.String,dependency));at.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.TypeSystem.Int32,soft?2:1));t.CustomAttributes.Add(at);}
    foreach(string reference in refs??Array.Empty<string>())a.MainModule.AssemblyReferences.Add(new AssemblyNameReference(reference,new Version(1,0)));
    foreach(var r in a.MainModule.AssemblyReferences.Where(r=>r.Name=="mscorlib"))r.PublicKeyToken=Array.Empty<byte>();
    a.Write(Path.Combine(dir,name+(disabled?".dll.old":".dll")));
}
ComponentDependencyGraph Graph()=>new ShopQueue(root).ReadComponentGraph();
string mce=Manifest("AiAeT-MCE","plugins",new[]{"Team-ModConfig-1.0.0"});
const string modConfigGuid="com.github.PEAKModding.PEAKLib.ModConfig",uiGuid="com.github.PEAKModding.PEAKLib.UI";
Dll(mce,"Mce",ComponentDependencyGraph.MceGuid,new[]{(modConfigGuid,false),("optional",true)});
string mod=Manifest("Team-ModConfig","plugins");Dll(mod,"ModConfig",modConfigGuid,new[]{(uiGuid,false)});
string ui=Manifest("Team-UI","plugins");Dll(ui,"UI",uiGuid,refs:new[]{"ThirdPartyCore"});
string core=Manifest("Team-Core","core");Dll(core,"ThirdPartyCore",refs:new[]{"Preloader"});File.WriteAllText(Path.Combine(core,"resources.txt"),"required data");
string patcher=Manifest("Team-Core","patchers");Dll(patcher,"Preloader");
string optional=Manifest("Team-Optional","plugins");Dll(optional,"Optional","optional");
string ordinary=Manifest("Team-Ordinary","plugins",new[]{"Team-Leaf-1.0.0"});Dll(ordinary,"Ordinary","ordinary");
string leaf=Manifest("Team-Leaf","plugins");Dll(leaf,"Leaf","leaf");
var graph=Graph();var cap=graph.GetCapability("Team-Core");Check(!cap.CanUninstall&&cap.ProtectionPath.SequenceEqual(new[]{"AiAeT-MCE","Team-ModConfig","Team-UI","Team-Core"}),"production collector MCE → ModConfig → UI → assembly third-party core path");
Check(graph.Components.Single(c=>c.Id=="Team-Core").Locations.Any(l=>l.StartsWith("patchers/"))&&graph.Components.Single(c=>c.Id=="Team-Core").Locations.Any(l=>l.EndsWith("resources.txt")),"core/patcher locations merged and co-resident necessary resources protected");
Check(graph.GetCapability("Team-Optional").CanUninstall,"soft dependency not permanent protection");Check(graph.GetCapability("Team-Leaf").CanUninstall,"non-MCE consumer is not permanent protection");
Check(cap.CanUpdate,"protected dependency permits update capability");
// Validate existing ordinary consumer and version checks without changing their implementation.
var consumerTask=new ShopTask{Packages=new(){new ShopPackage{Key="Team-Leaf",Version="1.0.0"}}};var checkConsumers=typeof(ShopQueue).GetMethod("CheckConsumers",BindingFlags.Instance|BindingFlags.NonPublic)!;
Check(((List<string>)checkConsumers.Invoke(new ShopQueue(root),new object[]{consumerTask,true})!).Any(s=>s.Contains("Team-Ordinary")),"ordinary CheckConsumers still blocks uninstall");
Check(((List<string>)checkConsumers.Invoke(new ShopQueue(root),new object[]{new ShopTask{Packages=new(){new ShopPackage{Key="Team-Core",Version="2.0.0"}}},false})!).Count==0,"update path is not prohibited by fixed protection");
Check(ShopDependencyValidation.Validate(new List<ShopDependencyValidation.Plugin>{ new("consumer","1.0.0","plugins/consumer.dll",true,new(){new("provider","2.0.0",false)}),new("provider","1.0.0","plugins/provider.dll",true,new())}).Count>0,"final minimum version check retained");
// Reverse tree has multiple evidence types, repeated/shared nodes and cycles, all bounded.
graph.Edges.Add(new(){From="Team-UI",To="AiAeT-MCE",Evidence="cycle fixture"});graph.Edges.Add(new(){From="AiAeT-MCE",To="Team-UI",Evidence="shared fixture"});
var tree=graph.ReverseTree("Team-Core");Check(tree.Any(e=>e.Cycle)&&tree.Any(e=>e.Shared)&&tree.Any(e=>e.Evidence.Contains("assembly ref"))&&tree.Any(e=>e.Evidence.Contains("manifest")),"reverse multi-level shared/cycle tree explains evidence");
Check(graph.ReverseTree("Team-Core",1,2).Count<=2&&graph.ReverseTree("Team-Core",1,2).Any(e=>e.Truncated),"reverse tree budget bounded");Check(graph.ReverseTree("missing:x")[0].Missing,"missing target represented without fake GUID");
string duplicate=Manifest("Other-Core","core");Dll(duplicate,"ThirdPartyCore");Check(!Graph().GetCapability("Other-Core").CanUninstall,"duplicate assembly providers conservatively protected");
Dll(mod,"ModConfig",modConfigGuid,new[]{(uiGuid,false)},disabled:true);File.Delete(Path.Combine(mod,"ModConfig.dll"));Check(!Graph().GetCapability("Team-Core").CanUninstall,"disabled MCE dependency remains permanently protected");
// Real worker API bypassing UI: preview must reject with full protection path before any store/network/claim download.
Files.RunningProbe=_=>false;var queue=new ShopQueue(root);queue.Handle(new(){Operation="preview-uninstall",Namespace="Team",Package="Core",Version="1.0.0"});await queue.Pump(new());Check(queue.Read()!.State=="failed"&&queue.Read()!.Error.Contains("AiAeT-MCE → Team-ModConfig → Team-UI → Team-Core"),"direct SourceUi worker uninstall rejected with MCE path");
// A legitimately unprotected preview becomes protected before confirm, and again before commit.
var tokenMethod=typeof(ShopQueue).GetMethod("Token",BindingFlags.Static|BindingFlags.NonPublic)!;
ShopTask SyntheticPreview(){var index=new PackStorage(root).Read();var t=new ShopTask{Id=Guid.NewGuid().ToString("N"),State="uninstall-preview",CreatedUtc=DateTime.UtcNow.ToString("O"),Request=new(){Operation="preview-uninstall",Namespace="Team",Package="Optional",Version="1.0.0"},ActivePackId=index.ActivePackId,IndexRevision=index.Revision,Packages=new(){new(){Key="Team-Optional",Version="1.0.0"}}};t.PlanToken=(string)tokenMethod.Invoke(null,new object[]{t})!;Files.Save(Path.Combine(root,"MCE/shop/task.json"),t);return t;}
var old=SyntheticPreview();Dll(ui,"UI",uiGuid,new[]{("optional",false)},refs:new[]{"ThirdPartyCore"});
bool rejected=false;try{queue.Handle(new(){Operation="confirm-uninstall",PreviewId=old.Id,PlanToken=old.PlanToken});}catch(IOException e){rejected=e.Message.Contains("MCE required component")&&e.Message.Contains("Team-Optional");}Check(rejected,"confirm recollects live graph rather than stale UI or preview cache");
old=SyntheticPreview();old.Request.Operation="confirm-uninstall";old.State="waiting-exit";old.PlanToken=(string)tokenMethod.Invoke(null,new object[]{old})!;Files.Save(Path.Combine(root,"MCE/shop/task.json"),old);await queue.Pump(new());Check(queue.Read()!.State=="failed"&&queue.Read()!.Error.Contains("MCE required component"),"commit recollects live graph even for direct forged flags");
// Independent live root, not previous graph/cache or stored inventories.
string other=Path.Combine(Path.GetDirectoryName(root)!,"other","BepInEx");Directory.CreateDirectory(Path.Combine(other,"plugins"));File.Copy(Path.Combine(mce,"Mce.dll"),Path.Combine(other,"plugins/Mce.dll"));
Check(new ShopQueue(other).ReadComponentGraph().GetCapability("Team-Core").Unknown,"current environment refresh does not borrow old package graph");
Dll(ui,"UI",uiGuid,new[]{("missing.required",false)},refs:new[]{"ThirdPartyCore"});Check(Graph().GetCapability("Team-Leaf").Unknown,"missing transitive hard evidence makes unrelated uninstall unknown");
Dll(ui,"UI",uiGuid,refs:new[]{"ThirdPartyCore"});
File.WriteAllBytes(Path.Combine(leaf,"corrupt.dll"),new byte[]{1,2,3});Check(Graph().GetCapability("Team-Leaf").Unknown,"corrupt DLL evidence fails closed rather than masquerading as native");
File.WriteAllText(Path.Combine(optional,"manifest.json"),"broken");Check(Graph().GetCapability("Team-Leaf").Unknown,"failed evidence cannot turn unknown into deletable");
Check(!Graph().GetCapability("Team-Core").CanUninstall,"known MCE path still reported even with failed evidence elsewhere");
Console.WriteLine($"PASS {checks} checks; sandbox only; no deploy/live mutation");
