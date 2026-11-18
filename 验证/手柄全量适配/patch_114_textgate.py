# -*- coding: utf-8 -*-
"""1.1.4 (user): every search / text box on a controller must only start typing after A, and B must only leave typing
(never go back a page). Done once, for every TMP_InputField:
- OnSelect prefix: with a controller, a box that is merely focused does not activate (shouldActivateOnSelect off,
  the gate component added) - also for boxes built without MarkSelectable;
- the gate eats B while typing: deactivates the field and swallows the back request for this frame and the next,
  so the page (GetParentPage / overview Back / keybindings page) does not also leave."""
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

edit('GamepadSupport.cs', [
('''            MethodInfo? inputUpdate = AccessTools.Method(typeof(InputHandler), "Update", Type.EmptyTypes);''',
 '''            // 1.1.4 (user): any text box, with a controller: focus ≠ typing; A starts typing, B only stops typing.
            MethodInfo? fieldSelect = AccessTools.Method(typeof(TMP_InputField), "OnSelect", new[] { typeof(BaseEventData) });
            if (fieldSelect != null) harmony.Patch(fieldSelect, prefix: new HarmonyMethod(typeof(GamepadSupport), nameof(InputFieldSelectPrefix)));
            MethodInfo? inputUpdate = AccessTools.Method(typeof(InputHandler), "Update", Type.EmptyTypes);'''),
('''    internal sealed class InputFieldGate : MonoBehaviour, ISubmitHandler
    {''', '''    /// <summary>Before TMP activates a box on selection: with a controller it must wait for A.</summary>
    private static void InputFieldSelectPrefix(TMP_InputField __instance)
    {
        if (!IsGamepad || __instance == null) return;
        __instance.shouldActivateOnSelect = false;
        if (__instance.GetComponent<InputFieldGate>() == null) __instance.gameObject.AddComponent<InputFieldGate>();
    }
    /// <summary>Frame until which a back request (B) is swallowed because it only left a text box.</summary>
    internal static int SwallowBackUntil = -1;
    internal static bool BackSwallowed => Time.frameCount <= SwallowBackUntil;

    internal sealed class InputFieldGate : MonoBehaviour, ISubmitHandler
    {'''),
('''        private void Update()
        {
            if (_steamFrames > 0) { _steamFrames--; if (_input != null && _input.isFocused) _input.DeactivateInputField(); }
            bool focused = _input != null && _input.isFocused;''', '''        private void Update()
        {
            if (_steamFrames > 0) { _steamFrames--; if (_input != null && _input.isFocused) _input.DeactivateInputField(); }
            // B while typing: leave typing only; the page must not go back on the same press.
            if (_input != null && _input.isFocused && IsGamepad && Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
            {
                _input.DeactivateInputField(); SwallowBackUntil = Time.frameCount + 1;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
            }
            bool focused = _input != null && _input.isFocused;'''),
('''        PanelFocus? focus = __instance.GetComponent<PanelFocus>();
        if (focus == null || !focus.ConsumeBack())''', '''        if (BackSwallowed) { __result = (__instance, NoopTransistion.Shared); return false; } // B only left a text box
        PanelFocus? focus = __instance.GetComponent<PanelFocus>();
        if (focus == null || !focus.ConsumeBack())'''),
])
# overview page back (B) and keybindings recording/back paths
edit('Overview/OverviewController.cs', [
('''    public (UIPage, PageTransistion) GetParentPage()
    {''', '''    public (UIPage, PageTransistion) GetParentPage()
    {
        if (GamepadSupport.BackSwallowed) return (this, GamepadSupport.NoopTransistion.Shared); // B only left a text box'''),
])
print('ok')
