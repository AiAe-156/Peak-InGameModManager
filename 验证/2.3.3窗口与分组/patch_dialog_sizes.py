import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "Overview")


def edit(name, pairs):
    path = os.path.join(ROOT, name)
    s = io.open(path, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in s else "\n"
    for a, b in pairs:
        a = a.replace("\n", nl)
        b = b.replace("\n", nl)
        n = s.count(a)
        assert n == 1, (name, n, a[:70])
        s = s.replace(a, b)
    io.open(path, "w", encoding="utf-8", newline="").write(s)


edit("OverviewGroupEditor.cs", [
    ("        panel.anchorMin = new Vector2(.18f, .16f); panel.anchorMax = new Vector2(.82f, .84f);",
     "        Size(panel, 760, 330);"),
    ('var panel = OverviewUi.Panel("Confirmation", shade, OverviewUi.Brown); panel.anchorMin = new Vector2(.18f, .27f); panel.anchorMax = new Vector2(.82f, .73f); panel.offsetMin = panel.offsetMax = Vector2.zero;',
     'var panel = OverviewUi.Panel("Confirmation", shade, OverviewUi.Brown); Size(panel, 760, 250);'),
    ("    public bool TryCloseTop()",
     "    /// <summary>Secondary prompts hold a title, a line or two and two buttons: a fixed size, centred, capped by the screen\n"
     "    /// (they used to fill 64% x 46-68% of it).</summary>\n"
     "    private void Size(RectTransform panel, float width, float height)\n"
     "    {\n"
     "        var screen = ((RectTransform)_modal!.transform).rect;\n"
     "        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.anchoredPosition = Vector2.zero;\n"
     "        panel.sizeDelta = new Vector2(Mathf.Min(width, Mathf.Max(380, screen.width - 60)), Mathf.Min(height, Mathf.Max(220, screen.height - 60)));\n"
     "    }\n"
     "    public bool TryCloseTop()"),
])
edit("OverviewUi.cs", [
    ('var panel = Panel("Warning", shade, Brown); panel.anchorMin = new Vector2(.20f, .27f); panel.anchorMax = new Vector2(.80f, .73f); panel.offsetMin = panel.offsetMax = Vector2.zero;',
     'var panel = Panel("Warning", shade, Brown); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.anchoredPosition = Vector2.zero;\n'
     '        { var screen = ((RectTransform)ModalParent).rect; panel.sizeDelta = new Vector2(Mathf.Min(820, Mathf.Max(380, screen.width - 60)), Mathf.Min(360, Mathf.Max(260, screen.height - 60))); }'),
    ('var panel = Panel("ChoicePanel", shade, Brown); panel.anchorMin = new Vector2(.25f, .13f); panel.anchorMax = new Vector2(.75f, .87f); panel.offsetMin = panel.offsetMax = Vector2.zero;',
     'var panel = Panel("ChoicePanel", shade, Brown); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.anchoredPosition = Vector2.zero;\n'
     '        { var screen = ((RectTransform)ModalParent).rect; panel.sizeDelta = new Vector2(Mathf.Min(640, Mathf.Max(380, screen.width - 60)), Mathf.Min(60 + options.Count * 45 + 65 + 10, Mathf.Max(260, screen.height - 60))); }'),
])
print("ok")
