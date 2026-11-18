# -*- coding: utf-8 -*-
"""1.1.4 second review (shop / log / dialogs), confirmed items:
1 link menu closed the store (Choose without owner → CloseModal) → WindowPopover.Menu on the store panel;
2 card grid / long lists: rows below the view were never targets → no visibility filter;
3 install / uninstall became interactable later but were never linked → link regardless of interactable, relink on change;
4 unwired layers: ShopFinalConfirm and the in-store uninstall overlay go through Popup(); task / uninstall pages get WireGamepad;
5 reading surface: tabs move down straight into it;
6 log WireLayer: bool state instead of a (destroyed) reference; base re-enabled on disable;
7 SFX Appear: the component is destroyed (changing the flag after Instantiate is too late)."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src'))
def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('Overview/ShopWindow.cs', [
# 1 link menu on the store's own popover host
('''        // Choose() turns into a full modal above 16 entries, which would replace (close) the store; keep it a dropdown.
        if(links.Count>16)links=links.Take(16).ToList();
        OverviewUi.Choose(T("Links on this page","本页链接"),links,"",url=>Application.OpenURL(url));''',
 '''        // OverviewUi.Choose has no owner and would CloseModal() — that is the store itself. Use the store's popover host.
        var anchor=_readingSurface!=null?(RectTransform)_readingSurface.transform:_networkAnchor;
        WindowPopover.Menu(_panel,anchor,T("Links on this page","本页链接"),links,"",url=>Application.OpenURL(url));'''),
# 2/3 candidates: active regardless of interactable / visibility
('''        var body=_body.GetComponentsInChildren<Selectable>(false).Where(s=>s.IsActive()&&s.IsInteractable()&&s.navigation.mode!=Navigation.Mode.None).ToList();''',
 '''        // Disabled buttons stay in the links (install / uninstall turn on once the inventory scan finishes); Unity skips a
        // non-interactable target, so nothing lands on them while they are off.
        var body=_body.GetComponentsInChildren<Selectable>(false).Where(s=>s.IsActive()&&s.navigation.mode!=Navigation.Mode.None).ToList();'''),
('''            if(c==from||!c.gameObject.activeInHierarchy)continue;
            if(!VisibleInScroll(c))continue;''', '''            if(c==from||!c.gameObject.activeInHierarchy)continue;'''),
# 5 tabs → reading surface
('''            if(control==_readingSurface)continue; // the reading surface handles its own moves
            control.navigation=nav;''', '''            if(control==_readingSurface)continue; // the reading surface handles its own moves
            if(_readingSurface!=null&&_keys.TryGetValue(control.gameObject,out var k)&&k.StartsWith("tab:"))nav.selectOnDown=_readingSurface;
            control.navigation=nav;'''),
# 4 unwired layers
('''                var popup=OverviewUi.Panel("ShopFinalConfirm",_panel,ModConfigLook.Surface(new Color(.14f,.12f,.10f,1)));''',
 '''                var popup=OverviewUi.Panel("ShopFinalConfirm",_panel,ModConfigLook.Surface(new Color(.14f,.12f,.10f,1)));Popup(popup,safeLast:true);'''),
('''        if(uninstall){var selected=_selected!;UninstallDialog.Open(selected.Name,new ShopRequest{Namespace=selected.Team,Package=selected.Name,Version=_installation.Version(selected.Key)},_panel);return;}''',
 '''        if(uninstall){var selected=_selected!;UninstallDialog.Open(selected.Name,new ShopRequest{Namespace=selected.Team,Package=selected.Name,Version=_installation.Version(selected.Key)},_panel);
            var overlay=_panel.Find("UninstallOverlay") as RectTransform;if(overlay!=null)Popup(overlay,safeLast:true);return;}'''),
('''        _showingTask=true;''', '''        _showingTask=true;StartCoroutine(WireAfterBuild());'''),
('''    private void ShowUninstallConfirmation(ShopTask task)
    {
        ClearBody();''', '''    private void ShowUninstallConfirmation(ShopTask task)
    {
        ClearBody();StartCoroutine(WireAfterBuild());'''),
('''    private T0 Key<T0>(T0 control,string key) where T0:Selectable{''', '''    /// <summary>Pages built outside Draw (task, uninstall confirmation): wire them once they exist.</summary>
    private System.Collections.IEnumerator WireAfterBuild(){yield return null;if(_body!=null){RememberFocus();WireGamepad();}}
    /// <summary>Install / uninstall / retry change state after the inventory scan; relink so they are reachable.</summary>
    internal void Relink(){if(isActiveAndEnabled)StartCoroutine(LinkNextFrame());}
    private T0 Key<T0>(T0 control,string key) where T0:Selectable{'''),
])
# relink where buttons become interactable / retry toggles
p = os.path.join(SRC, 'Overview', 'ShopWindow.cs'); s = io.open(p, encoding='utf-8').read()
import re
m = re.search(r'private void UpdateInstallationDetails\(\)\s*\{', s)
assert m
s = s[:m.end()] + 'Relink();' + s[m.end():]
io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('Overview/LogWindow.cs', [
('''    private RectTransform? _wiredLayer; private GameObject? _layerOpener; private CanvasGroup? _baseGroup;''',
 '''    private RectTransform? _wiredLayer; private bool _layerWired; private GameObject? _layerOpener; private CanvasGroup? _baseGroup;'''),
('''        if (top == null)
        {
            if (_wiredLayer == null) return;
            _wiredLayer = null; SetBaseInteractable(true);''', '''        if (top == null)
        {
            if (!_layerWired) return;
            _wiredLayer = null; _layerWired = false; SetBaseInteractable(true);'''),
('''        if (top == _wiredLayer) return;
        if (_wiredLayer == null) _layerOpener = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        _wiredLayer = top;''', '''        if (_layerWired && top == _wiredLayer) return;
        // First panel of a chain: remember the opener. A rebuilt panel (e.g. "favourites only") keeps the first opener.
        if (!_layerWired) _layerOpener = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        _wiredLayer = top; _layerWired = true;'''),
('''    private void LateUpdate() { if (_errorDetails == null && _options == null && _wiredLayer != null) WireLayer(null); }''',
 '''    private void LateUpdate() { if (_errorDetails == null && _options == null && _layerWired) WireLayer(null); }
    private void OnDisable() { if (_baseGroup != null) _baseGroup.interactable = true; _layerWired = false; }'''),
])

edit('Overview/OverviewUi.cs', [
('''        foreach (var sfx in peak.GetComponentsInChildren<SFX_PlayOneShot>(true))
            if (sfx.gameObject.name.IndexOf("Appear", StringComparison.OrdinalIgnoreCase) >= 0) { sfx.playOnEnable = false; sfx.playOnStart = false; }''',
 '''        // OnEnable already started its delayed Play when the template was instantiated; changing the flag is too late.
        foreach (var sfx in peak.GetComponentsInChildren<SFX_PlayOneShot>(true))
            if (sfx.gameObject.name.IndexOf("Appear", StringComparison.OrdinalIgnoreCase) >= 0) { sfx.StopAllCoroutines(); sfx.enabled = false; UnityEngine.Object.Destroy(sfx); }'''),
])
print('ok')
