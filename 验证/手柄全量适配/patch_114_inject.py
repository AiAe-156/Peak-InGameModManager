# -*- coding: utf-8 -*-
"""1.1.4: combos for controller-native settings press the mod's own controller button (virtual hold);
profile controller presets apply on full save."""
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

edit('GamepadCombos.cs', [
('''                    foreach (var code in CachedKeys(item, entry)) _nowHeld.Add(code);''',
 '''                    if (NativeGamepad(entry)) { var native = NativeControl(entry, pad); if (native != null) _nowPads.Add(native); }
                    else foreach (var code in CachedKeys(item, entry)) _nowHeld.Add(code);'''),
('''    private static readonly HashSet<KeyCode> _held = new(), _nowHeld = new();''',
 '''    private static readonly HashSet<KeyCode> _held = new(), _nowHeld = new();
    /// <summary>1.1.4: controller buttons pressed virtually for controller-native settings (a combo for such a setting
    /// makes the mod see its own button go down). Edge frames tracked like the keyboard keys.</summary>
    private static readonly HashSet<ButtonControl> _pads = new(), _nowPads = new();
    private static readonly Dictionary<ButtonControl, int> _padDown = new(), _padUp = new();'''),
('''            _nowHeld.Clear();
            if (pad != null) TrackModifiers(pad);''', '''            _nowHeld.Clear(); _nowPads.Clear();
            if (pad != null) TrackModifiers(pad);'''),
('''            _held.Clear(); foreach (var code in _nowHeld) _held.Add(code);''',
 '''            _held.Clear(); foreach (var code in _nowHeld) _held.Add(code);
            foreach (var control in _nowPads) if (!_pads.Contains(control)) _padDown[control] = Time.frameCount;
            foreach (var control in _pads) if (!_nowPads.Contains(control)) _padUp[control] = Time.frameCount;
            _pads.Clear(); foreach (var control in _nowPads) _pads.Add(control);'''),
('''        if (_items.Count == 0 && _held.Count == 0) return;''', '''        if (_items.Count == 0 && _held.Count == 0 && _pads.Count == 0) return;'''),
('''            if (control is KeyControl keyControl && control.device is Keyboard && _held.Count + _upFrame.Count > 0)
            {
                if (result) return;
                var code = ToKeyCode(keyControl.keyCode); if (code == KeyCode.None) return;
                result = kind == 0 ? Held(code) : kind == 1 ? Down(code) : Up(code);
            }''', '''            if (control is KeyControl keyControl && control.device is Keyboard && _held.Count + _upFrame.Count > 0)
            {
                if (result) return;
                var code = ToKeyCode(keyControl.keyCode); if (code == KeyCode.None) return;
                result = kind == 0 ? Held(code) : kind == 1 ? Down(code) : Up(code);
            }
            else if (!result && control.device is Gamepad && (_pads.Count > 0 || _padUp.Count > 0))
            {
                Tick();
                result = kind == 0 ? _pads.Contains(control)
                    : kind == 1 ? _padDown.TryGetValue(control, out int d) && d == Time.frameCount
                    : _padUp.TryGetValue(control, out int u) && u == Time.frameCount;
            }'''),
])
# full-profile save applies the profile's controller presets to this PC
edit('Overview/OverviewRepository.SaveScope.cs', [
('''            var all = ApplyProfileTransaction(mods, IsMainMenuScene, skipEnablement: skipEnablement, ignoreDependencyWarnings: ignoreDependencyWarnings);
            if (all.Success)
            {''', '''            var all = ApplyProfileTransaction(mods, IsMainMenuScene, skipEnablement: skipEnablement, ignoreDependencyWarnings: ignoreDependencyWarnings);
            if (all.Success)
            {
                GamepadCombos.ApplyPresets(State); // 1.1.4: the profile's controller keys become this PC's combos'''),
])
print('ok')
