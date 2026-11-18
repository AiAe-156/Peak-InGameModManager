from pathlib import Path
root = Path(__file__).resolve().parents[2]
def edit(rel, old, new, count=1):
    p = root / rel; s = p.read_text(encoding="utf-8")
    assert s.count(old) >= 1, (rel, old[:100])
    s = s.replace(old, new, count); p.write_text(s, encoding="utf-8")
lw = "src/Overview/LogWindow.cs"
# (a) with a controller the log does not start in follow mode (it kept snapping to the bottom); mouse keeps following
edit(lw, "        Btn(\"跳到底部\", () => { _follow = true; _hasSelection = false; ScrollTo(0); }, 388, h - 48, 104, UiActionRole.Overview);",
         "        Btn(\"跳到底部\", () => { _follow = true; _hasSelection = false; ScrollTo(0); }, 388, h - 48, 104, UiActionRole.Overview);\n        if (GamepadSupport.IsGamepad) _follow = false; // 1.1.5 (user: \"why does it jump to the bottom\"): follow only when asked")
# (b) the "|" divider sat inside the "jump to bottom" button
edit(lw, "        var divider = OverviewUi.Text(_panel, \"|\", 24); OverviewUi.Place(divider.rectTransform, 472, h - 48, 10, 32);",
         "        // 1.1.5 (user screenshot): the \"|\" divider was placed inside the jump-to-bottom button (472..482 vs 388..492); removed.")
# refresh icon: always in the toolbar chain (Unity skips a non-interactable explicit target), so it is reachable
tb = "src/Overview/OverviewPage.Toolbar.cs"
edit(tb, "        var navigation = _controls.GetComponentsInChildren<Selectable>(false).Where(s => s.IsActive() && s.IsInteractable()).OrderBy(s => s.transform.position.x).ToList();",
         "        // 1.1.5 (user: \"the refresh icon next to Sources and updates cannot be focused\"): collected by IsActive only.\n        // It was left out whenever it was briefly non-interactable (an automatic check running) and never linked back.\n        var navigation = _controls.GetComponentsInChildren<Selectable>(false).Where(s => s.IsActive() && (s.IsInteractable() || s == _sourceToolsRefreshButton)).OrderBy(s => s.transform.position.x).ToList();")
# refresh icon: while it cannot run, A says why instead of doing nothing
st = "src/Overview/OverviewPage.SourceTools.cs"
edit(st, "        _sourceToolsRefreshButton.interactable = _tab == PageTab.Mods && _list != null && !_list.IsCheckingUpdates && !AutomaticUpdateChecks.IsChecking;",
         "        // Stays focusable; while a check runs the click only reports it (1.1.5: a non-interactable button cannot hold the focus).\n        _sourceToolsRefreshButton.interactable = true;\n        _sourceToolsBusy = !(_tab == PageTab.Mods && _list != null && !_list.IsCheckingUpdates && !AutomaticUpdateChecks.IsChecking);")
edit(st, "        _refreshIconFeedback.SetAppearance(_sourceToolsRefreshButton.interactable, color);",
         "        _refreshIconFeedback.SetAppearance(!_sourceToolsBusy, color);")
edit(st, "        _sourceToolsRefreshButton.onClick.AddListener(() => ManagementAction(() => { _list?.CheckUpdates(true); }));",
         "        _sourceToolsRefreshButton.onClick.AddListener(() => { if (_sourceToolsBusy) { SetStatus(_tab != PageTab.Mods ? Loc.T(\"Update checks run from the mod list.\", \"检查更新需在模组列表页进行。\", \"檢查更新需在模組列表頁進行。\") : Loc.T(\"An update check is already running.\", \"正在检查更新，请稍候。\", \"正在檢查更新，請稍候。\")); return; } ManagementAction(() => { _list?.CheckUpdates(true); }); });")
edit(st, "    private void BuildSourceToolsIcon()", "    private bool _sourceToolsBusy;\n    private void BuildSourceToolsIcon()")
# update queue: "Clear" was never in the controller chain; rows keep left/right inside the row
uq = "src/Overview/UpdateQueueView.cs"
edit(uq, "        var controls = new List<Button> { _close, _queueTab, _historyTab, _selected, _all, _network, _save }.Where(button => button.gameObject.activeInHierarchy && button.IsInteractable()).ToList();",
         "        var controls = new List<Button> { _close, _queueTab, _historyTab, _selected, _all, _network, _save, _clear }.Where(button => button.gameObject.activeInHierarchy).ToList();")
edit(uq, "        controls.AddRange(_rows.SelectMany(row => row.Buttons).Where(button => button.IsInteractable()));",
         "        controls.AddRange(_rows.SelectMany(row => row.Buttons).Where(button => button.gameObject.activeInHierarchy));")
print("ok")
