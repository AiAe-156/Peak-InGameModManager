from pathlib import Path
root = Path(__file__).resolve().parents[2]
def edit(rel, old, new, count=1):
    p = root / rel; s = p.read_text(encoding="utf-8")
    assert s.count(old) >= 1, (rel, old[:100])
    s = s.replace(old, new, count); p.write_text(s, encoding="utf-8")

# ── 1. reading surface: pass the move on at the edge (store description could not be left downward) ──
edit("src/Overview/Help/HelpPage.cs", """    internal Action<float>? MovePage;
    internal Action? Links, Leave;""", """    internal Action<float>? MovePage;
    internal Action? Links, Leave;
    /// <summary>1.1.5 (focus review): returns true when MovePage actually scrolled. When it did not (top / bottom reached),
    /// the move goes to the explicit neighbour instead, so the buttons below the text can be reached.</summary>
    internal Func<float, bool>? TryMovePage;
    internal Selectable? ExitUp, ExitDown;""")
edit("src/Overview/Help/HelpPage.cs", """    public override void OnMove(AxisEventData data)
    {
        if (data.moveDir == MoveDirection.Up) MovePage?.Invoke(-90);
        else if (data.moveDir == MoveDirection.Down) MovePage?.Invoke(90);
        else if (data.moveDir == MoveDirection.Left) Leave?.Invoke();
        else if (data.moveDir == MoveDirection.Right) Links?.Invoke();
        data.Use();
    }""", """    public override void OnMove(AxisEventData data)
    {
        if (data.moveDir is MoveDirection.Up or MoveDirection.Down && TryMovePage != null)
        {
            bool up = data.moveDir == MoveDirection.Up;
            if (TryMovePage(up ? -90 : 90)) { data.Use(); return; }
            var exit = up ? ExitUp : ExitDown;
            if (exit != null && exit.IsActive() && exit.IsInteractable()) { EventSystem.current?.SetSelectedGameObject(exit.gameObject); data.Use(); return; }
            data.Use(); return;
        }
        if (data.moveDir == MoveDirection.Up) MovePage?.Invoke(-90);
        else if (data.moveDir == MoveDirection.Down) MovePage?.Invoke(90);
        else if (data.moveDir == MoveDirection.Left) Leave?.Invoke();
        else if (data.moveDir == MoveDirection.Right) Links?.Invoke();
        data.Use();
    }""")
sw = "src/Overview/ShopWindow.cs"
edit(sw, "        surface.MovePage=delta=>{if(scroll.content==null)return;var p=scroll.content.anchoredPosition;float max=Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height);p.y=Mathf.Clamp(p.y+delta,0,max);scroll.content.anchoredPosition=p;};",
         "        surface.TryMovePage=delta=>{if(scroll.content==null)return false;var p=scroll.content.anchoredPosition;float max=Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height);float y=Mathf.Clamp(p.y+delta,0,max);if(Mathf.Abs(y-p.y)<.5f)return false;p.y=y;scroll.content.anchoredPosition=p;return true;};")
# exits: up = current tab, down = the nearest button below the text
edit(sw, "            if(control==_readingSurface)continue; // the reading surface handles its own moves",
         "            if(control==_readingSurface){_readingSurface.ExitUp=nav.selectOnUp;_readingSurface.ExitDown=nav.selectOnDown;var tab=_keys.FirstOrDefault(p=>p.Value==\"tab:\"+_tab).Key;if(tab!=null)_readingSurface.ExitUp=tab.GetComponent<Selectable>();continue;} // scrolls first, leaves at the edge")
# the reading surface spans the whole content; tabs and the buttons under it must not route through its centre
edit(sw, "            if(_readingSurface!=null&&_keys.TryGetValue(control.gameObject,out var k)&&k.StartsWith(\"tab:\"))nav.selectOnDown=_readingSurface;",
         "            if(_readingSurface!=null&&_keys.TryGetValue(control.gameObject,out var k)&&k.StartsWith(\"tab:\"))nav.selectOnDown=_readingSurface;\n            if(_readingSurface!=null&&nav.selectOnUp==_readingSurface&&Pos(control).y>Pos(_readingSurface).y)nav.selectOnUp=Nearest(control,all.Where(c=>c!=_readingSurface).ToList(),Vector2.up);\n            if(_readingSurface!=null&&control!=_readingSurface&&!(_keys.TryGetValue(control.gameObject,out var k2)&&k2.StartsWith(\"tab:\"))&&Pos(control).y<Pos(_readingSurface).y&&nav.selectOnUp!=null&&nav.selectOnUp.transform.IsChildOf(_body)==false)nav.selectOnUp=_readingSurface;")

# ── 2. shared navigation ──
pn = "src/Overview/PublicationNavigation.cs"
edit(pn, """    internal static void Attach(RectTransform panel)
    { var navigation = panel.GetComponent<PublicationNavigation>() ?? panel.gameObject.AddComponent<PublicationNavigation>(); navigation.Schedule(); }""",
"""    internal static void Attach(RectTransform panel)
    {
        // 1.1.5 (focus review P1): one scope per window. A nested Attach used to build a second, partial chain that
        // overwrote the outer one; reschedule the outer scope instead.
        var outer = panel.parent != null ? panel.parent.GetComponentInParent<PublicationNavigation>() : null;
        if (outer != null && panel.GetComponent<PublicationNavigation>() == null) { outer.Schedule(); return; }
        var navigation = panel.GetComponent<PublicationNavigation>() ?? panel.gameObject.AddComponent<PublicationNavigation>(); navigation.Schedule();
    }
    /// <summary>Re-link after controls were rebuilt, shown, hidden or switched on / off.</summary>
    internal static void Invalidate(Transform anywhere) { var scope = anywhere.GetComponentInParent<PublicationNavigation>(); if (scope != null) scope.Schedule(); }
    private int _signature;
    private void LateUpdate()
    {
        // Cheap change detection: rebuilt rows, shown / hidden halves and interactable flips re-link on their own.
        int hash = 17; foreach (var s in GetComponentsInChildren<Selectable>(false)) if (s.IsActive()) hash = hash * 31 + s.GetInstanceID() + (s.IsInteractable() ? 1 : 0);
        if (hash != _signature) { _signature = hash; Schedule(); }
    }""")
edit(pn, "        var controls = GetComponentsInChildren<Selectable>(false).Where(s => s is not Editor.DocSurface && s.IsActive() && s.IsInteractable()).ToList();",
         "        var controls = GetComponentsInChildren<Selectable>(false).Where(s => s is not Editor.DocSurface && s is not Scrollbar && s.IsActive() && s.IsInteractable() && s.navigation.mode != Navigation.Mode.None\n            && !(s.transform != transform && s.transform.parent != null && s.transform.parent.GetComponentInParent<PublicationNavigation>() != this)).ToList();")
edit(pn, """        int Section(Selectable control)
        {
            if (control.GetComponentInParent<ScrollRect>() is ScrollRect scroll && scroll.transform.IsChildOf(transform)) return 1;
            Transform root = control.transform; while (root.parent != null && root.parent != transform) root = root.parent;
            return root is RectTransform rect && rect.anchorMin.y == 0 && rect.anchorMax.y == 0 ? 2 : 0;
        }""", """        // 1.1.5 (focus review P3): sections by geometry. Controls in a scroll area are 1; outside one, a control below the
        // lowest scroll viewport is a bottom bar (2), otherwise a top bar (0). Anchors no longer decide it.
        var viewports = GetComponentsInChildren<ScrollRect>(false).Where(s => s.viewport != null || s.transform is RectTransform).Select(s => (RectTransform)(s.viewport != null ? s.viewport : s.transform)).ToList();
        float lowestBottom = float.MaxValue;
        foreach (var v in viewports) { var c = new Vector3[4]; v.GetWorldCorners(c); lowestBottom = Mathf.Min(lowestBottom, transform.InverseTransformPoint(c[0]).y); }
        int Section(Selectable control)
        {
            if (control.GetComponentInParent<ScrollRect>() is ScrollRect scroll && scroll.transform.IsChildOf(transform)) return 1;
            if (lowestBottom < float.MaxValue && transform.InverseTransformPoint(control.transform.position).y < lowestBottom) return 2;
            Transform root = control.transform; while (root.parent != null && root.parent != transform) root = root.parent;
            return root is RectTransform rect && rect.anchorMin.y == 0 && rect.anchorMax.y == 0 ? 2 : 0;
        }""")
edit(pn, """            var previous = controls[(i + controls.Count - 1) % controls.Count]; var next = controls[(i + 1) % controls.Count];
            control.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = previous, selectOnRight = next, selectOnUp = Vertical(true) ?? previous, selectOnDown = Vertical(false) ?? next };""",
"""            var previous = controls[(i + controls.Count - 1) % controls.Count]; var next = controls[(i + 1) % controls.Count];
            // 1.1.5 (focus review): left / right stay on the same row (no wrapping into the next row).
            Selectable? Side(bool right) => controls.Where(c => c != control && Section(c) == section && Mathf.Abs(Position(c).y - position.y) <= 14 && (right ? Position(c).x > position.x + 2 : Position(c).x < position.x - 2))
                .OrderBy(c => Mathf.Abs(Position(c).x - position.x)).FirstOrDefault();
            control.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = Side(false), selectOnRight = Side(true), selectOnUp = Vertical(true) ?? previous, selectOnDown = Vertical(false) ?? next };""")
# first focus: never a danger button
edit(pn, "        if (system != null && (system.currentSelectedGameObject == null || !system.currentSelectedGameObject.transform.IsChildOf(transform))) system.SetSelectedGameObject(controls[0].gameObject);",
         "        if (system != null && (system.currentSelectedGameObject == null || !system.currentSelectedGameObject.activeInHierarchy || !system.currentSelectedGameObject.transform.IsChildOf(transform)))\n            system.SetSelectedGameObject((controls.FirstOrDefault(c => c is not Button b || (b.GetComponent<UiActionStyle>() is not UiActionStyle st || st.Role != UiActionRole.Danger)) ?? controls[0]).gameObject);")
print("ok")
