from pathlib import Path
import shutil
b=Path('备份/20261031_固定首屏就位前')
for n in ['OverviewPage.Toolbar.cs','OverviewListView.cs']:
 p=Path('src/Overview')/n;q=b/p;q.parent.mkdir(parents=True,exist_ok=True)
 if not q.exists():shutil.copy2(p,q)
p=Path('src/Overview/OverviewPage.Toolbar.cs');s=p.read_text(encoding='utf-8-sig').replace('    private OverviewToolbarReveal? _toolbarReveal;\n','').replace('        _toolbarReveal ??= _controls.gameObject.AddComponent<OverviewToolbarReveal>();\n        _toolbarReveal.Capture(_controls);\n','');p.write_text(s,encoding='utf-8')
p=Path('src/Overview/OverviewListView.cs');s=p.read_text(encoding='utf-8-sig')
# Stable shared geometry, independent of scanned model text/count.
a=s.index('            float indexWidth =');z=s.index('            var goalHeader =',a)
geometry=s[a:z];start=geometry.index('            string goalHeading')
geometry='            const float indexWidth = 48, versionWidth = 110;\n'+geometry[start:]
geometry=geometry.replace('float goalWidth = Mathf.Max(60, Measure(goalHeading, 18) + 16);','const float goalWidth = 60;')
s=s[:a]+geometry+s[z:]
# Header construction moves entirely out of the data Render.
a=s.index('            var goalHeader =');z=s.index('            float y = BuildInfrastructure',a);header=s[a:z]
s=s[:a]+'            EnsureFixedChrome();\n'+s[z:]
# Fixed toolbar/search/note is created once; keep advice data-dependent as its own small control.
a=s.index('            _tools.gameObject.SetActive(true);');z=s.index('            var pending =',a)
oldtools=s[a:z]
conflicts='''            var conflicts = PackConflictService.Evaluate(_repo, _mods).Where(c => c.Any).ToList();
            RefreshCompatibilityAdvice(conflicts);
'''
s=s[:a]+conflicts+s[z:]
s=s.replace('Clear(_tools); Clear(_header); Clear(_content);','Clear(_content);')
s=s.replace('internal void PlayAppearance() => _cascade.Play(_scroll, AppearanceRows, _header);','internal void PlayAppearance() => _cascade.Play(_scroll, AppearanceRows);')
# Initialize after all hint infrastructure has been created, without calling Render or scanning.
s=s.replace('        // 构造期不读数据：','        EnsureFixedChrome();\n        ShowInitialReadState();\n        // 构造期不读数据：')
s=s.replace('        if (_disposed) return; _opened = true;', '        if (_disposed) return; EnsureFixedChrome(); _opened = true;')
s=s.replace('            float width = Width;\n            if (!_building', '            float width = Width;\n            EnsureFixedChrome();\n            if (!_building')
method='''    private string _fixedChromeKey = "";
    private TextMeshProUGUI? _initialReadState;
    private Button? _compatibilityAdvice;
    private const string EnablementNote = "Checkboxes set the profile target. Save only keeps it; Apply and restart to change loaded mods.";
    private string EnablementHint => Loc.T(EnablementNote, "勾选只改方案目标；“仅保存”记住目标，“应用配置”后重启游戏生效。", "勾選只改方案目標；「僅儲存」記住目標，「套用設定」後重新啟動遊戲生效。");
    private void ShowInitialReadState()
    {
        if(_virtualRows.Count>0)return;
        if(_initialReadState==null)_initialReadState=Label(_content, Loc.T("Reading mods and components…", "正在读取模组与组件……", "正在讀取模組與組件……"),20,15,Width-50,45,24);
        _content.sizeDelta=new Vector2(0,70);
    }
    private void EnsureFixedChrome()
    {
        float width=Width;
        string key=width+":"+_authorMode+":"+OverviewLanguageBridge.Revision;
        if(key==_fixedChromeKey)return;
        _fixedChromeKey=key;
        Clear(_tools);Clear(_header);_compatibilityAdvice=null;
        _tools.gameObject.SetActive(true);OverviewUi.Place(_tools,0,0,width,27);
        BuildOverviewSearch(width);
        string enablementNote=EnablementHint;
        var note=Label(_tools,enablementNote,148,0,Mathf.Max(40,width-348),27,17);note.color=OverviewUi.Muted;AddHint(note.gameObject,enablementNote);
        OverviewUi.Place(_header,0,29,width,39);
'''+geometry+header+'''    }
    private void RefreshCompatibilityAdvice(List<PackConflictAdvice> conflicts)
    {
    }
'''
# Infer conflict element type rather than assume by generating generic method accepting enumerable? inspect later.
method=method[:method.index('    private void RefreshCompatibilityAdvice')]+'''    private void RefreshCompatibilityAdvice(System.Collections.Generic.List<PackConflictResult> conflicts)
    {
        int activeAdvice=conflicts.Count(c=>!c.Ignored);
        if(_compatibilityAdvice!=null) { _compatibilityAdvice.gameObject.SetActive(false);UnityEngine.Object.Destroy(_compatibilityAdvice.gameObject);_compatibilityAdvice=null; }
        if(activeAdvice==0)return;
        _compatibilityAdvice=OverviewUi.Button(_tools,Loc.F("Compatibility · {0}","冲突建议 · {0}","衝突建議 · {0}",activeAdvice),()=> { HideAllHints();PackConflictUi.Show(_repo,conflicts,()=> { Render();_headersChanged(); }); },172,26,UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)_compatibilityAdvice.transform,Width-192,0,172,26);UiActionStyle.Tint(_compatibilityAdvice,PackConflictUi.AdviceColor);
    }
'''
s=s.replace('    private float Width =>',method+'    private float Width =>')
p.write_text(s,encoding='utf-8')
