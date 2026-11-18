# -*- coding: utf-8 -*-
"""1.1.4 docs: final state of this round (official key icons, no Save button, Default/Clear column, slider proportion,
log window / shop / dialogs gamepad, list sound, menu wording, voice-fix bridge noted separately)."""
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

edit('CHANGELOG.md', [
('''- **Sliders:** slim track and round knob, wider; sliders whose maximum is effectively unlimited (e.g. 3.4×10³⁸) step sensibly and the knob moves with the controller.''',
 '''- **Sliders:** slim track and round knob (ModConfig's own number box : slider proportion kept); sliders whose maximum is effectively unlimited (e.g. 3.4×10³⁸) step sensibly and the knob moves with the controller.
- **Settings rows:** the per-row Save button is gone; every change is written to the mod's config at once. Default and Clear share one narrow column on the right (Default on top, Clear under it when the setting can be cleared).'''),
('''- Key chips follow the game's own glyph look (cream square, dark letters, larger).''',
 '''- **Key icons:** keys are drawn with PEAK's own key icons (keyboard, Xbox or PlayStation set, following the game's icon setting); keys without an icon, and "Not set", use a rounded chip in the game font. Key cells no longer show the ribbon frame. Key names such as Period or Comma are translated.
- **Keybinding menu:** reset to "Mod default", reset to the current "Profile preset", ignore conflict ←→ …, set the "current key" as the current "Profile preset".
- **Controller, other windows:** the log window scrolls with the right stick, its option and detail panels take the focus and B closes them one at a time, the search box uses the Steam keyboard, export skips the Windows save dialog. The store keeps the focus when pages redraw, moves between filters / cards / pager / details by direction, B backs out one layer at a time, and the document area scrolls with up/down with A opening a list of the page's links. Dialogs give the focus back to the button that opened them; the uninstall dialog starts on "Close". Up from the first list row reaches the buttons above; leaving the overview puts the focus back on its entry button.
- The first list load no longer piles up every button's appear sound over the row sound.'''),
])
edit('README.md', [
('''- 滑条：细轨道、圆形滑块、加长；上限近乎无限（如 3.4×10³⁸）的滑条步进正常，手柄左右能拖动滑块。''',
 '''- 滑条：细轨道、正圆滑块，数值框和滑条保持 ModConfig 原来的比例；上限近乎无限（如 3.4×10³⁸）的滑条步进正常，手柄左右能拖动滑块。
- 设置页每格去掉「保存」按钮，改了立即写入模组配置；「默认」「清除」并成右侧一列，「默认」在上、能清空的才在下面加「清除」。'''),
('''- 按键小方块改成游戏自带按键图标的样子（米色方块、深色字、更大）。''',
 '''- 按键直接用 PEAK 自己的按键图标（键盘、Xbox、PlayStation 三套，跟随游戏的图标设置）；没有图标的键和「未设置」用游戏字体的圆角卡片。快捷键格去掉缎带框。Period、Comma 等键名有中文名。
- 快捷键格菜单：重设为「模组默认」预设的快捷键、重设为当前「方案预设」中设定的快捷键、忽略冲突 ←→ …、将「当前快捷键」设为当前的「方案预设」。
- 其他窗口的手柄操作：日志窗口右摇杆能滚动，选项 / 详情弹层能聚焦、B 逐层关闭，搜索框走 Steam 键盘，导出不再弹 Windows 另存为。商店翻页、筛选、打开详情后焦点不丢，筛选列、卡片、翻页、详情之间按方向移动，B 逐层退出，文档区上下键滚动、A 打开本页链接列表。弹窗关闭后焦点回到打开它的按钮；卸载弹窗默认选「关闭」。列表第一行按 ↑ 能到上面的按钮；离开概览后焦点回到入口按钮。
- 首次加载列表不再叠加每个按钮的出现音效。'''),
])
for lang, a, b in [
 ('zh-cn', '- 滑条细轨道、圆形滑块、加长，超大上限的滑条步进正常。', '- 滑条细轨道、正圆滑块，保持原有比例；超大上限的滑条步进正常。\n- 设置页去掉每格的「保存」，改了立即生效；「默认」「清除」并成右侧一列。\n- 按键直接用游戏自带的按键图标，快捷键格去掉缎带框，键名有中文名。\n- 日志窗口、商店、各弹窗都能用手柄操作：焦点不丢、B 逐层退出、右摇杆滚动。'),
 ('zh-tw', '- 滑桿細軌道、圓形滑塊、加長，超大上限的滑桿步進正常。', '- 滑桿細軌道、正圓滑塊，保持原有比例；超大上限的滑桿步進正常。\n- 設定頁去掉每格的「儲存」，改了立即生效；「預設」「清除」併成右側一欄。\n- 按鍵直接用遊戲內建的按鍵圖示，快捷鍵格去掉緞帶框，鍵名有中文名。\n- 日誌視窗、商店、各彈窗都能用手把操作：焦點不丟、B 逐層退出、右搖桿捲動。'),
 ('en-us', '- Sliders: slim track, round knob, wider; huge-range sliders step sensibly.', '- Sliders: slim track, round knob, original proportion kept; huge-range sliders step sensibly.\n- Settings rows: no Save button, changes take effect at once; Default and Clear share one column.\n- Keys use the game\'s own key icons; key cells lose the ribbon frame; key names are translated.\n- Log window, store and dialogs work with a controller: focus kept, B backs out one layer, right stick scrolls.'),
]:
    edit(f'Help/{lang}/changelog.md', [(a, b)])
print('ok')
