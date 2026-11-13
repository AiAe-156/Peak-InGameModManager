#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PEAKLib.ModConfig;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using Zorro.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;
internal static class ChangeLedger
{
    private static ChangeJournal? _journal;
    private static string FilePath => Path.Combine(Loc.RuntimeDirectory, "changes.json");
    private static string ReadPath => Path.Combine(Loc.RuntimeDirectory, "changes-read.json");
    internal static int Revision { get; private set; }
    internal static OverviewProfile? AppliedProfile;
    private static ChangeJournal Journal
    {
        get
        {
            if (_journal == null)
            {
                try
                {
                    _journal = File.Exists(FilePath) ? JsonConvert.DeserializeObject<ChangeJournal>(File.ReadAllText(FilePath)) : null;
                    if (_journal != null && File.Exists(ReadPath)) { var read = JObject.Parse(File.ReadAllText(ReadPath)); if (read.Value<string>("generation") == _journal.generation && read["unread"] is JObject entries) _journal.unread = entries.ToObject<Dictionary<string, ChangeRecord>>() ?? new(); }
                }
                catch (Exception e) { Plugin.Log.LogWarning("[更新记录] " + e.Message); }
                _journal ??= new();
            }
            return _journal;
        }
    }
    private static void Save() { Journal.generation = Guid.NewGuid().ToString("N"); OverviewRepository.AtomicWrite(FilePath, JsonConvert.SerializeObject(Journal, Formatting.Indented)); Revision++; }
    private static void SaveRead() { OverviewRepository.AtomicWrite(ReadPath, new JObject { ["generation"] = Journal.generation, ["unread"] = JObject.FromObject(Journal.unread) }.ToString(Formatting.Indented)); Revision++; }
    private static string Digest(string path) { using var file = File.OpenRead(path); using var hash = SHA256.Create(); return Convert.ToBase64String(hash.ComputeHash(file)); }
    internal static string Key(string guid, ConfigEntryBase entry) => Key(guid, entry.ConfigFile.ConfigFilePath, entry.Definition.Section, entry.Definition.Key);
    internal static string Key(string guid, string path, string section, string key)
    {
        string root = Path.GetFullPath(Paths.ConfigPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string relative = Path.IsPathRooted(path) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : path;
        return "cfg:" + guid.ToUpperInvariant() + "\n" + relative.Replace('\\', '/').ToUpperInvariant() + "\n" + section + "\n" + key;
    }
    internal static void Capture()
    {
        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var plugin in Chainloader.PluginInfos.Values)
        {
            installed["mod:" + plugin.Metadata.GUID] = plugin.Metadata.Version.ToString();
            if (plugin.Instance == null) continue;
            foreach (var pair in plugin.Instance.Config)
                try { if (pair.Value.Description.Tags?.Any(t => t?.ToString() == "Hidden") != true) installed[Key(plugin.Metadata.GUID, pair.Value)] = pair.Value.SettingType.FullName + ":" + TomlTypeConverter.ConvertToString(pair.Value.DefaultValue, pair.Value.SettingType) + "\n" + pair.Value.Description.AcceptableValues?.ToDescriptionString(); } catch { }
        }
        Journal.Observe(InstalledScope, installed);
        foreach (var pack in PublishedPack.Packs.Where(p => p.Valid && p.Id.Length > 0))
        {
            var shared = new Dictionary<string, string>(StringComparer.Ordinal);
            if (pack.Release["enhanceDefaults"] is JObject defaults)
                foreach (var item in defaults.Properties()) { int split = item.Name.IndexOf('\n'); if (split > 0) shared[Key(Plugin.PluginGuid, Plugin.PluginGuid + ".cfg", item.Name.Substring(0, split), item.Name.Substring(split + 1))] = item.Value.ToString(); }
            foreach (string file in OverviewRepository.SafeFiles(pack.Root).Where(p => !p.Contains(Path.DirectorySeparatorChar + "profiles" + Path.DirectorySeparatorChar) && Path.GetFileName(p) != "signature.json" && Path.GetFileName(p) != "release.json"))
                if (new FileInfo(file).Length < 4 * 1024 * 1024) shared["data:" + file.Substring(pack.Root.Length + 1).Replace('\\', '/')] = Digest(file);
            Journal.Observe("author:" + pack.Id + ":shared", shared);
            var index = JObject.Parse(PublishedPack.Read(pack.Root, "profiles.json"));
            foreach (JObject reference in (index["profiles"] as JArray ?? new()).OfType<JObject>())
            {
                string id = reference.Value<string>("id") ?? "", folder = reference.Value<string>("directory") ?? reference.Value<string>("path") ?? "profiles/" + id;
                string directory = OverviewProfileAssets.CheckedPath(pack.Root, folder);
                var values = JObject.Parse(PublishedPack.Read(directory, "config-values.json"));
                var current = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JObject item in (values["values"] as JArray ?? new()).OfType<JObject>())
                    current[Key(item.Value<string>("pluginGuid") ?? "", item.Value<string>("configFile") ?? "", item.Value<string>("section") ?? "", item.Value<string>("key") ?? "")] = item.Value<string>("serializedValue") ?? "";
                var profile = JObject.Parse(PublishedPack.Read(directory, "profile.json"));
                foreach (JObject mod in (profile["mods"] as JArray ?? new()).OfType<JObject>()) current["mod:" + (mod.Value<string>("guid") ?? mod.Value<string>("id") ?? "")] = (mod.Value<bool?>("enabled") ?? true).ToString();
                current["data:profile:" + id] = profile["state"]?.ToString(Formatting.None) ?? "";
                string intro = Path.Combine(directory, "introduction.mcedoc.json"); if (File.Exists(intro)) current["data:introduction:" + id] = Digest(intro);
                Journal.Observe("author:" + pack.Id + ":" + id, current);
            }
        }
        Save();
    }
    private static string InstalledScope => ModUpdateMarkerPolicy.Scope(PackLibrary.Active?.Id ?? "");
    internal static ChangeRecord? FindModUpdate(string key)
    {
        if (!key.StartsWith("mod:", StringComparison.Ordinal)) return null;
        return Journal.unread.TryGetValue(InstalledScope + "|" + key, out var record) && ModUpdateMarkerPolicy.IsUpgrade(record) ? record : null;
    }
    private static string? AuthorScope(OverviewProfile? profile)
    {
        var pack = PublishedPack.Active; if (pack == null) return null;
        profile ??= AppliedProfile;
        string id = profile == null ? pack.DefaultProfile : profile.readOnly ? profile.id : profile.source;
        if (id.Length == 0) return null;
        id = id.Substring(id.LastIndexOf(':') + 1); return "author:" + pack.Id + ":" + id;
    }
    internal static ChangeRecord? Find(string key, OverviewProfile? profile = null)
    {
        if (key.StartsWith("summary:", StringComparison.Ordinal)) return SummaryRecord(key);
        string? scope = AuthorScope(profile);
        if (scope != null && Journal.unread.TryGetValue(scope + "|" + key, out var author)) return author;
        if (PublishedPack.Active is PublishedPack pack && Journal.unread.TryGetValue("author:" + pack.Id + ":shared|" + key, out var shared)) return shared;
        return Journal.unread.TryGetValue(InstalledScope + "|" + key, out var value) ? value : null;
    }
    private static bool SummaryMatch(string id, string key) => key == "summary:language" ? id.Contains("|data:language/") || id.Contains("|data:language\\") : id.StartsWith("author:", StringComparison.Ordinal);
    private static ChangeRecord? SummaryRecord(string key)
    {
        var changes = Journal.unread.Where(p => SummaryMatch(p.Key, key)).ToArray();
            return changes.Length == 0 ? null : new ChangeRecord { kind = "summary", before = "", after = string.Join("\n\n", changes.Select(Describe)) };
    }
    /// <summary>The blue chip's click: show the change and mark it as read. 1.1.0: also the gamepad row menu's entry.</summary>
    internal static void OpenModUpdate(string key, string? current = null)
    {
        var change = FindModUpdate(key); if (change == null) return;
        string detail = ChangeRecordText.Describe(key, change, current);
        if (OverviewUi.ModalParent != null) OverviewUi.ShowText(Loc.T("Updated", "更新变动", "更新變動"), detail); else ModConfigDescriptionPanel.Current?.ShowNotice(detail);
        Read(key);
    }
    internal static void Read(string key)
    {
        if (key.StartsWith("summary:", StringComparison.Ordinal)) { foreach (string id in Journal.unread.Keys.Where(id => SummaryMatch(id, key)).ToArray()) Journal.unread.Remove(id); SaveRead(); return; }
        if (!Journal.unread.Keys.Any(k => k.EndsWith("|" + key, StringComparison.Ordinal))) return; Journal.Read(key); SaveRead();
    }
    internal static void Clear() { Journal.unread.Clear(); SaveRead(); }
    private static string Describe(KeyValuePair<string, ChangeRecord> item)
    {
        string key = item.Key.Substring(item.Key.IndexOf('|') + 1);
        if (key.StartsWith("data:", StringComparison.Ordinal)) return key.Substring(5) + " · " + Loc.T("Author data updated", "作者资料已更新", "作者資料已更新");
        if (key.StartsWith("mod:", StringComparison.Ordinal)) { string guid = key.Substring(4); if (Chainloader.PluginInfos.TryGetValue(guid, out var plugin)) key = plugin.Metadata.Name; }
        else if (key.StartsWith("cfg:", StringComparison.Ordinal)) key = string.Join(" / ", key.Split('\n').Skip(2));
        return key + "\n" + item.Value.before + " → " + item.Value.after;
    }
    internal static string Summary() => Journal.unread.Count == 0 ? Loc.T("No unread changes.", "没有未读变更。", "沒有未讀變更。") : string.Join("\n\n", Journal.unread.Select(Describe));
    internal static void Dot(Transform target, string key, OverviewProfile? profile = null, Func<string>? current = null, Transform? markerHost = null, Action<string>? showHint = null, Action? hideHint = null)
    {
        var marker = target.GetComponent<UpdateDot>() ?? target.gameObject.AddComponent<UpdateDot>(); marker.Key = key; marker.Profile = profile; marker.Current = current; marker.MarkerHost = markerHost; marker.ShowHint=showHint; marker.HideHint=hideHint;
        if (key.StartsWith("summary:", StringComparison.Ordinal) || key.StartsWith("mod:", StringComparison.Ordinal)) return;
        foreach (var selectable in target.GetComponentsInChildren<Selectable>(true)) WireRead(selectable.gameObject, key);
        WireRead(target.gameObject, key);
    }
    private static void WireRead(GameObject target, string key)
    { var observer = target.GetComponent<ReadChangeOnClick>() ?? target.AddComponent<ReadChangeOnClick>(); observer.Key = key; }
    internal static void WireInteraction(GameObject target, string key) => WireRead(target, key);
    internal static void WireCell(SettingsUICell cell)
    {
        try
        {
            if (cell.m_settingsContentParent == null) return;
            var setting = cell.m_settingsContentParent.GetComponentInChildren<SettingInputUICell>(true)?._listeningSetting as IBepInExProperty;
            if (setting?.ConfigBase == null) return;
            string guid = setting.Pluginfo.Metadata.GUID; Dot(cell.transform, Key(guid, setting.ConfigBase), current: () => setting.ConfigBase.GetSerializedValue());
        }
        catch (Exception e) { Plugin.Log.LogDebug("[更新标记] " + e.Message); }
    }
}
internal sealed class ReadChangeOnClick : MonoBehaviour, IPointerDownHandler, ISubmitHandler
{
    internal string Key = "";
    public void OnPointerDown(PointerEventData data) { if (Key.Length > 0) ChangeLedger.Read(Key); }
    public void OnSubmit(BaseEventData data) { if (Key.Length > 0) ChangeLedger.Read(Key); }
}
internal sealed class UpdateDot : MonoBehaviour
{
    internal string Key = ""; internal OverviewProfile? Profile;
    internal Func<string>? Current;
    internal Transform? MarkerHost;
    internal Action<string>? ShowHint;
    internal Action? HideHint;
    private RectTransform? _dot; private int _revision = -1;
    private void Update()
    {
        if (_revision == ChangeLedger.Revision) return; _revision = ChangeLedger.Revision;
        bool show = Key.StartsWith("mod:", StringComparison.Ordinal) ? ChangeLedger.FindModUpdate(Key) != null : false;
        if (_dot == null && show)
        {
            // raycast: the whole 16x16 box takes the pointer (transparent hit image), not just the 8x8 glyph.
            _dot = StatusChip.Create(MarkerHost != null ? MarkerHost : transform, StatusChip.UnreadSlot, StatusChip.Unread, raycast: true);
            _dot.gameObject.AddComponent<DotPointerDown>();
            var hover=_dot.gameObject.AddComponent<OverviewHover>();
            hover.Enter=()=>{ var record=ChangeLedger.FindModUpdate(Key);if(record==null)return;
                string detail=ChangeRecordText.Describe(Key,record,Current?.Invoke());
                string preview=detail.Length>480?detail.Substring(0,480)+"...":detail;
                string tip=Loc.T("Blue: unread changes. Click for details.","蓝色：未读变动。点击查看详情。","藍色：未讀變動。點擊查看詳情。");
                if(ShowHint!=null)ShowHint("<color=#6EADF0>"+tip+"</color>\n"+preview.Replace("<","＜")); else ModConfigDescriptionPanel.Current?.ShowNotice(tip+"\n"+preview); };
            hover.Exit=()=>HideHint?.Invoke();
            var action = _dot.gameObject.AddComponent<OverviewTextClick>(); action.Clicked = _ => ChangeLedger.OpenModUpdate(Key, Current?.Invoke());
        }
        if (_dot != null) _dot.gameObject.SetActive(show);
    }
}
internal sealed class DotPointerDown : MonoBehaviour, IPointerDownHandler { public void OnPointerDown(PointerEventData data) { } }
internal sealed class PackBootstrap : MonoBehaviour
{
    private IEnumerator Start()
    {
        while (!OverviewRepository.IsMainMenuScene) yield return null;
        yield return null; yield return null;
        try { ChangeLedger.Capture(); } catch (Exception e) { Plugin.Log.LogWarning("[更新记录] " + e.Message); }
        try { ApplyDefault(); } catch (Exception e) { Plugin.Log.LogWarning("[首次配置] " + e.Message); }
        try { ApplyPackLanguage(); } catch (Exception e) { Plugin.Log.LogWarning("[整合包语言] " + e.Message); }
        try { NotifyTemporarySave(); } catch (Exception e) { Plugin.Log.LogWarning("[临时整合包保存结果] " + e.Message); }
    }
    internal static void NotifyTemporarySave()
    {
        var task = PackLibrary.Task();
        if (task == null || task.State != "completed" || task.Request.Operation != "confirm-adopt") return;
        string marker = Path.Combine(PackLibrary.Home, "temporary-save-notice.txt");
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == task.Id) return;
        OverviewUi.ShowText(Loc.T("Temporary modpack saved", "临时整合包保存完成", "臨時整合包儲存完成"), task.Message);
        Directory.CreateDirectory(PackLibrary.Home); File.WriteAllText(marker, task.Id);
    }
    /// <summary>Uses the language group chosen when the pack was added. It is applied once per pack and never overrides a pack without language files.</summary>
    internal static void ApplyPackLanguage()
    {
        var active = PackLibrary.Active; if (active == null || active.Language.Length == 0) return;
        string marker = Path.Combine(PackLibrary.Workspace, "language-applied.txt");
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == active.Language) return;
        if (!Loc.Scan().Any(l => string.Equals(l.Key, active.Language, StringComparison.OrdinalIgnoreCase))) return;
        if (OverviewLanguageBridge.Select(active.Language, out string error)) { Directory.CreateDirectory(Path.GetDirectoryName(marker)!); File.WriteAllText(marker, active.Language); Plugin.Log.LogInfo("[整合包语言] 已使用 " + active.Language); }
        else Plugin.Log.LogWarning("[整合包语言] " + error);
    }
    internal static void ApplyDefault()
    {
        if (PackLibrary.Task()?.BlocksUpdates == true) return;
        var pack = PublishedPack.Active; if (pack == null || pack.Id.Length == 0 || pack.DefaultProfile.Length == 0) return;
        string path = PublishedPack.BootstrapPath;
        var state = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
        if (state.Value<bool?>("profileApplied") == true || state.Value<bool?>("attempted") == true) return;
        state["attempted"] = true; OverviewRepository.AtomicWrite(path, state.ToString(Formatting.Indented));
        var repo = new OverviewRepository();
        var target = repo.Profiles.ListProfiles().FirstOrDefault(p => p.readOnly && p.IsValid && Path.GetFullPath(p.directory).StartsWith(Path.GetFullPath(pack.Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && p.id.EndsWith(":" + pack.DefaultProfile, StringComparison.Ordinal));
        if (target == null) throw new InvalidDataException("未找到发布包指定的默认方案。");
        repo.SelectProfile(target.id); var result = repo.ApplyViewedProfile(new OverviewCatalogue().Scan(repo), true);
        state["profileApplied"] = result.Success; state["message"] = result.Message; OverviewRepository.AtomicWrite(path, state.ToString(Formatting.Indented));
        Plugin.Log.LogInfo("[首次配置] " + result.Message);
        OverviewController.RefreshEntries();
    }
    internal static void Retry()
    {
        string path = PublishedPack.BootstrapPath;
        var state = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
        state["attempted"] = false; OverviewRepository.AtomicWrite(path, state.ToString(Formatting.Indented)); ApplyDefault();
    }
}
