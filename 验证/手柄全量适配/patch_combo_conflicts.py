# -*- coding: utf-8 -*-
"""1.1.3: controller combos take part in duplicate checks (vs game controls and controller-native mod keys)."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', 'src', 'Overview'))

def edit(name, pairs):
    p = os.path.join(ROOT, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('OverviewHotkeyService.cs', [
('''    public string GameAction = "";
    public bool IsGame => GameAction.Length > 0;''', '''    public string GameAction = "";
    public bool IsGame => GameAction.Length > 0;
    /// <summary>1.1.3: the clash is on the controller side (a controller combo of this or the other setting).</summary>
    public bool Combo;'''),
('''                if (OverviewHotkeyAdapter.Overlaps(signature, game.Signature))
                    entry.Conflicts.Add(new() { Mod = entry.Mod, GameAction = game.Action, Label = game.Label, Key = game.Display });
        }''', '''                if (OverviewHotkeyAdapter.Overlaps(signature, game.Signature))
                    entry.Conflicts.Add(new() { Mod = entry.Mod, GameAction = game.Action, Label = game.Label, Key = game.Display });
        }
        // 1.1.3: controller combos. A single-button combo also fires the game's action on that button; any combo's
        // button also reaches a mod that reads that controller button itself (the combo only masks it from the game).
        foreach (var entry in _entries)
        {
            var combo = GamepadCombos.For(entry.Id); if (combo == null) continue;
            string token = "gamepad:" + OverviewHotkeyAdapter.Canonical(combo.button) + ":";
            string label = GamepadCombos.Label(combo);
            if (combo.modifier.Length == 0)
                foreach (var game in GameBindings.Actions)
                    if (game.Signature == token) entry.Conflicts.Add(new() { Mod = entry.Mod, GameAction = game.Action, Label = game.Label, Key = game.Display, Combo = true });
            foreach (var other in _entries)
            {
                if (other == entry || !signatures[other.Id].StartsWith(token, StringComparison.Ordinal)) continue;
                entry.Conflicts.Add(new() { Mod = other.Mod, Key = other.Key, Label = other.CurrentLabel, EntryId = other.Id, Section = other.Section, Combo = true });
                other.Conflicts.Add(new() { Mod = entry.Mod, Key = entry.Key, Label = label, EntryId = entry.Id, Section = entry.Section, Combo = true });
            }
        }'''),
('''    private void OnSettingChanged(object sender, SettingChangedEventArgs args) { if (!_disposed) Refresh(); }''',
 '''    private void OnSettingChanged(object sender, SettingChangedEventArgs args) { if (!_disposed) Refresh(); }
    /// <summary>Recompute duplicates after a controller combo or the modifier changed.</summary>
    internal void RefreshConflicts() { if (!_disposed) Refresh(); }'''),
])
edit('OverviewHotkeyView.cs', [
('''                        bool padConflict=entry.Conflicts.Any(IsPadConflict), keyConflict=entry.Conflicts.Any(c=>!IsPadConflict(c));''',
 '''                        bool padConflict=native?entry.Conflicts.Count>0:entry.Conflicts.Any(c=>c.Combo), keyConflict=!native&&entry.Conflicts.Any(c=>!c.Combo);'''),
('''    /// <summary>The conflict is on the controller side: a controller-native setting, or another combo / game control on a gamepad button.</summary>
    private bool IsPadConflict(OverviewHotkeyConflict conflict) => conflict.Label.StartsWith("gamepad", StringComparison.OrdinalIgnoreCase)
        || conflict.Label.IndexOf("<Gamepad>", StringComparison.OrdinalIgnoreCase) >= 0 || conflict.Key.StartsWith("combo:", StringComparison.Ordinal);
''', ''),
('''            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            EndComboRecording();''', '''            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            EndComboRecording(); Service.RefreshConflicts();'''),
('''        if(GamepadCombos.For(entry.Id)!=null)items.Add((Loc.T("Remove controller combo","移除手柄组合键","移除手把組合鍵"),()=>{GamepadCombos.Clear(entry.Id);_status(''',
 '''        if(GamepadCombos.For(entry.Id)!=null)items.Add((Loc.T("Remove controller combo","移除手柄组合键","移除手把組合鍵"),()=>{GamepadCombos.Clear(entry.Id);Service.RefreshConflicts();_status('''),
('''                        var current=KeyButton(item,native?"":entry.CurrentLabel,()=>{if(native)Record(entry);else Record(entry);},xCurrent-rowIndent,3,xPad-xCurrent-4,OverviewUi.Ivory);''',
 '''                        var current=KeyButton(item,native?"":entry.CurrentLabel,()=>Record(entry),xCurrent-rowIndent,3,xPad-xCurrent-4,OverviewUi.Ivory);'''),
])
print('ok')
