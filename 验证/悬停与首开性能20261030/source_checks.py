from pathlib import Path
import json
root=Path(__file__).resolve().parents[2]
def read(name): return (root/name).read_text(encoding='utf-8-sig')
view=read('src/Overview/OverviewListView.cs')
hints=read('src/Overview/OverviewDependencyHints.cs')
canvas=read('src/Overview/HintCanvas.cs')
controller=read('src/Overview/OverviewController.cs')
probe=read('src/Overview/UiPerfProbe.cs')
checks={
 'shared 200ms hover dwell': 'Delay = .200f' in read('src/Overview/HoverDwell.cs') and '_showAt=Time.unscaledTime+HoverDwell.Delay' in hints,
 'normal tooltip isolated': 'HintCanvas.Attach(_hint,false)' in view,
 'dependency tooltip isolated': 'HintCanvas.Attach(_card, true)' in hints,
 'sort inherited not promoted over modal': 'canvas.overrideSorting = false' in canvas,
 'interactive card raycaster retained': 'if (interactive) rect.gameObject.AddComponent<GraphicRaycaster>()' in canvas,
 'no parent raycast group suppression': 'blocksRaycasts' not in canvas,
 'shader channels inherited': 'inherited.additionalShaderChannels' in canvas,
 'stationary ordinary pointer fast path': 'screen==_lastHintPointer && rootSize==_lastHintRootSize' in view,
 'position writes conditional': 'sqrMagnitude > .01f' in canvas,
 'logical prefix exact binary search': 'while(low<high)' in hints and 'MeasureContent(lines.GetRange(0,low)' in hints,
 'relation lru bounded': 'HintLru<string, CardLayout> _layouts = new(64' in hints,
 'ordinary metrics bounded': 'HintLru<string,float> _hintMetrics=new(64' in view,
 'cache distinguishes physical duplicate identities': 'mod.PackageRoot' in hints,
 'cache invalidated at structural render': '_dependencyHints.InvalidateDisplay()' in view,
 'cache mode font language inventory content config': all(x in hints for x in ['_cacheFixed!=_fixed','_cacheChains!=_chains','_fontAtShow!=_text.font','_languageAtShow!=Loc.TranslationRevision','_inventoryAtShow!=OverviewPageCache.Inventory','_contentAtShow!=OverviewPageCache.Content','_configurationAtShow!=OverviewPageCache.Configuration']),
 'mask scan list overload': 'GetComponentsInParent(false,_sourceMasks)' in read('src/Overview/OverviewDependencyHints.Components.cs'),
 'display number dictionary': '_displayNumbers.TryGetValue' in view and '_visible.IndexOf(mod)' not in view,
 'prewarm not while normal tooltip visible': '_hint.gameObject.activeSelf || OverviewUi.HasModal' in view,
 'prewarm not while dependency card visible': '_card.gameObject.activeSelf || _pendingSource!=null' in hints,
 'prewarm does not spin through models each idle frame': 'for(;_warmRow<_virtualRows.Count;)' in view,
 'editor lazy': 'EnsureDocument().Reload()' in read('src/Overview/OverviewPage.Caching.cs') and 'EnsureDocument().ExportPackData()' in controller,
 'read-only metadata prefetch': 'internal static void Prefetch()' in read('src/Overview/OverviewCatalogue.cs'),
 'config default false session opt-in': '"UI performance probe",false' in probe and '#if MCE_UI_PERF_SESSION' in probe,
 'diagnostic unregister static callbacks': 'Canvas.preWillRenderCanvases-=PreRender' in probe and 'Canvas.willRenderCanvases-=PostRender' in probe,
 'diagnostic not pretending GPU timing': 'canvas-event avg/max' in probe,
 'maintenance fallback 2 seconds': '_eventNext=Time.unscaledTime+2' in read('src/Overview/OverviewMaintenance.cs'),
 'pending low-frequency safety unchanged': '_next=Time.unscaledTime+.2f' in read('src/Overview/OverviewMaintenance.cs'),
 'font warmup no destructive shared-asset calls': all(x not in read('src/Overview/HintPrewarm.cs') for x in ['ClearFontAssetData(', 'isMultiAtlasTexturesEnabled =','atlasWidth =','atlasHeight =']),
}
# Compare the actual approved snapshot, not the repository's old/untracked baseline.
old=read('备份/20261030_悬停性能前/src/Overview/OverviewUi.cs')
now=read('src/Overview/OverviewUi.cs')
def buttons(s): return s[s.index('    internal static Button Button('):s.index('    internal static Toggle Check(')]
checks['button template appearance animation sound code unchanged']=buttons(old)==buttons(now).replace('        AnimatorRestGate.Attach(button);\n','')
checks['version synchronized']=('PluginVersion = "2.0.1"' in read('src/Plugin.cs') and '<Version>2.0.1</Version>' in read('ModConfigEnhance.csproj') and json.loads(read('manifest.json'))['version_number']=='2.0.1')
for name,ok in checks.items():
 print(('PASS ' if ok else 'FAIL ')+name)
assert all(checks.values())
print(f'RESULT {len(checks)} wiring contracts passed (not Unity runtime)')
