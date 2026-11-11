from pathlib import Path
root = Path(__file__).resolve().parents[2]
def read(name): return (root / 'src/Overview' / name).read_text(encoding='utf-8-sig')
checks = 0
def check(value, name):
    global checks
    assert value, name
    checks += 1
page = read('OverviewController.cs')
check('_status.textWrappingMode = TextWrappingModes.NoWrap' in page, 'status never wraps')
check('_status.overflowMode = TextOverflowModes.Ellipsis' in page, 'width-aware ellipsis')
check('_status.richText = false; _status.raycastTarget = true' in page, 'raw status and hover target')
check('AttachToolHint(_status.gameObject, () => _fullStatus)' in page, 'full hint, not truncated caption')
check('_fullStatus = text;' in page and '_status.text = OverviewListView.SingleLine(text)' in page, 'full text preserved before visible normalization')
check('RefreshActiveToolHint();' in page[page.index('private void SetStatus'):page.index('private void PickLanguage')], 'hover follows updated progress')
check('focus.Selected = hover.Enter; focus.Deselected = hover.Exit' in page, 'hint keyboard focus wiring')
source = read('OverviewPage.SourceTools.cs')
check('hit.gameObject.AddComponent<RefreshIconFeedback>()' in source, 'only 32px child hot zone has motion')
check('_refreshIconFeedback.Bind(_sourceToolsRefresh)' in source, 'glyph only animation binding')
check('width - 54, 1, 32, 32' in source, 'existing independent hit dimensions retained')
check('_sourceToolsRefreshButton.onClick.AddListener(() => ManagementAction(() => { _list?.CheckUpdates(true); }))' in source, 'original refresh click unchanged')
check('SetAppearance(_sourceToolsRefreshButton.interactable, color)' in source, 'busy/available style explicit')
animation = read('RefreshIconFeedback.cs')
check('void Update(' not in animation, 'no permanently scheduled Update')
check('while (_state.NeedsFrame)' in animation and '_transition = null;' in animation, 'finite transition coroutine')
check('if (_baseColor == color && _state.Available == available) return;' in animation, 'idle header poll does no transform writes')
check('OnDisable() { StopTransition(); _state.Reset(); Apply(); }' in animation, 'close/disable cancels and restores')
check('if (!available) { StopTransition(); _state.Reset(); }' in animation, 'busy cancels and resets')
check('_icon.transform.localScale' in animation and '_icon.transform.localRotation' in animation and 'transform.parent' not in animation, 'parent does not move')
check('ISelectHandler, IDeselectHandler' in animation and 'Time.unscaledDeltaTime' in animation, 'focus and paused menu timing')
view = read('OverviewListView.cs')
check(view.index('CaptureCheckProgressGroups(') < view.index('await Task.Run(()=> {'), 'capture before worker')
check('snapshot.Groups.TryGetValue' in view and 'report.Groups.TryGetValue' in view, 'raw counts from same service snapshot')
check('completed: true' in view and 'FormatCheckProgress(report.Summary' in view, 'completion wired to grouped formatter')
check('catch (OperationCanceledException)' in view and ' + ReportText();' in view, 'interrupted status retains report')
check('_checkProgress.Clear(); _checking = false;' in view, 'terminal cleanup preserved')
print(f'Update feedback UI wiring: {checks} checks passed (source-level, not Unity visual acceptance).')
