using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Mono.Cecil;
using System.Runtime.CompilerServices;
using ModConfigEnhance.Overview;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

class Program
{
    static int passed;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++; }
    static void Call(object value,string method)=>value.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(value,null);
    static void Main()
    {
        var window=new HoverPerfWindow();window.Frame(10,0);window.Frame(20,1);window.Record(0,100,64);window.Record(0,300,128);
        Check(window.Frames==2 && window.FrameMs==30 && window.MaxFrameMs==20 && window.Collections==1,"frame counters");
        Check(window.Stages[0].Calls==2 && window.Stages[0].Ticks==400 && window.Stages[0].MaxTicks==300 && window.Stages[0].AllocatedBytes==192,"stage counters");
        window.Clear();Check(window.Frames==0 && window.Stages[0].Calls==0,"all counters reset");
        var managed="F:/Games/SteamLibrary/steamapps/common/PEAK/PEAK_Data/Managed/";
        using(var ui=AssemblyDefinition.ReadAssembly(managed+"UnityEngine.UI.dll"))
        using(var game=AssemblyDefinition.ReadAssembly(managed+"Assembly-CSharp.dll"))
        {
            foreach(var item in new[]{("UnityEngine.EventSystems.EventSystem","RaycastAll",2),("UnityEngine.EventSystems.BaseInputModule","HandlePointerExitAndEnter",2),("UnityEngine.UI.Selectable","DoStateTransition",2),("UnityEngine.UI.CanvasUpdateRegistry","PerformUpdate",0)})
                Check(ui.MainModule.Types.Single(t=>t.FullName==item.Item1).Methods.Count(m=>m.Name==item.Item2 && m.Parameters.Count==item.Item3)==1,"current game hook unique: "+item.Item2);
            var soundType=game.MainModule.Types.Single(t=>t.Name=="NicksButtonSFX");
            Check(soundType.Methods.Count(m=>m.Name=="OnPointerEnter" && m.Parameters.Count==1)==1 && soundType.Methods.Count(m=>m.Name=="OnSelect" && m.Parameters.Count==1)==1,"current game sound hooks exist");
        }
        var probe=new UiHoverProbe();Call(probe,"Start");
        var windows=(HoverPerfWindow[])typeof(UiHoverProbe).GetField("Windows",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var settings=new Transform();UiHoverProbe.TrackSettings(settings);
        Time.frameCount=1;var system=new EventSystem();var list=new List<RaycastResult>();var data=new PointerEventData();
        system.RaycastAll(data,list);Check(list.Count==1 && windows[0].Stages[0].Calls==1,"real Harmony raycast instrumentation preserves results");
        Time.frameCount=2;Input.mousePosition=new Vector3(4,2);system.RaycastAll(data,list);
        Check(windows[1].Stages[0].Calls==1 && windows[0].Frames==1,"motion and previous-frame timing separated");
        data.Fail=true;
        try {system.RaycastAll(data,list);throw new Exception("exception suppressed");} catch(InvalidOperationException ex) {Check(ex.Message=="fixture error","finalizer preserves original exception");}
        Check(windows[1].Stages[0].Calls==2,"throwing call also measured");data.Fail=false;
        var input=new BaseInputModule();input.Trigger(data,settings.gameObject);
        var selectable=new Selectable();selectable.Trigger();
        var canvas=new CanvasUpdateRegistry();canvas.Trigger();
        var sound=new NicksButtonSFX();sound.OnPointerEnter(data);sound.OnSelect(new BaseEventData());
        Check(input.Calls==1 && windows[1].Stages[1].Calls==1,"pointer enter/exit stage");
        Check(selectable.Calls==1 && windows[1].Stages[2].Calls==1,"state transition stage");
        Check(canvas.Calls==1 && windows[1].Stages[3].Calls==1,"managed UI rebuild stage");
        Check(sound.Calls==2 && windows[1].Stages[4].Calls==2,"sound stage preserves playback calls");
        settings.gameObject.activeInHierarchy=false;OverviewUi.ModalParent=new Transform();Time.frameCount=3;system.RaycastAll(data,list);
        Check(windows[2].Stages[0].Calls==1,"overview/still bucket separate");
        OverviewUi.ModalParent.gameObject.activeInHierarchy=false;Time.frameCount=4;system.RaycastAll(data,list);
        Check(windows[2].Stages[0].Calls==1,"outside UI scope not measured");
        UiPerfProbe.Enabled=false;settings.gameObject.activeInHierarchy=true;Time.frameCount=5;system.RaycastAll(data,list);
        Check(windows[0].Stages[0].Calls==1,"disabled diagnostics do not record");UiPerfProbe.Enabled=true;
        Call(probe,"OnDestroy");system.RaycastAll(data,list);Check(list.Count==6,"unpatch keeps original functioning");
        Console.WriteLine("RESULT "+passed+" probe checks passed (actual Harmony + Unity stubs)");
    }
}
namespace UnityEngine
{
    public class GameObject {public bool activeInHierarchy=true;public T AddComponent<T>() where T:new()=>new T();}
    public class Transform {public GameObject gameObject=new();}
    public class MonoBehaviour {public bool enabled=true;}
    public static class Time {public static int frameCount;public static float unscaledTime=0,unscaledDeltaTime=.01f;}
    public static class Input {public static Vector3 mousePosition;}
    public readonly struct Vector3 {public readonly float x,y;public Vector3(float a,float b){x=a;y=b;}public static explicit operator Vector2(Vector3 value)=>new(value.x,value.y);}
    public readonly struct Vector2 {public readonly float x,y;public Vector2(float a,float b){x=a;y=b;}public float sqrMagnitude=>x*x+y*y;public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);}
}
namespace UnityEngine.InputSystem {public class Mouse {public static Mouse? current=null;public Position position=new();}public class Position {public Vector2 ReadValue()=>default;}}
namespace UnityEngine.EventSystems
{
    public class BaseEventData { }
    public class PointerEventData:BaseEventData {public bool Fail;}
    public struct RaycastResult { }
    public class EventSystem {[MethodImpl(MethodImplOptions.NoInlining)]public void RaycastAll(PointerEventData data,List<RaycastResult> results){if(data.Fail)throw new InvalidOperationException("fixture error");results.Add(default);}}
    public class BaseInputModule {public int Calls;[MethodImpl(MethodImplOptions.NoInlining)]protected void HandlePointerExitAndEnter(PointerEventData data,GameObject target){Calls++;}public void Trigger(PointerEventData data,GameObject target)=>HandlePointerExitAndEnter(data,target);}
}
namespace UnityEngine.UI
{
    public class Selectable {public int Calls;[MethodImpl(MethodImplOptions.NoInlining)]protected void DoStateTransition(int state,bool instant){Calls++;}public void Trigger()=>DoStateTransition(1,false);}
    public class CanvasUpdateRegistry {public int Calls;[MethodImpl(MethodImplOptions.NoInlining)]private void PerformUpdate(){Calls++;}public void Trigger()=>PerformUpdate();}
}
public class NicksButtonSFX {public int Calls;[MethodImpl(MethodImplOptions.NoInlining)]public void OnPointerEnter(PointerEventData data){Calls++;}[MethodImpl(MethodImplOptions.NoInlining)]public void OnSelect(BaseEventData data){Calls++;}}
namespace ModConfigEnhance {static class Plugin {internal const string PluginGuid="fixture.mce";internal static Log Log=new();}class Log {public void LogInfo(object value)=>Console.WriteLine(value);public void LogWarning(object value)=>Console.WriteLine(value);}}
namespace ModConfigEnhance.Overview {static class UiPerfProbe {public static bool Enabled=true;}static class OverviewUi {public static Transform? ModalParent;}}
