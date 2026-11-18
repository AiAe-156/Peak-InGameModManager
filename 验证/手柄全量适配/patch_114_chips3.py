# -*- coding: utf-8 -*-
"""1.1.4 (user): ribbon frame gone on every key cell, "Not set" as a muted rounded chip, focus stays on the cell rect;
chip font: the game's own LiberationSans (the OS-font route fails on this PC, so chips fell back to the heavy Daruma);
modifier change: combos stored with an older modifier now follow the current one (Load normalises)."""
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

edit('KeyChips.cs', [
('''        bool dimFirst = false, bool centre = false, float fontSize = 14f)''', '''        bool dimFirst = false, bool centre = false, float fontSize = 14f, bool muted = false)'''),
('''            bool dim = dimFirst && chips.Count < keys.Count - 1;''', '''            bool dim = muted || (dimFirst && chips.Count < keys.Count - 1);'''),
('''            SetRegular(text, font);
            text.text = label;''', '''            SetRegular(text, font, label);
            text.text = label;'''),
('''            SetRegular(sign, font);''', '''            SetRegular(sign, font, "+");'''),
('''    private static void SetRegular(TextMeshProUGUI text, TMP_FontAsset? fallback)
    {
        var regular = Overview.MceRegularFont.Get(fallback);
        if (regular != null) { text.font = regular; text.fontSharedMaterial = regular.material; }
        else if (fallback != null) text.font = fallback;
    }''', '''    /// <summary>Thin key letters: the game ships LiberationSans SDF (TMP's default font), a plain regular sans. The OS-font
    /// route (MceRegularFont) fails on some PCs ("Unable to load font face for [Arial]"), which left chips on the heavy
    /// Daruma font. Labels LiberationSans cannot draw (Chinese "Not set", arrows) keep the localised game font.</summary>
    private static void SetRegular(TextMeshProUGUI text, TMP_FontAsset? fallback, string label)
    {
        var sans = TMP_Settings.defaultFontAsset;
        if (sans != null && sans.HasCharacters(label, out _, false, false)) { text.font = sans; text.fontSharedMaterial = sans.material; return; }
        if (fallback != null) text.font = fallback;
        Overview.MceTextWeight.Apply(text);
    }'''),
])

edit('GamepadCombos.cs', [
('''            _items.RemoveAll(i => i == null || string.IsNullOrEmpty(i.id) || !Buttons.Contains(i.button) || i.Mod == i.button || (i.Mod.Length > 0 && !Modifiers.Contains(i.Mod)));
        }''', '''            _items.RemoveAll(i => i == null || string.IsNullOrEmpty(i.id) || !Buttons.Contains(i.button) || i.Mod == i.button || (i.Mod.Length > 0 && !Modifiers.Contains(i.Mod)));
            // 1.1.4 (user: "modifier is Back, the list still shows LT + X"): all combos share the one modifier. Files from
            // 1.1.2/1.1.3 hold combos recorded with LT while the stored modifier is something else; bring them in line.
            if (_modifier.Length == 0) { var first = _items.FirstOrDefault(i => i.Mod.Length > 0); if (first != null) _modifier = first.Mod; }
            else if (_items.Any(i => i.Mod.Length > 0 && i.Mod != _modifier))
            {
                _items.RemoveAll(i => i.Mod.Length > 0 && i.button == _modifier);
                foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = _modifier;
                Save();
            }
        }'''),
('''            _items.RemoveAll(i => i.id == preset.id || (i.button == preset.button && i.Mod == preset.Mod));
            _items.Add(new Item { id = preset.id, guid = preset.guid, section = preset.section, key = preset.key, modifier = preset.Mod, button = preset.button });''',
 '''            string mod = preset.Mod.Length > 0 && _modifier.Length > 0 ? _modifier : preset.Mod; // shared modifier
            if (mod.Length > 0 && preset.button == mod) continue;
            _items.RemoveAll(i => i.id == preset.id || (i.button == preset.button && i.Mod == mod));
            _items.Add(new Item { id = preset.id, guid = preset.guid, section = preset.section, key = preset.key, modifier = mod, button = preset.button });'''),
])

edit('Overview/OverviewHotkeyView.cs', [
('''                        if(!native)KeyChipsOn(current,RawKey(entry));''', '''                        KeyChipsOn(current,native?"":RawKey(entry));'''),
('''                        if(combo!=null)KeyChipsOn(pad,combo);
                        else if(native&&RawKey(entry).Length>0)KeyChipsOn(pad,RawKey(entry));
                        else{var empty=pad.GetComponentInChildren<TMP_Text>(true);if(empty!=null){empty.text=Loc.T("Not set","未设置","未設定");empty.color=PadMuted;}}''',
 '''                        if(combo!=null)KeyChipsOn(pad,combo);
                        else KeyChipsOn(pad,native?RawKey(entry):"");'''),
('''                        if(showPad){StylePad(original);if(native&&!OverviewHotkeyAdapter.IsNone(defaultRaw))KeyChipsOn(original,defaultRaw);else{var t=original.GetComponentInChildren<TMP_Text>(true);if(t!=null){t.text=Loc.T("Not set","未设置","未設定");t.color=PadMuted;}}}
                        else{string raw=RawOf(entry.Config.DefaultValue);if(raw.Length>0)KeyChipsOn(original,raw);}''',
 '''                        if(showPad){StylePad(original);KeyChipsOn(original,native&&!OverviewHotkeyAdapter.IsNone(defaultRaw)?defaultRaw:"");}
                        else KeyChipsOn(original,RawOf(entry.Config.DefaultValue));'''),
('''                        if(showPad){StylePad(pack);if(packCombo!=null)KeyChipsOn(pack,packCombo);else{var t=pack.GetComponentInChildren<TMP_Text>(true);if(t!=null){t.text=Loc.T("Not set","未设置","未設定");t.color=PadMuted;}}}
                        else if(entry.PackPreset!=null&&!entry.PackPreset.cleared){string raw="";try{raw=RawOf(entry.Adapter.Deserialize(entry.PackPreset.value));}catch{raw="";}if(raw.Length>0)KeyChipsOn(pack,raw);}''',
 '''                        if(showPad){StylePad(pack);if(packCombo!=null)KeyChipsOn(pack,packCombo);else KeyChipsOn(pack,"");}
                        else{string raw="";if(entry.PackPreset!=null&&!entry.PackPreset.cleared)try{raw=RawOf(entry.Adapter.Deserialize(entry.PackPreset.value));}catch{raw="";}KeyChipsOn(pack,raw);}'''),
('''        foreach(var part in label.Split('+').Select(x=>x.Trim()).Where(x=>x.Length>0)) keys.Add(KeyChips.Describe(part));
        if(keys.Count==0)return;
        var chipHost=OverviewUi.Rect("KeyChips",host);OverviewUi.Fill(chipHost);
        KeyChips.Build(chipHost,keys,text.font,24,dimFirst:false,centre:true,fontSize:15);
        Frameless(button);''', '''        foreach(var part in label.Split('+').Select(x=>x.Trim()).Where(x=>x.Length>0)) keys.Add(KeyChips.Describe(part));
        var chipHost=OverviewUi.Rect("KeyChips",host);OverviewUi.Fill(chipHost);
        // 1.1.4 (user): no ribbon on key cells; an empty cell is a muted "Not set" chip. The cell itself (fixed rect, the
        // old ribbon's size) keeps the click target and the focus ring.
        if(keys.Count==0)KeyChips.Build(chipHost,new List<(string,bool)>{(Loc.T("Not set","未设置","未設定"),false)},text.font,24,centre:true,fontSize:14,muted:true);
        else KeyChips.Build(chipHost,keys,text.font,24,dimFirst:false,centre:true,fontSize:15);
        Frameless(button);'''),
('''        if(combo.Mod.Length>0)keys.Add((GamepadCombos.ButtonName(combo.Mod),true));''',
 '''        string mod=combo.Mod.Length>0&&GamepadCombos.Modifier.Length>0?GamepadCombos.Modifier:combo.Mod; // one shared modifier
        if(mod.Length>0)keys.Add((GamepadCombos.ButtonName(mod),true));'''),
('''        KeyChips.Build(chipHost,keys,text.font,24,dimFirst:combo.Mod.Length>0,centre:true,fontSize:15);''',
 '''        KeyChips.Build(chipHost,keys,text.font,24,dimFirst:mod.Length>0,centre:true,fontSize:15);'''),
])
print('ok')
