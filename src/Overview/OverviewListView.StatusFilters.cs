#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewListView
{
    private HashSet<string>? _relatedMods;
    private HashSet<string> _relatedPackages = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<OverviewMod>? _issueMods;
    private float _issueModsUntil;
    internal void ShowRelatedMods(IEnumerable<OverviewMod> mods)
    {
        var members = mods.ToArray();
        _relatedMods = new HashSet<string>(members.Select(m => m.Id.Length > 0 ? m.Id : m.Guid), StringComparer.OrdinalIgnoreCase);
        _relatedPackages = new HashSet<string>(members.SelectMany(m => m.PackageDependencies).Select(d => d.LastIndexOf('-') is int at && at > 0 ? d.Substring(0, at) : d), StringComparer.OrdinalIgnoreCase);
        ClearOverviewSearch(); Tags.include.Clear(); Tags.exclude.Clear(); _sourceFilter = -1; _repo.State.hideDisabled = false;
        Render();
    }
    private bool MatchesRelated(OverviewMod mod) => _relatedMods == null || _relatedMods.Contains(mod.Id.Length > 0 ? mod.Id : mod.Guid);
    internal bool MatchesStatus(OverviewMod mod, string tag)
    {
        if (tag == "$update") return mod.HasUpdate;
        if (tag == "$enabled") return _repo.PlannedEnabled(mod);
        if (tag == "$disabled") return !_repo.PlannedEnabled(mod);
        if (tag == "$duplicate") return OverviewPluginCopies.Contains(mod, OverviewPluginCopies.Duplicates(_mods)) || OverviewPluginCopies.SameNameCopies(_mods).Any(g => g.Contains(mod));
        if (tag == "$changed") return _repo.PlannedEnabled(mod) != mod.TargetEnabled || _repo.DraftConfig.values.Any(v =>
            (v.pluginGuid.Equals(mod.Guid, StringComparison.OrdinalIgnoreCase) || mod.PluginGuids.Contains(v.pluginGuid, StringComparer.OrdinalIgnoreCase))
            && _repo.GetPendingState().Parameters.TryGetValue(v.Identity, out var pending) && pending.NeedsSave);
        if (tag == "$issue")
        {
            if (_issueMods == null || UnityEngine.Time.unscaledTime >= _issueModsUntil)
            { _issueMods = new HashSet<OverviewMod>(ConfigurationIssueReport.Related(_repo, _mods)); _issueModsUntil = UnityEngine.Time.unscaledTime + .2f; }
            return _issueMods.Contains(mod);
        }
        return OverviewTagSelection.Has(mod, tag);
    }
}

