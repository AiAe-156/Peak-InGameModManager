#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>State-only patch of an existing resident row. Never rebuilds its control tree.</summary>
internal sealed class OverviewEnablementBinding : MonoBehaviour
{
    internal OverviewMod Mod=null!;
    internal Func<bool> Target=null!;
    internal Toggle? Checkbox;
    private readonly Dictionary<TMP_Text,Color> _text=new();
    private readonly Dictionary<OverviewIconGraphic,Color> _icons=new();
    private readonly Dictionary<TMP_Text,Color> _chipInk=new();
    private ProfilePendingMarkers[] _marks=Array.Empty<ProfilePendingMarkers>();
    private EnablementBorderTint? _border;
    private bool _hasState, _lastTarget;
    private ProfileItemPending? _lastPending;
    internal void Capture()
    {
        foreach(var text in GetComponentsInChildren<TMP_Text>(true))if(!_text.ContainsKey(text))_text[text]=text.color;
        foreach(var icon in GetComponentsInChildren<OverviewIconGraphic>(true))if(!_icons.ContainsKey(icon))_icons[icon]=icon.color;
        foreach(var chip in GetComponentsInChildren<RoundedChip>(true))foreach(var text in chip.GetComponentsInChildren<TMP_Text>(true))_chipInk[text]=chip.Ink;
        _marks=GetComponentsInChildren<ProfilePendingMarkers>(true);
        _border=Checkbox?.GetComponent<EnablementBorderTint>();
        _hasState=false;
    }
    internal void Refresh(ProfileItemPending? pending)
    {
        bool target=Target();
        // A failed user click can change the visual while the model is unchanged.
        // Always restore both Toggle and custom tick before skipping other writes.
        if(Checkbox!=null)OverviewUi.SyncCheck(Checkbox, target);
        if(_hasState && target==_lastTarget && ReferenceEquals(pending,_lastPending))return;
        _hasState=true;_lastTarget=target;_lastPending=pending;
        if(Checkbox!=null)
        {
            var tint=ModEnablementPresentation.Tint(Mod,target,pending);
            _border ??= Checkbox.GetComponent<EnablementBorderTint>();if(_border!=null)_border.Color=tint;
            if(Checkbox.targetGraphic!=null)Checkbox.targetGraphic.color=tint;
        }
        foreach(var entry in _text)if(entry.Key!=null)entry.Key.color=target?entry.Value:OverviewUi.Muted;
        foreach(var entry in _icons)if(entry.Key!=null)entry.Key.color=target?entry.Value:OverviewUi.Muted;
        foreach(var entry in _chipInk)if(entry.Key!=null)entry.Key.color=entry.Value;
        foreach(var marks in _marks)if(marks!=null)marks.RefreshNow();
    }
}
