#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>
/// Planned enablement is recorded per GUID so profiles stay shareable, but one GUID may be installed in several
/// package folders. The entry then also names the copy it means, and only one copy is ever planned on: checking one
/// copy moves the choice there, unchecking a copy that is not the chosen one changes nothing. An entry without a
/// choice keeps every copy at its physical state, so opening the overview never produces a change by itself.
/// </summary>
internal static class OverviewEnablementDraft
{
    /// <summary>2.3.1: a mod nested in a mod that is planned off is planned off too, so saving disables it on disk even
    /// when the draft only names the outer (an imported profile, an older draft). A nested mod that cannot be toggled
    /// here (protected library, identity conflict) is left as it is rather than failing the outer.</summary>
    internal static bool Planned(IReadOnlyList<OverviewProfileMod> draft, OverviewMod mod)
        => Own(draft, mod) && (mod.Outer == null || !mod.CanToggle || Planned(draft, mod.Outer));

    /// <summary>Writes the carried state into the draft: a nested mod whose outer is planned off gets its own entry set to
    /// off, so what is saved matches what is applied (otherwise its entry kept saying on while the files were off, and it
    /// showed as "needs apply" forever). Returns whether anything changed.</summary>
    internal static bool CarryOuterState(List<OverviewProfileMod> draft, IEnumerable<OverviewMod> mods)
    {
        bool changed = false;
        foreach (var mod in mods)
            if (mod.Outer != null && mod.CanToggle && Own(draft, mod) && !Planned(draft, mod)) { Set(draft, mod, false); changed = true; }
        return changed;
    }

    /// <summary>The mod's own planned state, ignoring the mod it is nested in.</summary>
    private static bool Own(IReadOnlyList<OverviewProfileMod> draft, OverviewMod mod)
    {
        var entry = draft.FirstOrDefault(m => m.guid == mod.Guid);
        if (entry == null) return mod.TargetEnabled;
        if (!mod.DuplicateCopy) return entry.enabled;
        string copy = Resolve(entry.copy, mod);
        return entry.enabled && (copy.Length == 0 ? mod.TargetEnabled : Same(copy, mod.CopyKey));
    }

    /// <summary>Whether this row can be switched on now: not while the mod it is nested in is (planned) off.</summary>
    internal static bool BlockedByOuter(IReadOnlyList<OverviewProfileMod> draft, OverviewMod mod) => mod.Outer != null && mod.CanToggle && !Planned(draft, mod.Outer);

    /// <summary>Sets a mod and carries it to the mods nested in its folder: turning the outer off turns them off and
    /// remembers which were on; turning it back on restores exactly those. An inner mod cannot be turned on while its
    /// outer is off.</summary>
    internal static void SetWithNested(List<OverviewProfileMod> draft, OverviewMod mod, bool enabled)
    {
        if (enabled && BlockedByOuter(draft, mod)) throw new InvalidOperationException(Loc.F("{0} is inside {1}, which is disabled. Enable {1} first.", "{0} 位于已停用的「{1}」里，请先启用「{1}」。", "{0} 位於已停用的「{1}」裡，請先啟用「{1}」。", mod.Name, mod.Outer!.Name));
        bool was = Planned(draft, mod);
        // Read before the outer changes: once it is off every inner reads as off.
        var on = mod.Inner.Where(i => i.CanToggle && Planned(draft, i)).ToList();
        Set(draft, mod, enabled);
        if (mod.Inner.Count == 0 || was == enabled) return;
        var entry = draft.FirstOrDefault(m => m.guid == mod.Guid);
        if (!enabled)
        {
            if (entry != null) entry.nestedEnabled = on.Select(i => i.Guid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var inner in on) SetWithNested(draft, inner, false);
        }
        else
        {
            var restore = new HashSet<string>(entry?.nestedEnabled ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var inner in mod.Inner.Where(i => i.CanToggle && restore.Contains(i.Guid))) SetWithNested(draft, inner, true);
            if (entry != null) entry.nestedEnabled.Clear();
        }
    }

    internal static void Set(List<OverviewProfileMod> draft, OverviewMod mod, bool enabled)
    {
        foreach (string guid in mod.PluginGuids.Count > 0 ? mod.PluginGuids : new List<string> { mod.Guid })
        {
            var value = draft.FirstOrDefault(p => p.guid == guid);
            if (value == null) { value = new OverviewProfileMod { id = guid, guid = guid, name = mod.Name, version = mod.DisplayVersion }; draft.Add(value); }
            if (!mod.DuplicateCopy) { value.enabled = enabled; value.copy = ""; continue; }
            if (enabled) { value.enabled = true; value.copy = mod.CopyKey; continue; }
            string copy = Resolve(value.copy, mod);
            if (copy.Length > 0 && !Same(copy, mod.CopyKey)) continue;   // another copy is the chosen one; this one is already off
            // No choice recorded yet: keep a copy that is on today instead of disabling the whole GUID.
            var other = copy.Length == 0 ? mod.CopyRows.FirstOrDefault(c => !ReferenceEquals(c, mod) && c.TargetEnabled) : null;
            if (other != null) { value.enabled = true; value.copy = other.CopyKey; }
            else value.enabled = false;
        }
    }

    /// <summary>A recorded choice that matches no installed copy (folder removed, profile from another machine) counts as none.</summary>
    private static string Resolve(string copy, OverviewMod mod) => copy.Length > 0 && mod.CopyRows.Any(c => Same(c.CopyKey, copy)) ? copy : "";
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
