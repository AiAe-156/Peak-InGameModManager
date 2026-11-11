from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
H = 'OverviewHotkeyView.cs'
sub(H, '''            var visible=Service.Entries.Where(Matches).OrderBy(e=>_repo.State.groups.FirstOrDefault(g=>g.id==e.Mod.Rule.groupId)?.order??int.MaxValue).ThenBy(e=>e.Mod.Rule.order).ThenBy(e=>e.Mod.Name).ThenBy(e=>e.Section).ThenBy(e=>e.Key).ToList();''',
'''            var tree=new GroupTree(_repo.State.groups);
            var visible=Service.Entries.Where(Matches).OrderBy(e=>tree.Position(e.Mod.Rule.groupId)).ThenBy(e=>e.Mod.Rule.order).ThenBy(e=>e.Mod.Name).ThenBy(e=>e.Section).ThenBy(e=>e.Key).ToList();
            bool foldApplies=Tags.Count==0&&_filter==0&&string.IsNullOrWhiteSpace(_query);
            var emitted=new HashSet<string>(StringComparer.Ordinal);''')
old = '''            foreach(var grouping in visible.GroupBy(e=>e.Mod.Rule.groupId))
            {
                string id=grouping.Key; var group=_repo.State.groups.FirstOrDefault(g=>g.id==id);Color color=ColorUtility.TryParseHtmlString(group?.color??"",out var c)?c:OverviewUi.Muted;
                bool folded=Tags.Count==0&&_filter==0&&string.IsNullOrWhiteSpace(_query)&&_repo.State.collapsedGroups.Contains(id);
                var row=OverviewUi.Panel("Group_"+id,_content,Color.Lerp(new Color(.1f,.11f,.12f,.95f),new Color(color.r,color.g,color.b,.95f),.14f));OverviewUi.Place(row,0,y,width,34);
                var arrow=OverviewUi.IconButton(row,folded?"triangle-right":"triangle-down",()=> {if(!_repo.State.collapsedGroups.Remove(id))_repo.State.collapsedGroups.Add(id);_repo.SaveUiState();Render();},22,26);OverviewUi.Place((RectTransform)arrow.transform,6,3,22,26);Track(arrow,"group:"+id);
                string title=group?.name??T("Ungrouped","未分组");float nameWidth=Mathf.Min(width*.42f,Measure(title,20)+10);
                var titleText=Label(row,title,32,0,nameWidth,32,color,20);OverviewUi.ReadableName(titleText,nameWidth);titleText.richText=false;Hint(titleText.gameObject,title); titleText.gameObject.AddComponent<OverviewHotkeyTextAction>().DoubleClicked=()=>EditGroup?.Invoke(group);
                Label(row,group?.description??"",36+nameWidth,0,Math.Max(0,width-nameWidth-42),32,OverviewUi.Muted,14);y+=36;
                if(folded)continue;'''
new = '''            void Header(string id)
            {
                if(!emitted.Add(id))return;
                var group=tree.Get(id);Color color=ColorUtility.TryParseHtmlString(group?.color??"",out var c)?c:OverviewUi.Muted;
                int depth=Math.Max(1,tree.Depth(id));float indent=(depth-1)*OverviewListView.GroupIndent;
                bool folded=foldApplies&&_repo.State.collapsedGroups.Contains(id);
                var row=OverviewUi.Panel("Group_"+id,_content,Color.Lerp(new Color(.1f,.11f,.12f,.95f),new Color(color.r,color.g,color.b,.95f),depth<=1?.14f:.09f));OverviewUi.Place(row,0,y,width,depth<=1?34:30);
                float railX=0;foreach(var level in group==null?new List<OverviewGroup>():tree.Chain(id)){var rail=OverviewUi.Panel("GroupRail",row,ColorUtility.TryParseHtmlString(level.color,out var lc)?lc:OverviewUi.Muted);OverviewUi.Place(rail,railX,0,OverviewListView.RailWidth,depth<=1?34:30);railX+=OverviewListView.RailWidth+OverviewListView.RailGap;}
                var arrow=OverviewUi.IconButton(row,folded?"triangle-right":"triangle-down",()=> {if(!_repo.State.collapsedGroups.Remove(id))_repo.State.collapsedGroups.Add(id);_repo.SaveUiState();Render();},22,26);OverviewUi.Place((RectTransform)arrow.transform,6+indent,depth<=1?3:2,22,26);Track(arrow,"group:"+id);
                string title=group?.name??T("Ungrouped","未分组");float size=depth<=1?20:depth==2?18:17;float nameWidth=Mathf.Min(width*.42f,Measure(title,size)+10);
                var titleText=Label(row,title,32+indent,0,nameWidth,depth<=1?32:28,color,size);OverviewUi.ReadableName(titleText,nameWidth);titleText.richText=false;Hint(titleText.gameObject,group==null?title:tree.Path(id)); titleText.gameObject.AddComponent<OverviewHotkeyTextAction>().DoubleClicked=()=>EditGroup?.Invoke(group);
                Label(row,group?.description??"",36+indent+nameWidth,0,Math.Max(0,width-nameWidth-indent-42),depth<=1?32:28,OverviewUi.Muted,14);y+=depth<=1?36:32;
            }
            foreach(var grouping in visible.GroupBy(e=>tree.Contains(e.Mod.Rule.groupId)?e.Mod.Rule.groupId:""))
            {
                string id=grouping.Key;
                // Every ancestor header first, so a subgroup is never shown without its parent; a header under a collapsed ancestor folds away.
                bool hiddenByAncestor=foldApplies&&id.Length>0&&tree.HeaderHidden(id,_repo.State.collapsedGroups);
                if(hiddenByAncestor)continue;
                foreach(var ancestor in tree.Ancestors(id))Header(ancestor.id);
                Header(id);
                if(foldApplies&&_repo.State.collapsedGroups.Contains(id))continue;'''
sub(H, old, new)
sub(H, '''        _repo.State.collapsedGroups.Remove(entry.Mod.Rule.groupId); _repo.State.collapsedHotkeyMods.Remove(entry.Mod.Id);''',
'''        foreach (var level in new GroupTree(_repo.State.groups).Chain(entry.Mod.Rule.groupId)) _repo.State.collapsedGroups.Remove(level.id);
        _repo.State.collapsedGroups.Remove(entry.Mod.Rule.groupId); _repo.State.collapsedHotkeyMods.Remove(entry.Mod.Id);''')
print('ok')
