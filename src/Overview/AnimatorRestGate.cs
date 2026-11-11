#nullable enable
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>Only MCE-owned, single-layer non-looping Normal states may rest. Native triggers run unchanged.</summary>
internal sealed class AnimatorRestGate : MonoBehaviour
{
    private static readonly Dictionary<Selectable,AnimatorRestGate> Gates=new();
    private static ConfigEntry<bool>? _option;
    private static bool _hooked;
    private Animator? _animator;
    private Selectable? _button;
    private bool _sleeping, _originalKeep, _originalWrite, _installed;
    private int _state, _stable;
    private int _normalHash;
    private bool _observedNonNormal;
    private float _wakeAt, _nextCheck;
    internal static void Install(Harmony harmony,ConfigFile config)
    {
        _option=config.Bind("4. Mod overview","Rest idle button animations",true,
            "Pause completed non-looping MCE button Normal animations. Original hover, click, selection and sounds are retained.");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Selectable),"DoStateTransition"),
                prefix:new HarmonyMethod(typeof(AnimatorRestGate),nameof(BeforeTransition)));
        }
        catch(Exception error) { Plugin.Log.LogWarning("[按钮休眠] 保留原生动画："+error.Message);return; }
        _hooked=true;_option.SettingChanged+=OptionChanged;
    }
    internal static void Shutdown()
    {
        if(_option!=null)_option.SettingChanged-=OptionChanged;
        foreach(var gate in Gates.Values)if(gate!=null)gate.Restore();
        Gates.Clear();_hooked=false;
    }
    private static void OptionChanged(object sender,EventArgs e)
    { foreach(var gate in Gates.Values)if(gate!=null)gate.Wake(); }
    private static void BeforeTransition(Selectable __instance,int __0)
    {
        if(!Gates.TryGetValue(__instance,out var gate) || gate==null)return;
        if(__0!=0)gate._observedNonNormal=true;
        gate._state=__0;gate.Wake();
    }
    internal static void Attach(Button button)
    {
        if(!_hooked || button.transition!=Selectable.Transition.Animation)return;
        var animator=button.GetComponent<Animator>();
        if(animator==null || !animator.enabled || animator.runtimeAnimatorController==null)return;
        var gate=button.gameObject.AddComponent<AnimatorRestGate>();
        gate._button=button;gate._animator=animator;
        gate._originalKeep=animator.keepAnimatorStateOnDisable;
        gate._originalWrite=animator.writeDefaultValuesOnDisable;
        gate._installed=true;Gates[button]=gate;gate._wakeAt=Time.unscaledTime;
    }
    private void Wake()
    {
        _stable=0;_wakeAt=Time.unscaledTime;_nextCheck=0;
        if(_sleeping && _animator!=null)
        {
            _animator.enabled=true;_sleeping=false;
            _animator.keepAnimatorStateOnDisable=_originalKeep;
            _animator.writeDefaultValuesOnDisable=_originalWrite;
        }
        enabled=true;
    }
    private void Restore()
    {
        if(!_installed || _animator==null)return;
        if(_sleeping)_animator.enabled=true;
        _sleeping=false;
        _animator.keepAnimatorStateOnDisable=_originalKeep;
        _animator.writeDefaultValuesOnDisable=_originalWrite;
    }
    private void OnEnable() { _stable=0;_wakeAt=Time.unscaledTime;_nextCheck=0; }
    private void Update()
    {
        var animator=_animator;
        if(_sleeping || Time.unscaledTime<_nextCheck)return;
        if(_option?.Value!=true) { _nextCheck=Time.unscaledTime+2;return; }
        if(!_installed || animator==null || !animator.enabled
            || !animator.isInitialized || animator.layerCount!=1 || _state!=0
            || Time.unscaledTime-_wakeAt<.2f || animator.IsInTransition(0)) { _stable=0;return; }
        var state=animator.GetCurrentAnimatorStateInfo(0);
        if(state.loop || state.speed<=0 || animator.speed<=0) { _stable=0;_nextCheck=Time.unscaledTime+2;return; }
        if(state.normalizedTime<1) { _stable=0;return; }
        // Learn only a settled native Normal state after initial enable. During an
        // event transition Unity may expose the previous clip for one evaluation.
        if(_normalHash==0 && !_observedNonNormal)_normalHash=state.fullPathHash;
        if(_normalHash==0 || state.fullPathHash!=_normalHash) { _stable=0;return; }
        if(++_stable<3)return;
        animator.keepAnimatorStateOnDisable=true;animator.writeDefaultValuesOnDisable=false;
        _sleeping=true;animator.enabled=false;
    }
    private void OnDisable()
    {
        Restore();
    }
    internal static void DetachTree(Transform root)
    {
        foreach(var gate in root.GetComponentsInChildren<AnimatorRestGate>(true))gate.Detach();
    }
    private void Detach()
    { Restore();if(!ReferenceEquals(_button,null))Gates.Remove(_button);_installed=false; }
    private void OnDestroy() => Detach();
}
