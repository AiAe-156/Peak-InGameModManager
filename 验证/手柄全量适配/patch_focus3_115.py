from pathlib import Path
root = Path(__file__).resolve().parents[2]
def edit(rel, old, new, count=1):
    p = root / rel; s = p.read_text(encoding="utf-8")
    assert s.count(old) >= 1, (rel, old[:100])
    s = s.replace(old, new, count); p.write_text(s, encoding="utf-8")
mc = "src/Overview/OverviewManagementCenter.cs"
# scrollbars are not focus stops
edit(mc, "        var bar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>(); bar.targetGraphic = handle.GetComponent<Image>(); bar.handleRect = handle; bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;",
         "        var bar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>(); bar.targetGraphic = handle.GetComponent<Image>(); bar.handleRect = handle; bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;\n        bar.navigation = new Navigation { mode = Navigation.Mode.None }; // 1.1.5 (focus review P4): not a focus stop")
# every rebuilt page re-links and gets its focus back (it was done for Packs only)
edit(mc, """            if (tab == ManagementTab.Packs)
            {
                foreach (var scroll in host.GetComponentsInChildren<ScrollRect>(false))""", """            PublicationNavigation.Invalidate(host); // 1.1.5 (focus review P2): every rebuilt page re-links
            if (restoreFocus && tab != ManagementTab.Packs && (selected == null || !selected.activeInHierarchy))
            {
                var choices = host.GetComponentsInChildren<Selectable>(false).Where(s => s.IsInteractable() && s is not UnityEngine.UI.Scrollbar).ToList();
                var next = choices.Where(s => s.GetComponentInChildren<TMP_Text>(true)?.text == focusText).OrderBy(s => Vector3.SqrMagnitude(s.transform.position - focusPosition)).FirstOrDefault()
                    ?? choices.OrderBy(s => Vector3.SqrMagnitude(s.transform.position - focusPosition)).FirstOrDefault();
                if (next != null) EventSystem.current?.SetSelectedGameObject(next.gameObject);
            }
            if (tab == ManagementTab.Packs)
            {
                foreach (var scroll in host.GetComponentsInChildren<ScrollRect>(false))""")
# information page: switching halves re-links and moves the focus into the shown half
mg = "src/Overview/OverviewPage.Management.cs"
edit(mg, """            packHost.gameObject.SetActive(!individual); modHost.gameObject.SetActive(individual);
            if (packButton != null) UiActionStyle.Select(packButton, !individual);
            if (modButton != null) UiActionStyle.Select(modButton, individual);""", """            packHost.gameObject.SetActive(!individual); modHost.gameObject.SetActive(individual);
            if (packButton != null) UiActionStyle.Select(packButton, !individual);
            if (modButton != null) UiActionStyle.Select(modButton, individual);
            PublicationNavigation.Invalidate(host); // 1.1.5 (focus review IN1): the shown half joins the chain""")
print("ok")
