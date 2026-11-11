# Test-only static source scan (no Unity runtime). Reads production sources; writes nothing to them.
import re, pathlib, sys
src = pathlib.Path(__file__).resolve().parents[3] / "src"
def lines(name): return (src/"Overview"/name).read_text(encoding="utf-8").splitlines()
def find(name, pat):
    return [i+1 for i,l in enumerate(lines(name)) if re.search(pat,l)]
fails = 0
def check(ok, label, detail=""):
    global fails
    print(("PASS " if ok else "FAIL ")+label+(" | "+detail if detail and not ok else ""))
    fails += (not ok)
# 1 multiline: Input honours argument; nobody reassigns lineType after creation; caret only attached for single-line creation
check(any("input.lineType = multiline ?" in l for l in lines("OverviewUi.cs")), "OverviewUi.Input maps multiline->MultiLineNewline")
reassign = [(f.name,i+1) for f in (src.rglob("*.cs")) for i,l in enumerate(f.read_text(encoding="utf-8").splitlines()) if re.search(r"\.lineType\s*=(?!=)",l) and f.name not in ("OverviewUi.cs","OverviewDocumentView.cs")]  # OverviewDocumentView:313 builds its own IntroductionTextInput (intentional multiline, not OverviewUi.Input)
check(not reassign, "no caller forces/overrides lineType after Input()", str(reassign))
check(find("OverviewUi.cs", r"if \(!multiline\) root\.gameObject\.AddComponent<InputCaretLayout>"), "caret component only for single-line")
# 2 caret: TMP caretPosition already includes IME composition length (decompiled TMP_InputField.caretSelectPositionInternal)
dbl = find("InputCaretLayout.cs", r"caretPosition\+Input\.compositionString\.Length")
check(not dbl, "custom caret does not double-count IME composition", f"InputCaretLayout.cs line(s) {dbl}: TMP caretPosition getter = m_CaretSelectPosition + compositionLength; adding compositionString.Length again shifts caret right during IME")
check(bool(find("InputCaretLayout.cs", r"InverseTransformPoint")), "caret converted through text->viewport transform (scroll/canvas scale)")
check(bool(find("InputCaretLayout.cs", r"OnDisable")) and bool(find("InputCaretLayout.cs", r"customCaretColor=_originalCustom")), "caret restores original TMP caret on disable")
check(bool(find("InputCaretLayout.cs", r"SetActive\(!_input\.isFocused&&string\.IsNullOrEmpty")), "placeholder hidden on focus, restored when empty+unfocused")
# 3 popover: host-owned shade is Fill (not layout child); panel placed with fixed anchors; no LayoutGroup/Fitter in WindowPopover
wp = "\n".join(lines("WindowPopover.cs"))
check("LayoutGroup" not in wp and "ContentSizeFitter" not in wp, "WindowPopover has no layout components that could stretch it")
check("OverviewUi.Place(panel,box.X,box.Y,box.Width,box.Height)" in wp, "popover panel uses fixed top-left anchors from UiGeometry.Popover")
check("CloseModal" not in re.sub(r"///.*","",wp), "WindowPopover never calls CloseModal")
# 4 submission
ns = "\n".join(lines("NetworkUiSubmission.cs"))
check("OverviewUpdateQueue.SetNetwork(mode,address);" in ns and ns.index("SetNetwork")<ns.index("_mode=mode"), "state armed only after SetNetwork succeeds (throw leaves not pending)")
check("OnDisable()=>CancelObservation" in ns, "observation cancelled on disable")
stale = "OverviewUpdateQueue.LastError" in ns and "_baselineError" not in ns and "ErrorAtSubmit" not in ns
check(not stale, "submission error is attributed to this submit (baseline of LastError captured)", "NetworkUiSubmission.cs:29-30 reads global LastError (_error, set from queue snapshot/worker-error/read failures) without a pre-submit baseline: a stale/unrelated error >1s after Submit is reported as this submit's failure and observation stops")
late = "_mode=null;_status?.Invoke(Loc.LocalizeMessage(error))" in ns.replace(" ","") or "_mode=null;_status?.Invoke(Loc.LocalizeMessage(error));" in ns
check(not late, "after error/timeout a late worker confirmation still reaches _applied (dirty baseline)", "NetworkUiSubmission.cs:30-31 set _mode=null on error/15s timeout, dropping _applied; if the worker applies the network later, OverviewNetworkSettings._initial is never updated (HasDirty stays true) and no 'now active' feedback appears")
print("SOURCE SCAN FAILS=%d"%fails); sys.exit(1 if fails else 0)
