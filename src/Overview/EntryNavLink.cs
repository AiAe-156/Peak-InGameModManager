#nullable enable
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
    /// <summary>1.1.4 (user): coming back from the overview, the focus goes back onto the entry button.</summary>
    internal static bool ReturnFocus;
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
        if (ReturnFocus) { ReturnFocus = false; if (GamepadSupport.IsGamepad) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(gameObject); }
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
