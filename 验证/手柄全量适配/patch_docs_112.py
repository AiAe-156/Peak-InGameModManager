# -*- coding: utf-8 -*-
"""1.1.2 docs: LT combos and controller view on the keybindings page."""
import io, os
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

edit('CHANGELOG.md', [('''- **Duplicates include the game's own controls:**''', '''- **Controller keys on the keybindings tab:** like the game's own key page, the "Current key" column shows controller keys as soon as you use the controller and switches straight back on keyboard or mouse.
- **LT combos for keyboard-only mod keys:** most mods only read the keyboard. On a controller, press A on such a key, then hold LT and press a button: in game, holding LT and pressing that button presses the mod's key. One modifier only (LT); one button per hotkey. LT is also the game's secondary use, so holding it still does that. Stored per player (not exported with packs).
- **Duplicates include the game's own controls:**''')])
edit('README.md', [('''- 「重复键」检查把游戏自己的操作''', '''- 快捷键页像游戏自带的按键设置页一样：一用手柄「当前按键」列就显示手柄键，一碰键鼠马上切回键盘键。
- LT 组合键：大多数模组只认键盘。用手柄在这种快捷键上按 A，再按住 LT 按一个键，局内按住 LT 再按这个键就等于按下模组的快捷键。只有 LT 一个修饰键，每个快捷键配一个键。LT 也是游戏的副使用键，按住时游戏照常触发。只保存在本机，不随整合包导出。
- 「重复键」检查把游戏自己的操作''')])
for lang, a, b in [
 ('zh-cn', '- 「重复键」检查把游戏自己的操作（键盘和手柄）一起算上。', '- 快捷键页用手柄时显示手柄键，用键鼠马上切回键盘键。\n- LT 组合键：给只认键盘的模组快捷键配「按住 LT + 一个键」，局内直接用手柄触发。\n- 「重复键」检查把游戏自己的操作（键盘和手柄）一起算上。'),
 ('zh-tw', '- 「重複鍵」檢查把遊戲自己的操作（鍵盤和手把）一起算上。', '- 快捷鍵頁用手把時顯示手把鍵，用鍵鼠馬上切回鍵盤鍵。\n- LT 組合鍵：給只認鍵盤的模組快捷鍵配「按住 LT + 一個鍵」，遊戲中直接用手把觸發。\n- 「重複鍵」檢查把遊戲自己的操作（鍵盤和手把）一起算上。'),
 ('en-us', '''- "Duplicate" checks include the game's own controls (keyboard and controller).''', '''- The keybindings tab shows controller keys while you use the controller and switches back on keyboard or mouse.\n- LT combos: give a keyboard-only mod key a "hold LT + button" and use it in game with the controller.\n- "Duplicate" checks include the game's own controls (keyboard and controller).''')]:
    edit(f'Help/{lang}/changelog.md', [(a, b)])

edit('Help/zh-cn/overview.md', [('作者模式的多选、拖动排序、拖进分组，以及打开文件夹之后的操作，仍然用键鼠。', '''**快捷键页与 LT 组合键**：和游戏自带的按键设置页一样，一用手柄「当前按键」列就显示手柄键，一碰键鼠马上切回键盘键。大多数模组只认键盘，这类快捷键显示「仅键盘」：在上面按 A，再按住 **LT** 按一个键，以后在局内按住 LT 再按这个键，就等于按下这个模组的快捷键。只有 LT 一个修饰键；一个键只能分给一个快捷键，分给别的会自动移过去；按 Y 打开的菜单里可以移除。LT 也是游戏的副使用键，按住时游戏照常触发。组合键只保存在本机。「重复键」检查会把游戏自己的操作（跳跃、交互、使用等，键盘和手柄都算）一起算上。

作者模式的多选、拖动排序、拖进分组，以及打开文件夹之后的操作，仍然用键鼠。''')])
edit('Help/zh-tw/overview.md', [('作者模式的多選、拖動排序、拖進分組，以及開啟資料夾之後的操作，仍然用鍵鼠。', '''**快捷鍵頁與 LT 組合鍵**：和遊戲自帶的按鍵設定頁一樣，一用手把「目前按鍵」欄就顯示手把鍵，一碰鍵鼠馬上切回鍵盤鍵。大多數模組只認鍵盤，這類快捷鍵顯示「僅鍵盤」：在上面按 A，再按住 **LT** 按一個鍵，以後在遊戲中按住 LT 再按這個鍵，就等於按下這個模組的快捷鍵。只有 LT 一個修飾鍵；一個鍵只能分給一個快捷鍵，分給別的會自動移過去；按 Y 開啟的選單裡可以移除。LT 也是遊戲的副使用鍵，按住時遊戲照常觸發。組合鍵只儲存在本機。「重複鍵」檢查會把遊戲自己的操作（跳躍、互動、使用等，鍵盤和手把都算）一起算上。

作者模式的多選、拖動排序、拖進分組，以及開啟資料夾之後的操作，仍然用鍵鼠。''')])
edit('Help/en-us/overview.md', [('Author-mode multi-select, drag-to-reorder and dragging into groups, and anything after opening a folder, still need a keyboard and mouse.', '''**Keybindings and LT combos:** like the game's own key page, the "Current key" column shows controller keys as soon as you use the controller and switches straight back on keyboard or mouse. Most mods only read the keyboard; those keys show "Keyboard only". Press A on one, then hold **LT** and press a button: from then on, holding LT and pressing that button in game presses the mod's key. LT is the only modifier; a button belongs to one hotkey at a time (assigning it elsewhere moves it); remove a combo from the Y menu. LT is also the game's secondary use, so holding it still does that. Combos are stored on this PC only. "Duplicate" checks include the game's own controls (Jump, Interact, Use and so on, keyboard and controller).

Author-mode multi-select, drag-to-reorder and dragging into groups, and anything after opening a folder, still need a keyboard and mouse.''')])

# intro page: in-game LT layer
edit('文档/介绍页/build.py', [
("""const MODS=[['none','无'],['rt','按住 RT']];""", """const MODS=[['none','无'],['rt','按住 RT'],['lt','按住 LT（局内）']];"""),
(""" rt:{view:['按住 View 再按 RT：物品生成器窗口',W,'来自物品生成器增强（调试类模组），配置项 Controller Chord Modifier Path / Controller Toggle Path。'],rt:['（按住中）',P]},
};""", """ rt:{view:['按住 View 再按 RT：物品生成器窗口',W,'来自物品生成器增强（调试类模组），配置项 Controller Chord Modifier Path / Controller Toggle Path。'],rt:['（按住中）',P]},
 lt:{lt:['（按住中）· 游戏副使用照常',W,'LT 是游戏的副使用 / 投掷，按住时照常触发；K 键掉线重连在大厅外按 LT 会重连。'],
  a:['LT + A：你分配的模组快捷键',P,'在快捷键页给只认键盘的快捷键按 A，再按住 LT 按一个键即可分配。组合期间游戏不会把这个键当成跳跃。'],
  b:['LT + B：可分配',P],x:['LT + X：可分配',P],y:['LT + Y：可分配',P],lb:['LT + LB：可分配',P],rb:['LT + RB：可分配',P],rt:['LT + RT：可分配',P],
  dpad:['LT + ↑↓←→：各可分配',P],l3:['LT + L3：可分配',P],r3:['LT + R3：可分配',P],view:['LT + View：可分配',P],menu:['LT + Menu：可分配',P],
  ls:['移动（不变）',N],rs:['视角（不变）',N]},
};"""),
(""" if(scene==='play'){const m=mod==='rt'&&PLAY.rt[id]?PLAY.rt[id]:PLAY.none[id];return m||['—',I];}""",
 """ if(scene==='play'){const m=(mod!=='none'&&PLAY[mod]&&PLAY[mod][id])?PLAY[mod][id]:PLAY.none[id];return m||['—',I];}
 if(mod==='lt')return id==='lt'?['（只在局内用）',I]:entry0(id);"""),
("""function note(){""", """function entry0(id){const save=mod;mod='none';const e=entry(id);mod=save;return e;}
function note(){"""),
(""" if(scene==='play')t='局内是游戏自己的操作，管理器不接管任何手柄键；黄色是有模组同时在用的键。快捷键设置页的「重复」检查会把这些游戏按键一起算上。';""",
 """ if(scene==='play'&&mod==='lt')t='按住 LT 是管理器的组合键层：在快捷键页给只认键盘的模组快捷键分配「LT + 某键」，局内按住 LT 再按那个键就等于按下模组的键盘快捷键。只有 LT 一个修饰键。';
 else if(scene==='play')t='局内是游戏自己的操作；黄色是有模组同时在用的键。快捷键页的「重复」检查会把这些游戏按键一起算上。';
 else if(mod==='lt')t='LT 组合键只在局内生效，菜单里按住 LT 没有作用。';"""),
])
print('ok')
