from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
L = 'OverviewListView.cs'

# Tree order instead of flat group order; collapsed ancestors hide rows.
sub(L, '.OrderBy(m => GroupOrder(m.Rule.groupId)).ThenBy(m => m.Rule.order)',
       '.OrderBy(m => GroupSortKey(m.Rule.groupId)).ThenBy(m => m.Rule.order)')
sub(L, '!_repo.State.collapsedGroups.Contains(m.Rule.groupId)).ToList();',
       '!RowHiddenByCollapse(m.Rule.groupId)).ToList();')
# Rebuild the tree at the start of every render.
sub(L, '            RefreshRowData();\n            float width = Width; _lastWidth = width;',
       '            RefreshRowData(); InvalidateGroupTree();\n            float width = Width; _lastWidth = width;')

# Header emission: walk the header order, emitting every header up to and including the row's group.
sub(L, '''string? currentGroup = null; int index = 0;
            foreach (OverviewMod mod in layoutRows)
            {
                string groupId = mod.Rule.groupId;
                if (groupId != currentGroup)
                { currentGroup = groupId; y += y > 0 ? GroupGapBefore : 2; BuildGroupHeader(groupId, y, width); y += GroupHeight + GroupGapAfter; }
                if (_relatedMods == null && Tags.Count == 0 && _searchQuery.Length == 0 && _repo.State.collapsedGroups.Contains(groupId)) continue;''',
'''int index = 0;
            var headers = HeaderOrder(layoutRows); int nextHeader = 0;
            bool collapseApplies = _relatedMods == null && Tags.Count == 0 && _searchQuery.Length == 0;
            void EmitHeader(string id)
            {
                // A header under a collapsed ancestor is folded away with its parent.
                if (collapseApplies && Groups.HeaderHidden(id, _repo.State.collapsedGroups)) return;
                int depth = Math.Max(1, Groups.Depth(id));
                y += y > 0 ? GroupGapBefore : 2; BuildGroupHeader(id, y, width, layoutRows); y += HeaderHeight(depth) + GroupGapAfter;
            }
            void EmitHeadersThrough(string id)
            {
                string key = Groups.Contains(id) ? id : "";
                int at = headers.IndexOf(key, nextHeader);
                if (at < 0) return;
                for (; nextHeader <= at; nextHeader++) EmitHeader(headers[nextHeader]);
            }
            foreach (OverviewMod mod in layoutRows)
            {
                string groupId = mod.Rule.groupId;
                EmitHeadersThrough(groupId);
                if (collapseApplies && RowHiddenByCollapse(groupId)) continue;''')

# Member rows: rails for every level.
sub(L, '''                Color color = GroupColor(groupId); var rail = OverviewUi.Panel("GroupRail", row, color); OverviewUi.Place(rail, 0, 0, 3, RowHeight);''',
'''                DrawRails(row, groupId, RowHeight);''')

# Remaining (empty, author-mode) headers after the last row, in tree order.
sub(L, '''            if (_authorMode && Tags.Count == 0 && _searchQuery.Length == 0)
                foreach (var empty in _repo.State.groups.OrderBy(g => g.order).Where(g => !layoutRows.Any(m => m.Rule.groupId == g.id)))
                { y += GroupGapBefore; BuildGroupHeader(empty.id, y, width); y += GroupHeight + GroupGapAfter; }''',
'''            for (; nextHeader < headers.Count; nextHeader++) EmitHeader(headers[nextHeader]);''')

# Header builder: indent, rails per level, subtree count, right-click menu, header drag.
old_start = '    private void BuildGroupHeader(string groupId, float y, float width)\n'
p = root / L; s = p.read_text(encoding='utf-8')
a = s.index(old_start); b = s.index('    private void Select(OverviewMod mod, int index, PointerEventData data)')
new_header = '''    private void BuildGroupHeader(string groupId, float y, float width, IReadOnlyList<OverviewMod> layoutRows)
    {
        OverviewGroup? group = Groups.Get(groupId);
        int depth = Math.Max(1, Groups.Depth(groupId));
        float height = HeaderHeight(depth), font = HeaderFont(depth);
        Color groupTint = GroupColor(groupId);
        int count = group == null ? layoutRows.Count(m => !Groups.Contains(m.Rule.groupId)) : Groups.SubtreeCount(groupId, layoutRows, m => m.Rule.groupId);
        AddVirtualRow("group:"+groupId, "Group_"+groupId, y, height, width-15, row => {
        row.GetComponent<Image>().color=Color.Lerp(new Color(.1f,.11f,.12f,.95f), new Color(groupTint.r,groupTint.g,groupTint.b,.95f), depth <= 1 ? .14f : .09f);
        _dropAreas[row] = (groupId, null);
        float rails = DrawRails(row, groupId, height);
        float indent = (depth - 1) * GroupIndent, left = Mathf.Max(rails + 4, indent + 7);
        bool collapsed = Tags.Count == 0 && _repo.State.collapsedGroups.Contains(groupId);
        var arrow = OverviewUi.IconButton(row, collapsed ? "triangle-right" : "triangle-down", () => { if (!_repo.State.collapsedGroups.Remove(groupId)) _repo.State.collapsedGroups.Add(groupId); _repo.SaveUiState(); _selected.Clear(); _anchor = -1; Render(); }, 22, 26); OverviewUi.Place((RectTransform)arrow.transform, left, (height - 26) / 2, 22, 26);
        string groupName = group?.name ?? T("Ungrouped", "未分组");
        float captionX = left + 28, titleWidth = Mathf.Min(width * .45f, Measure(groupName, font) + 12);
        var caption = Label(row, EscapeRich(groupName), captionX, 0, titleWidth, height, font); caption.fontStyle = FontStyles.Normal; caption.color = groupTint; caption.characterSpacing = depth <= 1 ? 1.5f : 1f; OverviewUi.ReadableName(caption, titleWidth); OverviewUi.PlaceClipped(caption,captionX,0,titleWidth,height); AddHint(caption.gameObject, group == null ? groupName : Groups.Path(groupId)); OverviewEditableFrame.Set(caption.rectTransform, _authorMode);
        var counter = Label(row, count.ToString(), captionX + titleWidth + 6, 0, 60, height, font - 4); counter.color = OverviewUi.Muted; OverviewUi.PlaceClipped(counter, captionX + titleWidth + 6, 0, 60, height);
        float descriptionX = captionX + titleWidth + 6 + Mathf.Min(60, Measure(count.ToString(), font - 4) + 14);
        if (!string.IsNullOrWhiteSpace(group?.description)) { var description = Label(row, group!.description, descriptionX, 0, Mathf.Max(0, width - descriptionX - 21), height, 17); description.color = OverviewUi.Muted; OverviewUi.PlaceClipped(description,descriptionX,0,Mathf.Max(0,width-descriptionX-21),height); AddHint(description.gameObject, group.description); }
        var target = row.gameObject.AddComponent<OverviewRowInteraction>();
        target.NativeScroll = _scroll; target.ForwardScroll = () => !_authorMode || group == null;
        // A group heading dropped here becomes this group's subgroup; dropped mods join this group.
        target.Dropped = () => { if (_draggedGroup != null) { var dragged = _draggedGroup; EndGroupDrag(); if (group != null && dragged.id != groupId) AttachGroup(dragged.id, groupId); } else MoveSelected(groupId, null); };
        if (group != null) { target.DragStarted = () => BeginGroupDrag(group); target.DragMoved = MoveDragFeedback; target.DragFinished = EndGroupDrag; }
        Action<PointerEventData> clicked = data => {
            if (data.button == PointerEventData.InputButton.Right) { ShowGroupMenu(group, data); return; }
            if (data.clickCount >= 2) { if (group != null) EditGroup(group); else ManageGroups(); } };
        target.Clicked = clicked;
        caption.gameObject.AddComponent<OverviewTextClick>().Clicked = clicked;
        });
    }
'''
s = s[:a] + new_header + s[b:]
# GroupOrder no longer used for sorting; keep it routed through the tree so any remaining caller is consistent.
s = s.replace('    private int GroupOrder(string id) => _repo.State.groups.FirstOrDefault(g => g.id == id)?.order ?? int.MaxValue;\n',
              '    private int GroupOrder(string id) => GroupSortKey(id);\n', 1)
p.write_text(s, encoding='utf-8')
print('ok')
