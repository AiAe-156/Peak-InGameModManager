from pathlib import Path
r=Path(__file__).resolve().parents[2]
read=lambda n:(r/n).read_text(encoding='utf-8-sig')
def method(s,n):
 a=s.index(n);b=s.index('{',a)+1;depth=1
 while depth:
  depth+=(s[b]=='{')-(s[b]=='}');b+=1
 return s[a:b]
s=read('src/Overview/OverviewListView.cs');t=read('src/Overview/OverviewPage.Toolbar.cs');i=read('src/Overview/OverviewListView.Infrastructure.cs');v=read('src/Overview/OverviewListView.VirtualRows.cs')
chrome=method(s,'private void EnsureFixedChrome()');render=method(s,'private void Render()');ctor=method(s,'public OverviewListView(')
def geometry(text):
 a=text.index('const float indexWidth');b=text.index('float xAuthor',a);return ' '.join(line.strip() for line in text[a:text.index(';',b)+1].splitlines() if 'goalHeading' not in line)
checks={
 'fixed toolbar no reveal component':'OverviewToolbarReveal' not in t and '_toolbarReveal' not in t,
 'fixed toolbar no layer fade attachment':'OverviewLayerFade.Attach' not in t,
 'constructor installs chrome before any reload':'EnsureFixedChrome();' in ctor and 'ShowInitialReadState();' in ctor and 'new OverviewCatalogue' not in ctor,
 'opening ensures chrome before row refresh':method(s,'public void OnOpened(').index('EnsureFixedChrome();')<method(s,'public void OnOpened(').index('RefreshVirtualRows();'),
 'chrome not gated on component loading':'_infrastructureLoading' not in chrome,
 'chrome independent of scanned models':all(x not in chrome for x in ['_mods','_visible','_infrastructure','GetPendingState','PackConflictService','Render()','AddLayeredRow','AddVirtualRow']),
 'fixed chrome creates search header divider':all(x in chrome for x in ['BuildOverviewSearch(width)','OverviewUi.Place(_header','"ColumnLine"','"Version"','"Name"']),
 'fixed chrome cache returns before tree clear':chrome.index('if(width==_fixedChromeWidth')<chrome.index('Clear(_tools)'),
 'cache covers width mode language search':all(x in chrome.split('Clear(_tools)')[0] for x in ['_fixedChromeWidth','_fixedChromeAuthor','_fixedChromeLanguage','_fixedChromeSearch']) and 'string key=' not in chrome,
 'same stable geometry before and after data':geometry(chrome)==geometry(render),
 'no per-mod version or count measurement in geometry':'Measure(' not in geometry(render) and 'versionWidth = 110' in geometry(render),
 'render no fixed tree teardown':'Clear(_tools)' not in render and 'Clear(_header)' not in render,
 'row model still gated during scan':'_infrastructureLoading || !_cascade.BeforeRebuild()' in render,
 'reload does not build second model':'Render();' not in method(s,'public void Reload()'),
 'fixed header not passed to cascade':'_cascade.Play(_scroll, AppearanceRows);' in s and '_cascade.Play(_scroll, AppearanceRows, _header)' not in s,
 'initial loading is text not fake rows':'Reading mods and components' in method(s,'private void ShowInitialReadState()') and 'AddVirtualRow' not in method(s,'private void ShowInitialReadState()'),
 'metadata stays first graph stays local':i.index('_infrastructure = rows')<i.index('return InfrastructureDependencyCollector.Collect') and 'Render(' not in i[i.index('}).ContinueWith(task'):i.index('private void FlushInfrastructureRedraw')],
 'resident row ordering and budgets intact':all(x in v for x in ['if(_buildOrderCaptured)return;','_rowBuildBudget.TryTake(Time.frameCount','_layerBuildBudget.TryTake','row.ShellFrame==Time.frameCount']),
 '200ms untouched':'Delay = .200f' in read('src/Overview/HoverDwell.cs'),
}
for n,ok in checks.items():print(('PASS ' if ok else 'FAIL ')+n)
assert all(checks.values())
print('RESULT',len(checks),'fixed chrome source contracts passed; not Unity visual acceptance')
