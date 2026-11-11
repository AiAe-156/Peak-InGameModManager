#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using ModConfigEnhance.Overview;
class Program{
static int n;static void Check(bool value,string message){if(!value)throw new Exception(message);n++;}
static void Main(){string root=Path.Combine(System.IO.Path.GetFullPath("验证/统一保存重构/ConfigTransactionChecks"),"s-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(root);UnityEngine.Time.frameCount=1;var file=new ConfigFile(Path.Combine(root,"live.cfg"),false);file.SaveOnConfigSet=false;var live=file.Bind("S","Number",1,"");var restart=file.Bind("S","Restart",1,new ConfigDescription("",null,new object[]{"RestartRequired"}));file.Save();var service=new OverviewConfigService(root,Path.Combine(root,"backups"),()=>new[]{("test.mod",file)});var snapshot=service.Capture();snapshot.values.Single(v=>v.key=="Number").serializedValue="2";snapshot.values.Single(v=>v.key=="Restart").serializedValue="2";snapshot.values.Add(new(){pluginGuid="disabled.mod",configFile="disabled.cfg",section="S",key="Enabled",type="System.Boolean",serializedValue="true"});
var harmony=new Harmony("mce.transaction.test");ConfigurationNotificationGate.Install(harmony);int notifications=0;restart.SettingChanged+=(s,e)=>notifications++;
var tx=service.PrepareApply(snapshot);Check(live.Value==1&&!File.Exists(Path.Combine(root,"disabled.cfg")),"prepare leaves live and offline files untouched");
File.AppendAllText(file.ConfigFilePath,"\n[External]\nOther = keep\n");tx.Apply();Check(live.Value==2&&restart.Value==2,"apply updates loaded values");Check(notifications==0,"restart value does not run third-party listener");Check(File.ReadAllText(file.ConfigFilePath).Contains("Other = keep"),"unrelated external addition is preserved");Check(File.ReadAllText(Path.Combine(root,"disabled.cfg")).Contains("Enabled = true"),"disabled configuration saved before startup");Check(tx.RestartSnapshot.values.Single().key=="Restart","restart target recorded separately");Check(!service.Capture(snapshot).values.Single(v=>v.pluginGuid=="disabled.mod").missing,"disabled file with saved key is available offline");tx.Rollback();Check(live.Value==1&&restart.Value==1&&!File.Exists(Path.Combine(root,"disabled.cfg")),"rollback restores live and new offline file");Check(File.ReadAllText(file.ConfigFilePath).Contains("Other = keep"),"rollback preserves unrelated external addition");
var bad=service.Capture();bad.values.Single(v=>v.key=="Number").serializedValue="not-a-number";var partial=service.PrepareApply(bad);Check(partial.DeferredSnapshot.values.Single().key=="Number","invalid item is skipped instead of blocking all");partial.Apply();partial.Commit();
var next=service.Capture();next.values.Single(v=>v.key=="Number").serializedValue="3";var conflict=service.PrepareApply(next);File.WriteAllText(file.ConfigFilePath,File.ReadAllText(file.ConfigFilePath).Replace("Number = 1","Number = 7"));try{conflict.Apply();throw new Exception("overlapping file conflict accepted");}catch(InvalidDataException){n++;}Check(live.Value==1,"file conflict detected before live mutation");harmony.UnpatchSelf();Console.WriteLine("PASS "+n+" production config transaction checks");}}
namespace UnityEngine{internal static class Time{internal static int frameCount;}}
namespace ModConfigEnhance{internal static class Plugin{internal const string PluginGuid="com.aiae.modconfigenhance";}internal static class ModConfigSupport{internal const string Guid="modconfig";}internal static class Loc{internal static string T(string a,string b,string c)=>a;internal static string F(string a,string b,string c,params object[] args)=>string.Format(a,args);}}
namespace ModConfigEnhance.Overview{
internal sealed class OverviewState{}internal static class InstallationPaths{internal static bool AtGameRoot(string p)=>false;}
internal static class PublicationFilePolicy{internal static bool ProfileConfigPath(string p)=>!p.StartsWith("ModConfigEnhance/");}
internal static class OverviewRuntimeSettingsBridge{internal static ConfigurationEffectTiming Timing(ConfigEntryBase e)=>ConfigurationEffectPolicy.FromTags(e.Description.Tags);internal static bool RequiresRestart(ConfigEntryBase e)=>Timing(e)==ConfigurationEffectTiming.GameRestart;internal static void Synchronize(IEnumerable<ConfigEntryBase> e){}}
}
