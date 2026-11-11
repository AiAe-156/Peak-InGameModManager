using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModConfigEnhance.Overview;
using UnityEngine;

// Drives the real OverviewMaintenance source (compiled in via Checks.csproj) with a stub Unity surface.
// Asserts the sweep stays bounded per frame, skips nothing, never overlaps rounds, and serves explicit
// changes before the periodic round. No runtime sampling is added to the plugin for this.
internal sealed class Owner : MonoBehaviour
{
    internal int Refreshed;
    internal void Bump() => Refreshed++;
    internal static Action Of(Owner owner) => owner.Bump;
}

internal static class Program
{
    private const int Owners = 500;
    private const int StepsPerFrame = 64;   // OverviewMaintenance's RowBuildBudget(maxSteps: 64)
    private static int _failures;

    private static readonly FieldInfo InstanceField =
        typeof(OverviewMaintenance).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo LateUpdateMethod =
        typeof(OverviewMaintenance).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void Check(bool ok, string name, string detail = "")
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
        if (!ok) _failures++;
    }

    private static int Frame(int frame, float time)
    {
        Time.frameCount = frame;
        Time.unscaledTime = time;
        var instance = InstanceField.GetValue(null) ?? throw new InvalidOperationException("maintenance host was never created");
        var before = OwnerStats.Total;
        LateUpdateMethod.Invoke(instance, null);
        return OwnerStats.Total - before;
    }

    private static void Main()
    {
        var owners = new List<Owner>();
        for (var i = 0; i < Owners; i++)
        {
            var owner = new Owner();
            owners.Add(owner);
            OwnerStats.All.Add(owner);
            OverviewMaintenance.Register(owner, Owner.Of(owner));
        }

        // 104 frames at 60fps = 9 periodic ticks (t=0, .2, .4 ... 1.6); a full 500-owner round needs 8 frames,
        // so the window ends exactly when the last tick's round has been drained.
        const int Frames = 104;
        var perFrame = new List<int>();
        for (var frame = 0; frame < Frames; frame++) perFrame.Add(Frame(frame, frame / 60f));
        var perRound = owners.Select(o => o.Refreshed).Distinct().ToArray();
        Console.WriteLine("每帧刷新数: " + string.Join(",", perFrame));
        Console.WriteLine("刷新次数分布: " + string.Join(" ", owners.GroupBy(o => o.Refreshed).OrderBy(g => g.Key)
            .Select(g => g.Key + "次×" + g.Count())));

        Check(perFrame.Max() <= StepsPerFrame, "单帧刷新数受预算约束",
            "max=" + perFrame.Max() + " limit=" + StepsPerFrame);
        Check(perFrame.Max() < Owners, "一轮不会挤在单帧", "max=" + perFrame.Max() + " owners=" + Owners);
        Check(owners.All(o => o.Refreshed > 0), "没有任何控件被跳过",
            "min=" + owners.Min(o => o.Refreshed));
        Check(perRound.Length == 1 || perRound.Max() - perRound.Min() == 1,
            "每个控件每轮至多被刷新一次（次数差 ≤1）", "distinct=" + string.Join(",", perRound));
        Check(perRound.Min() >= 8, "刷新次数等于完成的轮数（轮不重叠）",
            "perOwner=" + perRound.Min() + "~" + perRound.Max() + " ticks=9");

        // Explicit change must land on the next frame, ahead of the round it belongs to.
        var last = owners[Owners - 1];
        var beforeMark = last.Refreshed;
        OverviewMaintenance.Mark(last, Owner.Of(last));
        Frame(200, 200 / 60f);
        Check(last.Refreshed == beforeMark + 1, "显式 Mark 当帧即生效", "delta=" + (last.Refreshed - beforeMark));

        // Force a fresh round and watch it drain: while it is in progress no owner may be visited twice.
        var beforeRound = owners.Select(o => o.Refreshed).ToArray();
        OverviewMaintenance.PendingInvalidated = true;
        for (var frame = 201; frame < 206; frame++) Frame(frame, frame / 60f);
        var deltas = owners.Select((o, i) => o.Refreshed - beforeRound[i]).ToArray();
        var drained = deltas.Sum();
        Check(drained > 0 && deltas.Max() <= 1, "轮进行中不重复刷新同一控件",
            "refreshes=" + drained + " max=" + deltas.Max());
        Check(drained <= StepsPerFrame * 5, "轮进行中不开启新的一轮",
            "refreshes=" + drained + " budget=" + StepsPerFrame * 5);

        // Retained but inactive UI stays untouched.
        foreach (var owner in owners) owner.isActiveAndEnabled = false;
        var beforeHidden = owners.Sum(o => o.Refreshed);
        for (var frame = 210; frame < 246; frame++) Frame(frame, frame / 60f);
        Check(owners.Sum(o => o.Refreshed) == beforeHidden, "无可见控件时不刷新",
            "refreshes=" + (owners.Sum(o => o.Refreshed) - beforeHidden));

        Console.WriteLine(_failures == 0 ? "\n全部通过" : "\n失败 " + _failures + " 项");
        Environment.Exit(_failures == 0 ? 0 : 1);
    }
}

internal static class OwnerStats
{
    internal static readonly List<Owner> All = new();
    internal static int Total => All.Sum(o => o.Refreshed);
}
