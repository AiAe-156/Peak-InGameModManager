from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
E = 'OverviewGroupEditor.cs'

# Fields: chosen parent, a new-group parent preset.
sub(E, '    private string _color = "#7CC7E6";\n', '    private string _color = "#7CC7E6";\n    private string _parentId = "", _initialParentId = "";\n    private TextMeshProUGUI? _parentCaption;\n    /// <summary>Parent for the next "new group" form, set by "New subgroup" entry points.</summary>\n    internal string NewGroupParent = "";\n')
sub(E, "        || !_initialCustom.SetEquals(_custom) || !_initialChecked.SetEquals(_checked) || _secondaryDirty?.Invoke() == true);",
       "        || !_initialCustom.SetEquals(_custom) || !_initialChecked.SetEquals(_checked) || !string.Equals(_parentId, _initialParentId, StringComparison.Ordinal) || _secondaryDirty?.Invoke() == true);")

# Initial values: parent from the group (or the preset for a new one), colour from the palette for a new one.
sub(E, '''        _group = group;
        _color = NormalizeColor(group?.color ?? "#7CC7E6") ?? "#7CC7E6";''',
'''        _group = group;
        _parentId = group?.parentId ?? NewGroupParent; NewGroupParent = "";
        if (_parentId.Length > 0 && !new GroupTree(_repository.State.groups).Contains(_parentId)) _parentId = "";
        _color = NormalizeColor(group?.color ?? GroupPalette.ForNewGroup(_repository.State.groups, _parentId)) ?? "#7CC7E6";''')

# Rail and grid: tree order with indentation.
sub(E, '''            foreach (var existing in _repository.State.groups.OrderBy(g => g.order))
            {
                var choice = existing;
                var button = OverviewUi.Button(railContent, choice.name, () => RequestChangeGroup(choice), railWidth - 52, 36, choice == group ? UiActionRole.Edit : UiActionRole.Neutral);
                OverviewUi.Place((RectTransform)button.transform, 0, rowY, railWidth - 52, 36);''',
'''            var railTree = new GroupTree(_repository.State.groups);
            foreach (var existing in railTree.Flat)
            {
                var choice = existing;
                float inset = (railTree.Depth(choice.id) - 1) * OverviewListView.GroupIndent;
                var button = OverviewUi.Button(railContent, choice.name, () => RequestChangeGroup(choice), railWidth - 52 - inset, 36, choice == group ? UiActionRole.Edit : UiActionRole.Neutral);
                OverviewUi.Place((RectTransform)button.transform, inset, rowY, railWidth - 52 - inset, 36);''')
sub(E, '''            foreach (var existing in _repository.State.groups.OrderBy(g => g.order))
            {
                var choice = existing;
                var button = OverviewUi.Button(choices, choice.name, () => RequestChangeGroup(choice), 150, 36, role: UiActionRole.Edit);''',
'''            var gridTree = new GroupTree(_repository.State.groups);
            foreach (var existing in gridTree.Flat)
            {
                var choice = existing;
                var button = OverviewUi.Button(choices, gridTree.Path(choice.id), () => RequestChangeGroup(choice), 150, 36, role: UiActionRole.Edit);''')
sub(E, '                var hover = button.gameObject.AddComponent<OverviewHover>(); hover.Enter = () => ShowName(choice.name);',
       '                var hover = button.gameObject.AddComponent<OverviewHover>(); hover.Enter = () => ShowName(gridTree.Path(choice.id));')
sub(E, '''            var create = OverviewUi.Button(rail, T("New group", "新建分组"), () => RequestChangeGroup(null), railWidth - 12, 34, UiActionRole.Commit);''',
'''            var create = OverviewUi.Button(rail, T("New group", "新建分组"), () => { NewGroupParent = ""; RequestChangeGroup(null); }, railWidth - 12, 34, UiActionRole.Commit);''')

# Parent selector right after the name.
sub(E, '''        _name = OverviewUi.Input(content, group?.name ?? ""); _name.characterLimit = 80; Height(_name.gameObject, 45);''',
'''        _name = OverviewUi.Input(content, group?.name ?? ""); _name.characterLimit = 80; Height(_name.gameObject, 45);
        Heading(content, T("Parent group", "上级分组"));
        var parentRow = OverviewUi.Rect("ParentRow", content); Height(parentRow.gameObject, 40);
        var parentButton = OverviewUi.Button(parentRow, "", ChooseParent, 320, 36, UiActionRole.Neutral); OverviewUi.Place((RectTransform)parentButton.transform, 0, 2, 320, 36);
        _parentCaption = parentButton.GetComponentInChildren<TextMeshProUGUI>(true); _parentCaption.overflowMode = TextOverflowModes.Ellipsis; _parentCaption.textWrappingMode = TextWrappingModes.NoWrap;
        var parentNote = OverviewUi.Text(parentRow, T("Groups nest at most three levels.", "分组最多嵌套三层。"), 16); parentNote.color = OverviewUi.Muted; OverviewUi.PlaceClipped(parentNote, 332, 10, 400, 24);
        RefreshParentCaption();''')
sub(E, '        _initialName = _name.text; _initialDescription = _description.text; _initialColor = _color;',
       '        _initialName = _name.text; _initialDescription = _description.text; _initialColor = _color; _initialParentId = _parentId;')

# Commit: write parentId; deleting moves contents up one level; new group order among its siblings.
sub(E, '''                if (remove)
                {
                    _repository.State.groups.RemoveAll(g => g.id == groupId);
                    foreach (var rule in _repository.State.mods.Where(r => r.groupId == groupId)) rule.groupId = "";
                    foreach (var mod in _mods.Where(m => m.Rule.groupId == groupId)) mod.Rule.groupId = "";
                }''',
'''                if (remove)
                {
                    // Subgroups and members move up one level (to the deleted group's parent; ungrouped at the top).
                    string up = new GroupTree(_repository.State.groups).Parent(groupId);
                    GroupTree.Remove(_repository.State.groups, _repository.State.mods, groupId, _repository.State.collapsedGroups);
                    foreach (var mod in _mods.Where(m => m.Rule.groupId == groupId)) mod.Rule.groupId = up;
                }''')
sub(E, '''                    if (group == null) { group = new OverviewGroup { id = groupId, order = _repository.State.groups.Count == 0 ? 0 : _repository.State.groups.Max(g => g.order) + 1 }; _repository.State.groups.Add(group); }
                    group.name = name; group.description = _description?.text.Trim() ?? ""; group.color = _color;''',
'''                    if (group == null) { group = new OverviewGroup { id = groupId, parentId = _parentId, order = GroupTree.NextOrder(_repository.State.groups, _parentId) }; _repository.State.groups.Add(group); }
                    else if (!string.Equals(group.parentId ?? "", _parentId, StringComparison.Ordinal) && !GroupTree.Attach(_repository.State.groups, groupId, _parentId, out string reason))
                        throw new InvalidOperationException(ParentReason(reason));
                    group.name = name; group.description = _description?.text.Trim() ?? ""; group.color = _color;''')
sub(E, '''Close(); _saved(); _status(!persist ? T("Group draft retained; save configuration to commit.", "分组草稿已保留，保存配置后提交。") : remove ? T("Group removed; members are ungrouped.", "分组已删除，成员已移到未分组。") : T("Group saved.", "分组已保存。"));''',
'''Close(); _saved(); _status(!persist ? T("Group draft retained; save configuration to commit.", "分组草稿已保留，保存配置后提交。") : remove ? T("Group removed; its contents moved up one level.", "分组已删除，内容已上移一层。") : T("Group saved.", "分组已保存。"));''')

# Move among siblings instead of the global list.
sub(E, '''            var groups = _repository.State.groups.OrderBy(g => g.order).ToList(); int index = groups.FindIndex(g => g.id == id), next = index + offset;
            if (index < 0 || next < 0 || next >= groups.Count) return;
            (groups[index], groups[next]) = (groups[next], groups[index]);
            for (int i = 0; i < groups.Count; i++) groups[i].order = i;
            _repository.SavePackInformation(ConfigurationSaveScope.Groups); _saved(); Show(groups.First(g => g.id == id));''',
'''            if (id == null || !GroupTree.MoveAmongSiblings(_repository.State.groups, id, offset)) return;
            _repository.SavePackInformation(ConfigurationSaveScope.Groups); _saved(); Show(_repository.State.groups.First(g => g.id == id));''')

# Helpers.
sub(E, '    private void RequestChangeGroup(OverviewGroup? group)\n',
'''    private void RefreshParentCaption()
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
''')
print('ok')
