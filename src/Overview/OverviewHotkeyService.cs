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
