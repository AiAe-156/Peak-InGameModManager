#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewPage
{
    private Button _more = null!;
    private readonly List<TextMeshProUGUI> _toolSeparators = new();
    private readonly List<ToolbarTool> _toolbarTools = new();
    private readonly HashSet<string> _hiddenTools = new();
    private RectTransform? _toolbarPopup;
    private string _toolbarLayoutKey = "";
    private long _automaticUpdateRevision = -1;
    private GameObject? _activeToolHintTarget;
    private Func<string>? _activeToolHintText;
    private string UpdateCheckHint() => Loc.T("Check all permitted sources now.", "立即检查所有允许的模组来源。", "立即檢查所有允許的模組來源。")
        + "\n\n" + AutomaticUpdateChecks.StatusText();
    private void ClearToolHint()
    {
        _activeToolHintTarget = null; _activeToolHintText = null;_list?.HideExternalHint();
    }
    private void RefreshActiveToolHint()
    {
        if (_activeToolHintText == null) return;
        if (_activeToolHintTarget == null || !_activeToolHintTarget.activeInHierarchy) { ClearToolHint(); _list?.HideExternalHint(); return; }
        _list?.RefreshExternalHint(_activeToolHintTarget,_activeToolHintText);
    }
    private void RefreshAutomaticUpdateHint()
    {
        RefreshSourceToolsIcon();
        long revision = AutomaticUpdateChecks.StateRevision;
        if (_automaticUpdateRevision == revision) return;
        _automaticUpdateRevision = revision; RefreshActiveToolHint();
    }

    private sealed class ToolbarTool
    {
        internal string Id = "", Label = "", GroupName = "", Reason = "";
        internal int Group, Order;
        internal float Width;
        internal Button Button = null!;
        internal UiActionRole Role;
        internal bool Checked;
    }
    private void LayoutToolbar(float width)
    {
        string layoutKey = width + ":" + _tab + ":" + _author + ":" + _languageRevision + ":" + _repo.ViewedProfile.name + ":" + _repo.State.hideDisabled + ":" + _repo.GetGlobalUpdateAllowed() + ":" + OverviewRepository.IsMainMenuScene + ":" + (_document?.IsImporting == true);
        if (layoutKey != _toolbarLayoutKey && _toolbarPopup != null && _toolbarPopup.gameObject.activeInHierarchy) OverviewUi.CloseModal();
        _toolbarLayoutKey = layoutKey;
        float available = Mathf.Max(0, width - 40);
        _toolbarTools.Clear(); bool list = _tab == PageTab.Mods, content = _tab != PageTab.Introduction;
        _profileApply.interactable = _repo.ViewedProfile.IsValid && _document?.IsImporting != true;
        void Tool(string id, Button button, string label, int group, string groupName, int order, float minimum, UiActionRole role, bool shown = true, bool check = false)
        {
            button.gameObject.SetActive(shown); if (!shown) return;
            var caption = button.GetComponentInChildren<TextMeshProUGUI>(true); if (caption != null && id != "edit" && id != "theme") caption.text = label;
            float measured = Mathf.Max(minimum, id == "edit" || id == "theme" || id == "import" || caption == null ? minimum : caption.GetPreferredValues(label).x + 24);
            if (id == "profile" && caption != null)
            {
                var margin = caption.margin; caption.margin = Vector4.zero; caption.richText = false;
                float textWidth = caption.GetPreferredValues(label).x; caption.margin = margin;
                measured = Mathf.Clamp(textWidth + 70, 160, 240) * .8f;
                caption.richText = true; caption.text = ProfileChoiceCaption(); caption.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (id == "sources" && caption != null)
            {
                var margin = caption.margin; caption.margin = Vector4.zero;
                measured = Mathf.Max(minimum, caption.GetPreferredValues(label).x + 12 + 8 + 54); caption.margin = margin;
            }
            UiActionStyle.Set(button, role);
            _toolbarTools.Add(new ToolbarTool { Id = id, Button = button, Label = label, Group = group, GroupName = groupName, Width = measured, Order = order, Role = role, Checked = check,
                Reason = id == "apply" && !button.interactable ? _document?.IsImporting == true ? T("Media is importing", "素材导入中") : T("This profile is unavailable", "此方案暂不可用") : "" });
        }
        string profile = T("Profile", "方案"), display = T("Display", "显示筛选"), sources = T("Sources and updates", "来源与更新"), editing = T("Editing", "编辑工具"), preferences = T("Preferences", "偏好");
        Tool("profile", _profileChoice, T("Profile: ", "方案：") + _repo.ViewedProfile.name, 0, profile, int.MaxValue, 160, UiActionRole.Edit);
        Tool("save", _profileSave, Loc.T("Save configuration", "保存配置", "儲存設定") + (ResourceOperationDraft.Snapshot().Count(c => !ResourceOperationDraft.IsUpdate(c)) is int pendingResources && pendingResources > 0 ? " · " + pendingResources : ""), 0, profile, 9, 92, UiActionRole.Commit);
        Tool("apply", _profileApply, Loc.T("Change review", "变更检查目录", "變更檢查目錄"), 0, profile, 10, 132, UiActionRole.Edit);
        var selection = _tab == PageTab.Hotkeys ? _repo.State.hotkeyTagFilter ??= new() : _repo.State.modTagFilter ??= new();
        Tool("tags", _tagFilter, OverviewTagFilter.Caption(selection), 1, display, 8, 105, UiActionRole.Edit, content);
        Tool("hide", _hide, T("Hide disabled", "隐藏已停用"), 1, display, 5, 112, _repo.State.hideDisabled ? UiActionRole.Edit : UiActionRole.Neutral, list, _repo.State.hideDisabled);
        Tool("refresh", _refresh, T("Refresh", "刷新"), 1, display, 2, 76, UiActionRole.Neutral, content);
        Tool("import", _importTool, Loc.T("Import", "导入", "匯入"), 2, sources, 0, 34, UiActionRole.Edit, list);
        Tool("shop",_shop,T("Mod store","模组商店"),2,sources,0,115,UiActionRole.Overview);
        Tool("sources", _sourceTools, Loc.T("Sources and updates", "来源与更新", "來源與更新"), 2, sources, 1, 130, UiActionRole.Edit, content);
        Tool("edit", _edit, T("Edit mode", "编辑模式"), 3, editing, 6, 32, _author ? UiActionRole.Edit : UiActionRole.Neutral, check: _author);
        Tool("groups", _groups, T("Management center", "管理中心"), 3, editing, int.MaxValue - 1, 105, UiActionRole.Edit);
        Tool("language", _language, T("Language", "语言"), 4, preferences, 4, 76, UiActionRole.Edit);
        Tool("theme", _theme, Loc.T("Colour theme", "配色主题", "配色主題"), 4, preferences, 3, 32, UiActionRole.Neutral);
        var guideCaption = _help.GetComponentInChildren<TextMeshProUGUI>(true); guideCaption.text = T("User guide", "使用说明");
        float guideWidth = Mathf.Max(88, guideCaption.GetPreferredValues(guideCaption.text).x + 24);
        var moreCaption = _more.GetComponentInChildren<TextMeshProUGUI>(true); moreCaption.text = T("More", "更多") + " " + OverviewUi.Caret;
        float moreWidth = Mathf.Max(76, moreCaption.GetPreferredValues(moreCaption.text).x + 24);
        var logCaption = _logs.GetComponentInChildren<TextMeshProUGUI>(true); logCaption.text = T("Logs", "日志");
        float logWidth = Mathf.Max(70, logCaption.GetPreferredValues(logCaption.text).x + 24);
        UiActionStyle.Set(_logs, UiActionRole.Overview);
        var result = ToolbarLayout.Arrange(available, guideWidth + logWidth + 8, moreWidth, _toolbarTools.Select(i => new ToolbarLayout.Item { Id = i.Id, Group = i.Group, Width = i.Width, CollapseOrder = i.Order }).ToList());
        var selected = EventSystem.current?.currentSelectedGameObject;
        bool movedFocus = selected != null && _toolbarTools.Any(i => result.Hidden.Contains(i.Id) && (selected == i.Button.gameObject || selected.transform.IsChildOf(i.Button.transform)));
        _hiddenTools.Clear(); _hiddenTools.UnionWith(result.Hidden);
        foreach (var tool in _toolbarTools)
        {
            bool visible = result.Visible.TryGetValue(tool.Id, out var slot); tool.Button.gameObject.SetActive(visible);
            if (visible)
            {
                OverviewUi.Place((RectTransform)tool.Button.transform, slot.X, 0, slot.Width, 34);
                if (tool.Id == "profile")
                {
                    var caption = tool.Button.GetComponentInChildren<TextMeshProUGUI>(true);
                    float inset = Mathf.Clamp(slot.Width * .10f, 16, 26);
                    caption.margin = new Vector4(inset, 0, inset + 22, 0); caption.alignment = TextAlignmentOptions.MidlineLeft;
                    caption.textWrappingMode = TextWrappingModes.NoWrap; caption.overflowMode = TextOverflowModes.Ellipsis;
                    _profileCaret.text = OverviewUi.Caret; OverviewUi.PlaceClipped(_profileCaret, slot.Width - inset - 17, 0, 17, 34);
                }
                if (tool.Id == "sources") LayoutSourceTools(slot.Width);
            }
        }
        _more.gameObject.SetActive(result.Hidden.Count > 0); if (_more.gameObject.activeSelf) OverviewUi.Place((RectTransform)_more.transform, result.MoreX, 0, moreWidth, 34);
        OverviewUi.Place((RectTransform)_logs.transform, result.HelpX, 0, logWidth, 34);
        OverviewUi.Place((RectTransform)_help.transform, result.HelpX + logWidth + 8, 0, guideWidth, 34);
        for (int i = 0; i < result.Separators.Count; i++)
        {
            if (i == _toolSeparators.Count) { var separator = OverviewUi.Text(_controls, "|", 22); separator.color = ModConfigLook.Surface(new Color(.72f, .68f, .56f, .48f)); separator.alignment = TextAlignmentOptions.Midline; _toolSeparators.Add(separator); }
            var text = _toolSeparators[i]; text.gameObject.SetActive(true); OverviewUi.PlaceClipped(text, result.Separators[i], 0, 8, 34);
        }
        for (int i = result.Separators.Count; i < _toolSeparators.Count; i++) _toolSeparators[i].gameObject.SetActive(false);
        _edit.GetComponentInChildren<OverviewIconGraphic>().color = OverviewUi.Ivory; UiActionStyle.Select(_hide, _repo.State.hideDisabled);
        // 1.1.5 (user: "the refresh icon next to Sources and updates cannot be focused"): collected by IsActive only.
        // It was left out whenever it was briefly non-interactable (an automatic check running) and never linked back.
        var navigation = _controls.GetComponentsInChildren<Selectable>(false).Where(s => s.IsActive() && (s.IsInteractable() || s == _sourceToolsRefreshButton)).OrderBy(s => s.transform.position.x).ToList();
        var bodyTargets = _body.GetComponentsInChildren<Selectable>(false).Where(s => s.IsActive() && s.IsInteractable()).ToList();
        var pageTabs = new[] { _back, _listTab.GetComponent<Button>(), _introTab.GetComponent<Button>(), _hotkeyTab.GetComponent<Button>() }.Where(s => s.IsActive() && s.IsInteractable()).ToList();
        for (int i = 0; i < navigation.Count; i++)
        {
            var control = navigation[i];
            var up = pageTabs.OrderBy(s => Mathf.Abs(s.transform.position.x - control.transform.position.x)).FirstOrDefault();
            var down = bodyTargets.OrderByDescending(s => s.transform.position.y).ThenBy(s => Mathf.Abs(s.transform.position.x - control.transform.position.x)).FirstOrDefault();
            control.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = navigation[(i + navigation.Count - 1) % navigation.Count], selectOnRight = navigation[(i + 1) % navigation.Count], selectOnUp = up, selectOnDown = down };
            GamepadSupport.MarkSelectable(control.gameObject, null, (RectTransform)control.transform);
        }
        movedFocus |= selected != null && selected.transform.IsChildOf(_controls) && !selected.activeInHierarchy;
        if (movedFocus) EventSystem.current?.SetSelectedGameObject(_more.gameObject.activeSelf ? _more.gameObject : _profileChoice.gameObject);
        RefreshActiveToolHint();
    }
    private RectTransform ToolAnchor(Button button) => (RectTransform)(button.gameObject.activeInHierarchy ? button.transform : _more.transform);
    private void ShowToolbarMore()
    {
        var entries = new List<UiMenuEntry>();
        foreach (var group in _toolbarTools.Where(t => _hiddenTools.Contains(t.Id)).GroupBy(t => t.Group))
        {
            entries.Add(new UiMenuEntry { Header = true, Label = group.First().GroupName });
            foreach (var tool in group)
            {
                entries.Add(new UiMenuEntry { Label = tool.Label, Role = tool.Role, Enabled = tool.Button.interactable, Checked = tool.Checked, DisabledReason = tool.Reason, CanInvoke = () => tool.Button.interactable, Invoke = () => tool.Button.onClick.Invoke() });
            }
        }
        _toolbarPopup = OverviewUi.Dropdown((RectTransform)_more.transform, T("More", "更多"), entries, 390);
    }
}
