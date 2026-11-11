from pathlib import Path
r=Path(__file__).resolve().parents[2]
def read(p):return (r/p).read_text(encoding='utf-8-sig')
h=read('src/Overview/OverviewDependencyHints.cs');c=read('src/Overview/OverviewController.cs');a=read('src/Overview/AnimatorRestGate.cs')
shell=c[c.index('internal IEnumerator BuildShell'):c.index('internal void BindRepository')]
checks={
'200ms common delay':'Delay = .200f' in read('src/Overview/HoverDwell.cs') and '+HoverDwell.Delay' in h,
'no starvation repeated target':'if(_pending==value)return' in read('src/Overview/HoverDwell.cs'),
'external hints dwell':'_hintDwell.Enter(text,Time.unscaledTime)' in read('src/Overview/OverviewListView.cs'),
'prebuild inactive before component creation':c.index('rect.gameObject.SetActive(false);')<c.index('rect.gameObject.AddComponent<OverviewPage>()'),
'pure shell repository references only deferred callbacks':all('=>' in line for line in shell.splitlines() if '_repo.' in line) and 'new OverviewRepository' not in shell and 'ShopWindow.ResumeGrouping' not in shell,
'frame spread shell':shell.count('yield return null')>=2,
'only actual open binds repository':'if(!_page.IsBound)_page.BindRepository(new OverviewRepository())' in c,
'partial shell destroy no global modal close':'if(!_bindingStarted)return' in c and 'if(OverviewUi.ModalParent==transform)OverviewUi.CloseModal()' in c,
'cold card job cancellation':'CancelPending();CancelLayout();' in h and '_layoutEpoch++' in h,
'cold job generation guard':'while(Current() && steps.MoveNext())yield return null' in h,
'cold job checks graph config language inventory':'ReferenceEquals(raw,SourceUi.DependencyGraph)' in h and 'configuration==OverviewPageCache.Configuration' in h and 'language==Loc.TranslationRevision' in h,
'hidden measurement avoids flashing binary probes':'_measureText.GetPreferredValues' in h and '_measureText.gameObject.SetActive(false)' in h,
'native transition wake before original':'prefix:new HarmonyMethod(typeof(AnimatorRestGate),nameof(BeforeTransition))' in a,
'animator no manual duplicate sound trigger':'SetTrigger(' not in a and 'ResetTrigger(' not in a,
'animator conservative single layer complete normal':'animator.layerCount!=1' in a and '_state!=0' in a and 'state.loop' in a and 'state.normalizedTime<1' in a,
'animator restore original flags':'animator.keepAnimatorStateOnDisable=_originalKeep' in a and 'animator.writeDefaultValuesOnDisable=_originalWrite' in a,
'animator option unsubscribed':'_option.SettingChanged-=OptionChanged' in a,
'explicit disposal for never-active pages':'internal void DisposeResources()' in c and '_page.DisposeResources();' in c,
'gate explicit tree release':'AnimatorRestGate.DetachTree(transform)' in c and '!ReferenceEquals(_button,null)' in a,
'prebuild exception rollback':'catch(Exception error) { FailOpen(error); }' in c and '_shellAt=Time.unscaledTime+5' in c,
'display invalidation restarts cancelled card':'_displayDirty=true' in h and '_displayDirty || VisibleBounds()' in h,
'labels indexed cold path':'BuildIndex()' in read('src/Overview/ComponentRelationshipLabels.cs') and '_lookup.Packages.TryGetValue' in read('src/Overview/ComponentRelationshipLabels.cs'),
}
for k,v in checks.items():print(('PASS ' if v else 'FAIL ')+k)
assert all(checks.values())
print('RESULT',len(checks),'contracts passed; Unity runtime pending')
