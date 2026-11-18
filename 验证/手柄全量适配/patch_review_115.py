from pathlib import Path
root = Path(__file__).resolve().parents[2]
def edit(rel, old, new, count=1):
    p = root / rel; s = p.read_text(encoding="utf-8")
    assert s.count(old) >= 1, (rel, old[:90])
    s = s.replace(old, new, count); p.write_text(s, encoding="utf-8")

hv = "src/Overview/OverviewHotkeyView.cs"
# #1: preset flag is passed per recording and always cleared; native single key in preset mode goes into the profile
edit(hv, "            if(native&&used.Length==0)\n            {",
         "            if(native&&used.Length==0&&!_comboToPreset)\n            {")
edit(hv, "private void EndComboRecording(){_comboRecording=false;_comboEntry=null;_take.Clear();GamepadLock.End();",
         "private void EndComboRecording(){_comboRecording=false;_comboEntry=null;_comboToPreset=false;_take.Clear();GamepadLock.End();ComboRecordingActive=false;")
edit(hv, "GamepadLock.Begin(_root.gameObject); // the A that started recording is already down: it never joins the take",
         "GamepadLock.Begin(_root.gameObject); ComboRecordingActive=true; // the A that started recording is already down: it never joins the take")
edit(hv, "    private bool _comboToPreset;",
         "    private bool _comboToPreset;\n    /// <summary>Any controller recording in progress (read by row actions such as Y = key menu).</summary>\n    internal static bool ComboRecordingActive;")
# #2: no popup while recording
edit(hv, "    private void Popup(", "    private void PopupGuarded(")
s = (root / hv).read_text(encoding="utf-8")
# reintroduce Popup wrappers: find the signature of PopupGuarded to build matching overloads
import re
sigs = re.findall(r"    private void PopupGuarded\(([^)]*)\)", s)
print("popup overloads:", sigs)
p = root / hv
s = s.replace("PopupGuarded(", "Popup(")
s = re.sub(r"(    private void Popup\(([^)]*)\)\s*\{)", lambda m: m.group(1) + "\n        if(_capture.IsCapturing||_comboRecording)return; // 1.1.5 review: no menu while recording", s)
p.write_text(s, encoding="utf-8")
# #4: device change / controller gone ends recording
edit(hv, "        if(pad==null)return;\n        string modifier=GamepadCombos.Modifier;",
         "        if(pad==null||!GamepadSupport.IsGamepad){EndComboRecording();_status(Loc.T(\"Recording stopped: the controller is no longer the input device.\",\"录制已停止：当前输入设备已不是手柄。\",\"錄製已停止：目前輸入裝置已不是手把。\"));return;}\n        string modifier=GamepadCombos.Modifier;")
# #9: a dialog over the page ends recording
edit(hv, "        string modifier=GamepadCombos.Modifier;\n        bool native=GamepadCombos.NativeGamepad(entry.Config);",
         "        if(OverviewUi.HasModal){EndComboRecording();_status(Loc.T(\"Recording stopped: a dialog opened.\",\"录制已停止：有对话框弹出。\",\"錄製已停止：有對話框彈出。\"));return;}\n        string modifier=GamepadCombos.Modifier;\n        bool native=GamepadCombos.NativeGamepad(entry.Config);")
# #10: restore the navigation flag that was in effect before recording
edit(hv, "GamepadIntent.ReleaseAll(); var system=EventSystem.current; if(system!=null)system.sendNavigationEvents=false;\n        GamepadLock.Begin",
         "GamepadIntent.ReleaseAll(); var system=EventSystem.current; _navigationBefore=system==null||system.sendNavigationEvents; if(system!=null)system.sendNavigationEvents=false;\n        GamepadLock.Begin")
edit(hv, "HoldRing.Hide(_root);var system=EventSystem.current;if(system!=null)system.sendNavigationEvents=true;}",
         "HoldRing.Hide(_root);var system=EventSystem.current;if(system!=null)system.sendNavigationEvents=_navigationBefore;}\n    private bool _navigationBefore=true;")

# #2 also: Y on a key cell (context action) does nothing while recording
ta = root / "src/Overview"
for f in ta.glob("*.cs"):
    t = f.read_text(encoding="utf-8")
    if "class OverviewHotkeyTextAction" in t:
        print("text action in", f.name)

# #5: Back/View does not toggle the prompt strip while recording
edit("src/Overview/OverviewPage.Gamepad.cs",
     "if (pad.selectButton.wasPressedThisFrame && !pad.rightTrigger.isPressed)",
     "if (pad.selectButton.wasPressedThisFrame && !pad.rightTrigger.isPressed && _hotkeys?.IsRecording != true)")

# #11: ring starts from zero
edit("src/Overview/HoldRing.cs",
     "        ring.gameObject.SetActive(true); ring.transform.SetAsLastSibling();",
     "        if (!ring.gameObject.activeSelf) ring._shown = 0f; // a new hold starts empty\n        ring.gameObject.SetActive(true); ring.transform.SetAsLastSibling();")

# #6 candidate A: baseline includes merged temporary rules
edit("src/Overview/OverviewRepository.Pending.cs",
     "    private void MarkSavedDraftBaseline()\n    {\n        InvalidatePendingView();",
     "    private void MarkSavedDraftBaseline()\n    {\n        MergeEditedRules(); // 1.1.5 review: GetPendingState merges these later; the baseline must include them too\n        InvalidatePendingView();")
edit("src/Overview/OverviewRepository.Profiles.cs",
     "    public bool IsProfileDirty => !_appliedView && _savedDraftSignature != DraftSignature(State, _draftConfig, _draftMods);",
     "    public bool IsProfileDirty { get { if (_appliedView) return false; MergeEditedRules(); return _savedDraftSignature != DraftSignature(State, _draftConfig, _draftMods); } }")
# #7: follow only for the applied (live) profile; keep the baseline signature in step either way
edit("src/Overview/OverviewRepository.Profiles.cs",
     "        if (_appliedView || guid == Plugin.PluginGuid) return;\n        string file = RelativeConfig",
     "        if (_appliedView || guid == Plugin.PluginGuid || ViewedProfileId != Profiles.AppliedId) return; // a previewed profile is not the live one\n        string file = RelativeConfig")
edit("src/Overview/OverviewRepository.Profiles.cs",
     "        if (wasClean) _savedDraftSignature = DraftSignature(State, _draftConfig, _draftMods);",
     "        // The saved baseline holds the live value too: recompute its signature from the baseline itself, so undoing\n        // other edits later still reads as clean (review #7).\n        _savedDraftSignature = wasClean ? DraftSignature(State, _draftConfig, _draftMods) : DraftSignature(_savedDraftProfile.state, _savedDraftProfile.config, _savedDraftProfile.mods);")
print("ok")
