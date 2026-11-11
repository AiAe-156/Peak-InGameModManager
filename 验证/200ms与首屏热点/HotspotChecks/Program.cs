using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;
using ModConfigEnhance;
class Program {
 static int passed;static void Check(bool ok,string title){if(!ok)throw new Exception(title);passed++;Console.WriteLine("PASS "+title);}
 static void Main(){
 var harmony=new Harmony("mce.settings-tab.check");SettingsTabActivationPatch.Install(harmony);
 var fixture=new SettingsTABSButton();fixture.Update();fixture.Update();Check(fixture.Graphic.calls==0&&fixture.lerps==2,"real Harmony patch preserves other Update operations");
 fixture.Selected=true;fixture.Update();fixture.Update();Check(fixture.Graphic.calls==1&&fixture.lerps==4,"real Harmony patched Update suppresses redundant activation");harmony.UnpatchSelf();
 var call=AccessTools.Method(typeof(UnityEngine.GameObject),"SetActive");
 var input=new List<CodeInstruction>{new(OpCodes.Nop),new(OpCodes.Callvirt,call),new(OpCodes.Ret)};
 var generator=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator();input[1].labels.Add(generator.DefineLabel());
 input[1].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
 var result=SettingsTabActivationPatch.Transpile(input).ToList();
 Check(result.Count==input.Count&&result[0].opcode==input[0].opcode&&result[2].opcode==input[2].opcode,"only matched call changes");
 Check(result[1].opcode==OpCodes.Call&&result[1].operand.Equals(AccessTools.Method(typeof(SettingsTabActivationPatch),"SetActiveIfChanged")),"static helper replaces virtual call");
 Check(input[1].opcode==OpCodes.Callvirt&&Equals(input[1].operand,call),"original IL not mutated");
 Check(result[1].labels.SequenceEqual(input[1].labels)&&result[1].blocks.SequenceEqual(input[1].blocks),"labels and exception boundaries preserved");
 Check(SettingsTabActivationPatch.Transpile(new[]{new CodeInstruction(OpCodes.Ret)}).Single().opcode==OpCodes.Ret,"zero matches fail open");
 var twice=new[]{new CodeInstruction(OpCodes.Call,call),new CodeInstruction(OpCodes.Callvirt,call)};
 Check(SettingsTabActivationPatch.Transpile(twice).SequenceEqual(twice),"multiple matches fail open without mutation");
 var target=new UnityEngine.GameObject();SettingsTabActivationPatch.SetActiveIfChanged(target,false);Check(target.calls==0,"same inactive no activation");
 SettingsTabActivationPatch.SetActiveIfChanged(target,true);SettingsTabActivationPatch.SetActiveIfChanged(target,true);Check(target.calls==1&&target.activeSelf,"actual activation once");
 SettingsTabActivationPatch.SetActiveIfChanged(target,false);Check(target.calls==2&&!target.activeSelf,"deactivation preserved");
 using var assembly=AssemblyDefinition.ReadAssembly("F:/Games/SteamLibrary/steamapps/common/PEAK/PEAK_Data/Managed/Assembly-CSharp.dll");
 var method=assembly.MainModule.Types.Single(t=>t.Name=="SettingsTABSButton").Methods.Single(m=>m.Name=="Update");
 var calls=method.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToArray();
 Check(calls.Count(m=>m.DeclaringType.FullName=="UnityEngine.GameObject"&&m.Name=="SetActive"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.Boolean")==1,"actual game Update has unique exact SetActive(bool)");
 Check(calls.Any(m=>m.DeclaringType.FullName=="UnityEngine.Color"&&m.Name=="Lerp"),"actual game text Lerp remains outside replaced call");
 Console.WriteLine($"RESULT {passed} checks passed (real Harmony instruction objects and real game IL; Unity activation stub)");
 }
}
namespace UnityEngine { public class GameObject {public bool activeSelf;public int calls;public void SetActive(bool value){calls++;activeSelf=value;}} }
namespace ModConfigEnhance { internal static class Plugin {internal static readonly TestLog Log=new();}internal class TestLog {public void LogWarning(object value)=>Console.WriteLine(value);public void LogInfo(object value)=>Console.WriteLine(value);} }

public class SettingsTABSButton { public UnityEngine.GameObject Graphic=new UnityEngine.GameObject();public bool Selected;public int lerps;[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void Update(){lerps++;Graphic.SetActive(Selected);} }
