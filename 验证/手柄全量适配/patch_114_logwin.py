# -*- coding: utf-8 -*-
"""1.1.4 review (log window + shared dialogs): right stick scrolls the log, sub-panels reachable and B closes them one at a
time, search box behind the gamepad gate, export without the Win32 dialog on a controller, focus back to the opener when a
modal closes, uninstall dialog starts on the safe button."""
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

edit('Overview/LogWindow.cs', [
# B closes the top sub-panel first; search box behind the gate
('''        var view = panel.gameObject.AddComponent<LogWindow>(); view._repo = repo; view._installedMods = installedMods.ToArray(); view._panel = panel; view.Build();''',
 '''        var view = panel.gameObject.AddComponent<LogWindow>(); view._repo = repo; view._installedMods = installedMods.ToArray(); view._panel = panel; view.Build();
        // 1.1.4 (gamepad review): B closes the error details / option panel first, the window only when none is open.
        OverviewUi.SetModalCloseHandler(() => { if (view == null || !view.CloseTopLayer()) OverviewUi.CloseModal(); });
        GamepadSupport.MarkSelectable(view._search.gameObject, null, null);'''),
# right stick scroll turns follow off; sub-panels get navigation + focus
('''        if (_follow && !_selecting && !_hasSelection) ScrollTo(0);''',
 '''        // 1.1.4 (gamepad review): the right stick scrolled, then follow pulled the view straight back to the newest line.
        if (_follow && GamepadSupport.IsGamepad && GamepadIntent.ScrollY != 0) _follow = false;
        if (_follow && !_selecting && !_hasSelection) ScrollTo(0);
        WireLayer(_errorDetails); WireLayer(_options);'''),
('''    internal void UserScroll() { _follow = false; }''',
 '''    internal void UserScroll() { _follow = false; }
    private RectTransform? _wiredLayer; private GameObject? _layerOpener; private CanvasGroup? _baseGroup;
    /// <summary>1.1.4 (gamepad review): option / detail panels are built under the window, outside its navigation, so the
    /// controller could not reach them. Each new panel gets its own navigation and the first control; the window behind
    /// it stops taking input until it closes, then the focus returns to the button that opened it.</summary>
    private void WireLayer(RectTransform? layer)
    {
        var top = _errorDetails != null ? _errorDetails : _options;
        if (layer != top) return;
        if (top == null)
        {
            if (_wiredLayer == null) return;
            _wiredLayer = null; SetBaseInteractable(true);
            if (_layerOpener != null && _layerOpener.activeInHierarchy && GamepadSupport.IsGamepad) EventSystem.current?.SetSelectedGameObject(_layerOpener);
            _layerOpener = null; return;
        }
        if (top == _wiredLayer) return;
        if (_wiredLayer == null) _layerOpener = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        _wiredLayer = top;
        var group = top.GetComponent<CanvasGroup>() ?? top.gameObject.AddComponent<CanvasGroup>(); group.ignoreParentGroups = true;
        SetBaseInteractable(false);
        PublicationNavigation.Attach(top);
        foreach (var input in top.GetComponentsInChildren<TMP_InputField>(true)) { if (input.readOnly) input.shouldActivateOnSelect = false; else GamepadSupport.MarkSelectable(input.gameObject, null, null); }
        var first = top.GetComponentsInChildren<Selectable>(false).FirstOrDefault(s => s.IsInteractable() && s is not TMP_InputField);
        if (first != null && GamepadSupport.IsGamepad) EventSystem.current?.SetSelectedGameObject(first.gameObject);
    }
    private void SetBaseInteractable(bool value)
    {
        if (_baseGroup == null) _baseGroup = _panel.GetComponent<CanvasGroup>() ?? _panel.gameObject.AddComponent<CanvasGroup>();
        _baseGroup.interactable = value;
    }
    private void LateUpdate() { if (_errorDetails == null && _options == null && _wiredLayer != null) WireLayer(null); }
    /// <summary>B: closes the top panel; false when none is open.</summary>
    internal bool CloseTopLayer()
    {
        if (_errorDetails != null) { Destroy(_errorDetails.gameObject); _errorDetails = null; return true; }
        if (_options != null) { Destroy(_options.gameObject); _options = null; return true; }
        return false;
    }'''),
# export on a controller: no blocking Win32 dialog
('''            if (!LogSavePicker.TrySelect(fileName, out string? destination, out string? pickerFailure))''',
 '''            string? destination = null, pickerFailure = null;
            // 1.1.4 (gamepad review): the Win32 save dialog cannot be driven with a controller; use the fixed folder.
            if (GamepadSupport.IsGamepad) pickerFailure = "controller";
            if (pickerFailure != null || !LogSavePicker.TrySelect(fileName, out destination, out pickerFailure))'''),
])

# every modal returns the focus to the control that opened it
edit('Overview/OverviewUi.cs', [
('''        CloseModal(); if (ModalParent == null) return null;
        var shade = Panel(name, ModalParent, ModConfigLook.Surface(new Color(0, 0, 0, .72f))); Fill(shade); shade.SetAsLastSibling(); _modal = shade.gameObject;''',
 '''        var opener = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        CloseModal(); if (ModalParent == null) return null;
        var shade = Panel(name, ModalParent, ModConfigLook.Surface(new Color(0, 0, 0, .72f))); Fill(shade); shade.SetAsLastSibling(); _modal = shade.gameObject;
        if (opener != null && !opener.transform.IsChildOf(ModalParent.transform.Find(name) ?? shade)) _modalOpener = opener;'''),
('''        if (_modal != null) { _modal.SetActive(false); UnityEngine.Object.Destroy(_modal); }
        _modal = null; _isModalDirty = null; _modalCloseHandler = null;''',
 '''        bool hadModal = _modal != null;
        if (_modal != null) { _modal.SetActive(false); UnityEngine.Object.Destroy(_modal); }
        _modal = null; _isModalDirty = null; _modalCloseHandler = null;
        // 1.1.4 (gamepad review): the focus went nowhere after a dialog closed; give it back to the button that opened it.
        var opener = _modalOpener; _modalOpener = null;
        if (hadModal && opener != null && opener.activeInHierarchy && GamepadSupport.IsGamepad) EventSystem.current?.SetSelectedGameObject(opener);'''),
('''    internal static void SetModalCloseHandler(Action? handler) => _modalCloseHandler = handler;''',
 '''    internal static void SetModalCloseHandler(Action? handler) => _modalCloseHandler = handler;
    private static GameObject? _modalOpener;'''),
])
edit('Overview/UninstallDialog.cs', [
('''        var cancel=OverviewUi.Button(panel,Loc.LocalizeMessage("Close"),close,108,34); OverviewUi.Place((RectTransform)cancel.transform,244,panel.rect.height-50,108,34);''',
 '''        var cancel=OverviewUi.Button(panel,Loc.LocalizeMessage("Close"),close,108,34); OverviewUi.Place((RectTransform)cancel.transform,244,panel.rect.height-50,108,34);
        // 1.1.4 (gamepad review): start on the safe button, not on "Confirm uninstall".
        if (GamepadSupport.IsGamepad) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(cancel.gameObject);'''),
])
print('ok')
