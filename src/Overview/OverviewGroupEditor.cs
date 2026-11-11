#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Local group draft; only Save commits names, palette entries and membership.</summary>
internal sealed class OverviewGroupEditor : IDisposable
{
    private readonly OverviewRepository _repository;
    private readonly IReadOnlyList<OverviewMod> _mods;
    private readonly Action<string> _status;
    private readonly Action _saved;
    private RectTransform? _embeddedHost;
    private GameObject? _modal;
    private GameObject? _secondary;
    private CanvasGroup? _formCanvas;
    private Func<bool>? _secondaryDirty;
    private GameObject? _secondarySelection;
    private OverviewGroup? _group;
    private TMP_InputField? _name;
    private TMP_InputField? _description;
    private TextMeshProUGUI? _colorCaption;
    private RectTransform? _palette;
    private Image? _colorPreview;
    private string _color = "#7CC7E6";
    private string _parentId = "", _initialParentId = "";
    private TextMeshProUGUI? _parentCaption;
    /// <summary>Parent for the next "new group" form, set by "New subgroup" entry points.</summary>
    internal string NewGroupParent = "";
    private readonly List<string> _custom = new();
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OverviewMod> _candidates = new(StringComparer.Ordinal);
    private string _initialName = "";
    private string _initialDescription = "";
    private string _initialColor = "";
    private readonly HashSet<string> _initialCustom = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _initialChecked = new(StringComparer.Ordinal);
    public bool IsOpen => _modal != null;
    public bool HasDirty => IsOpen && (!string.Equals(_name?.text ?? "", _initialName, StringComparison.Ordinal)
        || !string.Equals(_description?.text ?? "", _initialDescription, StringComparison.Ordinal)
        || !string.Equals(_color, _initialColor, StringComparison.OrdinalIgnoreCase)
        || !_initialCustom.SetEquals(_custom) || !_initialChecked.SetEquals(_checked) || !string.Equals(_parentId, _initialParentId, StringComparison.Ordinal) || _secondaryDirty?.Invoke() == true);

    internal OverviewGroupEditor(OverviewRepository repository, IReadOnlyList<OverviewMod> mods, Action<string> status, Action saved)
    { _repository = repository; _mods = mods; _status = status; _saved = saved; }

    internal void ShowEmbedded(RectTransform host, OverviewGroup? group)
    { _embeddedHost = host; Show(group); }

    internal void Show(OverviewGroup? group)
    {
        Close();
        if (_embeddedHost == null) OverviewUi.CloseModal();
        var parent = (Transform?)_embeddedHost ?? OverviewUi.ModalParent;
        if (parent == null) return;
        _group = group;
        _parentId = group?.parentId ?? NewGroupParent; NewGroupParent = "";
        if (_parentId.Length > 0 && !new GroupTree(_repository.State.groups).Contains(_parentId)) _parentId = "";
        _color = NormalizeColor(group?.color ?? GroupPalette.ForNewGroup(_repository.State.groups, _parentId)) ?? "#7CC7E6";
        _custom.Clear(); _custom.AddRange((_repository.State.customColors ?? new List<string>()).Select(NormalizeColor).Where(c => c != null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase));
        _checked.Clear(); _candidates.Clear();
        foreach (var mod in _mods)
        {
            bool member = group != null && mod.Rule.groupId == group.id;
            bool ungrouped = string.IsNullOrEmpty(mod.Rule.groupId) || !_repository.State.groups.Any(g => g.id == mod.Rule.groupId);
            if (!member && !ungrouped) continue;
            string id = string.IsNullOrEmpty(mod.Id) ? mod.Guid : mod.Id;
            if (string.IsNullOrEmpty(id) || _candidates.ContainsKey(id)) continue;
            _candidates.Add(id, mod); if (member) _checked.Add(id);
        }
        var shade = OverviewUi.Panel("MCE_GroupEditor", parent, _embeddedHost == null ? ModConfigLook.Surface(new Color(0, 0, 0, .78f)) : OverviewUi.Brown);
        OverviewUi.Fill(shade); shade.SetAsLastSibling(); _modal = shade.gameObject;
        var canvas = shade.gameObject.AddComponent<CanvasGroup>(); canvas.blocksRaycasts = true; canvas.interactable = true;
        var panel = OverviewUi.Panel("GroupDialog", shade, OverviewUi.Brown);
        _formCanvas = panel.gameObject.AddComponent<CanvasGroup>();
        panel.anchorMin = _embeddedHost == null ? new Vector2(.12f, .07f) : Vector2.zero;
        panel.anchorMax = _embeddedHost == null ? new Vector2(.88f, .93f) : Vector2.one; panel.offsetMin = panel.offsetMax = Vector2.zero;
        var title = OverviewUi.Text(panel, group == null ? T("Create group", "创建分组") : T("Edit group", "编辑分组"), 34);
        TopStretch(title.rectTransform, 20, 15, 42, 40);
        var host = OverviewUi.Rect("GroupScroll", panel); OverviewUi.Fill(host); host.offsetMin = new Vector2(22, 66); host.offsetMax = new Vector2(-22, -66);
        var scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = OverviewUi.Rect("Viewport", host); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-18, 0);
        viewport.gameObject.AddComponent<Image>().color = Color.clear; viewport.gameObject.AddComponent<RectMask2D>(); scroll.viewport = viewport;
        var content = OverviewUi.Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1); content.sizeDelta = Vector2.zero; scroll.content = content;
        var vertical = content.gameObject.AddComponent<VerticalLayoutGroup>(); vertical.childControlWidth = true; vertical.childControlHeight = true; vertical.childForceExpandHeight = false; vertical.childForceExpandWidth = true; vertical.spacing = 10;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        BuildScrollbar(host, scroll);
        if (_embeddedHost != null)
        {
            // The group rail is independent of the scrolling editor; hiding a tab keeps the draft alive.
            float railWidth = Mathf.Clamp(_embeddedHost.rect.width * .22f, 140, 210);
            host.offsetMin = new Vector2(railWidth + 18, 66);
            var rail = OverviewUi.Rect("GroupRail", panel); OverviewUi.Place(rail, 0, 64, railWidth, Mathf.Max(100, panel.rect.height - 130));
            var railContent = SourceUi.Rows(rail, 0, Mathf.Max(70, rail.rect.height - 98), out var railScroll);
            ManagementUi.Scrollbar((RectTransform)railScroll.transform, railScroll);
            float rowY = 0;
            var railTree = new GroupTree(_repository.State.groups);
            foreach (var existing in railTree.Flat)
            {
                var choice = existing;
                float inset = (railTree.Depth(choice.id) - 1) * OverviewListView.GroupIndent;
                var button = OverviewUi.Button(railContent, choice.name, () => RequestChangeGroup(choice), railWidth - 52 - inset, 36, choice == group ? UiActionRole.Edit : UiActionRole.Neutral);
                OverviewUi.Place((RectTransform)button.transform, inset, rowY, railWidth - 52 - inset, 36);
                var caption = button.GetComponentInChildren<TextMeshProUGUI>(true); caption.textWrappingMode = TextWrappingModes.NoWrap; caption.overflowMode = TextOverflowModes.Ellipsis;
                var hint = button.gameObject.AddComponent<OverviewHover>(); hint.Enter = () => _status(choice.name);
                rowY += 44;
            }
            railContent.sizeDelta = new Vector2(0, rowY);
            var create = OverviewUi.Button(rail, T("New group", "新建分组"), () => { NewGroupParent = ""; RequestChangeGroup(null); }, railWidth - 12, 34, UiActionRole.Commit);
            OverviewUi.Bottom((RectTransform)create.transform, 4, 6);
            if (group != null)
            {
                float half = (railWidth - 18) / 2;
                var up = OverviewUi.Button(rail, T("Up", "上移"), () => MoveGroup(-1), half, 30, UiActionRole.Neutral);
                var down = OverviewUi.Button(rail, T("Down", "下移"), () => MoveGroup(1), half, 30, UiActionRole.Neutral);
                OverviewUi.Bottom((RectTransform)up.transform, 4, 50); OverviewUi.Bottom((RectTransform)down.transform, half + 10, 50);
            }
        }
        if (_embeddedHost == null && group == null && _repository.State.groups.Count > 0)
        {
            Heading(content, T("Edit an existing group", "编辑现有分组"));
            var choices = Grid(content, "ExistingGroups", new Vector2(150, 36));
            string nameHint = Loc.T("Hover or focus a group to see its full name.", "悬停或聚焦分组，可在此查看完整名称。", "懸停或聚焦分組，可在此檢視完整名稱。");
            var fullName = OverviewUi.Text(content, nameHint, 18); fullName.color = OverviewUi.Muted;
            fullName.textWrappingMode = TextWrappingModes.Normal; fullName.margin = Vector4.zero; Height(fullName.gameObject, 32);
            void ShowName(string value)
            {
                if (fullName == null) return;
                fullName.text = value;
                float width = Mathf.Max(1, ((RectTransform)content).rect.width);
                var layout = fullName.GetComponent<LayoutElement>();
                layout.minHeight = layout.preferredHeight = Mathf.Max(32, Mathf.Ceil(fullName.GetPreferredValues(value, width, Mathf.Infinity).y) + 6);
            }
            var gridTree = new GroupTree(_repository.State.groups);
            foreach (var existing in gridTree.Flat)
            {
                var choice = existing;
                var button = OverviewUi.Button(choices, gridTree.Path(choice.id), () => RequestChangeGroup(choice), 150, 36, role: UiActionRole.Edit);
                var caption = button.GetComponentInChildren<TextMeshProUGUI>(true);
                caption.textWrappingMode = TextWrappingModes.NoWrap; caption.overflowMode = TextOverflowModes.Ellipsis;
                caption.margin = new Vector4(12, 0, 12, 0); caption.alignment = TextAlignmentOptions.Midline;
                var hover = button.gameObject.AddComponent<OverviewHover>(); hover.Enter = () => ShowName(gridTree.Path(choice.id));
                hover.Exit = () => { if (EventSystem.current?.currentSelectedGameObject != button.gameObject) ShowName(nameHint); };
                var focus = button.gameObject.AddComponent<OverviewHotkeyFocus>(); focus.Selected = hover.Enter; focus.Deselected = () => ShowName(nameHint);
                GamepadSupport.MarkSelectable(button.gameObject, scroll, (RectTransform)button.transform);
            }
        }
        Heading(content, group == null ? T("New group name", "新建分组名称") : T("Group name", "分组名称"));
        _name = OverviewUi.Input(content, group?.name ?? ""); _name.characterLimit = 80; Height(_name.gameObject, 45);
        Heading(content, T("Parent group", "上级分组"));
        var parentRow = OverviewUi.Rect("ParentRow", content); Height(parentRow.gameObject, 40);
        var parentButton = OverviewUi.Button(parentRow, "", ChooseParent, 320, 36, UiActionRole.Neutral); OverviewUi.Place((RectTransform)parentButton.transform, 0, 2, 320, 36);
        _parentCaption = parentButton.GetComponentInChildren<TextMeshProUGUI>(true); _parentCaption.overflowMode = TextOverflowModes.Ellipsis; _parentCaption.textWrappingMode = TextWrappingModes.NoWrap;
        var parentNote = OverviewUi.Text(parentRow, T("Groups nest at most three levels.", "分组最多嵌套三层。"), 16); parentNote.color = OverviewUi.Muted; OverviewUi.PlaceClipped(parentNote, 332, 10, 400, 24);
        RefreshParentCaption();
        Heading(content, T("Group description (optional)", "分组描述（可选，显示为浅色小字）"));
        _description = OverviewUi.Input(content, group?.description ?? ""); Height(_description.gameObject, 45);
        Heading(content, T("Theme color", "主题颜色"));
        _palette = Grid(content, "Palette", new Vector2(42, 42)); RebuildPalette();
        var selected = OverviewUi.Rect("SelectedColor", content); Height(selected.gameObject, 32);
        var chip = OverviewUi.Panel("ColorPreview", selected, Parse(_color)); OverviewUi.Place(chip, 0, 2, 25, 25); _colorPreview = chip.GetComponent<Image>();
        _colorCaption = OverviewUi.Text(selected, _color, 21); TopStretch(_colorCaption.rectTransform, 38, 1, 30, 42);
        var explanation = OverviewUi.Text(content, T("Changes apply only when saved.", "保存后才会提交；取消勾选的当前成员会移到未分组。"), 18); Height(explanation.gameObject, 46); explanation.textWrappingMode = TextWrappingModes.Normal;
        void MemberSection(bool currentMembers)
        {
            var members = _candidates.Where(p => (group != null && p.Value.Rule.groupId == group.id) == currentMembers)
                .OrderBy(p => OverviewLanguageBridge.GetDisplayName(p.Value), StringComparer.OrdinalIgnoreCase).ToList();
            Heading(content, currentMembers
                ? Loc.F("Current group members ({0})", "当前分组成员（{0}）", "目前分組成員（{0}）", members.Count)
                : Loc.F("Ungrouped mods available to add ({0})", "未分组，可加入（{0}）", "未分組，可加入（{0}）", members.Count));
            if (members.Count == 0)
            {
                var empty = OverviewUi.Text(content, currentMembers
                    ? Loc.T("This group has no members.", "当前分组暂无成员。", "目前分組尚無成員。")
                    : Loc.T("No ungrouped mods are available.", "没有可加入的未分组模组。", "沒有可加入的未分組模組。"), 18);
                empty.color = OverviewUi.Muted; Height(empty.gameObject, 32); return;
            }
            var memberRows = OverviewUi.Rect("MemberRows", content);
            var memberLayout = memberRows.gameObject.AddComponent<VerticalLayoutGroup>(); memberLayout.childControlWidth = true; memberLayout.childControlHeight = true; memberLayout.childForceExpandHeight = false; memberLayout.childForceExpandWidth = true; memberLayout.spacing = 2;
            BuildMemberColumns(memberRows, new[] { T("Name", "名称"), T("Version", "版本"), T("Author", "作者"), T("Description", "说明") }, true);
            foreach (var pair in members)
            {
                string id = pair.Key; var mod = pair.Value;
                string description = mod.Rule.descriptionOverridden ? mod.DisplayDescription : OverviewLanguageBridge.GetDescription(mod);
                var row = BuildMemberColumns(memberRows, new[] { OverviewLanguageBridge.GetDisplayName(mod), mod.DisplayVersion, string.IsNullOrWhiteSpace(mod.Author) ? "—" : mod.Author, description }, false);
                var check = OverviewUi.Check(row, _checked.Contains(id), v => { if (v) _checked.Add(id); else _checked.Remove(id); }); OverviewUi.Place((RectTransform)check.transform, 4, 7, 24, 24);
                var click = row.gameObject.AddComponent<OverviewGroupEditorMemberClick>(); click.Click = () => check.isOn = !check.isOn;
            }
        }
        if (group != null) MemberSection(true);
        MemberSection(false);
        var save = OverviewUi.Button(panel, T("Save", "保存"), Save, 130, 38, role: UiActionRole.Commit); OverviewUi.Bottom((RectTransform)save.transform, 20, 17);
        var cancel = OverviewUi.Button(panel, _embeddedHost == null ? T("Cancel", "取消") : T("Reset draft", "撤销修改"), RequestClose, 130, 38, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)cancel.transform, 160, 17);
        if (group != null)
        {
            var delete = OverviewUi.Button(panel, T("Remove group", "删除分组"), ConfirmRemoveGroup, 140, 38, role: UiActionRole.Danger);
            var rect = (RectTransform)delete.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0); rect.anchoredPosition = new Vector2(-20, 17);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(content); scroll.verticalNormalizedPosition = 1;
        _initialName = _name.text; _initialDescription = _description.text; _initialColor = _color; _initialParentId = _parentId;
        _initialCustom.Clear(); _initialCustom.UnionWith(_custom);
        _initialChecked.Clear(); _initialChecked.UnionWith(_checked);
        EventSystem.current?.SetSelectedGameObject(_name.gameObject); _name.ActivateInputField();
    }

    private void RebuildPalette()
    {
        if (_palette == null) return;
        foreach (Transform child in _palette) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
        foreach (string color in OverviewUi.Palette.Concat(_custom).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string value = color;
            var cell = OverviewUi.Panel("ColorSwatch", _palette, Parse(value));
            var button = cell.gameObject.AddComponent<Button>(); button.targetGraphic = cell.GetComponent<Image>(); button.onClick.AddListener(() => SelectColor(value));
            if (_custom.Contains(value, StringComparer.OrdinalIgnoreCase) && !OverviewUi.Palette.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                var remove = OverviewUi.Button(cell, "×", () => ConfirmRemoveColor(value), 21, 21, role: UiActionRole.Danger);
                foreach (var templateText in remove.GetComponentsInChildren<TMP_Text>(true)) templateText.gameObject.SetActive(false);
                var cross = OverviewUi.Text(remove.transform, "×", 19); OverviewUi.Fill(cross.rectTransform); cross.alignment = TextAlignmentOptions.Center;
                var rect = (RectTransform)remove.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; rect.anchoredPosition = new Vector2(5, 5);
                remove.gameObject.SetActive(false); var hover = cell.gameObject.AddComponent<OverviewGroupEditorSwatchHover>(); hover.Remove = remove.gameObject;
            }
            if (string.Equals(value, _color, StringComparison.OrdinalIgnoreCase))
            {
                var tick = OverviewUi.Text(cell, "✓", 25); OverviewUi.Fill(tick.rectTransform); tick.alignment = TextAlignmentOptions.Center; tick.color = Parse(value).grayscale > .55f ? OverviewUi.Brown : OverviewUi.Ivory;
            }
        }
        var add = OverviewUi.Panel("AddColor", _palette, ModConfigLook.Surface(new Color(.11f, .10f, .08f, .98f)));
        var addButton = add.gameObject.AddComponent<Button>(); addButton.targetGraphic = add.GetComponent<Image>(); addButton.onClick.AddListener(AskColor);
        var pencil = OverviewUi.Rect("AddColorPencil", add); pencil.anchorMin = pencil.anchorMax = pencil.pivot = Vector2.one; pencil.anchoredPosition = new Vector2(-2, -2); pencil.sizeDelta = new Vector2(15, 15);
        var glyph = pencil.gameObject.AddComponent<OverviewIconGraphic>(); glyph.Kind = "pencil"; glyph.color = OverviewUi.Ivory; glyph.raycastTarget = false;
        _palette.GetComponent<OverviewGroupEditorGrid>().Refresh();
    }
    private void SelectColor(string value)
    {
        _color = value; if (_colorCaption != null) _colorCaption.text = value; if (_colorPreview != null) _colorPreview.color = Parse(value); RebuildPalette();
    }
    private void AskColor()
    {
        var panel = Secondary(T("Add color", "新增颜色"));
        Size(panel, 760, 330);
        var hint = OverviewUi.Text(panel, T("Enter a color like #RRGGBB or 255,255,255.\nExample: #7CC7E6 (RRGGBB stands for hex digits).", "请输入类似 #RRGGBB 或 255,255,255 格式的颜色值。\n真实示例：#7CC7E6；RRGGBB 是格式占位，不是色值。"), 19); TopStretch(hint.rectTransform, 20, 68, 88, 40); hint.textWrappingMode = TextWrappingModes.Normal;
        var input = OverviewUi.Input(panel, ""); input.characterLimit = 40; TopStretch((RectTransform)input.transform, 20, 162, 42, 40);
        string initialColor = input.text; _secondaryDirty = () => !string.Equals(input.text, initialColor, StringComparison.Ordinal);
        var validation = OverviewUi.Text(panel, "", 17); TopStretch(validation.rectTransform, 20, 210, 46, 40); validation.textWrappingMode = TextWrappingModes.Normal;
        var save = OverviewUi.Button(panel, T("Add", "添加"), () =>
        {
            string? color = NormalizeColor(input.text); if (color == null) { validation.text = T("Invalid color. Try #7CC7E6 or 255,255,255; each RGB value is 0–255.", "颜色值无效，请填写 #7CC7E6 或 255,255,255；每个 RGB 数值为 0–255。"); return; }
            if (!OverviewUi.Palette.Contains(color, StringComparer.OrdinalIgnoreCase) && !_custom.Contains(color, StringComparer.OrdinalIgnoreCase)) _custom.Add(color);
            CloseSecondary(); SelectColor(color);
        }, 125, 36, role: UiActionRole.Commit); OverviewUi.Bottom((RectTransform)save.transform, 20, 18);
        var cancel = OverviewUi.Button(panel, T("Cancel", "取消"), RequestCloseSecondary, 125, 36, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)cancel.transform, 155, 18);
        EventSystem.current?.SetSelectedGameObject(input.gameObject); input.ActivateInputField();
    }
    private void ConfirmRemoveColor(string color)
    {
        var panel = Secondary(T("Remove this custom color?", "确认删除此自定义颜色？"));
        var hint = OverviewUi.Text(panel, T("Only the palette entry is removed. Groups retain their color values.", "只移除色盘中的颜色，不删除分组或模组；已使用的色值保持不变。"), 21); TopStretch(hint.rectTransform, 20, 75, 78, 40); hint.textWrappingMode = TextWrappingModes.Normal;
        var yes = OverviewUi.Button(panel, T("Remove", "删除"), () => { _custom.RemoveAll(c => string.Equals(c, color, StringComparison.OrdinalIgnoreCase)); CloseSecondary(); RebuildPalette(); }, 125, 36, role: UiActionRole.Danger); OverviewUi.Bottom((RectTransform)yes.transform, 20, 18);
        var no = OverviewUi.Button(panel, T("Cancel", "取消"), CloseSecondary, 125, 36, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)no.transform, 155, 18);
        EventSystem.current?.SetSelectedGameObject(no.gameObject);
    }
    private void ConfirmRemoveGroup()
    {
        var panel = Secondary(T("Remove this group?", "确认删除此分组？"));
        var hint = OverviewUi.Text(panel, T("Its mods and subgroups move up one level (to Ungrouped when it is a top-level group). No plugin files are removed.", "组内模组和子分组会上移一层（顶层分组的模组移到未分组），不删除任何插件文件。"), 21); TopStretch(hint.rectTransform, 20, 75, 78, 40); hint.textWrappingMode = TextWrappingModes.Normal;
        var yes = OverviewUi.Button(panel, T("Remove", "删除"), () => Commit(true), 125, 36, role: UiActionRole.Danger); OverviewUi.Bottom((RectTransform)yes.transform, 20, 18);
        var no = OverviewUi.Button(panel, T("Cancel", "取消"), CloseSecondary, 125, 36, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)no.transform, 155, 18);
        EventSystem.current?.SetSelectedGameObject(no.gameObject);
    }
    private void Save() => Commit(false);
    internal void StageDraft() { if (HasDirty) Commit(false, false); }
    private void Commit(bool remove, bool persist = true)
    {
        string name = _name?.text.Trim() ?? "";
        if (!remove && name.Length == 0) { _status(T("Enter a group name.", "请填写分组名称。")); return; }
        try
        {
            _repository.EnsureLocalPackCopy();
            var oldGroups = _repository.State.groups.Select(g => new OverviewGroup { id = g.id, name = g.name, description = g.description, color = g.color, order = g.order, parentId = g.parentId }).ToList();
            var oldColors = new List<string>(_repository.State.customColors ?? new List<string>());
            var oldRules = new List<OverviewModRule>(_repository.State.mods);
            var oldMembership = oldRules.Select(r => (Rule: r, Group: r.groupId)).ToList();
            var oldRows = _mods.Select(m => (Mod: m, Rule: m.Rule, Group: m.Rule.groupId)).ToList();
            try
            {
                string groupId = _group?.id ?? System.Guid.NewGuid().ToString("N");
                if (remove)
                {
                    // Subgroups and members move up one level (to the deleted group's parent; ungrouped at the top).
                    string up = new GroupTree(_repository.State.groups).Parent(groupId);
                    GroupTree.Remove(_repository.State.groups, _repository.State.mods, groupId, _repository.State.collapsedGroups);
                    foreach (var mod in _mods.Where(m => m.Rule.groupId == groupId)) mod.Rule.groupId = up;
                }
                else
                {
                    var group = _repository.State.groups.FirstOrDefault(g => g.id == groupId);
                    if (group == null) { group = new OverviewGroup { id = groupId, parentId = _parentId, order = GroupTree.NextOrder(_repository.State.groups, _parentId) }; _repository.State.groups.Add(group); }
                    else if (!string.Equals(group.parentId ?? "", _parentId, StringComparison.Ordinal) && !GroupTree.Attach(_repository.State.groups, groupId, _parentId, out string reason))
                        throw new InvalidOperationException(ParentReason(reason));
                    group.name = name; group.description = _description?.text.Trim() ?? ""; group.color = _color;
                    foreach (var pair in _candidates)
                    {
                        var rule = _repository.GetRule(pair.Key);
                        if (_checked.Contains(pair.Key)) rule.groupId = groupId; else if (rule.groupId == groupId) rule.groupId = "";
                        pair.Value.Rule = rule;
                    }
                }
                _repository.State.customColors = new List<string>(_custom);
                if (persist) _repository.SavePackInformation(ConfigurationSaveScope.Groups); else _repository.PersistRecoveryDraft();
            }
            catch
            {
                _repository.State.groups = oldGroups; _repository.State.customColors = oldColors;
                _repository.State.mods = oldRules;
                foreach (var prior in oldMembership) prior.Rule.groupId = prior.Group;
                foreach (var prior in oldRows) { prior.Mod.Rule = prior.Rule; prior.Rule.groupId = prior.Group; }
                throw;
            }
            var embedded = _embeddedHost;
            string? selectedId = remove ? null : _group?.id;
            Close(); _saved(); _status(!persist ? T("Group draft retained; save configuration to commit.", "分组草稿已保留，保存配置后提交。") : remove ? T("Group removed; its contents moved up one level.", "分组已删除，内容已上移一层。") : T("Group saved.", "分组已保存。"));
            if (embedded != null) Show(_repository.State.groups.FirstOrDefault(g => g.id == selectedId) ?? _repository.State.groups.FirstOrDefault());
        }
        catch (Exception ex) { _status(T("Unable to save group: ", "分组保存失败：") + ex.Message); }
    }
    private RectTransform Secondary(string title)
    {
        CloseSecondary();
        _secondarySelection = EventSystem.current?.currentSelectedGameObject;
        _secondarySelection?.GetComponent<TMP_InputField>()?.DeactivateInputField();
        if (_formCanvas != null) _formCanvas.interactable = false;
        var shade = OverviewUi.Panel("GroupConfirmation", _modal!.transform, ModConfigLook.Surface(new Color(0, 0, 0, .72f))); OverviewUi.Fill(shade); shade.SetAsLastSibling(); _secondary = shade.gameObject;
        shade.gameObject.AddComponent<CanvasGroup>();
        var panel = OverviewUi.Panel("Confirmation", shade, OverviewUi.Brown); Size(panel, 760, 250);
        var heading = OverviewUi.Text(panel, title, 27); TopStretch(heading.rectTransform, 20, 18, 43, 40); heading.textWrappingMode = TextWrappingModes.Normal;
        return panel;
    }
    /// <summary>Secondary prompts hold a title, a line or two and two buttons: a fixed size, centred, capped by the screen
    /// (they used to fill 64% x 46-68% of it).</summary>
    private void Size(RectTransform panel, float width, float height)
    {
        var screen = ((RectTransform)_modal!.transform).rect;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(Mathf.Min(width, Mathf.Max(380, screen.width - 60)), Mathf.Min(height, Mathf.Max(220, screen.height - 60)));
    }
    public bool TryCloseTop()
    {
        if (!IsOpen) return false;
        if (_secondary != null) RequestCloseSecondary(); else RequestClose();
        return true;
    }
    internal bool TryCloseSecondaryLayer()
    { if (_secondary == null) return false; RequestCloseSecondary(); return true; }
    public void RequestClose()
    {
        if (_secondary != null) { RequestCloseSecondary(); return; }
        if (!IsOpen) return;
        Action close = _embeddedHost == null ? Close : () => Show(_repository.State.groups.FirstOrDefault(g => g.id == _group?.id));
        if (HasDirty) OverviewUi.ConfirmDiscard(T("Discard unsaved group changes?", "放弃未保存的分组修改？"), close);
        else close();
    }
    private void MoveGroup(int offset)
    {
        void Move()
        {
            string? id = _group?.id; _repository.EnsureLocalPackCopy();
            if (id == null || !GroupTree.MoveAmongSiblings(_repository.State.groups, id, offset)) return;
            _repository.SavePackInformation(ConfigurationSaveScope.Groups); _saved(); Show(_repository.State.groups.First(g => g.id == id));
        }
        if (HasDirty) OverviewUi.ConfirmDiscard(T("Discard the current group draft?", "放弃当前未保存的分组修改？"), Move); else Move();
    }
    private void RefreshParentCaption()
    {
        if (_parentCaption == null) return;
        _parentCaption.text = _parentId.Length == 0 ? T("None (top level)", "无（顶层）") : new GroupTree(_repository.State.groups).Path(_parentId);
    }
    private string ParentReason(string reason) => reason == "depth" ? T("Groups nest at most three levels; this parent would go deeper.", "分组最多嵌套三层，选这个上级会超过三层。")
        : reason == "cycle" ? T("A group cannot go inside its own subgroup.", "分组不能放进它自己的子分组里。") : T("That parent group no longer exists.", "该上级分组已不存在。");
    /// <summary>Parents offered: top level, then every group except this group's own subtree and any that would push the subtree past three levels.</summary>
    private void ChooseParent()
    {
        var tree = new GroupTree(_repository.State.groups);
        string self = _group?.id ?? "";
        int height = self.Length == 0 ? 1 : tree.Height(self);
        var excluded = self.Length == 0 ? new HashSet<string>() : tree.Subtree(self);
        var options = new List<(string Label, string Value)> { (T("None (top level)", "无（顶层）"), "") };
        foreach (var candidate in tree.Flat)
            if (!excluded.Contains(candidate.id) && tree.Depth(candidate.id) + height <= GroupTree.MaxDepth)
                options.Add((new string(' ', (tree.Depth(candidate.id) - 1) * 2) + tree.Path(candidate.id), candidate.id));
        OverviewUi.Choose(T("Parent group", "上级分组"), options, _parentId, value =>
        {
            bool recolor = _group == null && string.Equals(_color, GroupPalette.ForNewGroup(_repository.State.groups, _parentId), StringComparison.OrdinalIgnoreCase);
            _parentId = value ?? "";
            // A new group that still has its suggested colour follows its new parent's family.
            if (recolor) { _color = GroupPalette.ForNewGroup(_repository.State.groups, _parentId); if (_colorCaption != null) _colorCaption.text = _color; if (_colorPreview != null) _colorPreview.color = Parse(_color); RebuildPalette(); }
            RefreshParentCaption();
        });
    }
    private void RequestChangeGroup(OverviewGroup? group)
    {
        if (HasDirty) OverviewUi.ConfirmDiscard(T("Discard the current group draft?", "放弃当前未保存的分组修改？"), () => Show(group));
        else Show(group);
    }
    private void RequestCloseSecondary()
    {
        if (_secondaryDirty?.Invoke() == true)
            OverviewUi.ConfirmDiscard(T("Discard the color you entered?", "放弃尚未添加的颜色输入？"), CloseSecondary);
        else CloseSecondary();
    }
    private void CloseSecondary() => CloseSecondary(true);
    private void CloseSecondary(bool restoreSelection)
    {
        if (_secondary != null) { _secondary.SetActive(false); UnityEngine.Object.Destroy(_secondary); }
        _secondary = null; _secondaryDirty = null;
        if (_formCanvas != null) _formCanvas.interactable = true;
        if (restoreSelection && _secondarySelection != null && _secondarySelection.activeInHierarchy)
        {
            EventSystem.current?.SetSelectedGameObject(_secondarySelection);
            _secondarySelection.GetComponent<TMP_InputField>()?.ActivateInputField();
        }
        _secondarySelection = null;
    }
    internal void Close()
    {
        CloseSecondary(false);
        if (_modal != null) { _modal.SetActive(false); UnityEngine.Object.Destroy(_modal); }
        _modal = null; _name = null; _description = null; _palette = null; _colorCaption = null; _colorPreview = null; _formCanvas = null;
    }
    public void Dispose() => Close();
    private static string T(string english, string chinese) => Loc.T(english, chinese, chinese);
    private static Color Parse(string color) => ColorUtility.TryParseHtmlString(color, out Color value) ? value : OverviewUi.Blue;
    private static string? NormalizeColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null; string value = text!.Normalize(NormalizationForm.FormKC).Trim();
        value = new string(value.Select(c => char.IsWhiteSpace(c) ? ' ' : c).ToArray());
        if (value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)) return value.ToUpperInvariant();
        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) value = value.Substring(3).Trim();
        value = value.Trim(' ', '(', ')').Replace('，', ',').Replace('、', ',').Replace('；', ';');
        string[] parts = value.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && byte.TryParse(parts[0], out byte r) && byte.TryParse(parts[1], out byte g) && byte.TryParse(parts[2], out byte b)) return $"#{r:X2}{g:X2}{b:X2}";
        return null;
    }
    private static void Height(GameObject go, float height) { var layout = go.AddComponent<LayoutElement>(); layout.minHeight = layout.preferredHeight = height; layout.flexibleHeight = 0; }
    private static RectTransform BuildMemberColumns(Transform parent, string[] values, bool heading)
    {
        var row = OverviewUi.Panel(heading ? "MemberColumnHeader" : "GroupMember", parent, heading ? ModConfigLook.Surface(new Color(.16f, .19f, .16f, .85f)) : ModConfigLook.Surface(new Color(.12f, .15f, .12f, .72f)));
        Height(row.gameObject, heading ? 34 : 38);
        var cells = new RectTransform[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var text = OverviewUi.Text(row, (values[i] ?? "").Replace('\r', ' ').Replace('\n', ' '), heading ? 18 : 19);
            text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis;
            cells[i] = text.rectTransform;
        }
        var columns = row.gameObject.AddComponent<OverviewGroupEditorMemberColumns>(); columns.Cells = cells; columns.Refresh();
        return row;
    }
    private static void Heading(Transform parent, string text) { var label = OverviewUi.Text(parent, text, 23); Height(label.gameObject, 32); }
    private static void TopStretch(RectTransform rect, float x, float y, float height, float right)
    { rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(-x - right, height); }
    private static RectTransform Grid(Transform parent, string name, Vector2 size)
    {
        var rect = OverviewUi.Rect(name, parent); var grid = rect.gameObject.AddComponent<GridLayoutGroup>(); grid.cellSize = size; grid.spacing = new Vector2(10, 10); grid.childAlignment = TextAnchor.UpperLeft;
        var layout = rect.gameObject.AddComponent<LayoutElement>(); var sizing = rect.gameObject.AddComponent<OverviewGroupEditorGrid>(); sizing.Grid = grid; sizing.Layout = layout; return rect;
    }
    private static void BuildScrollbar(RectTransform host, ScrollRect scroll)
    {
        var track = OverviewUi.Panel("Scrollbar", host, ModConfigLook.Surface(new Color(.08f, .10f, .08f, .8f))); track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one; track.pivot = new Vector2(1, 1); track.sizeDelta = new Vector2(9, 0); track.anchoredPosition = Vector2.zero;
        var handle = OverviewUi.Panel("Handle", track, OverviewUi.Ivory); OverviewUi.Fill(handle); var bar = track.gameObject.AddComponent<Scrollbar>(); bar.targetGraphic = handle.GetComponent<Image>(); bar.handleRect = handle; bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
    }
}

/// <summary>Header and rows share the same widths, measured from their actual modal viewport.</summary>
internal sealed class OverviewGroupEditorMemberColumns : MonoBehaviour
{
    internal RectTransform[] Cells = Array.Empty<RectTransform>();
    private static readonly float[] Shares = { .30f, .15f, .18f, .37f };
    private void OnEnable() => Refresh();
    private void OnRectTransformDimensionsChange() => Refresh();
    internal void Refresh()
    {
        var row = (RectTransform)transform;
        const float start = 42, gap = 10, right = 8;
        float available = Math.Max(0, row.rect.width - start - right - gap * 3);
        float x = start;
        for (int i = 0; i < Cells.Length && i < Shares.Length; i++)
        {
            float width = available * Shares[i];
            var cell = Cells[i]; cell.anchorMin = cell.anchorMax = cell.pivot = new Vector2(0, 1);
            var label = cell.GetComponent<TextMeshProUGUI>();
            if (label != null) OverviewUi.PlaceClipped(label, x, 3, width, Math.Max(0, row.rect.height - 6));
            else { cell.anchoredPosition = new Vector2(x, -3); cell.sizeDelta = new Vector2(width, Math.Max(0, row.rect.height - 6)); }
            x += width + gap;
        }
    }
}

internal sealed class OverviewGroupEditorGrid : MonoBehaviour
{
    internal GridLayoutGroup Grid = null!;
    internal LayoutElement Layout = null!;
    private bool _refreshing;
    private void OnEnable() => Refresh();
    private void OnRectTransformDimensionsChange() => Refresh();
    private void OnTransformChildrenChanged() => Refresh();
    internal void Refresh()
    {
        if (_refreshing || Grid == null || Layout == null) return; _refreshing = true;
        try
        {
            float width = ((RectTransform)transform).rect.width;
            int columns = Math.Max(1, (int)((Math.Max(width, Grid.cellSize.x) + Grid.spacing.x) / (Grid.cellSize.x + Grid.spacing.x)));
            int count = 0; foreach (Transform child in transform) if (child.gameObject.activeSelf) count++;
            int rows = (count + columns - 1) / columns;
            float height = rows == 0 ? 0 : rows * Grid.cellSize.y + (rows - 1) * Grid.spacing.y;
            Grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; Grid.constraintCount = columns;
            Layout.minHeight = Layout.preferredHeight = height; Layout.flexibleHeight = 0;
        }
        finally { _refreshing = false; }
    }
}
internal sealed class OverviewGroupEditorMemberClick : MonoBehaviour, IPointerClickHandler
{
    internal Action? Click;
    public void OnPointerClick(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) Click?.Invoke(); }
}
internal sealed class OverviewGroupEditorSwatchHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal GameObject Remove = null!;
    public void OnPointerEnter(PointerEventData eventData) { if (Remove != null) Remove.SetActive(true); }
    public void OnPointerExit(PointerEventData eventData) { if (Remove != null) Remove.SetActive(false); }
}
