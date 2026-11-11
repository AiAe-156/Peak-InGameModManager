using System;
using System.IO;
using ModConfigEnhance.Overview;
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string adapter = File.ReadAllText(Path.Combine(root, "src/ModConfigCompactDropdown.cs"));
string layout = File.ReadAllText(Path.Combine(root, "src/ModConfigTreeLayoutPatch.cs"));
int count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
foreach (string guard in new[] { "dropdown.MultiSelect", "MaximumOptions = 12", "option.image != null", "string.IsNullOrWhiteSpace(option.text)", "GetComponentInChildren<TMP_InputField>", "GetComponentInChildren<InputField>", "control is Toggle or Scrollbar", "menu.FilterDropdown?.Dropdown", "OverviewUi.HasModal", "dropdown.IsInteractable()", "ReferenceEquals(dropdown.options[index], option)" })
    Check(adapter.Contains(guard), "native fallback / stale-choice guard: " + guard);
foreach (string call in new[] { "OverviewUi.SourceToolsDropdown(", "Checked = index == selected", "dropdown.value = index", "dropdown.RefreshShownValue()", "rows[selected].gameObject", "private void OnDisable() => Close()", "OverviewUi.ModalParent = _previousParent", "GamepadSupport.NoopTransistion.Shared", "if (_ownsParent) ReleaseParent()" })
    Check(adapter.Contains(call), "shared UI / native event / lifecycle contract: " + call);
Check(layout.Contains("ModConfigCompactDropdown.Install(harmony)"), "patch registration");
foreach (var size in new[] { (1920f,1080f), (1280f,720f), (640f,360f), (320f,240f) })
foreach (var location in new[] { (0f,0f), (.5f,.5f), (.9f,.9f) })
foreach (int options in new[] { 1, 3, 12 })
{
    var box = UiGeometry.Popover(size.Item1, size.Item2,
        new UiGeometry.Box(size.Item1*location.Item1, size.Item2*location.Item2, 90, 32), 420, 42 + options*34);
    Check(box.X >= 0 && box.Y >= 0 && box.Width > 0 && box.Height >= 0 && box.X+box.Width <= size.Item1 && box.Y+box.Height <= size.Item2,
        "shared geometry stays in viewport");
}
var flipped = UiGeometry.Popover(1280,720,new UiGeometry.Box(100,650,100,32),300,200);
Check(flipped.Y < 650 && flipped.Height == 200, "bottom settings menu flips upward");
Console.WriteLine("PASS " + count + " source-contract / production-geometry checks (no Unity runtime)");
