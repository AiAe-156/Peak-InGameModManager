from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
G = 'ModConfigSidebarWidgets.Groups.cs'

# Slot knows its depth and its ancestor chain.
sub(G, '''            internal string Id = "";
            internal string Name = "";
            internal Color Color;''', '''            internal string Id = "";
            internal string Name = "";
            internal Color Color;
            internal int Depth = 1;
            /// <summary>Ancestors first, then this group: used for the rails and to fold the header under a collapsed ancestor.</summary>
            internal List<string> Chain = new List<string>();''')
sub(G, '''        private readonly Dictionary<string, Color> _groupColors = new Dictionary<string, Color>(StringComparer.Ordinal);''',
'''        private readonly Dictionary<string, Color> _groupColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        /// <summary>2.3.0 nested groups: the tree the slots were built from.</summary>
        private GroupTree _groupTree = new GroupTree(Array.Empty<OverviewGroup>());''')

# Build headers in tree order; include ancestors of groups that have members.
sub(G, '''                foreach (OverviewGroup group in repository.State.groups.OrderBy(g => g.order))
                {
                    if (_groupColors.ContainsKey(group.id) || !_rows.Any(r => !r.IsSelf && r.GroupId == group.id))
                    {
                        continue;
                    }

                    Color color = ParseGroupColor(group.color);
                    _groupColors[group.id] = color;
                    _groupSlots.Add(MakeGroupHeader(group.id, group.name, group.description ?? "", color));
                }''',
'''                _groupTree = new GroupTree(repository.State.groups);
                var wanted = new HashSet<string>(StringComparer.Ordinal);
                foreach (RowWidgets row in _rows)
                {
                    if (!row.IsSelf && _groupTree.Contains(row.GroupId))
                    {
                        foreach (OverviewGroup member in _groupTree.Chain(row.GroupId)) wanted.Add(member.id);
                    }
                }

                foreach (OverviewGroup group in _groupTree.Flat)
                {
                    if (!wanted.Contains(group.id) || _groupColors.ContainsKey(group.id))
                    {
                        continue;
                    }

                    Color color = ParseGroupColor(group.color);
                    _groupColors[group.id] = color;
                    GroupSlot slot = MakeGroupHeader(group.id, group.name, group.description ?? "", color,
                        _groupTree.Depth(group.id), _groupTree.Chain(group.id).Select(g => g.id).ToList());
                    slot.Tip!.Title = _groupTree.Path(group.id);
                    _groupSlots.Add(slot);
                }''')
sub(G, '''                    _groupSlots.Add(MakeGroupHeader("", Loc.Get("GROUP_UNGROUPED"), "", _groupColors[""]));''',
'''                    _groupSlots.Add(MakeGroupHeader("", Loc.Get("GROUP_UNGROUPED"), "", _groupColors[""], 1, new List<string>()));''')

# Member rows: rails for every level (row rail = own group; extra rails for ancestors).
sub(G, '''            widgets.GroupRail.color = _groupColors[widgets.GroupId];
            RectTransform rect = widgets.GroupRail.rectTransform;
            float left = pinned ? PinnedRailLeft : 0f;
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(left + RailWidth, rect.offsetMax.y);''',
'''            widgets.GroupRail.color = _groupColors[widgets.GroupId];
            RectTransform rect = widgets.GroupRail.rectTransform;
            float left = pinned ? PinnedRailLeft : 0f;
            // Ancestors' rails sit to the left of the own-group rail, like the overview list.
            List<OverviewGroup> chain = _groupTree.Chain(widgets.GroupId);
            int ancestors = Math.Max(0, chain.Count - 1);
            float ownLeft = left + ancestors * (RailWidth + 1f);
            rect.offsetMin = new Vector2(ownLeft, rect.offsetMin.y);
            rect.offsetMax = new Vector2(ownLeft + RailWidth, rect.offsetMax.y);
            SyncAncestorRails(widgets, chain, left);''')

# Counts include the subtree.
sub(G, '''                int count = _rows.Count(r => !IsPinnedRow(r) && (_groupColors.ContainsKey(r.GroupId) ? r.GroupId : "") == slot.Id);''',
'''                HashSet<string>? subtree = slot.Id.Length > 0 ? _groupTree.Subtree(slot.Id) : null;
                int count = _rows.Count(r => !IsPinnedRow(r) && (subtree != null ? subtree.Contains(r.GroupId) : !_groupColors.ContainsKey(r.GroupId) || r.GroupId.Length == 0));''')

# Header: depth indentation and a rail per level.
sub(G, '''        private GroupSlot MakeGroupHeader(string id, string name, string description, Color color)
        {
            GroupSlot slot = new GroupSlot { Id = id, Name = name, Color = color };''',
'''        private GroupSlot MakeGroupHeader(string id, string name, string description, Color color, int depth, List<string> chain)
        {
            GroupSlot slot = new GroupSlot { Id = id, Name = name, Color = color, Depth = Math.Max(1, depth), Chain = chain };
            float indent = (slot.Depth - 1) * 12f;''')
sub(G, '''            Image rail = MakeRail(row.transform, "MCE_GroupRail");
            rail.color = color;
            RectTransform railRect = rail.rectTransform;
            railRect.offsetMin = new Vector2(0f, 3f);
            railRect.offsetMax = new Vector2(RailWidth, -3f);''',
'''            // One rail per level, ancestors first (the overview list draws them the same way).
            for (int level = 0; level < Math.Max(1, chain.Count); level++)
            {
                Image rail = MakeRail(row.transform, "MCE_GroupRail");
                rail.color = level < chain.Count - 1 ? GroupColorOf(chain[level]) : color;
                RectTransform railRect = rail.rectTransform;
                float x = level * (RailWidth + 1f);
                railRect.offsetMin = new Vector2(x, 3f);
                railRect.offsetMax = new Vector2(x + RailWidth, -3f);
            }''')
sub(G, '''            arrowRect.anchoredPosition = new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + 4f, 0f);''',
'''            arrowRect.anchoredPosition = new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + 4f + indent, 0f);''')
sub(G, '''            labelRect.offsetMin = new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + 16f, 0f);''',
'''            labelRect.offsetMin = new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + 16f + indent, 0f);''')
sub(G, '''            text.fontSizeMax = 14f;
            text.fontSize = 14f;''', '''            text.fontSizeMax = slot.Depth <= 1 ? 14f : 13f;
            text.fontSize = slot.Depth <= 1 ? 14f : 13f;''')

# Collapse: a row hides when its group or any ancestor is collapsed; a header hides under a collapsed ancestor.
sub(G, '''                bool hide = wanted && !searching && row != selected && repository.State.collapsedGroups.Contains(group);''',
'''                bool hide = wanted && !searching && row != selected && (group.Length > 0 ? _groupTree.HiddenByCollapse(group, repository.State.collapsedGroups) : repository.State.collapsedGroups.Contains(group));''')
sub(G, '''                if (wanted)
                {
                    shown.Add(group);
                }''', '''                if (wanted)
                {
                    shown.Add(group);
                    // A subgroup's member keeps every ancestor header visible.
                    foreach (OverviewGroup ancestor in _groupTree.Ancestors(group)) shown.Add(ancestor.id);
                }''')
sub(G, '''                bool visible = shown.Contains(slot.Id);
                if (slot.Row.activeSelf != visible)''', '''                bool visible = shown.Contains(slot.Id) && (searching || repository == null || slot.Id.Length == 0 || !_groupTree.HeaderHidden(slot.Id, repository.State.collapsedGroups));
                if (slot.Row.activeSelf != visible)''')

# Helpers: ancestor rails on member rows and colour lookup.
sub(G, '''        private static string EscapeRich(string value) => value.Replace("<", "＜").Replace(">", "＞");''',
'''        private static string EscapeRich(string value) => value.Replace("<", "＜").Replace(">", "＞");

        private Color GroupColorOf(string id) => _groupColors.TryGetValue(id, out Color color) ? color : ParseGroupColor(_groupTree.Get(id)?.color);

        /// <summary>Extra rails to the left of a member row's own rail, one per ancestor group, created on demand.</summary>
        private void SyncAncestorRails(RowWidgets widgets, List<OverviewGroup> chain, float left)
        {
            if (widgets.GroupRail == null) return;
            Transform row = widgets.GroupRail.transform.parent;
            int needed = Math.Max(0, chain.Count - 1);
            for (int i = 0; ; i++)
            {
                Transform? existing = row.Find("MCE_AncestorRail" + i);
                if (i >= needed) { if (existing == null) break; existing.gameObject.SetActive(false); continue; }
                Image rail = existing != null ? existing.GetComponent<Image>() : MakeRail(row, "MCE_AncestorRail" + i);
                rail.gameObject.SetActive(true);
                rail.color = GroupColorOf(chain[i].id);
                RectTransform rect = rail.rectTransform; float x = left + i * (RailWidth + 1f);
                rect.offsetMin = new Vector2(x, widgets.GroupRail.rectTransform.offsetMin.y);
                rect.offsetMax = new Vector2(x + RailWidth, widgets.GroupRail.rectTransform.offsetMax.y);
            }
        }''')
print('ok')
