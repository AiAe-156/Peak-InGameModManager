"""Renders the help illustrations (2.3.10) for each language into Help/<lang>/media/.

Pictures are drawn from the real UI colours (src/Overview/StatusChip.cs, ModEnablementPresentation.cs,
UiActionStyle.cs, SourceUi.RoundedChip.Add, OverviewUi palette) so they match what the player sees.
Run: python -I 验证/2.3.10帮助重做/make_figures.py
"""
import os, sys
from PIL import Image, ImageDraw, ImageFont
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
FONT_CN = r'C:\Windows\Fonts\msyh.ttc'; FONT_CN_B = r'C:\Windows\Fonts\msyhbd.ttc'
FONT_TW = r'C:\Windows\Fonts\msjh.ttc'; FONT_TW_B = r'C:\Windows\Fonts\msjhbd.ttc'
def c(r, g, b, a=1.0): return (round(r * 255), round(g * 255), round(b * 255), round(a * 255))
BG = c(.16, .13, .10); PANEL = c(.21, .18, .14); ROW = c(.10, .11, .13); IVORY = c(.94, .91, .81); MUTED = c(.70, .70, .64)
CHIP = {'unread': c(.43, .68, .92), 'update': c(.50, .77, .49), 'unsaved': c(1, .55, .12), 'restart': c(.68, .58, .95),
        'nested': c(.88, .63, .33), 'resource': c(.38, .78, .76), 'loose': c(.88, .55, .74)}
BOX = {'on': c(.48, .78, .46), 'changing': c(.95, .76, .30), 'off': c(.73, .73, .69)}
SRC = {'store': c(.31, .40, .50), 'local': c(.31, .45, .43), 'pending': c(.43, .41, .37)}
ROLE = {'edit': (0x26, 0x40, 0x59, 255), 'commit': (0x26, 0x4d, 0x33, 255), 'neutral': (0x4a, 0x3b, 0x29, 255), 'danger': (0x66, 0x1a, 0x21, 255)}

T = {
 'zh-cn': dict(font=FONT_CN, bold=FONT_CN_B,
   chips_title='名称列小角标', chips_sub='每种角标位置固定，同一种在每一行都在同一个位置',
   chips=[('unread','未读变动','模组版本或作者设置变了，点开看详情'),('update','有新版本','商店有更高版本，点开可更新'),
          ('unsaved','未保存','启停改动还在草稿里，点「保存配置」'),('restart','待重启','已写入，重启 PEAK 后生效'),
          ('nested','嵌套模组','模组文件夹放在另一个模组文件夹里'),('resource','资源包','没有 DLL 的包（贴图、资源、数据）'),
          ('loose','散放 DLL','直接放在 plugins 根目录的单个 DLL')],
   box_title='启用方框与来源标签', boxes=[('on','已启用','本次已加载，目标也是启用'),('changing','变更中','目标与当前不同，保存或重启后生效'),('off','已停用','本次未加载，目标也是停用')],
   srcs=[('store','商店','已确认 Thunderstore 身份，可自动检查更新'),('local','本地','你确认没有商店来源'),('pending','待定','还没确认商店身份，暂不能自动更新')],
   flow_title='一次修改是怎样生效的', flow=['改参数 / 勾启停','草稿（橙色角标）','保存配置','生效时机'],
   flow_note=['立即','重新开局','重启游戏'], direct_title='这些操作不进草稿，点了就排队执行', direct=['安装','更新','卸载','本地导入','目录整理','基础组件启停'],
   toolbar_title='概览顶部工具条', toolbar=[('neutral','方案：xxx v','切换要查看的方案；第一项「管理配置方案」'),('commit','保存配置','提交草稿'),('edit','变更检查目录','保存前核对改了什么'),
     ('edit','标签筛选','按状态和商店标签过滤'),('neutral','隐藏已停用',''),('edit','导入','商店模组 / 本地文件 / 整合包'),('edit','来源与更新','检查更新、识别来源'),
     ('edit','管理中心','分组、方案、整合包、发布、工具'),('neutral','日志',''),('neutral','使用说明','')]),
 'zh-tw': dict(font=FONT_TW, bold=FONT_TW_B,
   chips_title='名稱欄小角標', chips_sub='每種角標位置固定，同一種在每一列都在同一個位置',
   chips=[('unread','未讀變動','模組版本或作者設定變了，點開看詳情'),('update','有新版本','商店有更高版本，點開可更新'),
          ('unsaved','未儲存','啟停變更還在草稿裡，按「儲存設定」'),('restart','待重新啟動','已寫入，重新啟動 PEAK 後生效'),
          ('nested','巢狀模組','模組資料夾放在另一個模組資料夾裡'),('resource','資源包','沒有 DLL 的套件（貼圖、資源、資料）'),
          ('loose','散放 DLL','直接放在 plugins 根目錄的單一 DLL')],
   box_title='啟用方框與來源標籤', boxes=[('on','已啟用','本次已載入，目標也是啟用'),('changing','變更中','目標與目前不同，儲存或重新啟動後生效'),('off','已停用','本次未載入，目標也是停用')],
   srcs=[('store','商店','已確認 Thunderstore 身分，可自動檢查更新'),('local','本機','你確認沒有商店來源'),('pending','待定','尚未確認商店身分，暫不能自動更新')],
   flow_title='一次修改是怎樣生效的', flow=['改參數 / 勾啟停','草稿（橙色角標）','儲存設定','生效時機'],
   flow_note=['立即','重新開局','重新啟動遊戲'], direct_title='這些操作不進草稿，按下就排隊執行', direct=['安裝','更新','解除安裝','本機匯入','目錄整理','基礎組件啟停'],
   toolbar_title='概覽頂部工具列', toolbar=[('neutral','方案：xxx v','切換要檢視的方案；第一項「管理設定方案」'),('commit','儲存設定','提交草稿'),('edit','變更檢查目錄','儲存前核對改了什麼'),
     ('edit','標簽篩選','依狀態和商店標籤過濾'),('neutral','隱藏已停用',''),('edit','匯入','商店模組 / 本機檔案 / 整合包'),('edit','來源與更新','檢查更新、識別來源'),
     ('edit','管理中心','分組、方案、整合包、發佈、工具'),('neutral','日誌',''),('neutral','使用說明','')]),
 'en-us': dict(font=FONT_CN, bold=FONT_CN_B,
   chips_title='Name-column markers', chips_sub='Each marker has a fixed slot, so the same kind lines up on every row',
   chips=[('unread','Unread change','Version or author setting changed; click for details'),('update','Update available','A newer store version exists; click to update'),
          ('unsaved','Unsaved','Enable/disable change is still a draft; Save configuration'),('restart','Pending restart','Written; takes effect after restarting PEAK'),
          ('nested','Nested mod','Its folder sits inside another mod\'s folder'),('resource','Resource pack','A package with no DLL (textures, assets, data)'),
          ('loose','Loose DLL','A single DLL directly in the plugins folder')],
   box_title='Enable box and source label', boxes=[('on','Enabled','Loaded now, and the target is enabled'),('changing','Changing','Target differs from now; applies after save or restart'),('off','Disabled','Not loaded now, and the target is disabled')],
   srcs=[('store','Store','Thunderstore identity confirmed; update checks work'),('local','Local','You confirmed it has no store source'),('pending','Pending','Store identity not confirmed yet; no automatic updates')],
   flow_title='How a change takes effect', flow=['Edit a setting / toggle','Draft (orange marker)','Save configuration','When it applies'],
   flow_note=['Immediately','Next round','After restart'], direct_title='These skip the draft and are queued as soon as you click', direct=['Install','Update','Uninstall','Local import','Folder organization','Component enable/disable'],
   toolbar_title='Overview toolbar', toolbar=[('neutral','Profile: xxx v','Switch the profile you view; first row "Manage profiles"'),('commit','Save configuration','Commit the draft'),('edit','Change review','Check what changed before saving'),
     ('edit','Tags','Filter by state and store tags'),('neutral','Hide disabled',''),('edit','Import','Store mods / local files / modpacks'),('edit','Sources and updates','Check updates, identify sources'),
     ('edit','Management center','Groups, profiles, modpacks, publishing, tools'),('neutral','Logs',''),('neutral','User guide','')]),
}

def font(path, size): return ImageFont.truetype(path, size)
def rounded(d, box, r, fill=None, outline=None, width=1): d.rounded_rectangle(box, r, fill=fill, outline=outline, width=width)

def chips(L, t, out):
    W, H = 1100, 120 + 66 * len(t['chips'])
    im = Image.new('RGBA', (W, H), BG); d = ImageDraw.Draw(im)
    d.text((40, 28), t['chips_title'], font=font(t['bold'], 34), fill=IVORY)
    d.text((40, 76), t['chips_sub'], font=font(t['font'], 20), fill=MUTED)
    # a sample row with every chip in its fixed slot
    y = 120
    for i, (k, name, desc) in enumerate(t['chips']):
        top = y + i * 66
        rounded(d, (40, top, W - 40, top + 56), 8, fill=ROW)
        slot = ['unread', 'update', 'unsaved', 'restart', 'nested', 'resource', 'loose'].index(k)
        # lane: right edge at x=330, step 20 (scaled from 10 in-game)
        cx = 330 - slot * 26
        for j in range(7): d.rounded_rectangle((330 - j * 26 - 8, top + 12, 330 - j * 26 + 8, top + 28), 3, outline=(80, 72, 60, 255))
        d.rounded_rectangle((cx - 8, top + 12, cx + 8, top + 28), 3, fill=CHIP[k])
        d.text((370, top + 8), name, font=font(t['bold'], 22), fill=CHIP[k])
        d.text((370 + max(170, d.textlength(name, font=font(t['bold'], 22)) + 24), top + 11), desc, font=font(t['font'], 19), fill=IVORY)
    im.save(out)

def boxes(L, t, out):
    W, H = 1100, 520
    im = Image.new('RGBA', (W, H), BG); d = ImageDraw.Draw(im)
    d.text((40, 28), t['box_title'], font=font(t['bold'], 34), fill=IVORY)
    y = 96
    for k, name, desc in t['boxes']:
        rounded(d, (40, y, W - 40, y + 56), 8, fill=ROW)
        rounded(d, (64, y + 12, 96, y + 44), 6, outline=BOX[k], width=4)
        if k != 'off': d.line((72, y + 28, 79, y + 36, 90, y + 19), fill=c(.55, .80, .95), width=4)
        d.text((120, y + 10), name, font=font(t['bold'], 22), fill=BOX[k])
        d.text((300, y + 13), desc, font=font(t['font'], 19), fill=IVORY)
        y += 66
    y += 14
    for k, name, desc in t['srcs']:
        rounded(d, (40, y, W - 40, y + 52), 8, fill=ROW)
        f = font(t['font'], 18); w = d.textlength(name, font=f) + 16
        rounded(d, (64, y + 13, 64 + w, y + 39), 5, fill=SRC[k]); d.text((72, y + 14), name, font=f, fill=c(.90, .88, .81))
        d.text((300, y + 12), desc, font=font(t['font'], 19), fill=IVORY)
        y += 60
    im.save(out)

def wrap(d, text, f, width):
    """Splits text into lines that fit width (spaces for Latin, characters for CJK)."""
    words = text.split(' ') if ' ' in text else list(text)
    sep = ' ' if ' ' in text else ''
    lines, line = [], ''
    for w in words:
        trial = (line + sep + w) if line else w
        if d.textlength(trial, font=f) <= width: line = trial
        else: lines.append(line); line = w
    if line: lines.append(line)
    return lines

def flow(L, t, out):
    W, H = 1100, 440
    im = Image.new('RGBA', (W, H), BG); d = ImageDraw.Draw(im)
    d.text((40, 28), t['flow_title'], font=font(t['bold'], 34), fill=IVORY)
    x, y, bw, bh = 40, 100, 232, 84
    colors = [ROLE['neutral'], (0x5a, 0x40, 0x1c, 255), ROLE['commit'], ROLE['edit']]
    for i, step in enumerate(t['flow']):
        rounded(d, (x, y, x + bw, y + bh), 10, fill=colors[i])
        f = font(t['bold'], 20); lines = wrap(d, step, f, bw - 28)
        ty = y + (bh - 28 * len(lines)) / 2
        for line in lines:
            tw = d.textlength(line, font=f); d.text((x + (bw - tw) / 2, ty), line, font=f, fill=IVORY); ty += 28
        if i < 3: d.polygon([(x + bw + 8, y + bh / 2 - 9), (x + bw + 26, y + bh / 2), (x + bw + 8, y + bh / 2 + 9)], fill=MUTED)
        x += bw + 36
    note = ' / '.join(t['flow_note']); f = font(t['font'], 18)
    d.text((W - 40 - d.textlength(note, font=f), y + bh + 12), note, font=f, fill=MUTED)
    d.text((40, 260), t['direct_title'], font=font(t['bold'], 24), fill=c(.95, .76, .30))
    x, row = 40, 310
    for item in t['direct']:
        f = font(t['font'], 19); w = d.textlength(item, font=f) + 28
        if x + w > W - 40: x = 40; row += 52
        rounded(d, (x, row, x + w, row + 40), 8, fill=ROLE['edit']); d.text((x + 14, row + 8), item, font=f, fill=IVORY); x += w + 12
    im.save(out)

def toolbar(L, t, out):
    items = t['toolbar']; W = 1100; H = 110 + 54 * len(items)
    im = Image.new('RGBA', (W, H), BG); d = ImageDraw.Draw(im)
    d.text((40, 28), t['toolbar_title'], font=font(t['bold'], 34), fill=IVORY)
    y = 96
    for role, label, desc in items:
        f = font(t['font'], 19); w = max(150, d.textlength(label, font=f) + 30)
        rounded(d, (40, y, 40 + w, y + 40), 8, fill=ROLE[role]); d.text((55, y + 8), label, font=f, fill=IVORY)
        if desc: d.text((40 + w + 24, y + 9), desc, font=font(t['font'], 18), fill=MUTED)
        y += 54
    im.save(out)

for L, t in T.items():
    folder = os.path.join(ROOT, 'Help', L, 'media'); os.makedirs(folder, exist_ok=True)
    chips(L, t, os.path.join(folder, 'chips.png')); boxes(L, t, os.path.join(folder, 'boxes.png'))
    flow(L, t, os.path.join(folder, 'flow.png')); toolbar(L, t, os.path.join(folder, 'toolbar.png'))
    print('wrote', L)
