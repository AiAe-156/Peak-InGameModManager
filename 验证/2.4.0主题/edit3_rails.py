import io, sys
SRC = sys.argv[1]

def edit(p, pairs):
    p = SRC + '/' + p
    s = io.open(p, encoding='utf-8').read()
    for a, b in pairs:
        n = s.count(a)
        assert n == 1, (p, a[:70], n)
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

G = 'ModConfigSidebarWidgets.Groups.cs'
edit(G, [
# member row own rail
('''            widgets.GroupRail.color = _groupColors[widgets.GroupId];
            RectTransform rect = widgets.GroupRail.rectTransform;
            float left = pinned ? PinnedRailLeft : 0f;
            // Ancestors' rails sit to the left of the own-group rail, like the overview list.
            List<OverviewGroup> chain = _groupTree.Chain(widgets.GroupId);
            int ancestors = Math.Max(0, chain.Count - 1);
            float ownLeft = left + ancestors * (RailWidth + 1f);
            rect.offsetMin = new Vector2(ownLeft, rect.offsetMin.y);
            rect.offsetMax = new Vector2(ownLeft + RailWidth, rect.offsetMax.y);
            SyncAncestorRails(widgets, chain, left);''',
'''            widgets.GroupRail.color = _groupColors[widgets.GroupId];
            float left = pinned ? PinnedRailLeft : 0f;
            // Ancestors' rails sit to the left of the own-group rail, like the overview list. Each deeper level is
            // thinner and shorter (bottom-aligned), see GroupRailShape.
            List<OverviewGroup> chain = _groupTree.Chain(widgets.GroupId);
            int ancestors = Math.Max(0, chain.Count - 1);
            GroupRailShape.Stretch(widgets.GroupRail.rectTransform, GroupRailShape.X(left, RailWidth, 1f, ancestors),
                GroupRailShape.Width(RailWidth, ancestors), 4f, ancestors);
            SyncAncestorRails(widgets, chain, left);'''),
('''                rail.color = GroupColorOf(chain[i].id);
                RectTransform rect = rail.rectTransform; float x = left + i * (RailWidth + 1f);
                rect.offsetMin = new Vector2(x, widgets.GroupRail.rectTransform.offsetMin.y);
                rect.offsetMax = new Vector2(x + RailWidth, widgets.GroupRail.rectTransform.offsetMax.y);''',
'''                rail.color = GroupColorOf(chain[i].id);
                GroupRailShape.Stretch(rail.rectTransform, GroupRailShape.X(left, RailWidth, 1f, i), GroupRailShape.Width(RailWidth, i), 4f, i);'''),
('''                RectTransform railRect = rail.rectTransform;
                float x = level * (RailWidth + 1f);
                railRect.offsetMin = new Vector2(x, 3f);
                railRect.offsetMax = new Vector2(x + RailWidth, -3f);''',
'''                GroupRailShape.Stretch(rail.rectTransform, GroupRailShape.X(0f, RailWidth, 1f, level), GroupRailShape.Width(RailWidth, level), 3f, level);'''),
])

L = 'Overview/OverviewListView.Groups.cs'
edit(L, [
('''        if (chain.Count == 0) { var none = OverviewUi.Rail("GroupRail", row, GroupColor(groupId)); OverviewUi.Place(none, 0, 0, RailWidth, height); return RailWidth; }
        float x = 0;
        foreach (var group in chain)
        {
            var rail = OverviewUi.Rail("GroupRail", row, GroupColor(group.id)); OverviewUi.Place(rail, x, 0, RailWidth, height);
            x += RailWidth + RailGap;
        }
        return x - RailGap;''',
'''        if (chain.Count == 0) { var none = OverviewUi.Rail("GroupRail", row, GroupColor(groupId)); OverviewUi.Place(none, 0, 0, RailWidth, height); return RailWidth; }
        float x = 0;
        // Each deeper level is thinner and shorter, bottom-aligned (GroupRailShape).
        for (int level = 0; level < chain.Count; level++)
        {
            float width = GroupRailShape.Width(RailWidth, level); var (y, h) = GroupRailShape.Vertical(0, height, level);
            var rail = OverviewUi.Rail("GroupRail", row, GroupColor(chain[level].id)); OverviewUi.Place(rail, x, y, width, h);
            x += width + RailGap;
        }
        return x - RailGap;'''),
])

H = 'Overview/OverviewHotkeyView.cs'
edit(H, [
('''float railX=0;foreach(var level in group==null?new List<OverviewGroup>():tree.Chain(id)){var rail=OverviewUi.Rail("GroupRail",row,ColorUtility.TryParseHtmlString(level.color,out var lc)?lc:OverviewUi.Muted);OverviewUi.Place(rail,railX,0,OverviewListView.RailWidth,depth<=1?34:30);railX+=OverviewListView.RailWidth+OverviewListView.RailGap;}''',
'''float railX=0;int railLevel=0;foreach(var level in group==null?new List<OverviewGroup>():tree.Chain(id)){var rail=OverviewUi.Rail("GroupRail",row,ColorUtility.TryParseHtmlString(level.color,out var lc)?lc:OverviewUi.Muted);float rw=GroupRailShape.Width(OverviewListView.RailWidth,railLevel);var (ry,rh)=GroupRailShape.Vertical(0,depth<=1?34:30,railLevel);OverviewUi.Place(rail,railX,ry,rw,rh);railX+=rw+OverviewListView.RailGap;railLevel++;}'''),
])

S = 'Overview/OverviewPage.SmartGrouping.cs'
edit(S, [
('''foreach (var level in tree.Chain(group.id)) { var rail = OverviewUi.Rail("Rail", row, Parse(level.color)); OverviewUi.Place(rail, railX, 0, OverviewListView.RailWidth, height); railX += OverviewListView.RailWidth + OverviewListView.RailGap; }''',
'''int railLevel = 0; foreach (var level in tree.Chain(group.id)) { var rail = OverviewUi.Rail("Rail", row, Parse(level.color)); float rw = GroupRailShape.Width(OverviewListView.RailWidth, railLevel); var (ry, rh) = GroupRailShape.Vertical(0, height, railLevel); OverviewUi.Place(rail, railX, ry, rw, rh); railX += rw + OverviewListView.RailGap; railLevel++; }'''),
])
print('ok')
