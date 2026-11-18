# -*- coding: utf-8 -*-
"""1.1.4 docs, changelogs, version bump."""
import io, os, re
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))

def edit(rel, pairs):
    p = os.path.join(ROOT, rel)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (rel, a[:80], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

def ins(rel, before, text): edit(rel, [(before, text + before)])

ins('CHANGELOG.md', '## 1.1.3 — 2026-11-12', '''## 1.1.4 — 2026-11-12

- **Keybindings take effect at once:** recording a key on the keybindings tab writes it to the mod straight away (no separate save step).
- **Controller column for every hotkey:** A on a controller cell records a controller button or "modifier + button". For a mod that has its own controller setting, a single button is written into that setting; a combo is kept by the manager, which presses the mod's own button for it in game (same as for keyboard-only mods). Recording: tap B to record B, hold B for a second to cancel.
- **Look:** the controller column, the modifier picker and expand/collapse-all (now right above that column) use their own teal colour; empty cells say "Not set"; key chips use a crisp regular font and a "+" between keys.
- **Right two columns follow the input device:** with a controller, "Mod default" shows the mod's controller default and "Profile preset" the controller key recorded in the profile (profiles now carry controller keys and apply them when saved); with keyboard and mouse they show keyboard keys.
- **Sliders:** slim track and round knob, wider; sliders whose maximum is effectively unlimited (e.g. 3.4×10³⁸) step sensibly and the knob moves with the controller.

''')
ins('README.md', '## 1.1.3 手柄列、修饰键选择与设置格对齐', '''## 1.1.4 快捷键直改直用、手柄列全面可录、滑条重做

- 快捷键页录完立即写入模组配置，不再需要另外保存。
- 每个快捷键的手柄列都能录：按 A 后按一个手柄键或「修饰键 + 键」。模组自己有手柄键设置的，单键直接写进它的设置；组合由管理器在局内替它按下它自己的键（和只认键盘的模组同一套做法）。录制时轻按 B 是录 B，长按 B 一秒取消。
- 手柄列、修饰键选择和全部展开 / 收起（挪到手柄列正上方）用专属青色；空格子显示「未设置」；按键小方块换清晰的常规字体，组合键之间加「+」。
- 「模组默认」「方案预设」两列跟随输入设备：用手柄时显示模组自带的手柄默认键和方案里记录的手柄键（方案现在能带手柄键，保存方案时应用），用键鼠时显示键盘键。
- 滑条：细轨道、圆形滑块、加长；上限近乎无限（如 3.4×10³⁸）的滑条步进正常，手柄左右能拖动滑块。

''')
for lang, before, text in [
 ('zh-cn', '## 1.1.3 — 手柄列、修饰键选择与设置格对齐 {#v1-1-3}', '''## 1.1.4 — 快捷键直改直用、手柄列全面可录、滑条重做 {#v1-1-4}

- 快捷键页录完立即生效，不用再保存。
- 每个快捷键都能录手柄键或「修饰键 + 键」；录制时轻按 B 录 B，长按 B 取消。
- 手柄列专属青色，空格子显示「未设置」，按键之间加「+」。
- 「模组默认」「方案预设」用手柄时显示手柄键；方案可以带手柄键。
- 滑条细轨道、圆形滑块、加长，超大上限的滑条步进正常。

'''),
 ('zh-tw', '## 1.1.3 — 手把欄、修飾鍵選擇與設定格對齊 {#v1-1-3}', '''## 1.1.4 — 快捷鍵直改直用、手把欄全面可錄、滑桿重做 {#v1-1-4}

- 快捷鍵頁錄完立即生效，不用再儲存。
- 每個快捷鍵都能錄手把鍵或「修飾鍵 + 鍵」；錄製時輕按 B 錄 B，長按 B 取消。
- 手把欄專屬青色，空格子顯示「未設定」，按鍵之間加「+」。
- 「模組預設」「方案預設」用手把時顯示手把鍵；方案可以帶手把鍵。
- 滑桿細軌道、圓形滑塊、加長，超大上限的滑桿步進正常。

'''),
 ('en-us', '## 1.1.3 — Controller column, modifier choice and aligned settings rows {#v1-1-3}', '''## 1.1.4 — Keys take effect at once, every hotkey records controller keys, new sliders {#v1-1-4}

- Recording on the keybindings tab takes effect at once; no separate save.
- Every hotkey can record a controller button or "modifier + button"; tap B to record B, hold B to cancel.
- The controller column has its own teal colour; empty cells say "Not set"; a "+" between keys.
- "Mod default" and "Profile preset" show controller keys when you use a controller; profiles can carry controller keys.
- Sliders: slim track, round knob, wider; huge-range sliders step sensibly.

''')]:
    ins(f'Help/{lang}/changelog.md', before, text)

edit('Help/zh-cn/overview.md', [('大多数模组只认键盘，手柄列显示「仅键盘」：在上面按 A，再按一个手柄键，局内按这个键就等于按下模组的键盘快捷键。',
  '在快捷键页录的键立即生效，不用再保存。手柄列没录过的显示「未设置」：在上面按 A，再按一个手柄键，局内按这个键就等于按下模组的快捷键（模组自己有手柄键设置的，单键直接写进它的设置）。录制时轻按 B 是录 B，长按 B 一秒取消。用手柄时，右侧「模组默认」「方案预设」两列显示手柄键，方案也能记录手柄键。')])
edit('Help/zh-tw/overview.md', [('大多數模組只認鍵盤，手把欄顯示「僅鍵盤」：在上面按 A，再按一個手把鍵，遊戲中按這個鍵就等於按下模組的鍵盤快捷鍵。',
  '在快捷鍵頁錄的鍵立即生效，不用再儲存。手把欄沒錄過的顯示「未設定」：在上面按 A，再按一個手把鍵，遊戲中按這個鍵就等於按下模組的快捷鍵（模組自己有手把鍵設定的，單鍵直接寫進它的設定）。錄製時輕按 B 是錄 B，長按 B 一秒取消。用手把時，右側「模組預設」「方案預設」兩欄顯示手把鍵，方案也能記錄手把鍵。')])
edit('Help/en-us/overview.md', [('''Most mods only read the keyboard; their controller cell says "Keyboard only". Press A on it, then press a controller button: in game that button presses the mod's keyboard key.''',
  '''Keys recorded on this tab take effect at once; there is no separate save. An empty controller cell says "Not set": press A on it, then press a controller button, and in game that button presses the mod's key (for a mod with its own controller setting, a single button is written into that setting). While recording, tap B to record B, hold B for a second to cancel. With a controller, the "Mod default" and "Profile preset" columns show controller keys, and profiles can record controller keys too.''')])

for rel in ['ModConfigEnhance.csproj', 'Updater/MCE.UpdateWorker.csproj', 'Desktop/MCE.Desktop.csproj']:
    edit(rel, [('<Version>1.1.3</Version>', '<Version>1.1.4</Version>')])
edit('manifest.json', [('"version_number": "1.1.3"', '"version_number": "1.1.4"')])
edit('src/Plugin.cs', [('public const string PluginVersion = "1.1.3";', 'public const string PluginVersion = "1.1.4";')])
print('ok')
