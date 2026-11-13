#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using ModConfigEnhance.Overview;
class Program{
static int count;static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;}
static void Main(){var root=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sandbox-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(root);var config=new ConfigFile(System.IO.Path.Combine(root,"test.cfg"),false);var entry=config.Bind("S","Key",1,"");var repo=new OverviewRepository();NativeConfigurationDraft.Repository=repo;var harmony=new Harmony("mce.unified-save.checks");NativeConfigurationDraft.Install(harmony);ConfigurationNotificationGate.Install(harmony);
var setting=new FakeSetting(entry);setting.Value=2;setting.ApplyValue();setting.Save(null!);Check(entry.Value==2&&setting.Applies==1&&setting.Saves==1,"native control writes the live config directly (no draft)");Check(repo.Followed==1,"profile record follows the live value");
setting.SetDefaultValue();Check(entry.Value==1&&repo.Followed==2,"native default writes directly and is followed");
using(new NativeConfigurationDraft.CommitScope()){setting.Value=4;setting.Save(null!);}Check(entry.Value==4&&repo.Followed==2,"runtime sync writes are not echoed back");
var generic=new FakeKeySetting(entry);generic.Value=5;generic.Save(null!);Check(entry.Value==5&&repo.Followed==3,"closed generic Save is followed");
int notifications=0;entry.SettingChanged+=(s,e)=>notifications++;
OverviewRuntimeSettingsBridge.Next=ConfigurationEffectTiming.GameRestart;using(new ConfigurationNotificationGate.Scope(new[]{entry})){entry.Value=6;}Check(entry.Value==6&&notifications==0,"restart persistence suppresses live notification");
OverviewRuntimeSettingsBridge.Next=ConfigurationEffectTiming.Immediate;using(new ConfigurationNotificationGate.Scope(new[]{entry})){entry.Value=7;}Check(notifications==1,"immediate setting preserves live notification");string configFile=config.ConfigFilePath;config.Save();string disk=System.IO.File.ReadAllText(configFile);using(new ConfigurationNotificationGate.Scope(new[]{entry})){entry.Value=8;config.Save();}Check(System.IO.File.ReadAllText(configFile)==disk,"callback ConfigFile.Save cannot overwrite document during controlled transaction");
harmony.UnpatchSelf();Console.WriteLine("PASS "+count+" production Harmony runtime checks");}
}
namespace PEAKLib.ModConfig{public interface IBepInExProperty{ConfigEntryBase ConfigBase{get;}BepInEx.PluginInfo Pluginfo{get;}void RefreshValueFromConfig();} }
namespace Zorro.Settings{public class Setting{[MethodImpl(MethodImplOptions.NoInlining)]public virtual void Save(object x){}[MethodImpl(MethodImplOptions.NoInlining)]public virtual void ApplyValue(){} }}
static class FakeMetadata{internal static readonly BepInEx.PluginInfo Info=Create();static BepInEx.PluginInfo Create(){var info=new BepInEx.PluginInfo();AccessTools.Property(typeof(BepInEx.PluginInfo),"Metadata").SetValue(info,new BepInEx.BepInPlugin("test.mod","Test","1.0.0"),null);return info;}}
public class FakeSetting:Zorro.Settings.Setting,PEAKLib.ModConfig.IBepInExProperty{
public ConfigEntryBase ConfigBase{get;}public BepInEx.PluginInfo Pluginfo=>FakeMetadata.Info;public int Value{get;set;}public int Applies,Saves;public FakeSetting(ConfigEntryBase entry){ConfigBase=entry;}
[MethodImpl(MethodImplOptions.NoInlining)]public override void ApplyValue(){Applies++;}
[MethodImpl(MethodImplOptions.NoInlining)]public override void Save(object x){Saves++;ConfigBase.BoxedValue=Value;}
[MethodImpl(MethodImplOptions.NoInlining)]public void RefreshValueFromConfig(){Value=(int)ConfigBase.BoxedValue;}
[MethodImpl(MethodImplOptions.NoInlining)]public void SetDefaultValue(){ConfigBase.BoxedValue=1;}}
public class GenericSetting<T>:Zorro.Settings.Setting,PEAKLib.ModConfig.IBepInExProperty{
public ConfigEntryBase ConfigBase{get;}public BepInEx.PluginInfo Pluginfo=>FakeMetadata.Info;public T Value{get;set;}=default!;public GenericSetting(ConfigEntryBase e){ConfigBase=e;}[MethodImpl(MethodImplOptions.NoInlining)]public override void Save(object x){ConfigBase.BoxedValue=Value;}[MethodImpl(MethodImplOptions.NoInlining)]public void RefreshValueFromConfig(){Value=(T)ConfigBase.BoxedValue;}}
public class FakeKeySetting:GenericSetting<int>{public FakeKeySetting(ConfigEntryBase e):base(e){}}
namespace UnityEngine{public class Object{public static void Destroy(Object o){}}public class GameObject:Object{public void SetActive(bool value){}}public class Component:Object{public Transform transform=new();}public class Transform{public GameObject gameObject=new();public string name="";public Transform? Find(string n)=>null;}public struct Vector2{public Vector2(float x,float y){}}public class RectTransform:Transform{public Vector2 anchorMin,anchorMax,pivot,anchoredPosition;}}
namespace ModConfigEnhance{static class Plugin{internal const string PluginGuid="mce";internal static readonly BepInEx.Logging.ManualLogSource Log=BepInEx.Logging.Logger.CreateLogSource("checks");}static class Loc{internal static string T(string a,string b,string c)=>a;}enum UiActionRole{Commit}}
namespace ModConfigEnhance.Overview{
internal enum ConfigurationEffectTiming{Immediate,GameRestart,NextRound}internal enum ConfigurationSaveScope{ModParameters}
internal static class OverviewRuntimeSettingsBridge{internal static ConfigurationEffectTiming Next;internal static ConfigurationEffectTiming Timing(ConfigEntryBase e)=>Next;}
internal sealed class OverviewRepository{internal int Followed;internal void FollowLiveValue(string g,ConfigEntryBase e){Followed++;}internal string? Target;internal bool SetDraftConfig(string g,ConfigEntryBase e,string value,out string error){Target=value;error="";return true;}internal OverviewConfigValue? FindDraftConfig(string g,ConfigEntryBase e)=>Target==null?null:new(){serializedValue=Target};internal Pending GetPendingState()=>new();}
internal sealed class OverviewConfigValue{internal string serializedValue="";}internal sealed class Pending{internal object? Parameter(string g,ConfigEntryBase e)=>null;}
internal sealed class OverviewCatalogue{internal object Scan(OverviewRepository r)=>new();}internal sealed class Result{internal bool Success=true;internal string Message="";}
internal static class ConfigurationSaveRequest{internal static void Run(OverviewRepository r,object m,ConfigurationSaveScope s,string g,Action<Result> done)=>done(new());}
internal sealed class Button:UnityEngine.Component{public string name="";}
internal static class OverviewUi{internal static Button Button(UnityEngine.RectTransform p,string t,Action a,int w,int h,UiActionRole role)=>new();internal static void ShowText(string title,string message){}}
internal static class ProfilePendingMarkers{internal static void Attach(UnityEngine.RectTransform t,Func<object?> state,Action<string> show,Action hide,bool reserveText){}}
internal static class SaveSlotMarker{internal static void Mark(UnityEngine.RectTransform save){}}
}
namespace ModConfigEnhance{
// 1.1.3 stub: the real one lays out the settings row's button columns.
internal static class SettingCellExtras{internal enum Column{Default,Clear}internal static void PlaceButton(UnityEngine.RectTransform button,Column column,bool stacked=false){}}
}
namespace ModConfigEnhance.Overview{internal static class SettingCommitPumpStubHolder{}}
