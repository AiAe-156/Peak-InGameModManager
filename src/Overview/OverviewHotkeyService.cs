#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace ModConfigEnhance.Overview;

[Serializable]
internal sealed class OverviewHotkeyPreset
{
    public string id = "", guid = "", section = "", key = "", type = "", value = "", label = "";
    public bool cleared, overridden;
    public string Label => label;
}
internal sealed class OverviewHotkeyRule { public string description = ""; }
[Serializable]
internal sealed class OverviewIgnoredHotkeyConflict
{
    public string idA = "", idB = "", signatureA = "", signatureB = "";
    public string OtherId(string entryId) => idA == entryId ? idB : idA;
}
internal sealed class OverviewHotkeyConflict
{
    public OverviewMod Mod = null!;
    public string Key = "", Label = "";
    public string EntryId = "", Section = "";
    /// <summary>1.1.2: the other side is one of the game's own controls (no entry to jump to).</summary>
    public string GameAction = "";
    public bool IsGame => GameAction.Length > 0;
    /// <summary>1.1.3: the clash is on the controller side (a controller combo of this or the other setting).</summary>
    public bool Combo;
}
internal sealed class OverviewHotkeyEntry
{
    public string Id = "", Guid = "", Section = "", Key = "";
    public OverviewMod Mod = null!;
    public ConfigEntryBase Config = null!;
    internal OverviewHotkeyAdapter Adapter = null!;
    public OverviewHotkeyPreset? PackPreset { get; internal set; }
    public int IgnoredCount { get; internal set; }
    public string Name => OverviewLanguageBridge.Translate(Key);
    public OverviewHotkeyRule Rule => new() { description = Loc.GetHotkeyDescription(Guid, Section, Key) };
    public string ShortDescription { get { string text = Rule.description; return text.Length == 0 ? ModConfigDescriptionPanel.FormatDescription(Config) : text; } }
    public string CurrentLabel => Adapter.Label(Config.BoxedValue);
    public string DefaultLabel => Adapter.Label(Config.DefaultValue);
    public bool IsUnbound => Adapter.Signature(Config.BoxedValue).Length == 0;
    public bool CanClear => Adapter.CanClear;
    public bool CanWrite => WriteReason.Length == 0;
    public string WriteReason
    {
        get
        {
            try { return File.Exists(Config.ConfigFile.ConfigFilePath) && (File.GetAttributes(Config.ConfigFile.ConfigFilePath) & FileAttributes.ReadOnly) != 0 ? Loc.T("The config file is read-only.", "配置文件为只读，无法写入。", "設定檔為唯讀，無法寫入。") : ""; }
            catch (Exception ex) { return ex.Message; }
        }
    }
    public List<OverviewHotkeyConflict> Conflicts = new();
}

internal sealed class OverviewHotkeyService : IDisposable
{
    private readonly OverviewRepository _repo;
    private readonly List<OverviewHotkeyEntry> _entries = new();
    private readonly HashSet<ConfigFile> _subscribedFiles = new();
    private bool _disposed;
    public bool AuthorMode { get; set; }
    public bool PackPresetMode => true;
    public IReadOnlyList<OverviewHotkeyEntry> Entries => _entries;
    public event Action? Changed;
    public OverviewHotkeyService(OverviewRepository repository) { _repo = repository; }
    public void Reload(IReadOnlyList<OverviewMod> mods)
    {
        Unsubscribe(); _entries.Clear();
        if (_disposed) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in Chainloader.PluginInfos.Values)
        {
            if (plugin.Instance == null) continue;
            if (plugin.Metadata.GUID == Plugin.PluginGuid || plugin.Metadata.GUID == XUnityBridge.Guid /* 它的快捷键（Alt+T 等）不归模组设置页管 */) continue;
            var mod = mods.FirstOrDefault(m => m.Loaded && (m.Guid == plugin.Metadata.GUID || m.PluginGuids.Contains(plugin.Metadata.GUID)));
            if (mod == null) continue;
            foreach (var pair in plugin.Instance.Config)
            {
                var adapter = OverviewHotkeyAdapter.Create(pair.Value); if (adapter == null) continue;
                string id = Loc.HotkeyStableId(plugin.Metadata.GUID, pair.Key.Section, pair.Key.Key);
                if (!seen.Add(id)) continue;
                var entry = new OverviewHotkeyEntry { Id = id, Guid = plugin.Metadata.GUID, Section = pair.Key.Section, Key = pair.Key.Key, Mod = mod, Config = pair.Value, Adapter = adapter };
                _entries.Add(entry);
                if (_subscribedFiles.Add(pair.Value.ConfigFile)) pair.Value.ConfigFile.SettingChanged += OnSettingChanged;
            }
        }
        Refresh();
    }
    private void OnSettingChanged(object sender, SettingChangedEventArgs args) { if (!_disposed) Refresh(); }
    /// <summary>Recompute duplicates after a controller combo or the modifier changed.</summary>
    internal void RefreshConflicts() { if (!_disposed) Refresh(); }
    private void Refresh()
    {
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in _entries)
        {
            entry.Conflicts.Clear();
            entry.IgnoredCount = 0;
            var target = _repo.FindDraftConfig(entry.Guid, entry.Config);
            object effective = entry.Config.BoxedValue;
            if (target != null) try { effective = entry.Adapter.Deserialize(target.serializedValue); } catch { }
            signatures[entry.Id] = entry.Adapter.Signature(effective);
            entry.PackPreset = ReadPreset(entry, target == null ? null : new OverviewHotkeyPreset { id = entry.Id, guid = entry.Guid,
                section = entry.Section, key = entry.Key, type = target.type, value = target.serializedValue }, Loc.T("Profile", "方案", "方案"));
        }
        for (int i = 0; i < _entries.Count; i++)
        for (int j = i + 1; j < _entries.Count; j++)
        {
            var a = _entries[i]; var b = _entries[j];
            if (!OverviewHotkeyAdapter.Overlaps(signatures[a.Id], signatures[b.Id])) continue;
            if (IsIgnored(a.Id, b.Id, signatures[a.Id], signatures[b.Id])) { a.IgnoredCount++; b.IgnoredCount++; continue; }
            a.Conflicts.Add(new() { Mod = b.Mod, Key = b.Key, Label = b.CurrentLabel, EntryId = b.Id, Section = b.Section });
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
        // 1.1.3: controller combos. A single-button combo also fires the game's action on that button; any combo's
        // button also reaches a mod that reads that controller button itself (the combo only masks it from the game).
        foreach (var entry in _entries)
        {
            var combo = GamepadCombos.For(entry.Id); if (combo == null) continue;
            string token = "gamepad:" + OverviewHotkeyAdapter.Canonical(combo.button) + ":";
            string label = GamepadCombos.Label(combo);
            if (combo.Mod.Length == 0)
                foreach (var game in GameBindings.Actions)
                    if (game.Signature == token) entry.Conflicts.Add(new() { Mod = entry.Mod, GameAction = game.Action, Label = game.Label, Key = game.Display, Combo = true });
            foreach (var other in _entries)
            {
                if (other == entry || !signatures[other.Id].StartsWith(token, StringComparison.Ordinal)) continue;
                entry.Conflicts.Add(new() { Mod = other.Mod, Key = other.Key, Label = other.CurrentLabel, EntryId = other.Id, Section = other.Section, Combo = true });
                other.Conflicts.Add(new() { Mod = entry.Mod, Key = entry.Key, Label = label, EntryId = entry.Id, Section = entry.Section, Combo = true });
            }
        }
        Changed?.Invoke();
    }
    private static OverviewHotkeyPreset? ReadPreset(OverviewHotkeyEntry entry, OverviewHotkeyPreset? preset, string kind)
    {
        if (preset == null || (preset.type.Length != 0 && preset.type != entry.Config.SettingType.FullName)) return null;
        try
        {
            object value = entry.Adapter.Deserialize(preset.value);
            if (!entry.Adapter.TryConvert(value, out value, out _)) return null;
            preset.label = entry.Adapter.Label(value); return preset;
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[快捷键] " + kind + "预设无法读取 " + entry.Id + ": " + ex.Message); return null; }
    }
    private string DraftSignature(OverviewHotkeyEntry entry)
    {
        var target = _repo.FindDraftConfig(entry.Guid, entry.Config);
        try { return entry.Adapter.Signature(target == null ? entry.Config.BoxedValue : entry.Adapter.Deserialize(target.serializedValue)); }
        catch { return entry.Adapter.Signature(entry.Config.BoxedValue); }
    }
    private bool IsIgnored(string a, string b, string signatureA, string signatureB) => _repo.State.ignoredHotkeyConflicts.Any(p =>
        (p.idA == a && p.idB == b && p.signatureA == signatureA && p.signatureB == signatureB) ||
        (p.idA == b && p.idB == a && p.signatureA == signatureB && p.signatureB == signatureA));
    public bool SetDescription(OverviewHotkeyEntry entry, string text, out string error)
    {
        bool success = Loc.WriteExternalTranslations(new[] { Loc.HotkeyDescriptionKey(entry.Guid, entry.Section, entry.Key) }, text, out error);
        if (success) Changed?.Invoke(); return success;
    }
    private OverviewHotkeyPreset MakePreset(OverviewHotkeyEntry entry, object value) => new() { id = entry.Id, guid = entry.Guid, section = entry.Section, key = entry.Key,
        type = entry.Config.SettingType.FullName ?? "", value = entry.Adapter.Serialize(value), label = entry.Adapter.Label(value), overridden = true };
    public bool TrySet(OverviewHotkeyEntry entry, object value, out string error)
    {
        error = "";
        if (_disposed || !_entries.Contains(entry)) { error = Loc.T("The keybinding is no longer available. Refresh the page.", "快捷键已失效，请刷新页面。", "快捷鍵已失效，請刷新頁面。"); return false; }
        if (!entry.Adapter.TryConvert(value, out object converted, out error)) return false;
        try
        {
            return _repo.SetDraftConfig(entry.Guid, entry.Config, entry.Adapter.Serialize(converted), out error);
        }
        catch (Exception ex) { error = Loc.T("Could not edit the profile keybinding: ", "方案按键修改失败：", "方案按鍵修改失敗：") + ex.Message; return false; }
        finally { Refresh(); }
    }
    public bool RestoreDefault(OverviewHotkeyEntry entry, out string error) => TrySet(entry, entry.Config.DefaultValue, out error);
    public bool Clear(OverviewHotkeyEntry entry, out string error)
    {
        if (!entry.CanClear) { error = Loc.T("The original mod does not allow an unbound value, so this binding cannot be cleared.", "原模组不允许保存未绑定值，无法清空。", "原模組不允許保存未綁定值，無法清空。"); return false; }
        return TrySet(entry, entry.Adapter.EmptyValue, out error);
    }
    private bool CheckEntry(OverviewHotkeyEntry entry, out string error)
    {
        error = "";
        if (_disposed || !_entries.Contains(entry)) { error = Loc.T("The keybinding is no longer available. Refresh the page.", "快捷键已失效，请刷新页面。", "快捷鍵已失效，請刷新頁面。"); return false; }
        return true;
    }
    private bool CheckPackEditing(out string error)
    {
        error = "";
        if (_disposed) { error = Loc.T("The keybindings page has closed. Open it again.", "快捷键页面已关闭，请重新打开。", "快捷鍵頁面已關閉，請重新打開。"); return false; }
        return true;
    }
    public bool SetPackPresetMode(bool enabled, out string error)
    {
        error = Loc.T("Presets come from the selected profile. Use the profile selector to switch.", "方案预设始终来自所选方案；请通过配置方案下拉切换。", "方案預設始終來自所選方案；請通過配置方案下拉切換。"); return false;
    }
    public bool SetPackPreset(OverviewHotkeyEntry entry, object value, out string error)
        => TrySet(entry, value, out error);
    public bool RestorePack(OverviewHotkeyEntry entry, out string error)
    {
        if (!CheckEntry(entry, out error)) return false;
        error = Loc.T("Profile keybindings are targets. Apply the profile to write them to the live configuration.", "方案按键作为目标值，应用方案后写入实际配置，局内也可应用。", "方案按鍵作為目標值，套用方案後寫入實際設定，遊戲中也可套用。"); return false;
    }
    public bool CopyCurrentToPackPresets(out int count, out string error)
    {
        count = 0;
        if (!CheckPackEditing(out error)) return false;
        try
        {
            if (!_repo.SetDraftConfigs(_entries.Select(entry => (entry.Guid, entry.Config, entry.Adapter.Serialize(entry.Config.BoxedValue))), out error)) return false;
            Refresh();
            count = _entries.Count; return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
    private static OverviewHotkeyPreset ClearedPreset(OverviewHotkeyPreset preset) => new()
    { id = preset.id, guid = preset.guid, section = preset.section, key = preset.key, type = preset.type, cleared = true, overridden = true };
    public bool ClearAllPackPresets(out string error)
    {
        if (!CheckPackEditing(out error)) return false;
        try { _repo.ClearAllDraftHotkeys(_entries.Select(entry => (entry.Guid, entry.Config))); Refresh(); return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }
    public bool ClearPack(OverviewHotkeyEntry entry, out string error)
    {
        if (!CheckPackEditing(out error) || !CheckEntry(entry, out error)) return false;
        try { _repo.ClearDraftConfig(entry.Guid, entry.Config); Refresh(); return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }
    private bool SavePackPresets(List<OverviewHotkeyPreset> replacement, out string error)
    {
        var previous = _repo.State.packHotkeyPresets; error = "";
        try { _repo.State.packHotkeyPresets = replacement; _repo.Save(); Refresh(); return true; }
        catch (Exception ex) { _repo.State.packHotkeyPresets = previous; error = ex.Message; Refresh(); return false; }
    }
    public bool IgnoreConflict(OverviewHotkeyEntry entry, string targetId, out string error)
    {
        if (!CheckEntry(entry, out error)) return false;
        var other = _entries.FirstOrDefault(e => e.Id == targetId && e.Id != entry.Id);
        if (other == null) { error = Loc.T("The conflict target is no longer available. Refresh the page.", "冲突目标已失效，请刷新页面。", "沖突目標已失效，請刷新頁面。"); return false; }
        string first = DraftSignature(entry), second = DraftSignature(other);
        if (!OverviewHotkeyAdapter.Overlaps(first, second)) { error = Loc.T("These keybindings no longer conflict. Refresh the page.", "这两个快捷键目前没有冲突，请刷新页面。", "這兩個快捷鍵目前沒有沖突，請刷新頁面。"); return false; }
        if (IsIgnored(entry.Id, other.Id, first, second)) return true;
        var replacement = _repo.State.ignoredHotkeyConflicts.ToList();
        bool ascending = string.CompareOrdinal(entry.Id, other.Id) < 0;
        replacement.Add(new() { idA = ascending ? entry.Id : other.Id, idB = ascending ? other.Id : entry.Id,
            signatureA = ascending ? first : second, signatureB = ascending ? second : first });
        return SaveIgnored(replacement, out error);
    }
    public IReadOnlyList<OverviewIgnoredHotkeyConflict> GetIgnored(OverviewHotkeyEntry entry)
    {
        if (_disposed || !_entries.Contains(entry)) return Array.Empty<OverviewIgnoredHotkeyConflict>();
        return _repo.State.ignoredHotkeyConflicts.Where(p =>
        {
            if (p.idA != entry.Id && p.idB != entry.Id) return false;
            var a = _entries.FirstOrDefault(e => e.Id == p.idA); var b = _entries.FirstOrDefault(e => e.Id == p.idB);
            if (a == null || b == null) return false;
            string first = DraftSignature(a), second = DraftSignature(b);
            return first == p.signatureA && second == p.signatureB && OverviewHotkeyAdapter.Overlaps(first, second);
        }).ToList();
    }
    public bool RestoreIgnoredFor(OverviewHotkeyEntry entry, out string error)
    {
        if (!CheckEntry(entry, out error)) return false;
        return SaveIgnored(_repo.State.ignoredHotkeyConflicts.Where(p => p.idA != entry.Id && p.idB != entry.Id).ToList(), out error);
    }
    public bool RestoreIgnoredConflict(OverviewHotkeyEntry entry, string targetId, out string error)
    {
        if (!CheckEntry(entry, out error)) return false;
        return SaveIgnored(_repo.State.ignoredHotkeyConflicts.Where(p => !((p.idA == entry.Id && p.idB == targetId) ||
            (p.idB == entry.Id && p.idA == targetId))).ToList(), out error);
    }
    public bool ClearIgnored(out string error)
    {
        error = "";
        if (_disposed) { error = Loc.T("The keybindings page has closed. Open it again.", "快捷键页面已关闭，请重新打开。", "快捷鍵頁面已關閉，請重新打開。"); return false; }
        return SaveIgnored(new(), out error);
    }
    private bool SaveIgnored(List<OverviewIgnoredHotkeyConflict> replacement, out string error)
    {
        var previous = _repo.State.ignoredHotkeyConflicts; error = "";
        try { _repo.State.ignoredHotkeyConflicts = replacement; _repo.SaveUiState(); Refresh(); return true; }
        catch (Exception ex) { _repo.State.ignoredHotkeyConflicts = previous; error = ex.Message; Refresh(); return false; }
    }
    private void Unsubscribe() { foreach (var file in _subscribedFiles) file.SettingChanged -= OnSettingChanged; _subscribedFiles.Clear(); }
    public void Dispose() { if (_disposed) return; _disposed = true; Unsubscribe(); _entries.Clear(); Changed = null; }
}

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
