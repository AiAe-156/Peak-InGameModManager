using System;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine {
 public struct Color : IEquatable<Color> { public int Value; public Color(int value){Value=value;} public bool Equals(Color b)=>Value==b.Value; public override bool Equals(object? b)=>b is Color c&&Equals(c); public override int GetHashCode()=>Value; }
 public class Component {
  public readonly List<Component> Children=new();
  public T? GetComponent<T>() where T:Component => this as T ?? Children.OfType<T>().FirstOrDefault();
  public T[] GetComponentsInChildren<T>(bool inactive) where T:Component => Children.OfType<T>().Concat(Children.SelectMany(c=>c.GetComponentsInChildren<T>(inactive))).ToArray();
 }
 public class MonoBehaviour:Component {}
}
namespace TMPro { public class TMP_Text:UnityEngine.Component { public UnityEngine.Color color; } }
namespace UnityEngine.UI {
 public class Graphic:UnityEngine.Component {public UnityEngine.Color color;}
 public class Toggle:UnityEngine.Component {public bool isOn;public int Notify;public Graphic? targetGraphic=new();public void SetIsOnWithoutNotify(bool v)=>isOn=v;}
}
namespace ModConfigEnhance.Overview {
 internal class OverviewMod {public bool Loaded;}
 internal class ProfileItemPending {public bool Changing;}
 internal class OverviewIconGraphic:UnityEngine.UI.Graphic {}
 internal class EnablementBorderTint:UnityEngine.Component {public UnityEngine.Color Color;}
 internal class RoundedChip:UnityEngine.Component {public UnityEngine.Color Ink=new(7);}
 internal class ProfilePendingMarkers:UnityEngine.Component {public int Calls;public void RefreshNow()=>Calls++;}
 internal class OverviewToggleVisual:UnityEngine.Component {public Action<bool>? Sync;}
 internal static partial class OverviewUi {internal static UnityEngine.Color Muted=new(0);}
 internal static class ModEnablementPresentation {
  internal static UnityEngine.Color Changing=new(1), Enabled=new(2),Disabled=new(3);
  internal static bool IsChanging(OverviewMod m,bool target,ProfileItemPending? pending)=>pending?.Changing==true;
 }
}
