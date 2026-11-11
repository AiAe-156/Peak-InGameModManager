from pathlib import Path
r=Path(__file__).resolve().parents[2]
def read(f):return (r/'src/Overview'/f).read_text(encoding='utf-8-sig')
s=read('OverviewDependencyHints.cs');c=read('OverviewDependencyHints.Components.cs');d=read('ComponentDetailsView.cs');g=read('SharedRelationshipSnapshot.cs')
checks={
 'hover at most two levels':'reverse ? 1 : _chains ? 2 : 1' in c,
 'forward no eight-item limit':'reverse ? 8 : 2048' in c,
 'third level omission explained':'第三级及更深依赖已省略' in c,
 'height 480':'Mathf.Min(480 - 24, bounds.height - 40)' in s,
 'details initially collapse deeper branches':'r.Depth >= 2 && r.HasChildren' in d,
 'direction switch resets default expansion':'reverse = incoming; collapsed.Clear(); defaultExpansion = true;' in d,
 'manual expand all works':'collapsed.Clear(); defaultExpansion = false; Render();' in d,
 'graph replacement restores two levels':'if (changedGraph) { collapsed.Clear(); defaultExpansion = true; }' in d,
 'shared DLL provider not entire mod':'"assembly:" + assembly' in g and 'providers <= 1' in g,
 'forward/reverse remain independent':'AddDirection(false); AddDirection(true);' in c,
}
for name,ok in checks.items():print(('PASS ' if ok else 'FAIL ')+name)
assert all(checks.values())
print('RESULT',len(checks),'source contracts')
