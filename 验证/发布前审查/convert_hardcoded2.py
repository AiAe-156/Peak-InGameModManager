"""Second batch of hard-coded Chinese → three-language conversions (2.3.8): storage browser, stored pack details,
pack content files, introduction editor, shop sort, misc. One-shot; every edit asserts a single match.

Background-thread text (storage tree built in Task.Run, stored-pack details built off the main thread) is not
passed through Loc there; instead the display code translates it with StorageText.Display (main thread).
"""
import os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def edit(rel, pairs):
    p = os.path.join(ROOT, 'src', rel)
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if s.count(a) != 1: sys.exit(f'{rel}: expected 1 match, got {s.count(a)}: {a[:90]}')
        s = s.replace(a, b)
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print('edited', rel, len(pairs))
def T(en, cn, tw): return f'Loc.T("{en}", "{cn}", "{tw}")'

# ── Storage browser: translate at display time ──────────────────────────
STORAGE = [
 ("下载缓存", "Download cache", "下載快取"), ("详情缓存", "Details cache", "詳情快取"), ("商店下载与操作备份", "Store downloads and operation backups", "商店下載與操作備份"),
 ("商店图标与文档缓存", "Store icon and document cache", "商店圖示與文件快取"), ("切换恢复备份", "Switch recovery backups", "切換還原備份"),
 ("整合包回收区", "Modpack recycle area", "整合包回收區"), ("切换备份", "Switch backup", "切換備份"),
 ("已结束；清理下载包、解压缓存和操作备份，不影响已安装模组", "Finished; removes download packages, extraction cache and operation backups, not installed mods", "已結束；清理下載套件、解壓快取和操作備份，不影響已安裝模組"),
 ("待确认、下载、安装或恢复中的任务，受保护", "Task awaiting confirmation, downloading, installing or recovering; protected", "待確認、下載、安裝或還原中的任務，受保護"),
 ("缺少有效商店任务记录，受保护", "No valid store task record; protected", "缺少有效商店任務記錄，受保護"),
 ("图标／文档缓存，可重新下载", "Icon / document cache; can be downloaded again", "圖示／文件快取，可重新下載"),
 ("日期来自文件时间", "date from file time", "日期取自檔案時間"), ("；日期来自文件时间", "; date from file time", "；日期取自檔案時間"),
 ("已完成，确认后可清理整个项目", "Completed; the whole item can be cleaned after confirmation", "已完成，確認後可清理整個項目"),
 ("未完成，受保护", "Unfinished; protected", "未完成，受保護"), ("缺少有效记录，受保护", "No valid record; protected", "缺少有效記錄，受保護"),
 ("回收项目，永久删除需确认", "Recycled item; permanent deletion needs confirmation", "回收項目，永久刪除需確認"),
 ("缺少有效回收记录，受保护", "No valid recycle record; protected", "缺少有效回收記錄，受保護"),
 ("目录过大，停止展开；请在资源管理器查看", "Folder too large to expand; view it in File Explorer", "目錄過大，停止展開；請在檔案總管檢視"),
 ("链接，受保护", "Link; protected", "連結，受保護"),
 ("仅展示部分内容，请在资源管理器查看完整目录", "Only part of the folder is shown; view it in File Explorer", "僅顯示部分內容，請在檔案總管檢視完整目錄"),
]
rows = ',\n'.join(f'        ("{cn}", "{en}", "{tw}")' for cn, en, tw in STORAGE)
helper = '''
/// <summary>2.3.8: the storage tree is built on a worker thread in Simplified Chinese; this translates its fixed
/// phrases on the main thread when a row is drawn (Loc is not thread-safe).</summary>
internal static class StorageText
{
    private static readonly (string Cn, string En, string Tw)[] Phrases =
    {
''' + rows + '''
    };
    internal static string Display(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string probe = Loc.T("en", "zh-cn", "zh-tw");
        if (probe == "zh-cn") return text;
        foreach (var p in Phrases.OrderByDescending(p => p.Cn.Length)) text = text.Replace(p.Cn, probe == "en" ? p.En : p.Tw);
        return text;
    }
}
'''
p = os.path.join(ROOT, 'src', 'Overview', 'PackStorageBrowser.cs')
s = open(p, encoding='utf-8').read()
if 'class StorageText' not in s:
    s = s.rstrip() + '\n' + helper
    open(p, 'w', encoding='utf-8', newline='').write(s)
edit('Overview/PackStorageBrowser.cs', [
 ('var title = OverviewUi.Text(row, node.Name, depth == 0 ? 18 : 15);', 'var title = OverviewUi.Text(row, StorageText.Display(node.Name), depth == 0 ? 18 : 15);'),
 ('(node.Note.Length > 0 ? " · " + node.Note : "")', '(node.Note.Length > 0 ? " · " + StorageText.Display(node.Note) : "")'),
])

# ── Pack content files (editor captions shown in modal) ─────────────────
edit('Overview/PackContentFiles.cs', [
 ('public string Notice => "修改保存为个人副本；作者原包和当前游戏文件保持不变。编辑后点击“保存为个人副本”提交。";',
  'public string Notice => ' + T("Edits are saved as a personal copy; the author\'s pack and the current game files stay unchanged. Click \\\"Save as personal copy\\\" after editing.", "修改保存为个人副本；作者原包和当前游戏文件保持不变。编辑后点击“保存为个人副本”提交。", "修改儲存為個人副本；作者原包和目前遊戲檔案保持不變。編輯後點擊「儲存為個人副本」提交。") + ';'),
 ('public string SaveLabel => "保存文件修改";', 'public string SaveLabel => ' + T("Save file changes", "保存文件修改", "儲存檔案修改") + ';'),
])

# ── Shop sort options ───────────────────────────────────────────────────
edit('Overview/ShopWindow.cs', [
 ('new[]{("Last updated / 最近更新","updated"),("Downloads / 下载量","downloads"),("Name / 名称","name")}',
  'new[]{(Loc.T("Last updated","最近更新","最近更新"),"updated"),(Loc.T("Downloads","下载量","下載量"),"downloads"),(Loc.T("Name","名称","名稱"),"name")}'),
])

# ── Mod-sidebar group summary (shown in the description panel) ──────────
edit('ModConfigSidebarWidgets.Groups.cs', [
 ('string summary = $"{repository?.State.groups.Count ?? 0} 个分组，{matched}/{_rows.Count} 行属于分组";',
  'string summary = Loc.F("{0} groups, {1}/{2} rows grouped", "{0} 个分组，{1}/{2} 行属于分组", "{0} 個分組，{1}/{2} 列屬於分組", repository?.State.groups.Count ?? 0, matched, _rows.Count);'),
])

# ── Introduction editor: add Traditional to its label map ───────────────
edit('Overview/Authoring/OverviewDocumentView.cs', [
 ('string localized=Loc.T(english,label,label);',
  'string localized=Loc.T(english,label,TraditionalLabel(label));'),
 ('    private static GameObject Node(Transform parent,string name)',
  '''    /// <summary>2.3.8: Traditional Chinese for the editor's built-in labels (they are written in Simplified).</summary>
    private static string TraditionalLabel(string label)=>label switch{"新增段落"=>"新增段落","源码/预览"=>"原始碼/預覽","保存"=>"儲存","撤销"=>"復原","重做"=>"重做","打开草稿"=>"開啟草稿","导出资料"=>"匯出資料","删除块"=>"刪除區塊","资源预算"=>"資源預算","粗体"=>"粗體","斜体"=>"斜體","下划线"=>"底線","删除线"=>"刪除線","上移"=>"上移","下移"=>"下移","缩进＋"=>"縮排＋","缩进－"=>"縮排－","左对齐"=>"靠左","居中"=>"置中","右对齐"=>"靠右","分割线"=>"分隔線","链接"=>"連結","图片"=>"圖片","短视频"=>"短影片","确认"=>"確認","取消"=>"取消","文字颜色"=>"文字顏色","字号"=>"字級","字体"=>"字型","背景"=>"背景","布局"=>"版面","素材"=>"素材","文件"=>"檔案","更多"=>"更多","透明"=>"透明","打开字体目录"=>"開啟字型目錄","刷新字体列表"=>"重新整理字型清單","源码"=>"原始碼",_=>label.EndsWith("号")?label.Substring(0,label.Length-1)+" 級":label};
    private static GameObject Node(Transform parent,string name)'''),
])
print('ok')
