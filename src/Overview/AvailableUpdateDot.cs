#nullable enable
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>One available-update appearance for normal mods and Core/Patcher rows. The chip itself is drawn by
/// <see cref="StatusChip"/> so it matches the other markers in the name column.</summary>
internal static class AvailableUpdateDot
{
    internal const float HitSize=16,VersionReserve=18;
    internal static readonly Color Green=new(.50f,.77f,.49f,1);
    internal static string Hint(string current,string latest)=>Loc.F("Update available: {0} → {1}",
        "有新版本：{0} → {1}","有新版本：{0} → {1}",current,latest);

    internal static void Name(RectTransform host,bool unread,string hint,Action open,
        Action<GameObject,string> bindHover,Action<string> show,Action hide,ScrollRect scroll)
        =>Create(StatusChip.Create(host,StatusChip.UpdateSlot,Green),
            hint,open,bindHover,show,hide,scroll);
    internal static void Version(Transform row,float x,float y,float width,string hint,Action open,
        Action<GameObject,string> bindHover,Action<string> show,Action hide,ScrollRect scroll)
        =>Create(StatusChip.CreateAt(row,x+Mathf.Max(0,width-HitSize),y,Green),hint,open,bindHover,show,hide,scroll);

    internal static void BindVersion(GameObject target,string hint,Action open,
        Action<GameObject,string> bindHover,Action<string> show,Action hide,ScrollRect scroll)
        =>Bind(target,hint,open,bindHover,show,hide,scroll);

    private static void Create(RectTransform target,string hint,Action open,
        Action<GameObject,string> bindHover,Action<string> show,Action hide,ScrollRect scroll)
    {
        var hit=target.gameObject.AddComponent<Image>();hit.color=Color.clear;
        var button=target.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.transition=Selectable.Transition.None;
        button.onClick.AddListener(()=>{hide();open();});
        // Button already handles mouse and controller submission.
        Bind(target.gameObject,hint,null,bindHover,show,hide,scroll);
    }
    private static void Bind(GameObject target,string hint,Action? submit,
        Action<GameObject,string> bindHover,Action<string> show,Action hide,ScrollRect scroll)
    {
        bindHover(target,hint);
        var selectable=target.GetComponent<Selectable>();
        if(selectable==null){selectable=target.AddComponent<Selectable>();selectable.transition=Selectable.Transition.None;selectable.targetGraphic=target.GetComponent<Graphic>();}
        var focus=target.GetComponent<AvailableUpdateFocus>()??target.AddComponent<AvailableUpdateFocus>();
        focus.Show=()=>show(hint);focus.Hide=hide;focus.Submit=submit;
        GamepadSupport.MarkSelectable(target,scroll,(RectTransform)target.transform);
    }
}

internal sealed class AvailableUpdateFocus:MonoBehaviour,ISelectHandler,IDeselectHandler,ISubmitHandler
{
    internal Action? Show,Hide,Submit;
    private bool _focused;
    public void OnSelect(BaseEventData data){_focused=true;Show?.Invoke();}
    public void OnDeselect(BaseEventData data){_focused=false;Hide?.Invoke();}
    public void OnSubmit(BaseEventData data){if(Submit==null)return;Hide?.Invoke();Submit();data.Use();}
    private void OnDisable(){if(_focused)Hide?.Invoke();_focused=false;}
}
