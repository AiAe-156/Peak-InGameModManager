from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')

# Management mod selector: tree order, indented headers with path tooltip, subtree-aware fold.
M = 'ManagementModSelector.cs'
sub(M, '''        foreach (var group in filtered.GroupBy(m => m.Rule.groupId).OrderBy(g => _repo.State.groups.FirstOrDefault(p => p.id == g.Key)?.order ?? int.MaxValue).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            string id = group.Key; var definition = _repo.State.groups.FirstOrDefault(g => g.id == id);
            string name = definition?.name ?? Loc.T("Ungrouped", "未分组", "未分組");''',
'''        var tree = new GroupTree(_repo.State.groups);
        foreach (var group in filtered.GroupBy(m => tree.Contains(m.Rule.groupId) ? m.Rule.groupId : "").OrderBy(g => tree.Position(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            string id = group.Key; var definition = tree.Get(id);
            if (_query.Length == 0 && id.Length > 0 && tree.HeaderHidden(id, _collapsed)) continue;
            string name = definition == null ? Loc.T("Ungrouped", "未分组", "未分組") : tree.Path(id);''')

# Log window: tree order, path label.
W = 'LogWindow.cs'
sub(W, '''        foreach (var group in entries.GroupBy(e => e.Group).OrderBy(g => _repo.State.groups.FirstOrDefault(x => x.id == g.Key)?.order ?? int.MaxValue))
        {
            string id = group.Key; var info = _repo.State.groups.FirstOrDefault(g => g.id == id);
            string name = info?.name ?? T("Runtime / other sources", "运行时／其他来源");''',
'''        var tree = new GroupTree(_repo.State.groups);
        foreach (var group in entries.GroupBy(e => e.Group).OrderBy(g => tree.Position(g.Key)))
        {
            string id = group.Key; var info = tree.Get(id);
            string name = info == null ? T("Runtime / other sources", "运行时／其他来源") : tree.Path(id);''')

# Dependency hint: tree order, path label.
D = 'OverviewDependencyHints.Components.cs'
sub(D, '''            foreach (var group in branches.GroupBy(b => Group(b[0]))
                .OrderBy(g => _repo.State.groups.FirstOrDefault(s => s.id == g.Key)?.order ?? int.MaxValue))
            {
                string title = group.Key == "@infrastructure" ? InfrastructureOverviewModel.GroupTitle
                    : group.Key.Length > 0 ? _repo.State.groups.FirstOrDefault(g => g.id == group.Key)?.name ?? Loc.T("Ungrouped", "未分组", "未分組")''',
'''            var tree = new GroupTree(_repo.State.groups);
            foreach (var group in branches.GroupBy(b => Group(b[0]))
                .OrderBy(g => tree.Position(g.Key)))
            {
                string title = group.Key == "@infrastructure" ? InfrastructureOverviewModel.GroupTitle
                    : group.Key.Length > 0 ? (tree.Contains(group.Key) ? tree.Path(group.Key) : Loc.T("Ungrouped", "未分组", "未分組"))''')

# Check progress: callers pass tree position as Order and the path as Name (see OverviewListView capture).
L = 'OverviewListView.cs'
sub(L, '''                _repo.State.groups.Select(g => (g.id, g.name, g.order)),''',
'''                new GroupTree(_repo.State.groups) is var progressTree ? progressTree.Flat.Select(g => (g.id, progressTree.Path(g.id), progressTree.Position(g.id))) : Enumerable.Empty<(string, string, int)>(),''')

# Change review: group label = path, order = tree position.
R = 'ProfileChangeReview.cs'
sub(R, '''        string GroupName(string id) => State.groups.FirstOrDefault(g => g.id == id)?.name ?? previous.state.groups.FirstOrDefault(g => g.id == id)?.name ?? Loc.T("Ungrouped", "未分组", "未分組");''',
'''        var currentTree = new GroupTree(State.groups); var previousTree = new GroupTree(previous.state.groups);
        string GroupName(string id) => currentTree.Contains(id) ? currentTree.Path(id) : previousTree.Contains(id) ? previousTree.Path(id) : Loc.T("Ungrouped", "未分组", "未分組");''')
sub(R, '''                GroupOrder = definition?.order ?? int.MaxValue, ModOrder''', '''                GroupOrder = currentTree.Position(group), ModOrder''')
# Parent change shows names rather than ids.
sub(R, '''            else Fields(ProfileReviewCategory.Groups, "", old, target, groupFields, id);''',
'''            else
            {
                Fields(ProfileReviewCategory.Groups, "", old, target, groupFields.Where(f => f.Item1 != "parentId").ToArray(), id);
                if ((old.parentId ?? "") != (target.parentId ?? ""))
                    Add(ProfileReviewCategory.Groups, "", Loc.T("Parent group", "上级分组", "上級分組"),
                        (old.parentId ?? "").Length == 0 ? Loc.T("Top level", "顶层", "頂層") : GroupName(old.parentId!),
                        (target.parentId ?? "").Length == 0 ? Loc.T("Top level", "顶层", "頂層") : GroupName(target.parentId!), "", id);
            }''')

# Store install group picker: tree order, indented names.
S = 'ShopWindow.cs'
sub(S, '''        foreach(var group in _repo.State.groups.OrderBy(g=>g.order)){ColorUtility.TryParseHtmlString(group.color,out var tint);Item(group.name,group.id,tint);}rows.sizeDelta=new Vector2(rows.rect.width,y);''',
'''        var tree=new GroupTree(_repo.State.groups);
        foreach(var group in tree.Flat){ColorUtility.TryParseHtmlString(group.color,out var tint);Item(new string(' ',(tree.Depth(group.id)-1)*3)+(tree.Depth(group.id)>1?"└ ":"")+group.name,group.id,tint);}rows.sizeDelta=new Vector2(rows.rect.width,y);''')
print('ok')
