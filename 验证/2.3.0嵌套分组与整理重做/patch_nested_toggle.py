from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')

# Row knows its outer and inner mods.
sub('OverviewModels.cs', '''    internal string CopyKey = "";
    internal bool DuplicateCopy => CopyRows.Count > 1;''', '''    internal string CopyKey = "";
    internal bool DuplicateCopy => CopyRows.Count > 1;
    /// <summary>2.3.0: the mod whose folder this one is nested in (nearest, packaging layout excluded); null at top level.</summary>
    internal OverviewMod? Outer;
    /// <summary>Mods nested directly in this one's folder.</summary>
    internal List<OverviewMod> Inner = new();''')

# Profile entry remembers which inner mods were on when this outer mod was turned off.
sub('OverviewProfileModels.cs', '''    public string copy = "";''', '''    public string copy = "";
    /// <summary>2.3.0: GUIDs of nested mods that were enabled when this (outer) mod was disabled; restored when it is enabled again.</summary>
    public List<string> nestedEnabled = new();''')

# Catalogue links rows after consolidation.
sub('OverviewCatalogue.cs', '''        return rows.OrderBy(r => r.Rule.order).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string CopyKey(''', '''        LinkNested(rows);
        return rows.OrderBy(r => r.Rule.order).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Marks each row with the mod whose folder it is nested in (see <see cref="NestedMods"/>). Rows sharing a
    /// package root are one package, so only distinct roots are compared.</summary>
    private static void LinkNested(List<OverviewMod> rows)
    {
        var byRoot = rows.Where(r => !string.IsNullOrEmpty(r.PackageRoot) && Directory.Exists(r.PackageRoot))
            .GroupBy(r => NestedMods.Normalize(Path.GetFullPath(r.PackageRoot)), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var outers = NestedMods.OuterOf(byRoot.Keys, NestedMods.ManifestName);
        foreach (var row in rows) { row.Outer = null; row.Inner.Clear(); }
        foreach (var pair in outers)
        {
            if (pair.Value.Length == 0 || !byRoot.TryGetValue(pair.Value, out var outerRows) || !byRoot.TryGetValue(pair.Key, out var innerRows)) continue;
            var outer = outerRows.OrderByDescending(r => r.Loaded).First();
            foreach (var inner in innerRows) { inner.Outer = outer; foreach (var o in outerRows) if (!o.Inner.Contains(inner)) o.Inner.Add(inner); }
        }
    }

    private static string CopyKey(''')

# Enablement draft: cascade with restore memory; an inner mod stays off while its outer is off.
sub('OverviewEnablementDraft.cs', '''    internal static void Set(List<OverviewProfileMod> draft, OverviewMod mod, bool enabled)
    {''', '''    /// <summary>Whether this row can be switched on now: not while the mod it is nested in is (planned) off.</summary>
    internal static bool BlockedByOuter(IReadOnlyList<OverviewProfileMod> draft, OverviewMod mod) => mod.Outer != null && !Planned(draft, mod.Outer);

    /// <summary>Sets a mod and carries it to the mods nested in its folder: turning the outer off turns them off and
    /// remembers which were on; turning it back on restores exactly those. An inner mod cannot be turned on while its
    /// outer is off.</summary>
    internal static void SetWithNested(List<OverviewProfileMod> draft, OverviewMod mod, bool enabled)
    {
        if (enabled && BlockedByOuter(draft, mod)) throw new InvalidOperationException(Loc.F("{0} is inside {1}, which is disabled. Enable {1} first.", "{0} 位于已停用的「{1}」里，请先启用「{1}」。", "{0} 位於已停用的「{1}」裡，請先啟用「{1}」。", mod.Name, mod.Outer!.Name));
        bool was = Planned(draft, mod);
        Set(draft, mod, enabled);
        if (mod.Inner.Count == 0 || was == enabled) return;
        var entry = draft.FirstOrDefault(m => m.guid == mod.Guid);
        if (!enabled)
        {
            var on = mod.Inner.Where(i => Planned(draft, i)).ToList();
            if (entry != null) entry.nestedEnabled = on.Select(i => i.Guid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var inner in on) SetWithNested(draft, inner, false);
        }
        else
        {
            var restore = new HashSet<string>(entry?.nestedEnabled ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var inner in mod.Inner.Where(i => restore.Contains(i.Guid))) SetWithNested(draft, inner, true);
            if (entry != null) entry.nestedEnabled.Clear();
        }
    }

    internal static void Set(List<OverviewProfileMod> draft, OverviewMod mod, bool enabled)
    {''')
sub('OverviewRepository.Profiles.cs', '''            OverviewEnablementDraft.Set(_draftMods, mod, enabled);
        }
        PersistRecoveryDraft();''', '''            OverviewEnablementDraft.SetWithNested(_draftMods, mod, enabled);
        }
        PersistRecoveryDraft();''')
sub('OverviewRepository.Profiles.cs', '''    public bool PlannedEnabled(OverviewMod mod) => OverviewEnablementDraft.Planned(_draftMods, mod);''',
'''    public bool PlannedEnabled(OverviewMod mod) => OverviewEnablementDraft.Planned(_draftMods, mod);
    /// <summary>2.3.0: an inner mod cannot be switched on while the mod it is nested in is planned off.</summary>
    public bool BlockedByOuter(OverviewMod mod) => OverviewEnablementDraft.BlockedByOuter(_draftMods, mod);''')

# List: the inner checkbox is unavailable while the outer is off; badge for nested mods.
sub('OverviewListView.cs', '''                var enabled = OverviewUi.Check(row, _repo.PlannedEnabled(mod), value => Toggle(mod, value)); enabled.interactable = mod.CanToggle;''',
'''                var enabled = OverviewUi.Check(row, _repo.PlannedEnabled(mod), value => Toggle(mod, value)); enabled.interactable = mod.CanToggle && !_repo.BlockedByOuter(mod);
                if (mod.Outer != null && _repo.BlockedByOuter(mod)) AddHint(enabled.gameObject, T("The outer mod is disabled: ", "外层模组已停用：") + OverviewLanguageBridge.GetDisplayName(mod.Outer));''')
print('ok')
