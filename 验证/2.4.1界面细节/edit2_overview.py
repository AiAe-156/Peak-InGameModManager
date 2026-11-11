import io, sys
SRC = sys.argv[1]

def edit(p, pairs):
    p = SRC + '/' + p
    s = io.open(p, encoding='utf-8').read()
    for a, b in pairs:
        n = s.count(a)
        assert n == 1, (p, a[:70], n)
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

C = 'Overview/OverviewController.cs'
edit(C, [
# OverviewController.OpenHotkeys: from the mod settings page, open the overview on its Keybindings tab.
('''    internal static void RefreshEntries() { foreach (var entry in Entries) if (entry != null) entry.Refresh(); }''',
'''    internal static void RefreshEntries() { foreach (var entry in Entries) if (entry != null) entry.Refresh(); }
    /// <summary>
    /// 2.4.1：模组设置页表头「跳转到快捷键设置页」。找与设置页同一个菜单（同一 UIPageHandler）的概览入口，
    /// 打开概览并切到快捷键设置页签；概览未建好时走入口的分帧打开，建好后再切页签。
    /// </summary>
    internal static void OpenHotkeys(UIPage? from)
    {
        if (from == null) return;
        var handler = from.GetComponentInParent<UIPageHandler>();
        foreach (var entry in Entries)
            if (entry != null && entry.BelongsTo(handler)) { entry.OpenOnHotkeys(); return; }
        Plugin.Log.LogWarning("[模组概览] 跳转快捷键设置页：找不到当前菜单的概览入口。");
    }'''),
('''    private void Open()
    {
        if (_parent == null) return;''',
'''    private bool _openHotkeys;
    internal bool BelongsTo(UIPageHandler? handler) => _parent != null && handler != null && _parent.GetComponentInParent<UIPageHandler>() == handler;
    internal void OpenOnHotkeys() { _openHotkeys = true; Open(); }
    private void Open()
    {
        if (_parent == null) return;'''),
('''            var handler = _parent.GetComponentInParent<UIPageHandler>();
            if (handler != null) handler.TransistionToPage(_page, new SetActivePageTransistion());
             
        }''',
'''            var handler = _parent.GetComponentInParent<UIPageHandler>();
            if (handler != null) handler.TransistionToPage(_page, new SetActivePageTransistion());
            if (_openHotkeys) { _openHotkeys = false; _page.ShowHotkeysTab(); }
        }'''),
# OverviewPage.ShowHotkeysTab
('''    internal void MarkReturnFromSettings() { _returnFromSettings = true; _settingsScrollPosition = _list?.ScrollPosition ?? 1; }''',
'''    internal void MarkReturnFromSettings() { _returnFromSettings = true; _settingsScrollPosition = _list?.ScrollPosition ?? 1; }
    internal void ShowHotkeysTab() => SwitchTab(PageTab.Hotkeys);'''),
# Toolbar: theme button (icon) built with the other tools.
('''        _more = OverviewUi.Button(_controls, Loc.T("More", "更多", "更多"), ShowToolbarMore, 76, 34, role: UiActionRole.Neutral);''',
'''        _theme = OverviewUi.IconButton(_controls, "theme", ToggleTheme);
        yield return null;
        _more = OverviewUi.Button(_controls, Loc.T("More", "更多", "更多"), ShowToolbarMore, 76, 34, role: UiActionRole.Neutral);'''),
('''    private Button _edit = null!, _hide = null!, _language = null!;''',
'''    private Button _edit = null!, _hide = null!, _language = null!, _theme = null!;
    /// <summary>2.4.1：工具条主题钮。切换后概览要按新主题重建：先回到上一层再立即重新打开（见 OverviewEntry 的 Epoch 判断）。</summary>
    private void ToggleTheme()
    {
        RequestProfileLeave(() =>
        {
            ModConfigSidebarWidgets.ToggleTheme();
            SetStatus(ModConfigLook.IsDark ? Loc.T("Purple theme. Reopen the overview to apply it here.", "已切到紫色主题，重新打开概览后此页生效。", "已切到紫色主題，重新開啟概覽後此頁生效。")
                : Loc.T("Blue theme. Reopen the overview to apply it here.", "已切到蓝色主题，重新打开概览后此页生效。", "已切到藍色主題，重新開啟概覽後此頁生效。"));
        });
    }'''),
])

T = 'Overview/OverviewPage.Toolbar.cs'
edit(T, [
('''            var caption = button.GetComponentInChildren<TextMeshProUGUI>(true); if (caption != null && id != "edit") caption.text = label;
            float measured = Mathf.Max(minimum, id == "edit" || id == "import" || caption == null ? minimum : caption.GetPreferredValues(label).x + 24);''',
'''            var caption = button.GetComponentInChildren<TextMeshProUGUI>(true); if (caption != null && id != "edit" && id != "theme") caption.text = label;
            float measured = Mathf.Max(minimum, id == "edit" || id == "theme" || id == "import" || caption == null ? minimum : caption.GetPreferredValues(label).x + 24);'''),
('''        Tool("language", _language, T("Language", "语言"), 4, preferences, 4, 76, UiActionRole.Edit);''',
'''        Tool("language", _language, T("Language", "语言"), 4, preferences, 4, 76, UiActionRole.Edit);
        Tool("theme", _theme, Loc.T("Colour theme", "配色主题", "配色主題"), 4, preferences, 3, 32, UiActionRole.Neutral);'''),
('''        _edit.GetComponentInChildren<OverviewIconGraphic>().color = OverviewUi.Ivory; UiActionStyle.Select(_hide, _repo.State.hideDisabled);''',
'''        _edit.GetComponentInChildren<OverviewIconGraphic>().color = OverviewUi.Ivory; _theme.GetComponentInChildren<OverviewIconGraphic>().color = OverviewUi.Ivory; UiActionStyle.Select(_hide, _repo.State.hideDisabled);'''),
])

U = 'Overview/OverviewUi.cs'
edit(U, [
('''        else if (Kind == "color") { Line(.20f, .50f, .80f, .50f, .60f); }''',
'''        else if (Kind == "color") { Line(.20f, .50f, .80f, .50f, .60f); }
        else if (Kind == "theme")
        {
            // Crescent moon: outer arc on the left, inner arc biting in from the upper right (stroke icon, like the others).
            for (int i = 0; i < 16; i++) { float a = Mathf.PI * (.30f + 1.40f * i / 16f), b = Mathf.PI * (.30f + 1.40f * (i + 1) / 16f); Line(.5f + .38f * Mathf.Cos(a), .5f + .38f * Mathf.Sin(a), .5f + .38f * Mathf.Cos(b), .5f + .38f * Mathf.Sin(b), .10f); }
            Vector2 c = new Vector2(.66f, .62f);
            for (int i = 0; i < 12; i++) { float a = Mathf.PI * (.86f + 1.04f * i / 12f), b = Mathf.PI * (.86f + 1.04f * (i + 1) / 12f); Line(c.x + .30f * Mathf.Cos(a), c.y + .30f * Mathf.Sin(a), c.x + .30f * Mathf.Cos(b), c.y + .30f * Mathf.Sin(b), .08f); }
        }'''),
])

# Profile list: the green viewed/applied marker sat on the row's right edge (listWidth-46 ignores the row's -26 inset)
# and was not vertically centred. Place it inside the row, centred on the row's 50px height.
P = 'Overview/OverviewPage.Profiles.cs'
edit(P, [
('''                var flag = OverviewUi.Text(row, badge, 18); flag.color = profile.id == appliedId ? OverviewUi.Green : OverviewUi.Blue;
                flag.alignment = TextAlignmentOptions.MidlineRight;
                OverviewUi.Place(flag.rectTransform, listWidth - 46, 4, 34, 26);''',
'''                var flag = OverviewUi.Text(row, badge, 18); flag.color = profile.id == appliedId ? OverviewUi.Green : OverviewUi.Blue;
                flag.alignment = TextAlignmentOptions.MidlineRight;
                // 2.4.1：行宽是 listWidth-26（左缩 18、右缩 8），原先按 listWidth-46 放，右端压到行外；改成行内右侧 10、垂直居中
                flag.rectTransform.anchorMin = flag.rectTransform.anchorMax = flag.rectTransform.pivot = new Vector2(1, .5f);
                flag.rectTransform.anchoredPosition = new Vector2(-10, 0); flag.rectTransform.sizeDelta = new Vector2(40, 30);'''),
('''            OverviewUi.PlaceClipped(name, 12, 3, Mathf.Max(60, listWidth - 58 - (badge.Length > 0 ? 28 : 0)), 26);''',
'''            OverviewUi.PlaceClipped(name, 12, 3, Mathf.Max(60, listWidth - 46 - (badge.Length > 0 ? 44 : 0)), 26);'''),
])
print('ok')
