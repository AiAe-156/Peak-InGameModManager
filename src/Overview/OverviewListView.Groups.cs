#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Nested groups in the overview list (2.3.0): tree order, indented headers with one colour rail per level,
/// collapse that hides the whole subtree, a right-click menu on headers, and dragging a header onto another header to
/// hang it underneath.</summary>
internal sealed partial class OverviewListView
{
    internal const float GroupIndent = 14, RailWidth = 3, RailGap = 2;
    private GroupTree? _groupTree;
    private OverviewGroup? _draggedGroup;

    private GroupTree Groups => _groupTree ??= new GroupTree(_repo.State.groups);
    private void InvalidateGroupTree() => _groupTree = null;

    /// <summary>Sort key of a member row. 1.1.0（实测修正，用户 2026-11-16）：子分组排在本组直属模组前面
    /// （<see cref="GroupTree.RowKey"/>；原来是直属模组在前、子分组在底部）。</summary>
    private int GroupSortKey(string groupId) => Groups.RowKey(groupId);

    private bool RowHiddenByCollapse(string groupId) => Groups.HiddenByCollapse(groupId, _repo.State.collapsedGroups);

    /// <summary>Header heights and type sizes per depth: top 32/20, then 28/18, then 28/17.</summary>
    private static float HeaderHeight(int depth) => depth <= 1 ? GroupHeight : 28;
    private static float HeaderFont(int depth) => depth <= 1 ? 20 : depth == 2 ? 18 : 17;

    /// <summary>The colour rails drawn at the left of a header or member row: one per level, ancestors first.
    /// Returns the x after the last rail.</summary>
    private float DrawRails(RectTransform row, string groupId, float height)
    {
        var chain = Groups.Chain(groupId);
        if (chain.Count == 0) { var none = OverviewUi.Rail("GroupRail", row, GroupColor(groupId)); OverviewUi.Place(none, 0, 0, RailWidth, height); return RailWidth; }
        float x = 0;
        // Each deeper level is thinner and shorter, bottom-aligned (GroupRailShape).
        for (int level = 0; level < chain.Count; level++)
        {
            float width = GroupRailShape.Width(RailWidth, level); var (y, h) = GroupRailShape.Vertical(0, height, level);
            var rail = OverviewUi.Rail("GroupRail", row, GroupColor(chain[level].id)); OverviewUi.Place(rail, x, y, width, h);
            x += width + RailGap;
        }
        return x - RailGap;
    }

    /// <summary>Every group the list should show a header for, in tree order: groups with visible members, their
    /// ancestors (so a subtree is never orphaned), and in author mode the empty ones too.</summary>
    private List<string> HeaderOrder(IReadOnlyList<OverviewMod> layoutRows)
    {
        var used = new HashSet<string>(layoutRows.Select(m => m.Rule.groupId ?? ""), StringComparer.Ordinal);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in used.Where(Groups.Contains)) foreach (var group in Groups.Chain(id)) wanted.Add(group.id);
        // Every group shows, empty ones included (2.3.3): a group just created, or one whose mods all moved into its
        // subgroups, used to vanish outside edit mode, so a new subgroup looked like it had not been created at all.
        // A filter or search still shows only the groups that hold a match.
        if (Tags.Count == 0 && _searchQuery.Length == 0 && _relatedMods == null) foreach (var group in Groups.Flat) wanted.Add(group.id);
        var order = Groups.Flat.Where(g => wanted.Contains(g.id)).Select(g => g.id).ToList();
        // Ungrouped (and ids of deleted groups) last, once.
        if (used.Any(id => !Groups.Contains(id))) order.Add("");
        return order;
    }

    /// <summary>2.3.1: the menu edits change State in place before saving; a failed save puts everything back so the
    /// list never shows (and a later full save never commits) an edit the user was told had failed.</summary>
    private Action GroupEditCheckpoint()
    {
        var state = _repo.State;
        var groups = state.groups.Select(g => (Group: g, g.parentId, g.order)).ToList();
        var list = state.groups.ToList();
        var rules = state.mods.Select(r => (Rule: r, r.groupId, r.order)).ToList();
        var ruleList = state.mods.ToList();
        var collapsed = state.collapsedGroups.ToList();
        var rows = _mods.Select(m => (Mod: m, m.Rule)).ToList();
        return () =>
        {
            state.groups.Clear(); state.groups.AddRange(list);
            foreach (var (group, parentId, order) in groups) { group.parentId = parentId; group.order = order; }
            state.mods.Clear(); state.mods.AddRange(ruleList);
            foreach (var (rule, groupId, order) in rules) { rule.groupId = groupId; rule.order = order; }
            state.collapsedGroups.Clear(); state.collapsedGroups.AddRange(collapsed);
            foreach (var (mod, rule) in rows) mod.Rule = rule;
            InvalidateGroupTree(); _pump.Enqueue(Render);
        };
    }

    private void ShowGroupMenu(OverviewGroup? group, PointerEventData data)
    {
        HideAllHints();
        OverviewUi.PointerDropdown(data.position, data.pressEventCamera, group == null ? T("Ungrouped", "未分组") : Groups.Path(group.id), GroupMenuEntries(group).ToArray());
    }
    /// <summary>Right-click on a group heading; 1.1.0: also RT+Y on a gamepad.</summary>
    private List<UiMenuEntry> GroupMenuEntries(OverviewGroup? group)
    {
        var entries = new List<UiMenuEntry>();
        if (group == null)
        {
            entries.Add(new UiMenuEntry { Label = T("New group", "新建分组"), Role = UiActionRole.Edit, Invoke = () => NewGroupIn("") });
            return entries;
        }
        bool deep = Groups.Depth(group.id) >= GroupTree.MaxDepth;
        entries.Add(new UiMenuEntry { Label = T("New subgroup", "新建子分组"), Role = UiActionRole.Edit, Enabled = !deep,
            DisabledReason = T("Groups nest at most three levels.", "分组最多嵌套三层。"), Invoke = () => NewGroupIn(group.id) });
        entries.Add(new UiMenuEntry { Label = T("Edit group", "编辑分组"), Role = UiActionRole.Neutral, Invoke = () => EditGroup(group) });
        entries.Add(new UiMenuEntry { Label = T("Move to top level", "移到顶层"), Role = UiActionRole.Neutral, Enabled = Groups.Parent(group.id).Length > 0,
            DisabledReason = T("Already a top-level group.", "已经是顶层分组。"), Invoke = () => AttachGroup(group.id, "") });
        entries.Add(new UiMenuEntry { Label = T("Delete group", "删除分组"), Role = UiActionRole.Danger, Invoke = () => ConfirmDeleteGroup(group) });
        return entries;
    }

    private void ConfirmDeleteGroup(OverviewGroup group)
    {
        int children = Groups.Children(group.id).Count;
        string where = Groups.Parent(group.id).Length == 0 ? T("Ungrouped", "未分组") : Groups.Path(Groups.Parent(group.id));
        OverviewUi.ConfirmText(T("Delete group", "删除分组") + " · " + group.name,
            T("Its mods", "它的模组") + (children > 0 ? T(" and its subgroups", "和子分组") : "") + T(" move up to ", "会上移到「") + where + T(".", "」。") + "\n" + T("No files are changed.", "不会改动任何文件。"),
            () =>
            {
                var undo = GroupEditCheckpoint();
                try
                {
                    _repo.EnsureLocalPackCopy();
                    GroupTree.Remove(_repo.State.groups, _repo.State.mods, group.id, _repo.State.collapsedGroups);
                    foreach (var mod in _mods) mod.Rule = _repo.GetRule(mod.Guid);
                    _repo.SavePackInformation(ConfigurationSaveScope.Groups);
                    InvalidateGroupTree(); _pump.Enqueue(Render);
                    _status(T("Group deleted; its contents moved up one level.", "分组已删除，内容已上移一层。"));
                }
                catch (Exception ex) { undo(); _status(T("Unable to save group: ", "分组保存失败：") + Loc.LocalizeMessage(ex.Message)); }
            }, UiActionRole.Danger, T("Delete", "删除"));
    }

    private void AttachGroup(string childId, string parentId)
    {
        var undo = GroupEditCheckpoint();
        try
        {
            _repo.EnsureLocalPackCopy();
            if (!GroupTree.Attach(_repo.State.groups, childId, parentId, out string reason))
            {
                _status(reason == "depth" ? T("Groups nest at most three levels; this move would go deeper.", "分组最多嵌套三层，这样移动会超过三层。")
                    : reason == "cycle" ? T("A group cannot go inside its own subgroup.", "分组不能放进它自己的子分组里。")
                    : T("Group not found.", "找不到该分组。"));
                return;
            }
            _repo.SavePackInformation(ConfigurationSaveScope.Groups);
            InvalidateGroupTree(); _pump.Enqueue(Render);
            _status(parentId.Length == 0 ? T("Moved to the top level.", "已移到顶层。") : T("Now a subgroup of ", "已挂到「") + Groups.Path(parentId) + T(".", "」下。"));
        }
        catch (Exception ex) { undo(); _status(T("Unable to save group: ", "分组保存失败：") + Loc.LocalizeMessage(ex.Message)); }
    }

    private void BeginGroupDrag(OverviewGroup group)
    {
        if (!_authorMode) return;
        _draggedGroup = group; HideAllHints(); _groupDropStatus = "";
        _dragCaption.text = Groups.Path(group.id);
        _dragGhost.gameObject.SetActive(true); _dragGhost.SetAsLastSibling();
        _status(GroupDragIntro);
    }
    private string GroupDragIntro => T("Drop on another group heading: top or bottom edge = before / after it (same level), middle = make it a subgroup.",
        "拖到另一个分组标题上松开：靠上缘 / 下缘 = 排到它前面 / 后面（同一层），中间 = 成为它的子分组。",
        "拖到另一個分組標題上放開：靠上緣 / 下緣 = 排到它前面 / 後面（同一層），中間 = 成為它的子分組。");

    private void EndGroupDrag() { _draggedGroup = null; _groupDropStatus = ""; _dragGhost.gameObject.SetActive(false); _dropLine.gameObject.SetActive(false); _dropHighlight.gameObject.SetActive(false); }

    // ── 分组标题的拖放落点（实测修正，用户 2026-11-16）──────────────────────────
    // 原来拖到另一个分组标题上只会挂成它的子分组。现在按落点分：标题上 1/4 = 插到它前面、下 1/4 = 插到它后面（与它同层，
    // GroupTree.PlaceBeside），中间 = 成为它的子分组（GroupTree.Attach）。上 / 下缘画蓝线（按目标的层级缩进），中间给整条标题罩半透明蓝色；
    // 状态行同步写「松开后……」。放不下（超过三层、放进自己的子树）时什么都不画，状态行写原因。「未分组」标题不接受分组。
    private string _groupDropStatus = "";

    /// <summary>指针在分组标题里的落点（<see cref="GroupSteps.Zone"/>）；换算不了时按中间算。</summary>
    private static GroupSteps.DropZone ZoneOnHeader(RectTransform row, Vector2 screen, Camera? camera)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(row, screen, camera, out var local) || row.rect.height <= 0) return GroupSteps.DropZone.Inside;
        return GroupSteps.Zone(Mathf.Clamp01((row.rect.yMax - local.y) / row.rect.height));
    }

    /// <summary>松开时的落点：按此刻指针在该分组标题里的位置算（OnDrop 不带坐标）。</summary>
    private GroupSteps.DropZone GroupDropZoneFor(string groupId)
    {
        foreach (var area in _dropAreas)
            if (area.Value.Before == null && area.Value.Group == groupId && area.Key != null && area.Key.gameObject.activeInHierarchy)
                return ZoneOnHeader(area.Key, PointerPosition, EventCamera);
        return GroupSteps.DropZone.Inside;
    }

    /// <summary>拖分组标题时的提示（由 MoveDragFeedback 调用）：蓝线 = 同层前 / 后，蓝色罩 = 成为子分组。</summary>
    private void GroupDragFeedback(PointerEventData data, Camera? camera)
    {
        var dragged = _draggedGroup; if (dragged == null) return;
        RectTransform? row = null; string target = "";
        foreach (var area in _dropAreas)
        {
            if (area.Value.Before != null || area.Key == null || !area.Key.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(area.Key, data.position, camera)) continue;
            row = area.Key; target = area.Value.Group; break;
        }
        string message = GroupDragIntro;
        if (row != null && Groups.Contains(target) && target != dragged.id)
        {
            var zone = ZoneOnHeader(row, data.position, camera);
            string name = Groups.Get(target)?.name ?? "";
            string parent = zone == GroupSteps.DropZone.Inside ? target : Groups.Parent(target);
            if (!Groups.CanAttach(dragged.id, parent, out string reason))
                message = GroupMoveReason(reason);
            else if (zone == GroupSteps.DropZone.Inside)
            {
                OverviewUi.Place(_dropHighlight, row.anchoredPosition.x, -row.anchoredPosition.y, row.rect.width, row.rect.height);
                _dropHighlight.SetAsLastSibling(); _dropHighlight.gameObject.SetActive(true);
                message = Loc.F("Release to make it a subgroup of \"{0}\".", "松开后成为「{0}」的子分组。", "放開後成為「{0}」的子分組。", name);
            }
            else
            {
                bool after = zone == GroupSteps.DropZone.After;
                float indent = (Math.Max(1, Groups.Depth(target)) - 1) * GroupIndent, top = -row.anchoredPosition.y;
                float lineY = after ? top + row.rect.height - 1 : Mathf.Max(0, top - 2);
                OverviewUi.Place(_dropLine, 4 + indent, lineY, Mathf.Max(30, _viewport.rect.width - 8 - indent), 3);
                _dropLine.SetAsLastSibling(); _dropLine.gameObject.SetActive(true);
                message = after ? Loc.F("Release to place it after \"{0}\" (same level).", "松开后排到「{0}」后面（同一层）。", "放開後排到「{0}」後面（同一層）。", name)
                    : Loc.F("Release to place it before \"{0}\" (same level).", "松开后排到「{0}」前面（同一层）。", "放開後排到「{0}」前面（同一層）。", name);
            }
        }
        if (message == _groupDropStatus) return;
        _groupDropStatus = message; _status(message);
    }

    private string GroupMoveReason(string reason) => reason == "depth" ? T("Groups nest at most three levels; it cannot go here.", "分组最多嵌套三层，不能放在这里。", "分組最多巢狀三層，不能放在這裡。")
        : reason == "cycle" ? T("A group cannot go inside its own subgroup.", "分组不能放进它自己的子分组里。", "分組不能放進它自己的子分組裡。")
        : T("Group not found.", "找不到该分组。", "找不到該分組。");

    /// <summary>分组标题松开在另一个分组标题上：按落点插到前 / 后（同层）或挂成子分组。</summary>
    private void DropGroupOn(string id, string targetId, GroupSteps.DropZone zone)
    {
        if (zone == GroupSteps.DropZone.Inside) { AttachGroup(id, targetId); return; }
        bool after = zone == GroupSteps.DropZone.After;
        var undo = GroupEditCheckpoint();
        try
        {
            _repo.EnsureLocalPackCopy();
            if (!GroupTree.PlaceBeside(_repo.State.groups, id, targetId, after, out string reason)) { _status(GroupMoveReason(reason)); return; }
            _repo.SavePackInformation(ConfigurationSaveScope.Groups);
            InvalidateGroupTree(); _pump.Enqueue(Render);
            string name = Groups.Get(targetId)?.name ?? "";
            _status(after ? Loc.F("Moved after \"{0}\".", "已排到「{0}」后面。", "已排到「{0}」後面。", name)
                : Loc.F("Moved before \"{0}\".", "已排到「{0}」前面。", "已排到「{0}」前面。", name)); // 整句带参数：词条表以英文为键，片段会串味
        }
        catch (Exception ex) { undo(); _status(T("Unable to save group: ", "分组保存失败：", "分組保存失敗：") + ex.Message); return; }
        _headersChanged();
    }
}
