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

# Blue chrome (edit role, overview teal, selected/active tab) turns purple in the purple theme; green/red stay.
edit('UiActionStyle.cs', [
('''            UiActionRole.Edit => new Color32(0x26, 0x40, 0x59, 255),''',
'''            UiActionRole.Edit => ModConfigLook.SurfaceFor(new Color32(0x26, 0x40, 0x59, 255), dark),'''),
('''            UiActionRole.Overview => new Color32(0x35, 0x55, 0x52, 255),''',
'''            UiActionRole.Overview => ModConfigLook.SurfaceFor(new Color32(0x35, 0x55, 0x52, 255), dark),'''),
('''        if (dark) { color.r *= .82f; color.g *= .82f; color.b *= .82f; }''',
'''        if (dark && role is UiActionRole.Commit or UiActionRole.Danger) { color.r *= .82f; color.g *= .82f; color.b *= .82f; }'''),
('''        if (_selected) color = dark ? new Color32(0x28, 0x68, 0x83, 255) : new Color32(0x32, 0x7b, 0x98, 255);''',
'''        if (_selected) color = dark ? ModConfigLook.SurfaceFor(new Color32(0x28, 0x68, 0x83, 255), true) : new Color32(0x32, 0x7b, 0x98, 255);'''),
])
edit('Overview/OverviewController.cs', [
('''tab.GetComponent<OverviewTabGraphic>().color = active ? new Color(.20f, .48f, .60f) :''',
'''tab.GetComponent<OverviewTabGraphic>().color = active ? ModConfigLook.Surface(new Color(.20f, .48f, .60f)) :'''),
])
edit('Overview/OverviewUi.SourceToolsDropdown.cs', [
('''item.Checked ? new Color(.20f, .48f, .60f) :''',
'''item.Checked ? ModConfigLook.Surface(new Color(.20f, .48f, .60f)) :'''),
])

# Tool buttons on the settings page: cream paper in blue, dark paper in purple.
edit('ModConfigSidebarWidgets.cs', [
('''            Tint(background, Cream, ModConfigTreeLayoutPatch.TabBackground);''',
'''            Tint(background, ModConfigLook.CreamOf(false), ModConfigLook.BoxOf(true));'''),
])

# ModConfig's filter dropdown (slate blue / purple in the screenshot) follows the theme accent.
edit('ModConfigTreeLayoutPatch.cs', [
('''            ModSettingsMenu? menu = Menu;
            if (menu != null && menu.Content != null)
            {
                ModConfigLook.ApplyMain(menu.Content);
                ModConfigLook.ApplyOptions(menu.Content);
            }''',
'''            ModSettingsMenu? menu = Menu;
            if (menu != null && menu.Content != null)
            {
                ModConfigLook.ApplyMain(menu.Content);
                ModConfigLook.ApplyOptions(menu.Content);
            }

            Image? filter = menu != null && menu.FilterDropdown != null ? menu.FilterDropdown.Background : null;
            if (filter != null)
            {
                Color accent = ModConfigLook.Accent;
                filter.color = new Color(accent.r, accent.g, accent.b, filter.color.a);
            }'''),
])

# Overview: rebuild a page whose colours were built under another theme, the next time it opens.
edit('Overview/OverviewController.cs', [
('''    private void EnsureShell()
    {
        if(_page!=null || _parent==null)return;''',
'''    private int _builtEpoch=-1;
    private void EnsureShell()
    {
        // 2.4.0: colours are baked in when the page is built; a page built under the other theme is rebuilt.
        if(_page!=null && !_opening && _builtEpoch!=ModConfigLook.Epoch && !_page.gameObject.activeSelf)
        { (_shell as IDisposable)?.Dispose();_shell=null;_page.DisposeResources();UnityEngine.Object.Destroy(_page.gameObject);_page=null; }
        if(_page!=null || _parent==null)return;
        _builtEpoch=ModConfigLook.Epoch;'''),
('''        _opening=true;_stage=0;
        try { EnsureShell(); } catch(Exception error) { FailOpen(error);return; }''',
'''        try { EnsureShell(); } catch(Exception error) { FailOpen(error);return; }
        _opening=true;_stage=0;'''),
])
print('ok')
