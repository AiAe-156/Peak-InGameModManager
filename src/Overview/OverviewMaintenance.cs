#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
namespace ModConfigEnhance.Overview;
/// <summary>One low-frequency safety sweep, plus explicit dirty work, instead of per-control LateUpdate.
/// A sweep is a round of owners drained under a shared per-frame budget, so one frame never pays for the
/// whole table's refreshes at once.</summary>
internal sealed class OverviewMaintenance:MonoBehaviour
{
    private static OverviewMaintenance? _instance;
    private static readonly Dictionary<MonoBehaviour,Action> Checks=new();
    private static readonly Dictionary<MonoBehaviour,Action> Dirty=new();
    private static readonly Dictionary<MonoBehaviour,Action> EventChecks=new();
    private float _eventNext;
    private bool _dark;
    private int _language=-1;
    private float _next;
    private readonly List<MonoBehaviour> _round=new();
    private readonly HashSet<MonoBehaviour> _roundSet=new();
    private int _roundCursor;
    private readonly List<MonoBehaviour> _urgent=new();
    private readonly HashSet<MonoBehaviour> _urgentSet=new();
    private int _urgentCursor;
    // Same shape as the row builder's budget: at least one step a frame, then a shared 2ms ceiling.
    private readonly RowBuildBudget _sweepBudget=new(maxSteps:64);
    /// <summary>Raised when something outside the periodic tick invalidated what the sweep displays.</summary>
    internal static bool PendingInvalidated;
    private static void Ensure()
    {
        if(_instance!=null)return;
        var host=new GameObject("MCE display maintenance");UnityEngine.Object.DontDestroyOnLoad(host);
        _instance=host.AddComponent<OverviewMaintenance>();
    }
    internal static void Register(MonoBehaviour owner,Action refresh) { Ensure();Checks[owner]=refresh;Mark(owner,refresh); }
    internal static void RegisterEvent(MonoBehaviour owner,Action refresh) { Ensure();EventChecks[owner]=refresh;Mark(owner,refresh); }
    internal static void Mark(MonoBehaviour owner,Action refresh) { Ensure();Dirty[owner]=refresh;Enqueue(owner); }
    internal static void Remove(MonoBehaviour owner)
    { Checks.Remove(owner);EventChecks.Remove(owner);Dirty.Remove(owner);_instance?._urgentSet.Remove(owner); }
    private static void Enqueue(MonoBehaviour owner)
    {
        if(_instance==null || !_instance._urgentSet.Add(owner))return;
        _instance._urgent.Add(owner);
    }
    private void LateUpdate()
    {
        // Do not churn dirty dictionaries/theme reflection for retained but inactive UI.
        bool visible = false;
        foreach (var owner in Checks.Keys) if (owner != null && owner.isActiveAndEnabled) { visible = true; break; }
        if (!visible) foreach (var owner in EventChecks.Keys) if (owner != null && owner.isActiveAndEnabled) { visible = true; break; }
        if (!visible) foreach (var owner in Dirty.Keys) if (owner != null && owner.isActiveAndEnabled) { visible = true; break; }
        if (!visible) return;
        bool dark=ModConfigLook.IsDark;int language=Loc.TranslationRevision;
        bool tickDue=Time.unscaledTime>=_next;
        bool eventDue=dark!=_dark || language!=_language || Time.unscaledTime>=_eventNext;
        bool pendingDue=PendingInvalidated;PendingInvalidated=false;
        if(tickDue)_next=Time.unscaledTime+.2f;
        if(eventDue){_dark=dark;_language=language;_eventNext=Time.unscaledTime+2;}
        // One round at a time: re-marking the table while a round is still draining would keep pushing
        // finished owners back into the queue, so a slow round could never finish.
        if(_roundCursor>=_round.Count)
        {
            bool sweepDue=tickDue||pendingDue;
            if(sweepDue)foreach(var item in Checks)Dirty[item.Key]=item.Value;
            if(eventDue)foreach(var item in EventChecks)Dirty[item.Key]=item.Value;
            if(sweepDue||eventDue||_urgent.Count>0)
            {
                _round.Clear();_roundSet.Clear();_roundCursor=0;
                foreach(var item in Dirty)AddToRound(item.Key);
            }
        }
        for(;;)
        {
            // Entries that no longer need a refresh cost no budget: an owner already served through the
            // explicit queue must not spend the rest of the table's frame budget.
            while(_urgentCursor>=_urgent.Count && _roundCursor<_round.Count && !Dirty.ContainsKey(_round[_roundCursor]))_roundCursor++;
            if(_urgentCursor>=_urgent.Count && _roundCursor>=_round.Count)return;
            if(!_sweepBudget.TryTake(Time.frameCount,System.Diagnostics.Stopwatch.GetTimestamp()))return;
            MonoBehaviour owner;
            if(_urgentCursor<_urgent.Count)
            {
                owner=_urgent[_urgentCursor++];_urgentSet.Remove(owner);
                if(_urgentCursor>=_urgent.Count){_urgent.Clear();_urgentCursor=0;}
            }
            else owner=_round[_roundCursor++];
            if(!Dirty.Remove(owner,out var refresh))continue;
            if(owner==null) { Checks.Remove(owner!);EventChecks.Remove(owner!);continue; }
            if(owner.isActiveAndEnabled)refresh();
        }
    }
    // Reference comparison: a destroyed owner still has to reach the sweep so it can be dropped there.
    private void AddToRound(MonoBehaviour owner) { if(!ReferenceEquals(owner,null) && _roundSet.Add(owner))_round.Add(owner); }
}
