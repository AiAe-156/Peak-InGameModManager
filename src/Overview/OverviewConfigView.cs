#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Edits the viewed profile's parameter draft. Actual ConfigEntries are only read for metadata.</summary>
internal static class OverviewConfigView
{
    internal static void Show(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, Action changed, Action<string> status,
        Action<Action>? requestLeave = null, Func<ProfilePendingState>? pendingState = null, string initialGuid = "")
        => new Window(repo, mods, changed, status, requestLeave, pendingState, initialGuid).Open();

    private static string T(string en, string zh) => Loc.T(en, zh, zh);

    private sealed class ParameterRow
    {
        internal OverviewConfigDiff? Diff;
        internal OverviewConfigValue? Key;
        internal OverviewConfigFileStatus? File;
        internal ConfigEntryBase? Entry;
        internal string Guid => Key?.pluginGuid ?? File?.pluginGuid ?? "";
    }

    private sealed class Window
    {
        private readonly OverviewRepository _repo;
        private readonly IReadOnlyList<OverviewMod> _mods;
        private readonly Action _changed;
        private readonly Action<string> _status;
        private readonly Action<Action>? _requestLeave;
        private readonly Func<ProfilePendingState>? _pendingProvider;
        private ProfilePendingState _pending = new();
        private readonly Transform? _page;
        private string _profileId, _guid = "", _query = "", _focusedIdentity = "";
        private float _pixel;
        private readonly List<ParameterRow> _rows = new();
        private RectTransform _panel = null!, _content = null!, _viewport = null!, _hint = null!;
        private ScrollRect _scroll = null!;
        private TextMeshProUGUI _heading = null!, _count = null!, _hintText = null!;
        private Button _filter = null!;
        private ConfigModalReturn? _return;

        internal Window(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, Action changed, Action<string> status, Action<Action>? requestLeave, Func<ProfilePendingState>? pendingState, string initialGuid)
        { _guid = initialGuid; _repo = repo; _mods = mods; _changed = changed; _status = status; _requestLeave = requestLeave; _pendingProvider = pendingState; _page = OverviewUi.ModalParent; _profileId = repo.ViewedProfileId; }

        internal void Open()
        {
            if (!CanReturn()) return;
            try { ReloadRows(); }
            catch (Exception ex) { _status(T("Could not read parameters: ", "参数读取失败：") + Loc.LocalizeMessage(ex.Message)); return; }
            var panel = OverviewUi.CreateModalPanel("MCE_ProfileParameters", 1120, 720);
            if (panel == null) return;
            _panel = panel;
            if (_requestLeave is { } leave) OverviewUi.SetModalCloseHandler(() => leave(OverviewUi.CloseModal));
            float width = panel.rect.width;
            _heading = Label(panel, Heading(), 24, 16, width - 48, 38, 27);
            var help = Label(panel, T("Edit here, then save settings. Each mod determines whether the change is immediate, next-round or next-launch.",
                "修改后点击保存参数；各项按模组声明立即、重新开局或重启游戏生效。"), 24, 60, width - 48, 48, 16);
            help.textWrappingMode = TextWrappingModes.Normal;
            float filterWidth = Mathf.Clamp(width * .32f, 120, 320);
            _filter = OverviewUi.Button(panel, FilterLabel(), ChooseMod, filterWidth, 36, role: UiActionRole.Edit);
            OverviewUi.Place((RectTransform)_filter.transform, 24, 113, filterWidth, 36);
            var search = OverviewUi.Input(panel, _query); search.characterLimit = 256;
            OverviewUi.Place((RectTransform)search.transform, filterWidth + 36, 113, width - filterWidth - 60, 36);
            var placeholder = OverviewUi.Text(search.transform, T("Search mod, section or setting", "搜索模组、分区或参数"), 16);
            OverviewUi.Fill(placeholder.rectTransform, 7); placeholder.color = OverviewUi.Muted; search.placeholder = placeholder;
            search.onValueChanged.AddListener(value => { _query = value; _pixel = 0; RenderRows(false); });
            GamepadSupport.MarkSelectable(_filter.gameObject, null, null); GamepadSupport.MarkSelectable(search.gameObject, null, null);

            var host = OverviewUi.Rect("Parameters", panel); OverviewUi.Fill(host);
            host.offsetMin = new Vector2(24, 96); host.offsetMax = new Vector2(-24, -161);
            _scroll = host.gameObject.AddComponent<ScrollRect>(); _scroll.horizontal = false; _scroll.scrollSensitivity = 40;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _viewport = OverviewUi.Panel("Viewport", host, Color.clear); OverviewUi.Fill(_viewport);
            _viewport.offsetMax = new Vector2(-16, 0); _viewport.gameObject.AddComponent<RectMask2D>();
            _content = OverviewUi.Rect("Rows", _viewport); _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0, 1); _scroll.viewport = _viewport; _scroll.content = _content;
            var bar = OverviewUi.Panel("Scrollbar", host, ModConfigLook.Surface(new Color(.1f, .1f, .1f, .5f)));
            bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.offsetMin = new Vector2(-12, 0); bar.offsetMax = Vector2.zero;
            var handle = OverviewUi.Panel("Handle", bar, OverviewUi.Muted); OverviewUi.Fill(handle);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop; _scroll.verticalScrollbar = scrollbar;
            _count = OverviewUi.Text(panel, "", 16); _count.color = OverviewUi.Muted;
            OverviewUi.Bottom(_count.rectTransform, 24, 62); _count.rectTransform.sizeDelta = new Vector2(width - 48, 28);
            var saveParameters = OverviewUi.Button(panel, Loc.T("Save settings", "保存参数", "儲存參數"), () => {
                ConfigurationSaveRequest.Run(_repo, _mods, _guid.Length > 0 && _guid != "$unassociated" ? ConfigurationSaveScope.ModParameters : ConfigurationSaveScope.Parameters, _guid,
                    result => { _status(result.Message); _changed(); Open(); });
            }, 150, 36, role: UiActionRole.Commit);
            OverviewUi.Bottom((RectTransform)saveParameters.transform, 186, 18);
            var close = OverviewUi.Button(panel, T("Close", "关闭"), OverviewUi.RequestCloseModal, 150, 36, role: UiActionRole.Neutral);
            OverviewUi.Bottom((RectTransform)close.transform, 24, 18); GamepadSupport.MarkSelectable(close.gameObject, null, null);
            _hint = OverviewUi.Panel("ParameterHint", panel, ModConfigLook.Surface(new Color(.08f, .08f, .1f, .98f)));
            _hint.GetComponent<Image>().raycastTarget = false; OverviewUi.Bottom(_hint, 24, 100);
            _hint.sizeDelta = new Vector2(Mathf.Min(width - 48, 680), 210);
            _hintText = OverviewUi.Text(_hint, "", 17); OverviewUi.Fill(_hintText.rectTransform, 10);
            _hintText.textWrappingMode = TextWrappingModes.Normal; _hintText.overflowMode = TextOverflowModes.Ellipsis;
            _hint.gameObject.SetActive(false);
            RenderRows(true);
            if (_focusedIdentity.Length == 0) EventSystem.current?.SetSelectedGameObject(_filter.gameObject);
        }

        private bool CanReturn() => _page != null && _page == OverviewUi.ModalParent && _page.gameObject.activeInHierarchy && _repo.ViewedProfileId == _profileId;
        private string Heading() => T("Profile parameters", "方案参数") + " · " + _repo.ViewedProfile.name;
        private string ModName(string guid)
        {
            var mod = _mods.FirstOrDefault(m => m.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase) || m.PluginGuids.Contains(guid, StringComparer.OrdinalIgnoreCase));
            if (mod != null) return OverviewLanguageBridge.GetDisplayName(mod);
            return _repo.DraftMods.FirstOrDefault(m => m.guid.Equals(guid, StringComparison.OrdinalIgnoreCase))?.name ??
                (guid.Length == 0 ? T("Unassociated files", "未确认归属的文件") : guid);
        }
        private string FilterLabel() => (_guid.Length == 0 ? T("All mods", "全部模组") : ModName(_guid == "$unassociated" ? "" : _guid)) + " " + OverviewUi.Caret;
        private void ReloadRows()
        {
            var entries = _repo.Configs.LoadedEntries();
            _rows.Clear();
            foreach (var diff in _repo.ParameterDifferences())
            {
                var key = diff.ProfileValue ?? diff.CurrentValue; if (key == null) continue;
                entries.TryGetValue(key.Identity, out var entry);
                _rows.Add(new ParameterRow { Diff = diff, Key = key, Entry = entry });
            }
            foreach (var file in _repo.Configs.Scan(_repo.DraftConfig))
            {
                if (file.loaded || _rows.Any(r => r.Key != null && r.Guid.Equals(file.pluginGuid, StringComparison.OrdinalIgnoreCase) &&
                    r.Key.configFile.Equals(file.configFile, StringComparison.OrdinalIgnoreCase))) continue;
                _rows.Add(new ParameterRow { File = file });
            }
            _rows.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(ModName(a.Guid) + "\n" + a.Key?.section + "\n" + a.Key?.key + "\n" + a.File?.configFile,
                ModName(b.Guid) + "\n" + b.Key?.section + "\n" + b.Key?.key + "\n" + b.File?.configFile));
        }
        private bool Matches(ParameterRow row)
        {
            if (_guid.Length > 0 && !(_guid == "$unassociated" ? row.Guid.Length == 0 : row.Guid.Equals(_guid, StringComparison.OrdinalIgnoreCase))) return false;
            string text = ModName(row.Guid) + "\n" + row.Guid + "\n" + row.Key?.configFile + "\n" + row.Key?.section + "\n" + row.Key?.key + "\n" +
                OverviewLanguageBridge.Translate(row.Key?.section ?? "") + "\n" + OverviewLanguageBridge.Translate(row.Key?.key ?? "") + "\n" +
                row.Entry?.Description.Description + "\n" + row.File?.configFile;
            return _query.Length == 0 || text.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private void RenderRows(bool restoreFocus)
        {
            PublicationNavigation.Preserve(_panel);
            _pending = _pendingProvider?.Invoke() ?? _repo.GetPendingState();
            _hint.gameObject.SetActive(false); _scroll.StopMovement();
            for (int i = _content.childCount - 1; i >= 0; i--) { var go = _content.GetChild(i).gameObject; go.SetActive(false); UnityEngine.Object.Destroy(go); }
            var visible = _rows.Where(Matches).ToList(); float width = Mathf.Max(100, _panel.rect.width - 64);
            GameObject? focus = null;
            for (int i = 0; i < visible.Count; i++)
            {
                var item = visible[i]; var diff = item.Diff; var key = item.Key;
                var row = OverviewUi.Panel("Parameter_" + (key?.Identity ?? item.File!.configFile), _content, ModConfigLook.Surface(new Color(.10f, .12f, .14f, i % 2 == 0 ? .45f : .22f)));
                row.anchorMin = new Vector2(0, 1); row.anchorMax = Vector2.one; row.pivot = new Vector2(0, 1);
                row.sizeDelta = new Vector2(0, 98); row.anchoredPosition = new Vector2(0, -i * 101);
                float textWidth = width - 158;
                bool fresh = diff?.Kind == OverviewConfigDiffKind.CurrentOnlyNotRecorded;
                string section = key == null ? "" : " / " + OverviewLanguageBridge.Translate(key.section);
                var mod = Label(row, ModName(item.Guid) + section + (fresh ? T(" · unrecorded", " · 未记录") : ""), 10, 4, textWidth, 24, 15);
                mod.color = fresh ? OverviewUi.Gold : OverviewUi.Muted;
                Label(row, key == null ? item.File!.configFile : OverviewLanguageBridge.Translate(key.key), 10, 29, textWidth, 27, 20);
                if (key != null)
                {
                    string actual = diff!.CurrentValue?.serializedValue ?? T("Not loaded", "未加载");
                    string target = diff.ProfileValue?.serializedValue ?? T("Keep current (unrecorded)", "保持现值（未记录）");
                    float half = Mathf.Max(30, textWidth / 2);
                    Label(row, T("Actual: ", "实际：") + OneLine(actual), 10, 64, half - 5, 25, 17);
                    var wanted = Label(row, T("Target: ", "目标：") + OneLine(target), 10 + half, 64, half - 5, 25, 17);
                    wanted.color = diff.Kind == OverviewConfigDiffKind.Changed ? OverviewUi.Blue : OverviewUi.Ivory;
                }
                else Label(row, T("Not supported · current files stay untouched", "暂不支持 · 保留现有文件"), 10, 64, textWidth, 25, 17).color = OverviewUi.Gold;
                RectTransform? markers = null;
                if (key != null)
                {
                    markers = OverviewUi.Rect("ParameterMarkers", row); OverviewUi.Place(markers, width - 148, 7, 44, 18);
                    ChangeLedger.Dot(row, ChangeLedger.Key(key.pluginGuid, key.configFile, key.section, key.key), _repo.ViewedProfile, markerHost: markers);
                }
                bool editable = key != null && (item.Entry == null ? key.configFile.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(key.type, item.Entry.SettingType.FullName ?? item.Entry.SettingType.Name, StringComparison.Ordinal));
                var edit = OverviewUi.Button(row, editable ? T("Edit", "编辑") : T("Details", "说明"), () => {
                    _pixel = Mathf.Max(0, _content.anchoredPosition.y); _focusedIdentity = key?.Identity ?? item.File!.configFile;
                    if (editable) Edit(item); else ShowDetails(item);
                }, 86, 32, role: editable ? UiActionRole.Edit : UiActionRole.Neutral);
                OverviewUi.Place((RectTransform)edit.transform, width - 96, 8, 86, 32); GamepadSupport.MarkSelectable(edit.gameObject, _scroll, row);
                BindHint(row.gameObject, item); BindHint(edit.gameObject, item);
                if (key != null && markers != null)
                {
                    ProfilePendingMarkers.Attach(markers, () => {
                        var state = _pendingProvider?.Invoke() ?? _pending;
                        return state.Parameters.TryGetValue(key.Identity, out var pending) ? pending : null;
                    }, text => { _hintText.richText = true; _hintText.text = text; _hint.gameObject.SetActive(true); _hint.SetAsLastSibling(); }, () => _hint.gameObject.SetActive(false), edit.gameObject, reserveText: false);
                    ChangeLedger.WireInteraction(edit.gameObject, ChangeLedger.Key(key.pluginGuid, key.configFile, key.section, key.key));
                }
                if (fresh && editable)
                {
                    var include = OverviewUi.Button(row, T("Record", "纳入"), () => {
                        _pixel = Mathf.Max(0, _content.anchoredPosition.y); _focusedIdentity = key!.Identity;
                        if (!WriteDraft(key, diff!.CurrentValue!.serializedValue, out string error)) { _status(error); return; }
                        ReloadRows(); _heading.text = Heading(); RenderRows(true);
                    }, 86, 32, role: UiActionRole.Commit);
                    OverviewUi.Place((RectTransform)include.transform, width - 96, 54, 86, 32); GamepadSupport.MarkSelectable(include.gameObject, _scroll, row); BindHint(include.gameObject, item);
                    ChangeLedger.WireInteraction(include.gameObject, ChangeLedger.Key(key!.pluginGuid, key.configFile, key.section, key.key));
                }
                else if (key != null && item.Entry == null)
                    Label(row, T("Retained", "失联保留"), width - 96, 57, 86, 25, 15).color = OverviewUi.Gold;
                if (restoreFocus && _focusedIdentity == (key?.Identity ?? item.File!.configFile)) focus = edit.gameObject;
            }
            _content.sizeDelta = new Vector2(0, Mathf.Max(1, visible.Count * 101));
            _content.anchoredPosition = new Vector2(0, Mathf.Clamp(_pixel, 0, Mathf.Max(0, _content.sizeDelta.y - _viewport.rect.height)));
            _count.text = T("Visible: ", "当前显示：") + visible.Count + T(" · Unrecorded: ", " · 未记录：") + _rows.Count(r => r.Diff?.Kind == OverviewConfigDiffKind.CurrentOnlyNotRecorded) +
                T(" · Edits take effect only after saving.", " · 修改保存后才生效。");
            if (visible.Count == 0) Label(_content, T("No matching parameters.", "没有符合条件的参数。"), 10, 10, width - 20, 36, 22);
            if (focus != null) EventSystem.current?.SetSelectedGameObject(focus);
            else if (restoreFocus && _focusedIdentity.Length > 0) EventSystem.current?.SetSelectedGameObject(_filter.gameObject);
        }

        private void ChooseMod()
        {
            _pixel = Mathf.Max(0, _content.anchoredPosition.y);
            var options = new List<(string Label, string Value)> { (T("All mods", "全部模组"), "") };
            options.AddRange(_rows.Select(r => r.Guid).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(ModName)
                .Select(guid => (ModName(guid), guid.Length == 0 ? "$unassociated" : guid)));
            OverviewUi.Choose(T("Choose a mod", "选择模组"), options, _guid, guid => { CancelReturn(); _guid = guid; _pixel = 0; _focusedIdentity = ""; Open(); });
            AttachReturn();
        }
        private void ShowDetails(ParameterRow row)
        {
            OverviewUi.ShowText(T("Parameter details", "参数说明"), Description(row)); AttachReturn();
        }
        private void Edit(ParameterRow row)
        {
            var key = row.Key!;
            var live = _repo.Configs.FindLoadedEntry(key);
            string initial = row.Diff!.ProfileValue?.serializedValue ?? row.Diff.CurrentValue!.serializedValue;
            if (live != null && live.SettingType.IsEnum && !live.SettingType.IsDefined(typeof(FlagsAttribute), false))
            {
                var choices = Enum.GetNames(live.SettingType).Select(name => (OverviewLanguageBridge.Translate(name), name)).ToList();
                OverviewUi.Choose(OverviewLanguageBridge.Translate(key.key), choices, initial, value => {
                    CancelReturn(); if (!WriteDraft(key, value, out string error)) _status(error); Open();
                }); AttachReturn(); return;
            }
            var panel = OverviewUi.CreateModalPanel("MCE_ParameterEditor", 780, 540); if (panel == null) return;
            float width = panel.rect.width;
            Label(panel, OverviewLanguageBridge.Translate(key.key), 24, 16, width - 48, 38, 27);
            var explanation = Label(panel, T("This writes a profile draft; the actual setting stays unchanged.", "此处写入方案草稿，当前实际参数保持原值。"), 24, 63, width - 48, 40, 17);
            explanation.textWrappingMode = TextWrappingModes.Normal;
            Label(panel, T("Target value", "方案目标值") + " · " + (live == null ? key.type : FriendlyType(live.SettingType)), 24, 108, width - 48, 28, 19);
            Func<string> value;
            GameObject first;
            if (live?.SettingType == typeof(bool) || live == null && key.type == typeof(bool).FullName)
            {
                bool boolean = bool.TryParse(initial, out bool parsed) && parsed;
                var check = OverviewUi.Check(panel, boolean, changed => boolean = changed); OverviewUi.Place((RectTransform)check.transform, 24, 148, 30, 30);
                Label(panel, T("Checked = on / allowed", "勾选表示开启／允许"), 66, 146, width - 90, 34, 20);
                value = () => boolean ? "true" : "false"; first = check.gameObject;
            }
            else
            {
                var input = OverviewUi.Input(panel, initial, live?.SettingType == typeof(string)); input.characterLimit = 16384;
                OverviewUi.Place((RectTransform)input.transform, 24, 143, width - 48, 62); value = () => input.text; first = input.gameObject;
            }
            string initialEditorValue = value(); OverviewUi.SetModalDirtyCheck(() => !string.Equals(value(), initialEditorValue, StringComparison.Ordinal));
            var errorText = Label(panel, "", 24, 211, width - 48, 45, 17); errorText.color = new Color(.95f, .57f, .43f); errorText.textWrappingMode = TextWrappingModes.Normal;
            var host = OverviewUi.Rect("Description", panel); OverviewUi.Fill(host); host.offsetMin = new Vector2(24, 75); host.offsetMax = new Vector2(-24, -266);
            // On a short canvas, keep the edit controls available instead of giving the description a negative viewport.
            host.gameObject.SetActive(panel.rect.height > 350);
            var scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = OverviewUi.Panel("Viewport", host, Color.clear); OverviewUi.Fill(viewport); viewport.gameObject.AddComponent<RectMask2D>();
            var details = OverviewUi.Text(viewport, Description(row), 17); details.color = OverviewUi.Muted; details.textWrappingMode = TextWrappingModes.Normal;
            var content = details.rectTransform; content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1); content.sizeDelta = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; scroll.viewport = viewport; scroll.content = content;
            void FocusEditor()
            { if (first.GetComponent<TMP_InputField>() is TMP_InputField field) OverviewUi.FocusInput(field); else EventSystem.current?.SetSelectedGameObject(first); }
            void SaveEditor()
            {
                try
                {
                    if (!WriteDraft(key, value(), out string error)) { errorText.text = error; FocusEditor(); return; }
                    CancelReturn(); Open();
                }
                catch (Exception ex) { errorText.text = T("Could not record parameter: ", "参数写入失败：") + Loc.LocalizeMessage(ex.Message); FocusEditor(); }
            }
            OverviewUi.SetModalCloseHandler(() => {
                if (string.Equals(value(), initialEditorValue, StringComparison.Ordinal)) { OverviewUi.CloseModal(); return; }
                OverviewUi.ConfirmUnsaved(T("Save this edit to the profile draft before closing?", "将此次修改写入方案草稿后关闭？"),
                    SaveEditor, OverviewUi.CloseModal, FocusEditor);
            });
            var save = OverviewUi.Button(panel, T("Write to draft", "写入草稿"), SaveEditor, 150, 36, role: UiActionRole.Commit); OverviewUi.Bottom((RectTransform)save.transform, 24, 18);
            var cancel = OverviewUi.Button(panel, T("Close", "关闭"), OverviewUi.RequestCloseModal, 130, 36, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)cancel.transform, 184, 18);
            GamepadSupport.MarkSelectable(first, null, null); GamepadSupport.MarkSelectable(save.gameObject, null, null); GamepadSupport.MarkSelectable(cancel.gameObject, null, null);
            AttachReturn(); FocusEditor();
        }
        private bool WriteDraft(OverviewConfigValue key, string value, out string error)
        {
            if (!_repo.Configs.TryValidateDraft(key, value, out string normalized, out error)) return false;
            if (!_repo.SetDraftConfig(key, normalized, out error)) return false;
            _profileId = _repo.ViewedProfileId; _changed();
            _status(T("Parameter edited; save settings here or save configuration in the overview.",
                "参数已修改；可在此保存参数，或在概览保存全部配置。")); return true;
        }
        private string Description(ParameterRow row)
        {
            if (row.Key == null) return ModName(row.Guid) + "\n" + row.File!.configFile + "\n\n" + Loc.LocalizeMessage(row.File.message) + "\n" +
                T("This file has no confirmed parameter mapping. It is not included or written by this editor.", "此文件尚无可靠的参数对应关系，暂不纳入方案，也不会直接写入。 ");
            var key = row.Key; var diff = row.Diff!;
            string description = row.Entry == null ? "" : ModConfigDescriptionPanel.FormatDescription(row.Entry);
            string reason = row.Entry == null ? T("This setting is not loaded. Its raw configuration value can be edited and saved for use when the mod is enabled. Check the mod documentation for its allowed type and range.",
                "此项当前未加载，可编辑并保存原始配置值；启用模组后使用。类型和范围依据现有记录，请核对原模组说明。") :
                !string.Equals(key.type, row.Entry.SettingType.FullName ?? row.Entry.SettingType.Name, StringComparison.Ordinal)
                    ? T("Its recorded type differs from the loaded setting. It cannot be edited or applied safely.", "方案记录的类型与当前参数不一致，暂不编辑；应用时会提示类型变化。") : "";
            return ModName(row.Guid) + "\n[" + OverviewLanguageBridge.Translate(key.section) + "] " + OverviewLanguageBridge.Translate(key.key) +
                "\n" + T("File: ", "文件：") + key.configFile + "\n" + T("Type: ", "类型：") + key.type +
                "\n" + T("Actual: ", "实际：") + (diff.CurrentValue?.serializedValue ?? T("Not loaded", "未加载")) +
                "\n" + T("Target: ", "目标：") + (diff.ProfileValue?.serializedValue ?? T("Keep current · unrecorded", "保持现值 · 未记录")) +
                (description.Length > 0 ? "\n\n" + description : "") + (row.Entry?.Description.AcceptableValues != null ? "\n" + row.Entry.Description.AcceptableValues.ToDescriptionString() : "") +
                (diff.Message.Length > 0 ? "\n\n" + Loc.LocalizeMessage(diff.Message) : "") + (reason.Length > 0 ? "\n" + reason : "");
        }
        private void BindHint(GameObject target, ParameterRow row)
        {
            var hover = target.AddComponent<ConfigHint>(); hover.Enter = () => {
                if (_hint == null) return; _hintText.richText = false; _hintText.text = Description(row); _hint.gameObject.SetActive(true); _hint.SetAsLastSibling();
            }; hover.Exit = () => { if (_hint != null) _hint.gameObject.SetActive(false); };
        }
        private void AttachReturn()
        {
            if (_page == null || _page.childCount == 0) return;
            var modal = _page.GetChild(_page.childCount - 1).gameObject;
            if (!OverviewUi.HasModal || !modal.activeInHierarchy) return;
            _return = modal.AddComponent<ConfigModalReturn>();
            _return.Return = () => { _return = null; if (CanReturn() && !OverviewUi.HasModal) Open(); };
        }
        private void CancelReturn() { if (_return != null) _return.Return = null; _return = null; }
        private static string OneLine(string value) => value.Replace("\r", " ").Replace("\n", " ");
        private static string FriendlyType(Type type) => type == typeof(bool) ? T("On / off", "开启／关闭") : type == typeof(string) ? T("Text", "文本") :
            type.IsPrimitive || type == typeof(decimal) ? T("Number", "数字") : T("Config format", "配置格式");
    }

    private static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float width, float height, float size)
    { var label = OverviewUi.Text(parent, text, size); OverviewUi.Place(label.rectTransform, x, y, width, height); label.overflowMode = TextOverflowModes.Ellipsis; return label; }

    private sealed class ConfigModalReturn : MonoBehaviour
    {
        internal Action? Return;
        private void OnDestroy() { var action = Return; Return = null; action?.Invoke(); }
    }
    private sealed class ConfigHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        internal Action? Enter, Exit;
        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
        public void OnSelect(BaseEventData eventData) => Enter?.Invoke();
        public void OnDeselect(BaseEventData eventData) => Exit?.Invoke();
        private void OnDisable() => Exit?.Invoke();
    }
}
