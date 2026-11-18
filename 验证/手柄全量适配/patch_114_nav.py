# -*- coding: utf-8 -*-
"""1.1.4: entry button reachable by controller, keybindings scroll follows focus everywhere, changing the modifier
updates recorded combos, Back as default modifier."""
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

# --- entry button in the controller navigation ---
edit('Overview/OverviewController.cs', [
('''        MatchNativeRibbon(_button, caption);''', '''        MatchNativeRibbon(_button, caption);
        // 1.1.4 (user): the entry sits top-left, away from the menu column, so the controller could never reach it.
        _button.gameObject.AddComponent<EntryNavLink>();
        GamepadSupport.MarkSelectable(_button.gameObject, null, null);'''),
])
p = os.path.join(SRC, 'Overview', 'EntryNavLink.cs')
io.open(p, 'w', encoding='utf-8', newline='\n').write('''#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.4：把左上角的「模组概览」入口接进手柄导航。入口离菜单那一列很远，自动导航找不到它：
/// 菜单里左边没有目标的按钮，按 ← 到入口；入口按 → / ↓ 回到离它最近的菜单按钮。
/// 只补「原本为空」的方向，不改游戏自己设好的导航。页面每次显示时重连一次（菜单按钮会重建）。
/// </summary>
internal sealed class EntryNavLink : MonoBehaviour
{
    private int _linkAt = -1;
    private void OnEnable() => _linkAt = Time.frameCount + 2; // after the page has laid out its buttons
    private void Update()
    {
        if (_linkAt < 0 || Time.frameCount < _linkAt) return;
        _linkAt = -1;
        var self = GetComponent<Selectable>(); if (self == null) return;
        var page = transform.parent; while (page != null && page.GetComponent<Zorro.UI.UIPage>() == null) page = page.parent;
        if (page == null) page = transform.parent;
        if (page == null) return;
        Selectable? nearest = null; float best = float.MaxValue;
        foreach (var other in page.GetComponentsInChildren<Selectable>(false))
        {
            if (other == self || !other.IsInteractable() || other.transform.IsChildOf(transform)) continue;
            var nav = other.navigation;
            if (nav.mode == Navigation.Mode.Explicit && nav.selectOnLeft == null)
            { nav.selectOnLeft = self; other.navigation = nav; }
            float d = (other.transform.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; nearest = other; }
        }
        if (nearest == null) return;
        var mine = self.navigation; mine.mode = Navigation.Mode.Explicit;
        mine.selectOnRight = nearest; mine.selectOnDown = nearest; self.navigation = mine;
        // Automatic-navigation buttons: the closest one also gets "left → entry" via an explicit copy of its links.
        var near = nearest.navigation;
        if (near.mode == Navigation.Mode.Automatic)
        {
            var copy = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = nearest.FindSelectableOnUp(), selectOnDown = nearest.FindSelectableOnDown(),
                selectOnLeft = self, selectOnRight = nearest.FindSelectableOnRight() };
            nearest.navigation = copy;
        }
    }
}
''')

# --- keybindings: scroll follows every focusable cell (names with dependency hints, gears, etc.) ---
edit('Overview/OverviewHotkeyView.cs', [
('''            if(ownFocus){if(_targets.TryGetValue(_selectedId,out var target))EventSystem.current?.SetSelectedGameObject(target);else EventSystem.current?.SetSelectedGameObject(_targets.Values.FirstOrDefault());}
            LinkToolbarNavigation(save);''', '''            if(ownFocus){if(_targets.TryGetValue(_selectedId,out var target))EventSystem.current?.SetSelectedGameObject(target);else EventSystem.current?.SetSelectedGameObject(_targets.Values.FirstOrDefault());}
            LinkToolbarNavigation(save);
            // 1.1.4 (user): moving down on the name column did not scroll; every focusable cell keeps itself in view.
            foreach(var selectable in _content.GetComponentsInChildren<Selectable>(false))
                if(selectable.GetComponent<ScrollFollow>()==null){var follow=selectable.gameObject.AddComponent<ScrollFollow>();var rect=(RectTransform)selectable.transform;follow.Selected=()=>EnsureVisible(rect);}'''),
('''internal sealed class OverviewHotkeyFocus : MonoBehaviour,ISelectHandler,IDeselectHandler''', '''/// <summary>1.1.4: keeps a focused cell scrolled into view (separate from OverviewHotkeyFocus, whose handlers get replaced).</summary>
internal sealed class ScrollFollow : MonoBehaviour,ISelectHandler{internal Action? Selected;public void OnSelect(BaseEventData data)=>Selected?.Invoke();}
internal sealed class OverviewHotkeyFocus : MonoBehaviour,ISelectHandler,IDeselectHandler'''),
])

# --- modifier change rewrites combos; Back default ---
edit('GamepadCombos.cs', [
('''    internal static string Modifier { get { Load(); return _modifier; } set { Load(); _modifier = Modifiers.Contains(value) ? value : ""; Save(); } }
    private static string _modifier = "";''',
 '''    /// <summary>1.1.4 (user): changing the modifier changes every combo that uses one (they all share it); a combo whose
    /// button becomes the new modifier is dropped. Default is Back (user: "back works well as the default").</summary>
    internal static string Modifier
    {
        get { Load(); return _modifier; }
        set
        {
            Load(); string next = Modifiers.Contains(value) ? value : "";
            if (next == _modifier) return;
            if (next.Length > 0)
            {
                _items.RemoveAll(i => i.Mod.Length > 0 && i.button == next);
                foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = next;
            }
            _modifier = next; Save();
        }
    }
    private static string _modifier = "select";'''),
('''                _modifier = store?.modifier != null && Modifiers.Contains(store.modifier) ? store.modifier : "";''',
 '''                _modifier = store?.modifier != null && Modifiers.Contains(store.modifier) ? store.modifier : "select";'''),
])
print('ok')
