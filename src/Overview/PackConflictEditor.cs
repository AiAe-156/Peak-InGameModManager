#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Author-owned pack draft; saving advice never changes a profile or plugin files.</summary>
internal sealed class PackConflictEditor
{
    private readonly OverviewRepository _repo;
    private readonly string _packId;
    private readonly Action<PackConflictFile> _saved;
    private readonly IReadOnlyList<OverviewMod> _mods;
    private readonly RectTransform _panel;
    private PackConflictFile _draft;
    private string _baseline, _selected = "", _error = "";
    private bool _choosing;
    private TextMeshProUGUI? _selectedCaption;
    private static readonly Color Advice = new(.91f, .85f, .60f);
    private bool Dirty => PackConflictStore.Serialize(_draft) != _baseline;
    private static string T(string en, string zh, string tw) => Loc.T(en, zh, tw);
    private PackConflictRule? Selected => _draft.rules.FirstOrDefault(r => r.id == _selected);

    internal static void Show(OverviewRepository repo, string packId, PackConflictFile file, Action<PackConflictFile> saved)
    {
        // Scan before replacing a modal, so an inventory error cannot discard another editor.
        var mods = new OverviewCatalogue().Scan(repo);
        var panel = OverviewUi.CreateModalPanel("MCE_PackConflictEditor", 1080, 780);
        if (panel == null) return;
        _ = new PackConflictEditor(repo, packId, file, saved, mods, panel);
    }
    private PackConflictEditor(OverviewRepository repo, string packId, PackConflictFile file, Action<PackConflictFile> saved, IReadOnlyList<OverviewMod> mods, RectTransform panel)
    {
        _repo = repo; _packId = packId; _saved = saved; _mods = mods; _panel = panel;
        _draft = PackConflictStore.Clone(file); _baseline = PackConflictStore.Serialize(_draft); _selected = _draft.rules.FirstOrDefault()?.id ?? "";
        OverviewUi.SetModalDirtyCheck(() => Dirty); OverviewUi.SetModalCloseHandler(RequestClose); Draw();
    }
    private void Draw()
    {
        Clear(_panel); _selectedCaption = null;
        float width = _panel.rect.width, height = _panel.rect.height;
        var heading = OverviewUi.Text(_panel, _choosing ? T("Add a conflict member", "添加冲突成员", "新增衝突成員") : T("Modpack conflict presets", "整合包冲突预设", "整合包衝突預設"), 26);
        OverviewUi.PlaceClipped(heading, 22, 13, width - 44, 38);
        var scope = OverviewUi.Text(_panel, T("Shared by every profile · advice only · exported with this modpack", "所有方案共用 · 只作建议 · 随本整合包导出", "所有方案共用 · 僅作建議 · 隨本整合包匯出"), 16); scope.color = Advice;
        OverviewUi.PlaceClipped(scope, 22, 55, width - 44, 30);
        var error = OverviewUi.Text(_panel, _error, 15); error.color = new Color(.96f, .65f, .52f); error.textWrappingMode = TextWrappingModes.Normal;
        OverviewUi.Place(error.rectTransform, 22, height - 94, width - 44, 42);
        var body = OverviewUi.Rect("ConflictEditorBody", _panel); OverviewUi.Place(body, 22, 94, width - 44, Mathf.Max(120, height - 198));
        if (_choosing) DrawSelector(body); else DrawRules(body);
        float gap = 10, buttonWidth = Mathf.Min(170, (width - 44 - gap * 3) / 4);
        AddFooter(T("Save presets", "保存预设", "儲存預設"), () => Save(), 0, UiActionRole.Commit);
        AddFooter(T("Reset draft", "撤销修改", "撤銷修改"), Reset, 1, UiActionRole.Neutral);
        var remove = AddFooter(T("Delete rule", "删除规则", "刪除規則"), Remove, 2, UiActionRole.Danger); remove.interactable = !_choosing && Selected != null;
        AddFooter(_choosing ? T("Back", "返回", "返回") : T("Close", "关闭", "關閉"), RequestClose, 3, UiActionRole.Neutral);
        Button AddFooter(string label, Action action, int index, UiActionRole role)
        {
            var button = OverviewUi.Button(_panel, label, action, buttonWidth, 36, role); OverviewUi.Bottom((RectTransform)button.transform, 22 + index * (buttonWidth + gap), 16);
            var text = button.GetComponentInChildren<TMP_Text>(true); text.enableAutoSizing = true; text.fontSizeMin = 10; text.fontSizeMax = 17;
            return button;
        }
        PublicationNavigation.Attach(_panel);
    }
    private void DrawRules(RectTransform body)
    {
        bool compact = body.rect.width < 650;
        float listWidth = compact ? body.rect.width : Mathf.Clamp(body.rect.width * .28f, 210, 300);
        float listHeight = compact ? Mathf.Min(150, body.rect.height * .32f) : body.rect.height;
        var left = OverviewUi.Rect("Rules", body); OverviewUi.Place(left, 0, 0, listWidth, listHeight);
        var add = OverviewUi.Button(left, T("Add rule", "新建规则", "新增規則"), () => {
            var rule = new PackConflictRule { id = Guid.NewGuid().ToString("N"), title = T("New conflict advice", "新的冲突建议", "新的衝突建議") };
            _draft.rules.Add(rule); _selected = rule.id; _error = ""; Draw();
        }, Mathf.Min(160, listWidth), 34, UiActionRole.Edit); OverviewUi.Place((RectTransform)add.transform, 0, 0, Mathf.Min(160, listWidth), 34);
        var rows = Scroll(left, 44, listHeight - 44, out _, out float rowWidth);
        float y = 0;
        foreach (var rule in _draft.rules)
        {
            var current = rule;
            var button = OverviewUi.Button(rows, "", () => { _selected = current.id; _error = ""; Draw(); }, rowWidth, 60, current.id == _selected ? UiActionRole.Edit : UiActionRole.Neutral);
            OverviewUi.Place((RectTransform)button.transform, 0, y, rowWidth, 60);
            float inset = Mathf.Clamp(rowWidth * .1f, 16, 26);
            var title = Label(button.transform, RuleTitle(current), 17, OverviewUi.Ivory); OverviewUi.PlaceClipped(title, inset, 4, rowWidth - inset * 2, 29);
            var summary = Label(button.transform, current.members.Count + " · " + ModeLabel(current), 13, Advice); OverviewUi.PlaceClipped(summary, inset, 31, rowWidth - inset * 2, 23);
            if (current.id == _selected) _selectedCaption = title;
            y += 66;
        }
        rows.sizeDelta = new Vector2(0, y);
        float detailX = compact ? 0 : listWidth + 20, detailY = compact ? listHeight + 16 : 0;
        var detail = OverviewUi.Rect("RuleDetails", body); OverviewUi.Place(detail, detailX, detailY, body.rect.width - detailX, body.rect.height - detailY);
        if (Selected == null)
        {
            var hint = OverviewUi.Text(detail, T("Create a rule, choose at least two independently toggled mods, then describe the problem for players.", "新建规则，选择至少两个可独立启停的模组，再填写给玩家看的冲突原因。", "新增規則，選擇至少兩個可獨立啟停的模組，再填寫給玩家看的衝突原因。"), 19); hint.color = OverviewUi.Muted; hint.textWrappingMode = TextWrappingModes.Normal; OverviewUi.Fill(hint.rectTransform, 10); return;
        }
        DrawDetails(detail, Selected);
    }
    private void DrawDetails(RectTransform host, PackConflictRule rule)
    {
        var content = Scroll(host, 0, host.rect.height, out _, out float width); float y = 0;
        void Note(string value, Color color, float size = 16)
        {
            var label = Label(content, value, size, color); label.textWrappingMode = TextWrappingModes.Normal;
            float h = Mathf.Max(28, label.GetPreferredValues(value, width, 0).y + 6); OverviewUi.Place(label.rectTransform, 0, y, width, h); y += h + 8;
        }
        void Field(string caption, string value, bool multiline, Action<string> update)
        {
            Note(caption, OverviewUi.Muted);
            var field = OverviewUi.Input(content, value, multiline); field.characterLimit = multiline ? 4000 : 160; field.onValueChanged.AddListener(v => update(v));
            OverviewUi.Place((RectTransform)field.transform, 0, y, width, multiline ? 100 : 40); y += multiline ? 116 : 52;
        }
        Field(T("Rule title", "规则标题", "規則標題"), rule.title, false, value => { rule.title = value; if (_selectedCaption != null) _selectedCaption.text = RuleTitle(rule); });
        Note(T("When to warn", "何时提醒", "何時提醒"), OverviewUi.Muted);
        ModeChoice(PackConflictRule.AnyTwo, T("Any two enabled", "任意两项同时启用", "任意兩項同時啟用"));
        ModeChoice(PackConflictRule.All, T("All enabled together", "全部同时启用", "全部同時啟用"));
        void ModeChoice(string mode, string title)
        {
            var check = OverviewUi.Check(content, rule.mode == mode, value => { if (value) rule.mode = mode; Draw(); }); OverviewUi.Place((RectTransform)check.transform, 0, y, 26, 26);
            var label = Label(content, title, 18, rule.mode == mode ? Advice : OverviewUi.Muted); OverviewUi.PlaceClipped(label, 38, y - 1, width - 40, 31); y += 36;
        }
        Note(rule.mode == PackConflictRule.All
            ? T("For A, B and C: warn only when all three are enabled; enabling a pair is allowed.", "例如 A、B、C：仅三项全部启用时提醒，任意两项组合不提醒。", "例如 A、B、C：僅三項全部啟用時提醒，任意兩項組合不提醒。")
            : T("For A, B and C: A+B, A+C, B+C and all three trigger the advice.", "例如 A、B、C：A+B、A+C、B+C，以及三项全部启用都会提醒。", "例如 A、B、C：A+B、A+C、B+C，以及三項全部啟用都會提醒。"), Advice);
        Note(T("Members", "成员模组", "成員模組") + " · " + rule.members.Count, OverviewUi.Ivory, 20);
        var add = OverviewUi.Button(content, T("Add member", "添加成员", "新增成員"), () => { _choosing = true; _error = ""; Draw(); }, 140, 34, UiActionRole.Edit);
        OverviewUi.Place((RectTransform)add.transform, 0, y, Mathf.Min(140, width), 34); y += 46;
        if (rule.members.Count == 0) Note(T("No members yet. At least two independent toggle units are required.", "尚未添加成员；至少需要两个独立启停单元。", "尚未新增成員；至少需要兩個獨立啟停單元。"), Advice);
        foreach (var member in rule.members.ToArray())
        {
            var mod = Resolve(member.guid); string name = mod?.Name ?? (member.name.Length > 0 ? member.name : member.guid);
            var title = Label(content, name, 18, ModConfigLook.Surface(new Color(.98f, .97f, .91f))); OverviewUi.PlaceClipped(title, 0, y, width - 88, 29);
            var remove = OverviewUi.Button(content, T("Remove", "移除", "移除"), () => { rule.members.Remove(member); Draw(); }, 76, 30, UiActionRole.Neutral); OverviewUi.Place((RectTransform)remove.transform, width - 80, y, 76, 30); y += 31;
            string state = mod == null ? T("Missing locally · retained for export", "本机未找到 · 保留并随包导出", "本機未找到 · 保留並隨包匯出") : IsLinked(mod) ? T("Package-linked toggle · selected once for this unit", "随包联动 · 同一启停单元只选一项", "隨包連動 · 同一啟停單元只選一項") : T("Independent toggle unit", "独立启停单元", "獨立啟停單元");
            var status = Label(content, state, 14, mod == null ? Advice : OverviewUi.Muted); OverviewUi.PlaceClipped(status, 0, y, width - 4, 25); y += 31;
            var guid = Label(content, member.guid, 12, OverviewUi.Muted); OverviewUi.PlaceClipped(guid, 0, y, width - 4, 21); y += 29;
        }
        Field(T("Reason shown to players", "给玩家看的冲突原因", "給玩家看的衝突原因"), rule.reason, true, value => rule.reason = value);
        Note(T("Players can still apply their choices or ignore active advice locally. Saving these presets changes no enabled mods.", "玩家仍可继续应用，或在本机忽略主动提醒。保存预设只记录建议，不改变模组启停。", "玩家仍可繼續套用，或在本機忽略主動提醒。儲存預設只記錄建議，不改變模組啟停。"), OverviewUi.Muted);
        content.sizeDelta = new Vector2(0, y + 8);
    }
    private void DrawSelector(RectTransform body)
    {
        var rule = Selected; if (rule == null) { _choosing = false; Draw(); return; }
        var used = new HashSet<string>(rule.members.Select(m => Resolve(m.guid)).Where(m => m != null).Select(m => PackConflictService.ToggleUnit(m!)), StringComparer.OrdinalIgnoreCase);
        var candidates = _mods.Where(m => !string.IsNullOrWhiteSpace(m.Guid) && !m.Guid.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            .Where(m => !rule.members.Any(member => member.guid.Equals(m.Guid, StringComparison.OrdinalIgnoreCase)) && !used.Contains(PackConflictService.ToggleUnit(m)))
            .GroupBy(PackConflictService.ToggleUnit, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var note = Label(body, T("Mods in one package toggle together and appear once. Missing saved members remain in the rule.", "同包模组会联动启停，这里只显示一项；已记录但本机缺失的成员继续保留。", "同包模組會連動啟停，這裡僅顯示一項；已記錄但本機缺少的成員繼續保留。"), 16, Advice); note.textWrappingMode = TextWrappingModes.Normal;
        OverviewUi.Place(note.rectTransform, 0, 0, body.rect.width, 52);
        var selector = OverviewUi.Rect("MemberSelector", body); OverviewUi.Place(selector, 0, 62, body.rect.width, body.rect.height - 62);
        _ = new ManagementModSelector(selector, _repo, candidates, "", mod => {
            rule.members.Add(new PackConflictMember { guid = mod.Guid, name = mod.Name }); _choosing = false; _error = ""; Draw();
        });
    }
    private OverviewMod? Resolve(string guid) => _mods.FirstOrDefault(m => m.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase) || m.PluginGuids.Contains(guid, StringComparer.OrdinalIgnoreCase));
    private bool IsLinked(OverviewMod mod) => mod.PluginGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 || _mods.Count(m => PackConflictService.ToggleUnit(m).Equals(PackConflictService.ToggleUnit(mod), StringComparison.OrdinalIgnoreCase)) > 1;
    private static string RuleTitle(PackConflictRule rule) => string.IsNullOrWhiteSpace(rule.title) ? T("Untitled advice", "未命名建议", "未命名建議") : rule.title;
    private static string ModeLabel(PackConflictRule rule) => rule.mode == PackConflictRule.All ? T("All enabled", "全部启用", "全部啟用") : T("Any pair", "任意两项", "任意兩項");
    private bool Save()
    {
        try
        {
            var errors = PackConflictStore.Validate(_draft, _mods);
            if (errors.Count > 0) { _error = string.Join("\n", errors.Take(2)); _choosing = false; Draw(); return false; }
            PackConflictStore.SaveAuthorDraft(_packId, _draft, _mods); _baseline = PackConflictStore.Serialize(_draft);
            _saved(PackConflictStore.Clone(_draft)); _error = T("Publication draft saved. Export and install the modpack to show this advice in every profile.", "发布草稿已保存；导出并安装整合包后，所有方案都会显示这些建议。", "發布草稿已儲存；匯出並安裝整合包後，所有方案都會顯示這些建議。"); Draw(); return true;
        }
        catch (Exception error) { _error = Loc.LocalizeMessage(error.Message); Draw(); return false; }
    }
    private void Reset()
    {
        void Reload() { _draft = PackConflictStore.LoadAuthorDraft(_packId); _baseline = PackConflictStore.Serialize(_draft); _selected = _draft.rules.FirstOrDefault()?.id ?? ""; _choosing = false; _error = ""; Draw(); }
        if (Dirty) OverviewUi.ConfirmDiscard(T("Discard unsaved preset changes?", "放弃尚未保存的预设修改？", "放棄尚未儲存的預設修改？"), Reload); else Reload();
    }
    private void Remove()
    {
        if (Selected is not PackConflictRule rule) return;
        _draft.rules.Remove(rule); _selected = _draft.rules.FirstOrDefault()?.id ?? "";
        _error = T("Rule removed from this draft. Save presets to keep the removal, or reset to undo.", "已从草稿移除规则；保存预设后保留删除，撤销修改可恢复。", "已從草稿移除規則；儲存預設後保留刪除，撤銷修改可復原。"); Draw();
    }
    private void RequestClose()
    {
        if (_choosing) { _choosing = false; Draw(); return; }
        if (!Dirty) { OverviewUi.CloseModal(); return; }
        OverviewUi.ConfirmUnsaved(T("Save this modpack's conflict advice before leaving?", "离开前保存本整合包的冲突建议？", "離開前儲存本整合包的衝突建議？"), () => { if (Save()) OverviewUi.CloseModal(); }, OverviewUi.CloseModal, () => { });
    }
    private static TextMeshProUGUI Label(Transform parent, string text, float size, Color color)
    { var label = OverviewUi.Text(parent, text, size); label.richText = false; label.color = color; label.alignment = TextAlignmentOptions.MidlineLeft; label.margin = Vector4.zero; return label; }
    private static RectTransform Scroll(RectTransform host, float top, float height, out ScrollRect scroll, out float contentWidth)
    {
        var area = OverviewUi.Rect("Scroll", host); OverviewUi.Place(area, 0, top, host.rect.width, Mathf.Max(32, height));
        scroll = area.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        var viewport = OverviewUi.Rect("Viewport", area); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-18, 0); viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var content = OverviewUi.Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1); content.sizeDelta = Vector2.zero;
        scroll.viewport = viewport; scroll.content = content; ManagementUi.Scrollbar(area, scroll); contentWidth = host.rect.width - 24; return content;
    }
    private static void Clear(RectTransform parent)
    { foreach (Transform child in parent) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); } }
}
