using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ModConfigEnhance;
class Program
{
 static int n;static void Check(bool ok,string text){if(!ok)throw new Exception(text);n++;Console.WriteLine("PASS "+text);}
 static void Main(){try{Run();}catch(Exception e){Console.WriteLine("FAIL "+e.Message);Environment.ExitCode=1;}}
 static void Run(){
  var dll=System.IO.Path.GetFullPath("../../BepInEx/plugins/0核心&基础模组/[0依赖]Mods/[依赖]实时翻译框架-XUnity_AutoTranslator/XUnity.AutoTranslator.Plugin.Core.dll");
  using(var assembly=Mono.Cecil.AssemblyDefinition.ReadAssembly(dll))
  {
   var types=assembly.MainModule.Types;
   Mono.Cecil.TypeDefinition Type(string name){foreach(var t in types)if(t.FullName==name)return t;throw new Exception(name);}
   foreach(string method in new[]{"CreateTranslationJobFor","TranslateByEndpoint","SetTranslatedText","Translate","ReloadTranslations"})
    {bool found=false;foreach(var m in Type("XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin").Methods)if(m.Name==method)found=true;Check(found,"installed core signature exists "+method);}
   foreach(string method in new[]{"EnqueueTranslation","Translate","HandleNextJob","HandleNextBatch","OnSingleTranslationCompleted","OnBatchTranslationCompleted"})
    {bool found=false;foreach(var m in Type("XUnity.AutoTranslator.Plugin.Core.Endpoints.TranslationEndpointManager").Methods)if(m.Name==method)found=true;Check(found,"installed endpoint signature exists "+method);}
  }
  XUnityMachineTranslationGuard.StartGuard(new UnityEngine.GameObject());
  Check(typeof(XUnityMachineTranslationGuard).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance)==null,"no periodic Update polling");
  var plugin=new XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin();var endpoint=new XUnity.AutoTranslator.Plugin.Core.Endpoints.TranslationEndpointManager();
  Check(plugin.CreateTranslationJobFor()==0,"job creation blocked");Check(endpoint.EnqueueTranslation()==null,"queue blocked");
  endpoint.HandleNextJob();endpoint.HandleNextBatch();Check(endpoint.Sent==0,"dispatch blocked");
  endpoint.OnSingleTranslationCompleted();endpoint.OnBatchTranslationCompleted();Check(endpoint.Applied==0,"late results blocked");
  Check(!endpoint.Translate().MoveNext()&&endpoint.Sent==0,"coroutine blocked");
  Check(plugin.Translate("dict",endpoint,true,true)=="dictionary","dictionary still available");
  var value=plugin.Translate("missing",endpoint,true,true);Console.WriteLine("EXPLICIT="+value);Check(value=="no endpoint","explicit API forced dictionary only");
  var settings=typeof(XUnity.AutoTranslator.Plugin.Core.Configuration.Settings);
  XUnity.AutoTranslator.Plugin.Core.Configuration.Settings.SetEndpoint("Google");XUnity.AutoTranslator.Plugin.Core.Configuration.Settings.SetFallback("Bing");
  Check(XUnity.AutoTranslator.Plugin.Core.Configuration.Settings.Main==""&&XUnity.AutoTranslator.Plugin.Core.Configuration.Settings.Fallback=="","both endpoint setters coerced");
  var native=new UnityEngine.Component{Localized=true};var info=new Info{OriginalText="游戏中文"};
  plugin.SetTranslatedText(native,"garbage",null!,info);Check(plugin.Last=="游戏中文","native original preserved");
  plugin.SetTranslatedText(new UnityEngine.Component(),"模组译文","mod original",info);Check(plugin.Last=="模组译文","mod dictionary result retained");
  plugin.Initialize();Check(XUnityBridge.Enforcements>0,"initialize postfix enforces dictionary mode");
  plugin.ReloadTranslations();Check(XUnityBridge.Reloads==1,"reload fence invoked");
  Plugin.TameXUnity.Value=false;Check(plugin.CreateTranslationJobFor()==1,"switch off restores original method");
  endpoint.HandleNextJob();Check(endpoint.Sent==1,"switch off restores dispatch");
  typeof(XUnityMachineTranslationGuard).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(new XUnityMachineTranslationGuard(),null);
  Plugin.TameXUnity.Value=true;Check(plugin.CreateTranslationJobFor()==1,"unload removes guard patches");
  Console.WriteLine("RESULT "+n+" actual Harmony guard checks passed");
 }
 public class Info{public string OriginalText{get;set;}="";}
}
namespace UnityEngine { public class Object {public static bool operator==(Object? a,Object? b)=>ReferenceEquals(a,b);public static bool operator!=(Object? a,Object? b)=>!ReferenceEquals(a,b);public override bool Equals(object? x)=>ReferenceEquals(this,x);public override int GetHashCode()=>base.GetHashCode();} public class Component:Object {public bool Localized;public T? GetComponent<T>() where T:class,new()=>Localized?new T():null;} public class MonoBehaviour:Component{} public class GameObject {public T AddComponent<T>() where T:new()=>new T();} public static class Time{public static float unscaledTime;} }
public class LocalizedText{}
namespace BepInEx.Configuration { public class ConfigEntry<T>{public T Value=default!;public event EventHandler? SettingChanged;public void Fire()=>SettingChanged?.Invoke(this,EventArgs.Empty);} }
namespace ModConfigEnhance {static class Plugin {public const string PluginGuid="test.mce";public static BepInEx.Configuration.ConfigEntry<bool> TameXUnity=new(){Value=true};public static Logger Log=new();} class Logger{public void LogInfo(object s){}public void LogWarning(object s){}public void LogError(object s){Console.WriteLine(s);}} static class XUnityBridge {public static int Reloads,Enforcements;public static bool GuardNeedsEnforcement(out object? c){c=null;return false;}public static void EnforceDictionaryOnly(){Enforcements++;}public static void ResetGuardCache(){}public static void PrepareGuardReload(){Reloads++;}} }
namespace XUnity.AutoTranslator.Plugin.Core {
 public class AutoTranslationPlugin {
 public string Last="";
 [MethodImpl(MethodImplOptions.NoInlining)]public void Initialize(){}
 [MethodImpl(MethodImplOptions.NoInlining)]public int CreateTranslationJobFor()=>1;
 [MethodImpl(MethodImplOptions.NoInlining)]public void TranslateByEndpoint(){}
 [MethodImpl(MethodImplOptions.NoInlining)]public string Translate(string text,XUnity.AutoTranslator.Plugin.Core.Endpoints.TranslationEndpointManager? endpoint,bool allowStartTranslateImmediate,bool allowFallback)=>text=="dict"?"dictionary":endpoint==null?"no endpoint":"network";
 [MethodImpl(MethodImplOptions.NoInlining)]public void SetTranslatedText(object ui,string translatedText,string originalText,object info){Last=translatedText;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void ReloadTranslations(){}
 }
}
namespace XUnity.AutoTranslator.Plugin.Core.Endpoints {public class TranslationEndpointManager {
 public int Sent,Applied;
 [MethodImpl(MethodImplOptions.NoInlining)]public object? EnqueueTranslation()=>new object();
 [MethodImpl(MethodImplOptions.NoInlining)]public void HandleNextJob(){Sent++;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void HandleNextBatch(){Sent++;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void OnSingleTranslationCompleted(){Applied++;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void OnBatchTranslationCompleted(){Applied++;}
 [MethodImpl(MethodImplOptions.NoInlining)]public IEnumerator Translate(){Sent++;return new object[1].GetEnumerator();}
} }
namespace XUnity.AutoTranslator.Plugin.Core.Configuration { public static class Settings {public static string Main="",Fallback="";[MethodImpl(MethodImplOptions.NoInlining)]public static void SetEndpoint(string id){Main=id;}[MethodImpl(MethodImplOptions.NoInlining)]public static void SetFallback(string id){Fallback=id;} } }
