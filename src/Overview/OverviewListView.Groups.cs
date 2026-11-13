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

    /// <summary>Tree position of a row's group; members of a group sort before its child groups because a group's
    /// position is smaller than every descendant's.</summary>
    private int GroupSortKey(string groupId) => Groups.Position(groupId);

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
        _draggedGroup = group; HideAllHints();
        _dragCaption.text = Groups.Path(group.id);
        _dragGhost.gameObject.SetActive(true); _dragGhost.SetAsLastSibling();
        _status(T("Drop on another group heading to make it a subgroup there.", "拖到另一个分组标题上松开，即可挂到它下面。"));
    }

    private void EndGroupDrag() { _draggedGroup = null; _dragGhost.gameObject.SetActive(false); _dropLine.gameObject.SetActive(false); }
}
