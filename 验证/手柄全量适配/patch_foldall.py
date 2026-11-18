# -*- coding: utf-8 -*-
"""1.1.3 mod list: expand/collapse all button + LT+RB / LT+LB."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', 'src', 'Overview'))

def edit(name, pairs):
    p = os.path.join(ROOT, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('OverviewListView.cs', [
('''        var note=Label(_tools,enablementNote,SearchWidth+18,0,Mathf.Max(40,width-SearchWidth-218),32,15);note.color=OverviewUi.Muted;AddHint(note.gameObject,enablementNote);''',
'''        // 1.1.3 (user): one button to expand / collapse every group (controller: LT + RB / LT + LB).
        _foldAllButton=OverviewUi.IconButton(_tools,"triangle-down",()=>SetAllFolded(null),30,30);
        OverviewUi.Place((RectTransform)_foldAllButton.transform,SearchWidth+14,1,30,30);
        AddHint(_foldAllButton.gameObject,Loc.T("Expand or collapse all groups. Controller: LT + RB expands all, LT + LB collapses all.","展开或收起全部分组。手柄：LT + RB 全部展开，LT + LB 全部收起。","展開或收起全部分組。手把：LT + RB 全部展開，LT + LB 全部收起。"));
        RefreshFoldAllIcon();
        var note=Label(_tools,enablementNote,SearchWidth+52,0,Mathf.Max(40,width-SearchWidth-252),32,15);note.color=OverviewUi.Muted;AddHint(note.gameObject,enablementNote);'''),
])
edit('OverviewListView.Gamepad.cs', [
('''    private void FoldAll()
    {''', '''    private Button? _foldAllButton;
    /// <summary>Expand (true) / collapse (false) every group, or flip: expand if anything is folded.</summary>
    internal void SetAllFolded(bool? expand)
    {
        var all = _repo.State.groups.Select(g => g.id).ToList();
        bool open = expand ?? all.Any(_repo.State.collapsedGroups.Contains) || (expand == null && _infrastructureCollapsed);
        _repo.State.collapsedGroups.Clear();
        if (!open) _repo.State.collapsedGroups.AddRange(all);
        _infrastructureCollapsed = !open;
        _repo.SaveUiState(); _selected.Clear(); _anchor = -1; RenderFold(); RefreshFoldAllIcon();
        _status(open ? Loc.T("All groups expanded.", "已展开全部分组。", "已展開全部分組。") : Loc.T("All groups collapsed.", "已收起全部分组。", "已收起全部分組。"));
    }
    private void RefreshFoldAllIcon()
    {
        if (_foldAllButton == null) return;
        bool anyFolded = _repo.State.groups.Any(g => _repo.State.collapsedGroups.Contains(g.id));
        var icon = _foldAllButton.GetComponentInChildren<OverviewIconGraphic>(true);
        if (icon != null) { icon.Kind = anyFolded ? "triangle-right" : "triangle-down"; icon.SetVerticesDirty(); }
    }
    private void FoldAll()
    {'''),
('''        if (GamepadIntent.Layer)
        {''', '''        // LT + RB / LT + LB: expand / collapse all (the page skips its own LB/RB tab switch while LT is held).
        if (pad.leftTrigger.ReadValue() > .5f)
        {
            if (pad.rightShoulder.wasPressedThisFrame) { SetAllFolded(true); return; }
            if (pad.leftShoulder.wasPressedThisFrame) { SetAllFolded(false); return; }
        }
        if (GamepadIntent.Layer)
        {'''),
('''        _repo.SaveUiState(); _selected.Clear(); _anchor = -1;
        string key = "group:" + groupId;
        RenderFold();''', '''        _repo.SaveUiState(); _selected.Clear(); _anchor = -1;
        string key = "group:" + groupId;
        RenderFold(); RefreshFoldAllIcon();'''),
])
edit('OverviewPage.Gamepad.cs', [
('''        int step = TabStep();
        if (step == 0) return;''', '''        int step = TabStep();
        if (step == 0) return;
        if (pad.leftTrigger.ReadValue() > .5f) return; // LT + LB / RB = collapse / expand all, handled by the page content'''),
])
print('ok')
