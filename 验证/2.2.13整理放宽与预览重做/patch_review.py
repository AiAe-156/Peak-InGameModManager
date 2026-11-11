from pathlib import Path
import sys
root = Path(sys.argv[1])
def sub(path, old, new, count=1):
    p = root / path; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (path, old[:70], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
f = 'src/Overview/OverviewPage.Installation.cs'
sub(f, '''        if (task.Tree.Count > 0 || task.Details.Count > 0 || task.Desired.Count > 0) body.Add(LayoutPreviewText(task));''',
'''        bool drawTree = layout && task.Tree.Count > 0;
        if (!drawTree && (task.Tree.Count > 0 || task.Details.Count > 0 || task.Desired.Count > 0)) body.Add(LayoutPreviewText(task));''')
sub(f, '''        var panel = OverviewUi.CreateModalPanel("MCE_InstallationReview", 860, 560); if (panel == null) return;''',
'''        var panel = OverviewUi.CreateModalPanel("MCE_InstallationReview", drawTree ? 1040 : 860, drawTree ? 760 : 560); if (panel == null) return;''')
sub(f, '''        OverviewUi.SaveDialogBody(panel, string.Join("\\n\\n", body), attention: !finished);''',
'''        // A layout plan is drawn as a framed tree; everything else (and plans from before the tree existed) stays text.
        if (drawTree) LayoutReviewView.Show(panel, task.Tree, body.Select((line, index) => (line, index > 0 && (task.Error.Length > 0 || task.Blockers.Count > 0) && line != message)).ToList());
        else OverviewUi.SaveDialogBody(panel, string.Join("\\n\\n", body), attention: !finished);''')
print('ok')
