using System;
using System.IO;
using System.Linq;
using ModConfigEnhance.Overview;

// Isolated policy/geometry checks for the compact dropdown + source-refresh work.
// No Unity runtime: source-contract assertions plus direct calls into the production UiGeometry.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));

string menu = Read("src/Overview/OverviewUi.SourceToolsDropdown.cs");
string compact = Read("src/Overview/OverviewUi.CompactDropdown.cs");
string popover = Read("src/Overview/WindowPopover.cs");
string sourceTools = Read("src/Overview/OverviewPage.SourceTools.cs");
string toolbar = Read("src/Overview/OverviewPage.Toolbar.cs");
string ui = Read("src/Overview/OverviewUi.cs");

int count = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); count++; }
int Occurrences(string text, string needle) => (text.Length - text.Replace(needle, "").Length) / needle.Length;

// ── Gate 1: an owned popover must never destroy the window that owns it ─────────────
Check(Occurrences(menu, "CloseModal()") == 2, "owned menu only reaches CloseModal through the non-owned branch");
Check(menu.Contains("void CloseMenu() { if (owned) owner!.GetComponent<WindowPopover>()?.Close(); else CloseModal(); }"),
    "row activation closes the owned shade, not the modal stack");
Check(menu.Contains("if (owned) owner!.GetComponent<WindowPopover>()?.Close(); else CloseModal();"),
    "opening a replacement menu closes the owned shade, not the modal stack");
Check(menu.Contains("if (owned) WindowPopover.Adopt(owner!, shade); else _modal = shade.gameObject;"),
    "owned shade is adopted by the window instead of becoming the page modal");
Check(!menu.Contains("CloseModal(); item.Execute();"), "row activation never force-closes the page modal");
Check(popover.Contains("internal void Close(){if(_shade!=null){_shade.gameObject.SetActive(false);Destroy(_shade.gameObject);_shade=null;}}"),
    "WindowPopover.Close destroys only its own shade");
Check(!popover.Contains("OverviewUi.CloseModal"), "WindowPopover never calls the page modal stack");
Check(popover.Contains("owner.Close(); owner._shade = shade; _active = owner;"), "Adopt replaces the previous shade before adopting");
Check(popover.Contains("_active.Close();_closedFrame=Time.frameCount;return true;"), "back/Escape consumes exactly one close per frame");
Check(popover.Contains("return _closedFrame==Time.frameCount;"), "the same frame cannot close a second layer");

// ── Gate 2: the refresh control is its own hit target and fires once ────────────────
Check(Occurrences(sourceTools, "_sourceToolsRefreshButton.onClick.AddListener(") == 1, "refresh click handler is registered once");
Check(sourceTools.Contains("var hit = OverviewUi.Rect(\"UpdateCycleHit\", _sourceTools.transform);"), "hit target is a child of the sources button");
Check(sourceTools.Contains("var icon = OverviewUi.Rect(\"UpdateCycle\", hit); OverviewUi.Fill(icon, 6);"),
    "icon is a child of the hit target, so the nested Button consumes the click once");
Check(sourceTools.Contains("_sourceToolsRefresh.raycastTarget = false;"), "icon never competes for the raycast");
Check(sourceTools.Contains("background.raycastTarget = true;"), "hit target is the only raycast surface");
Check(sourceTools.Contains("OverviewUi.Place((RectTransform)_sourceToolsRefreshButton.transform, width - 54, 1, 32, 32);"),
    "hit slot is fixed at width-54, 32 wide");
Check(sourceTools.Contains("OverviewUi.PlaceClipped(_sourceToolsCaret, width - 19, 0, 12, 34);"), "caret slot is fixed at width-19, 12 wide");
Check(sourceTools.Contains("caption.margin = new Vector4(12, 0, 56, 0);"), "caption reserves the full 56px suffix area");
Check(toolbar.Contains("measured = Mathf.Max(minimum, caption.GetPreferredValues(label).x + 12 + 8 + 54);"),
    "sources width budget matches the caption margin plus the hit slot");
Check(toolbar.Contains("if (tool.Id == \"sources\") LayoutSourceTools(slot.Width);"), "suffix is re-laid out with the final slot width");

// Hit / caret / caption must stay disjoint at every toolbar width the layout can produce.
foreach (float width in new[] { 130f, 160f, 200f, 260f, 320f, 420f, 640f, 1200f })
{
    float hitLeft = width - 54, hitRight = width - 22, caretLeft = width - 19, caretRight = width - 7, captionRight = width - 56;
    Check(hitLeft >= 0 && hitRight <= caretLeft && caretRight <= width, "hit and caret do not overlap at width " + width);
    Check(captionRight <= hitLeft, "caption text stops before the hit target at width " + width);
    Check(hitRight - hitLeft == 32f && caretRight - caretLeft == 12f, "suffix slots keep their fixed sizes at width " + width);
}

// ── Gate 3: compact spacing and keyboard navigation ────────────────────────────────
Check(menu.Contains("heading.gameObject.SetActive(group.Label.Length > 0);"), "an empty group label hides its heading");
Check(menu.Contains("if (group.Label.Length > 0) { PlaceClipped(heading, 12, y, Mathf.Max(1, rowWidth - 24), headingHeight); y += headingHeight; }"),
    "a hidden heading also releases its vertical space");
Check(menu.Contains("if (y > 0)"), "the first group never draws a separator rule");
Check(menu.Contains("float rowHeight = Mathf.Max(32, Mathf.Ceil(label.GetPreferredValues(label.text).y) + 4);"), "rows grow with the real line height");
Check(menu.Contains("y += rowHeight + 2;"), "rows keep a 2px compact gap");
Check(menu.Contains("float width = Mathf.Clamp(measured, Mathf.Min(260, availableWidth), Mathf.Min(420, availableWidth));"),
    "menu width is clamped to the host and to the compact range");
Check(menu.Contains("mode = Navigation.Mode.Explicit, selectOnUp = i > 0 ? controls[i - 1] : controls[controls.Count - 1],"),
    "up navigation wraps to the last row");
Check(menu.Contains("selectOnDown = i + 1 < controls.Count ? controls[i + 1] : controls[0]"), "down navigation wraps to the first row");
Check(menu.Contains("if (item.Enabled) { controls.Add(button); if (item.Checked && selected == null) selected = button; }"),
    "disabled rows stay out of the navigation ring");
Check(menu.Contains("var navigation = row.navigation; navigation.selectOnLeft = row; navigation.selectOnRight = row;"),
    "an embedded checkbox hands left/right back to its row");
Check(menu.Contains("if (controls.Count > 0) EventSystem.current?.SetSelectedGameObject((selected ?? controls[0]).gameObject);"),
    "focus starts on the checked row, or the first row");
Check(menu.Contains("if (_host == null || _anchor == null || !_anchor.gameObject.activeInHierarchy) { _close(); return; }"),
    "the menu closes when its anchor disappears");
Check(compact.Contains("return SourceToolsDropdown(anchor, groups, (target, hint) => { }, owner);"), "the compact path reuses the shared renderer");
Check(ui.Contains("if (options.Count(item => !item.Header) <= 16)"), "the compact path counts only real options");

// The shared popover geometry must keep every compact menu inside the host and flip when needed.
foreach (var size in new[] { (1920f, 1080f), (1280f, 720f), (640f, 360f), (320f, 240f) })
foreach (var location in new[] { (0f, 0f), (.5f, .5f), (.9f, .9f) })
foreach (int options in new[] { 1, 6, 16 })
{
    var box = UiGeometry.Popover(size.Item1, size.Item2,
        new UiGeometry.Box(size.Item1 * location.Item1, size.Item2 * location.Item2, 130, 34), 420, 16 + options * 34);
    Check(box.X >= 0 && box.Y >= 0 && box.Width > 0 && box.Height >= 0
        && box.X + box.Width <= size.Item1 && box.Y + box.Height <= size.Item2,
        "compact menu stays inside " + size.Item1 + "x" + size.Item2);
}
var flipped = UiGeometry.Popover(1280, 720, new UiGeometry.Box(100, 660, 130, 34), 300, 260);
Check(flipped.Y < 660 && flipped.Height == 260, "a bottom toolbar menu flips above its anchor");
var narrow = UiGeometry.Popover(320, 240, new UiGeometry.Box(10, 40, 130, 34), 420, 400);
Check(narrow.Width <= 320 - 16 && narrow.Height <= 240 - 16, "an oversized menu is clamped to the host");

foreach (string suffix in new[] { "bak", "off", "disabled", "old", "custom.backup", "禁用" })
    Check(PublicationFilePolicy.Keep("BepInEx/plugins/Example/Example.dll." + suffix), "publication retains disabled DLL " + suffix);
Check(!PublicationFilePolicy.Keep("BepInEx/plugins/Example/Example.dll.mce-part"), "publication excludes updater temporary payload");
Check(!PublicationFilePolicy.Keep("BepInEx/plugins/Example/readme.bak"), "ordinary backup document remains excluded");
Check(Read("src/Overview/PackPublisher.cs").Contains("relative = PluginFileNames.OriginalPath(relative);"), "publisher projects all disabled suffixes through shared policy");
Console.WriteLine("PASS " + count + " dropdown suffix / lifecycle / navigation checks (no Unity runtime)");
