# -*- coding: utf-8 -*-
"""1.1.4 (user): chips still too heavy (game font + outline); preset columns as chips too; no ribbon frame behind chips;
menu wording keeps the column names marked as proper nouns."""
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
('''            if (font != null) text.font = font;
            // Game font (the one its own glyphs use), medium weight via a light outline instead of the blurry synthetic bold.
            if (font != null) text.font = font;
            text.text = label; text.fontSize = fontSize; text.fontStyle = FontStyles.Normal; text.richText = false; text.parseCtrlCharacters = false;
            text.outlineWidth = 0.12f; text.outlineColor = new Color32(30, 30, 34, 255);''',
 '''            // 1.1.4 (user: "still too thick"): the game font is heavy by design and the outline thickened it again.
            // Plain regular sans, no outline, no shadow: thin dark letters on the cream face, like the official glyphs.
            SetRegular(text, font);
            text.text = label; text.fontSize = fontSize; text.fontStyle = FontStyles.Normal; text.richText = false; text.parseCtrlCharacters = false;'''),
('''            if (font != null) sign.font = font;
            sign.text = "+";''', '''            SetRegular(sign, font);
            sign.text = "+";'''),
('''    /// <summary>Builds one or more chips into''', '''    private static void SetRegular(TextMeshProUGUI text, TMP_FontAsset? fallback)
    {
        var regular = Overview.MceRegularFont.Get(fallback);
        if (regular != null) { text.font = regular; text.fontSharedMaterial = regular.material; }
        else if (fallback != null) text.font = fallback;
    }

    /// <summary>Builds one or more chips into'''),
])

edit('Overview/OverviewHotkeyView.cs', [
# keyboard-mode default / preset columns as chips
('''                        if(showPad){StylePad(original);if(native&&!OverviewHotkeyAdapter.IsNone(defaultRaw))KeyChipsOn(original,defaultRaw);else{var t=original.GetComponentInChildren<TMP_Text>(true);if(t!=null){t.text=Loc.T("Not set","未设置","未設定");t.color=PadMuted;}}}''',
 '''                        if(showPad){StylePad(original);if(native&&!OverviewHotkeyAdapter.IsNone(defaultRaw))KeyChipsOn(original,defaultRaw);else{var t=original.GetComponentInChildren<TMP_Text>(true);if(t!=null){t.text=Loc.T("Not set","未设置","未設定");t.color=PadMuted;}}}
                        else{string raw=RawOf(entry.Config.DefaultValue);if(raw.Length>0)KeyChipsOn(original,raw);}'''),
('''                        if(showPad){StylePad(pack);if(packCombo!=null)KeyChipsOn(pack,packCombo);else{var t=pack.GetComponentInChildren<TMP_Text>(true);if(t!=null){t.text=Loc.T("Not set","未设置","未設定");t.color=PadMuted;}}}''',
 '''                        if(showPad){StylePad(pack);if(packCombo!=null)KeyChipsOn(pack,packCombo);else{var t=pack.GetComponentInChildren<TMP_Text>(true);if(t!=null){t.text=Loc.T("Not set","未设置","未設定");t.color=PadMuted;}}}
                        else if(entry.PackPreset!=null&&!entry.PackPreset.cleared){string raw="";try{raw=RawOf(entry.Adapter.Deserialize(entry.PackPreset.value));}catch{raw="";}if(raw.Length>0)KeyChipsOn(pack,raw);}'''),
# chips: no ribbon frame behind them
('''        var chipHost=OverviewUi.Rect("KeyChips",host);OverviewUi.Fill(chipHost);
        KeyChips.Build(chipHost,keys,text.font,24,dimFirst:false,centre:true,fontSize:15);
    }''', '''        var chipHost=OverviewUi.Rect("KeyChips",host);OverviewUi.Fill(chipHost);
        KeyChips.Build(chipHost,keys,text.font,24,dimFirst:false,centre:true,fontSize:15);
        Frameless(button);
    }
    /// <summary>1.1.4 (user): a cell showing key chips drops the ribbon frame behind them; the cell stays clickable and
    /// focusable (transparent, not disabled, so it still takes the pointer and shows the focus ring).</summary>
    private static void Frameless(Button button)
    {
        var style=button.GetComponent<UiActionStyle>();if(style!=null){style.enabled=false;UnityEngine.Object.Destroy(style);}
        var peak=button.GetComponent<PEAKLib.UI.Elements.PeakMenuButton>();if(peak==null)return;
        foreach(var image in new[]{peak.Panel,peak.BorderTop,peak.BorderBottom})if(image!=null){var c=image.color;c.a=0;image.color=c;}
    }'''),
('''        KeyChips.Build(chipHost,keys,text.font,24,dimFirst:combo.Mod.Length>0,centre:true,fontSize:15);
    }''', '''        KeyChips.Build(chipHost,keys,text.font,24,dimFirst:combo.Mod.Length>0,centre:true,fontSize:15);
        Frameless(button);
    }'''),
('''    private static string RawKey(OverviewHotkeyEntry entry)
    {
        object value=entry.Config.BoxedValue;''', '''    private static string RawKey(OverviewHotkeyEntry entry)=>RawOf(entry.Config.BoxedValue);
    private static string RawOf(object? value)
    {'''),
# menu wording: column names marked as proper nouns (user marked them with backticks / spaces)
('''(Loc.T("Reset to the mod's default key","重设为模组默认预设的快捷键","重設為模組預設的快捷鍵")''',
 '''(Loc.T("Reset to the \\"Mod default\\" key","重设为「模组默认」预设的快捷键","重設為「模組預設」的快捷鍵")'''),
('''(Loc.T("Reset to the key set in the current profile preset","重设为当前方案预设中设定的快捷键","重設為目前方案預設中設定的快捷鍵")''',
 '''(Loc.T("Reset to the key set in the current \\"Profile preset\\"","重设为当前「方案预设」中设定的快捷键","重設為目前「方案預設」中設定的快捷鍵")'''),
('''(Loc.T("Set the current key as the current profile preset","将当前快捷键设为当前的方案预设","將目前快捷鍵設為目前的方案預設")''',
 '''(Loc.T("Set the current key as the current \\"Profile preset\\"","将「当前快捷键」设为当前的「方案预设」","將「目前快捷鍵」設為目前的「方案預設」")'''),
])
print('ok')
