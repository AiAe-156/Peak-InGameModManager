# -*- coding: utf-8 -*-
"""1.1.2: game controls count as hotkey conflicts (one-off patch script)."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src', 'Overview'))

def patch(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:80], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)
    return nl

nl = patch('OverviewHotkeyService.cs', [
('''internal sealed class OverviewHotkeyConflict
{
    public OverviewMod Mod = null!;
    public string Key = "", Label = "";
    public string EntryId = "", Section = "";
}''', '''internal sealed class OverviewHotkeyConflict
{
    public OverviewMod Mod = null!;
    public string Key = "", Label = "";
    public string EntryId = "", Section = "";
    /// <summary>1.1.2: the other side is one of the game's own controls (no entry to jump to).</summary>
    public string GameAction = "";
    public bool IsGame => GameAction.Length > 0;
}'''),
('''            a.Conflicts.Add(new() { Mod = b.Mod, Key = b.Key, Label = b.CurrentLabel, EntryId = b.Id, Section = b.Section });
            b.Conflicts.Add(new() { Mod = a.Mod, Key = a.Key, Label = a.CurrentLabel, EntryId = a.Id, Section = a.Section });
        }
        Changed?.Invoke();''', '''            a.Conflicts.Add(new() { Mod = b.Mod, Key = b.Key, Label = b.CurrentLabel, EntryId = b.Id, Section = b.Section });
            b.Conflicts.Add(new() { Mod = a.Mod, Key = a.Key, Label = a.CurrentLabel, EntryId = a.Id, Section = a.Section });
        }
        // 1.1.2 (user): the keybindings page is about keys used while playing, so the game's own controls count too
        // (keyboard and controller). A mod key on the same button as Jump / Interact / Use fires both.
        foreach (var entry in _entries)
        {
            string signature = signatures[entry.Id];
            if (signature.Length == 0) continue;
            foreach (var game in GameBindings.Actions)
                if (OverviewHotkeyAdapter.Overlaps(signature, game.Signature))
                    entry.Conflicts.Add(new() { Mod = entry.Mod, GameAction = game.Action, Label = game.Label, Key = game.Display });
        }
        Changed?.Invoke();'''),
])

APPEND = r'''
/// <summary>The game's own in-play controls (its "Default" action map), read live from the Input System so player
/// rebinds are respected. Menu-only actions and the catch-all AnyKey are left out. Two-button chords (push to talk
/// = LB + RB) are skipped: a single mod key on LB alone does not trigger them.</summary>
internal static class GameBindings
{
    internal sealed class GameAction { public string Action = "", Signature = "", Label = "", Display = ""; }
    private static List<GameAction>? _cache;
    private static int _cacheFrame = -1000;
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    { "AnyKey", "Look", "Move", "Navigate", "Navigate Analog", "NavigateWheel", "Point", "Click", "RightClick", "MiddleClick", "ScrollWheel",
      "TrackedDevicePosition", "TrackedDeviceOrientation", "UIConfirm", "UICancel", "UITabLeft", "UITabRight", "OpenDebugMenu", "Submit", "Cancel",
      "TabLeft", "TabRight" };
    internal static IReadOnlyList<GameAction> Actions
    {
        get
        {
            if (_cache != null && UnityEngine.Time.frameCount - _cacheFrame < 600) return _cache;
            _cacheFrame = UnityEngine.Time.frameCount;
            var list = new List<GameAction>();
            try
            {
                var map = UnityEngine.InputSystem.InputSystem.actions != null ? UnityEngine.InputSystem.InputSystem.actions.FindActionMap("Default") : null;
                if (map != null)
                    foreach (var action in map.actions)
                    {
                        if (Skip.Contains(action.name)) continue;
                        foreach (var binding in action.bindings)
                        {
                            if (binding.isComposite || binding.isPartOfComposite) continue;
                            string path = binding.effectivePath ?? "";
                            if (!OverviewHotkeyAdapter.TryPath(path, out string device, out string control)) continue;
                            string signature = device.ToLowerInvariant() + ":" + OverviewHotkeyAdapter.Canonical(control) + ":";
                            if (list.Any(g => g.Action == action.name && g.Signature == signature)) continue;
                            list.Add(new GameAction { Action = action.name, Signature = signature, Label = device + " / " + control, Display = Name(action.name) });
                        }
                    }
            }
            catch (Exception error) { Plugin.Log.LogDebug("[hotkeys] reading game controls failed: " + error.Message); }
            _cache = list;
            return list;
        }
    }
    /// <summary>Readable name for a game action (three languages).</summary>
    internal static string Name(string action) => action switch
    {
        "Jump" => Loc.T("Jump", "跳跃", "跳躍"), "Interact" => Loc.T("Interact", "交互", "互動"), "Drop" => Loc.T("Drop", "丢弃", "丟棄"),
        "Crouch" => Loc.T("Crouch", "蹲下", "蹲下"), "CrouchToggle" => Loc.T("Crouch (toggle)", "蹲下（切换）", "蹲下（切換）"),
        "Sprint" => Loc.T("Sprint", "疾跑", "衝刺"), "SprintToggle" => Loc.T("Sprint (toggle)", "疾跑（切换）", "衝刺（切換）"),
        "UsePrimary" => Loc.T("Use", "使用", "使用"), "UseSecondary" => Loc.T("Use (secondary)", "使用（副）", "使用（副）"),
        "Emote" => Loc.T("Emote", "表情", "表情"), "Ping" => Loc.T("Ping", "标记", "標記"), "PushToTalk" => Loc.T("Push to talk", "按键说话", "按鍵說話"),
        "Pause" => Loc.T("Pause", "暂停", "暫停"), "UnselectSlot" => Loc.T("Put away item", "收起物品", "收起物品"),
        "SelectSlotForward" => Loc.T("Next slot", "下一个物品格", "下一個物品格"), "SelectSlotBackward" => Loc.T("Previous slot", "上一个物品格", "上一個物品格"),
        "ScrollForward" => Loc.T("Scroll forward", "物品栏向前", "物品欄向前"), "ScrollBackward" => Loc.T("Scroll backward", "物品栏向后", "物品欄向後"),
        "SelectBackpack" => Loc.T("Backpack", "背包", "背包"), "SpectateLeft" => Loc.T("Spectate previous", "观战上一个", "觀戰上一個"),
        "SpectateRight" => Loc.T("Spectate next", "观战下一个", "觀戰下一個"),
        "MoveForward" => Loc.T("Move forward", "前进", "前進"), "MoveBackward" => Loc.T("Move back", "后退", "後退"),
        "MoveLeft" => Loc.T("Move left", "左移", "左移"), "MoveRight" => Loc.T("Move right", "右移", "右移"),
        _ when action.StartsWith("Hotbar", StringComparison.Ordinal) => Loc.F("Slot {0}", "物品格 {0}", "物品格 {0}", action.Substring(6)),
        _ => action
    };
}
'''
p = os.path.join(SRC, 'OverviewHotkeyService.cs')
s = io.open(p, encoding='utf-8').read()
s = s.rstrip() + nl + APPEND.replace('\n', nl)
io.open(p, 'w', encoding='utf-8', newline='').write(s)

patch('OverviewHotkeyView.cs', [('''        var conflicts = entry.Conflicts.ToList();
        if (conflicts.Count == 1) { JumpTo(conflicts[0].EntryId); return; }
        Popup(anchor, conflicts.Select(c => (OverviewLanguageBridge.GetDisplayName(c.Mod) + " / " + c.Section + " / " + OverviewLanguageBridge.Translate(c.Key) + " · " + c.Label, (Action)(() => JumpTo(c.EntryId)), true)).ToList());''',
'''        var conflicts = entry.Conflicts.ToList();
        if (conflicts.Count == 1 && !conflicts[0].IsGame) { JumpTo(conflicts[0].EntryId); return; }
        Popup(anchor, conflicts.Select(c => c.IsGame
            ? (Loc.T("Game: ", "游戏：", "遊戲：") + c.Key + " · " + c.Label, (Action)(() => _status(Loc.T("This key is also a game control; pressing it triggers both.", "这个键也是游戏自己的操作，按下时两边都会触发。", "這個鍵也是遊戲自己的操作，按下時兩邊都會觸發。"))), true)
            : (OverviewLanguageBridge.GetDisplayName(c.Mod) + " / " + c.Section + " / " + OverviewLanguageBridge.Translate(c.Key) + " · " + c.Label, (Action)(() => JumpTo(c.EntryId)), true)).ToList());''')])
print('ok')
