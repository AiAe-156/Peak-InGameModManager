# -*- coding: utf-8 -*-
"""1.1.3 docs (three languages), changelogs, version bump."""
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

def ins(rel, before, text):
    edit(rel, [(before, text + before)])

ins('CHANGELOG.md', '## 1.1.2 — 2026-11-12', '''## 1.1.3 — 2026-11-12

- **Keybindings tab:** a permanent "Controller" column next to "Keyboard". Keys are drawn as small rounded key chips in the manager's style (no more stray emoji glyphs). A combo shows the modifier chip dimmed and the key bright.
- **Choose the controller modifier:** a dropdown at the top (None, LT, RT, LB, RB, Back, Start, RS, LS; default None). Modifiers are optional: hold it while recording for "modifier + key", or just press a button for a single-button binding. One modifier at a time; recorded combos keep theirs.
- **Duplicates:** "← Duplicate" marks a clash on the controller side, "Duplicate →" one on the keyboard side. Controller combos are checked against game controls and controller-native mod keys.
- **Expand / collapse all groups** on the mod list and the keybindings tab (button, or LT + RB / LT + LB).
- **Settings rows:** every row has the same three button columns (Default | Save | Clear), reserved even when empty; the default value sits right before the white box in a rounded chip (keys as key chips), cut with "…" when long; white boxes are a little taller; the conflict mark is a small corner badge on the key box. The manager profile directory row no longer has its Default button inside the text box.
- **Fixed:** "LOC: MCE_PIN" and three other untranslated labels on the settings page sidebar.

''')
ins('README.md', '## 1.1.2 快捷键页焦点框与游戏按键冲突', '''## 1.1.3 手柄列、修饰键选择与设置格对齐

- 快捷键页新增常驻「手柄按键」列，按键画成本模组风格的圆角小方块（不再出现表情符号）；组合键里修饰键暗、功能键亮。
- 顶部可选手柄修饰键：无 / LT / RT / LB / RB / Back / Start / RS / LS，默认「无」。修饰键可用可不用：录制时按住它就记成「修饰键 + 键」，不按就记单键；同一时间只有一个修饰键，已录好的不受影响。
- 重复键：手柄侧冲突显示「← 重复键」，键盘侧显示「重复键 →」；手柄组合也会和游戏操作、原生支持手柄的模组键比对。
- 模组列表和快捷键页都有「展开 / 收起全部分组」按钮，手柄 LT + RB 全部展开、LT + LB 全部收起。
- 设置格：每行固定三列按钮（默认 | 保存 | 清空），没有也留位；默认值贴着白框左边、用圆角框显示（按键用按键小方块），太长自动省略；白框加高；冲突改为按键框右上角的小角标；修复「管理器档案目录」的默认按钮跑进白框里。
- 修复设置页侧栏「LOC: MCE_PIN」等四处未翻译文字。

''')
for lang, before, text in [
 ('zh-cn', '## 1.1.2 — 快捷键页焦点框与游戏按键冲突 {#v1-1-2}', '''## 1.1.3 — 手柄列、修饰键选择与设置格对齐 {#v1-1-3}

- 快捷键页新增常驻「手柄按键」列，按键显示为圆角小方块；组合键里修饰键暗、功能键亮。
- 顶部选择手柄修饰键（默认「无」），可用可不用。
- 手柄侧冲突「← 重复键」，键盘侧冲突「重复键 →」。
- 模组列表与快捷键页加「展开 / 收起全部分组」，手柄 LT + RB / LT + LB。
- 设置格三列按钮固定对齐，默认值贴白框显示，冲突改为小角标；修复侧栏未翻译文字。

'''),
 ('zh-tw', '## 1.1.2 — 快捷鍵頁焦點框與遊戲按鍵衝突 {#v1-1-2}', '''## 1.1.3 — 手把欄、修飾鍵選擇與設定格對齊 {#v1-1-3}

- 快捷鍵頁新增常駐「手把按鍵」欄，按鍵顯示為圓角小方塊；組合鍵裡修飾鍵暗、功能鍵亮。
- 頂部選擇手把修飾鍵（預設「無」），可用可不用。
- 手把側衝突「← 重複鍵」，鍵盤側衝突「重複鍵 →」。
- 模組列表與快捷鍵頁加「展開 / 收起全部分組」，手把 LT + RB / LT + LB。
- 設定格三欄按鈕固定對齊，預設值貼白框顯示，衝突改為小角標；修復側欄未翻譯文字。

'''),
 ('en-us', '## 1.1.2 — Keybindings focus ring and game-control duplicates {#v1-1-2}', '''## 1.1.3 — Controller column, modifier choice and aligned settings rows {#v1-1-3}

- A permanent "Controller" column on the keybindings tab, with keys drawn as rounded key chips; combos show a dim modifier and a bright key.
- Choose the controller modifier at the top (default None); using it is optional.
- "← Duplicate" for controller-side clashes, "Duplicate →" for keyboard-side ones.
- Expand / collapse all groups on the mod list and keybindings tab (LT + RB / LT + LB).
- Settings rows: fixed Default | Save | Clear columns, the default value right before the box, a corner badge for key clashes; untranslated sidebar labels fixed.

''')]:
    ins(f'Help/{lang}/changelog.md', before, text)

# overview help: replace the LT-only paragraph with the modifier version
edit('Help/zh-cn/overview.md', [(re.search(r'\*\*快捷键页与 LT 组合键\*\*：.*?\n', io.open(os.path.join(ROOT, 'Help/zh-cn/overview.md'), encoding='utf-8').read()).group(0),
 '''**快捷键页与手柄组合键**：每行有「键盘按键」和「手柄按键」两列，按键显示为圆角小方块。大多数模组只认键盘，手柄列显示「仅键盘」：在上面按 A，再按一个手柄键，局内按这个键就等于按下模组的键盘快捷键。顶部可以选一个**手柄修饰键**（无 / LT / RT / LB / RB / Back / Start / RS / LS，默认「无」）：录制时先按住它，就记成「修饰键 + 键」，组合里的第二个键不会再触发游戏原本的操作；不按就记成单键，游戏原本的操作照常。同一时间只有一个修饰键，已录好的组合不受影响；一个键只能分给一个快捷键；按 Y 打开的菜单里可以移除。组合只保存在本机。「重复键」检查会把游戏自己的操作一起算上：手柄侧冲突显示「← 重复键」，键盘侧显示「重复键 →」。**LT + RB** 展开全部分组，**LT + LB** 收起全部分组（模组列表也一样）。
''')])
edit('Help/zh-tw/overview.md', [(re.search(r'\*\*快捷鍵頁與 LT 組合鍵\*\*：.*?\n', io.open(os.path.join(ROOT, 'Help/zh-tw/overview.md'), encoding='utf-8').read()).group(0),
 '''**快捷鍵頁與手把組合鍵**：每行有「鍵盤按鍵」和「手把按鍵」兩欄，按鍵顯示為圓角小方塊。大多數模組只認鍵盤，手把欄顯示「僅鍵盤」：在上面按 A，再按一個手把鍵，遊戲中按這個鍵就等於按下模組的鍵盤快捷鍵。頂部可以選一個**手把修飾鍵**（無 / LT / RT / LB / RB / Back / Start / RS / LS，預設「無」）：錄製時先按住它，就記成「修飾鍵 + 鍵」，組合裡的第二個鍵不會再觸發遊戲原本的操作；不按就記成單鍵，遊戲原本的操作照常。同一時間只有一個修飾鍵，已錄好的組合不受影響；一個鍵只能分給一個快捷鍵；按 Y 開啟的選單裡可以移除。組合只儲存在本機。「重複鍵」檢查會把遊戲自己的操作一起算上：手把側衝突顯示「← 重複鍵」，鍵盤側顯示「重複鍵 →」。**LT + RB** 展開全部分組，**LT + LB** 收起全部分組（模組列表也一樣）。
''')])
edit('Help/en-us/overview.md', [(re.search(r'\*\*Keybindings and LT combos:\*\*.*?\n', io.open(os.path.join(ROOT, 'Help/en-us/overview.md'), encoding='utf-8').read()).group(0),
 '''**Keybindings and controller combos:** each row has a "Keyboard" and a "Controller" column, with keys drawn as rounded key chips. Most mods only read the keyboard; their controller cell says "Keyboard only". Press A on it, then press a controller button: in game that button presses the mod's keyboard key. At the top you can pick one **controller modifier** (None, LT, RT, LB, RB, Back, Start, RS, LS; default None). Hold it while recording to get "modifier + key" (the second key then no longer does its normal game action); press a button alone for a single-button binding (the game still does its normal action). One modifier at a time; recorded combos keep theirs; a button belongs to one hotkey; remove a combo from the Y menu. Combos are stored on this PC only. "Duplicate" checks include the game's own controls: "← Duplicate" for controller-side clashes, "Duplicate →" for keyboard-side ones. **LT + RB** expands all groups, **LT + LB** collapses them (on the mod list too).
''')])

for rel in ['ModConfigEnhance.csproj', 'Updater/MCE.UpdateWorker.csproj', 'Desktop/MCE.Desktop.csproj']:
    edit(rel, [('<Version>1.1.2</Version>', '<Version>1.1.3</Version>')])
edit('manifest.json', [('"version_number": "1.1.2"', '"version_number": "1.1.3"')])
edit('src/Plugin.cs', [('public const string PluginVersion = "1.1.2";', 'public const string PluginVersion = "1.1.3";')])
print('ok')
