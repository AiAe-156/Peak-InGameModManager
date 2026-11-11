"""Converts the remaining hard-coded Chinese UI text in the MCE plugin to three-language Loc.T calls (2.3.8).

Each edit is an exact (file, old, new) replacement and asserts that the old text occurs exactly once, so a re-run
after success fails loudly instead of editing twice. Run once; collect_terms.py afterwards adds the new English to
the catalogue.
"""
import os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def edit(rel, pairs):
    p = os.path.join(ROOT, 'src', rel)
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if s.count(a) != 1: sys.exit(f'{rel}: expected 1 match, got {s.count(a)}: {a[:80]}')
        s = s.replace(a, b)
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print('edited', rel, len(pairs))

def T(en, cn, tw): return f'Loc.T("{en}", "{cn}", "{tw}")'

# Update queue job states: NOT converted — they feed the background-thread runtime log (see hardcoded_chinese.py).

# ── Log window (phrases also translated by DiagnosticUiLanguage; these go through Loc so the catalogue has them) ──
edit('Overview/LogWindow.cs', [
 ('private static readonly string[] LevelNames = { "全部", "调试", "信息", "警告", "错误", "严重错误", "未识别" };',
  'private static readonly string[] LevelNames = { "全部", "调试", "信息", "警告", "错误", "严重错误", "未识别" }; // display goes through DiagnosticUiLanguage'),
])

# ── Modpack pages ───────────────────────────────────────────────────────
edit('Overview/OverviewPage.Packs.cs', [
 ('form.Text("空间管理与整合包回收区", true);', 'form.Text(' + T("Storage and modpack recycle area", "空间管理与整合包回收区", "空間管理與整合包回收區") + ', true);'),
 ('form.Text("展开项目查看文件；删除按项目整体处理，执行前重新核对范围。受保护项目不提供删除操作。");',
  'form.Text(' + T("Expand an item to see its files. Deleting removes the whole item and rechecks the scope first. Protected items cannot be deleted.", "展开项目查看文件；删除按项目整体处理，执行前重新核对范围。受保护项目不提供删除操作。", "展開項目檢視檔案；刪除以項目整體處理，執行前重新核對範圍。受保護項目不提供刪除操作。") + ');'),
 ('if (busy) form.Text("整合包任务进行中，删除操作暂不可用。");',
  'if (busy) form.Text(' + T("A modpack task is running; deleting is unavailable for now.", "整合包任务进行中，删除操作暂不可用。", "整合包任務進行中，刪除操作暫不可用。") + ');'),
 ('form.Text("正在后台读取文件树……");', 'form.Text(' + T("Reading the file tree in the background…", "正在后台读取文件树……", "正在背景讀取檔案樹……") + ');'),
 ('form.Action("刷新", () => { _storageLoading = null;', 'form.Action(' + T("Refresh", "刷新", "重新整理") + ', () => { _storageLoading = null;'),
 ('form.Action("打开管理目录", ', 'form.Action(' + T("Open MCE data folder", "打开管理目录", "開啟管理目錄") + ', '),
 ('"删除整合包 · 核对范围" : "空间管理 · 核', T("Delete modpack · check scope", "删除整合包 · 核对范围", "刪除整合包 · 核對範圍") + ' : "空间管理 · 核'),
 ('if (!busy) form.Action(_packStorageTab ? "返回空间管理" : ', 'if (!busy) form.Action(_packStorageTab ? ' + T("Back to storage", "返回空间管理", "返回空間管理") + ' : '),
 ('form.Text("含未保存运行环境；切回自动恢复。", true);', 'form.Text(' + T("Has an unsaved running environment; switching back restores it.", "含未保存运行环境；切回自动恢复。", "含未儲存執行環境；切回時自動還原。") + ', true);'),
 ('form.Button("预览升级为独立环境", ', 'form.Button(' + T("Preview upgrade to an independent environment", "预览升级为独立环境", "預覽升級為獨立環境") + ', '),
 ('form.Button("重试恢复原环境", ', 'form.Button(' + T("Retry restoring the original environment", "重试恢复原环境", "重試還原原環境") + ', '),
 ('form.Text(task.Message.Length > 0 ? task.Message : "任务未完成；请查看事务详情确认恢复状态。");',
  'form.Text(task.Message.Length > 0 ? task.Message : ' + T("The task did not finish. Check the transaction details to confirm the recovery state.", "任务未完成；请查看事务详情确认恢复状态。", "任務未完成；請查看交易詳情確認還原狀態。") + ');'),
 ('form.Button("重新检查并重试切换", ', 'form.Button(' + T("Recheck and retry switch", "重新检查并重试切换", "重新檢查並重試切換") + ', '),
 (': upgrading ? "确认退出后升级环境" : ', ': upgrading ? ' + T("Upgrade environment after exit", "确认退出后升级环境", "確認結束後升級環境") + ' : '),
 ('form.Action("保存当前配置并切换", ', 'form.Action(' + T("Save current and switch", "保存当前配置并切换", "儲存目前設定並切換") + ', '),
 ('form.Action("直接切换", ', 'form.Action(' + T("Switch without saving", "直接切换", "直接切換") + ', '),
 ('ManagementStatus("正在分析文件范围与可清理空间……");', 'ManagementStatus(' + T("Analysing file scope and reclaimable space…", "正在分析文件范围与可清理空间……", "正在分析檔案範圍與可清理空間……") + ');'),
 ('OverviewUi.Choose("选择要移除的模组目录", ', 'OverviewUi.Choose(' + T("Choose the mod folder to remove", "选择要移除的模组目录", "選擇要移除的模組目錄") + ', '),
 ('form.Text("维护预览缺失，请重新操作。"); return; }', 'form.Text(' + T("The maintenance preview is missing; please try again.", "维护预览缺失，请重新操作。", "維護預覽遺失，請重新操作。") + '); return; }'),
 ('form.Text("删除／清理范围", true);', 'form.Text(' + T("Delete / clean-up scope", "删除／清理范围", "刪除／清理範圍") + ', true);'),
 ('form.Text("整合包：" + preview.PackName + "\\n项目：" + preview.Items.Count + "\\n文件：" + preview.TotalFiles + "\\n空间：" + (preview.TotalBytes / 1048576d).ToString("0.00") + " MB");',
  'form.Text(Loc.F("Modpack: {0}\\nItems: {1}\\nFiles: {2}\\nSpace: {3} MB", "整合包：{0}\\n项目：{1}\\n文件：{2}\\n空间：{3} MB", "整合包：{0}\\n項目：{1}\\n檔案：{2}\\n空間：{3} MB", preview.PackName, preview.Items.Count, preview.TotalFiles, (preview.TotalBytes / 1048576d).ToString("0.00")));'),
 ('form.Text("无法执行：" + blocker.Message);', 'form.Text(' + T("Cannot run: ", "无法执行：", "無法執行：") + ' + blocker.Message);'),
 ('form.Text("注意：" + warning);', 'form.Text(' + T("Note: ", "注意：", "注意：") + ' + warning);'),
 ('form.Text(item.Path + " · " + item.FileCount + " 个文件");', 'form.Text(item.Path + " · " + Loc.F("{0} files", "{0} 个文件", "{0} 個檔案", item.FileCount));'),
 ('"确认移入回收区" : "确认执行清理／移除"', T("Move to recycle area", "确认移入回收区", "確認移入回收區") + ' : ' + T("Run clean-up / removal", "确认执行清理／移除", "確認執行清理／移除")),
 ('OverviewUi.ConfirmText("确认文件操作", "将执行以上预览范围。整合包删除先移入回收区；缓存清理与回收区永久清除不可恢复。继续？", ',
  'OverviewUi.ConfirmText(' + T("Confirm file operation", "确认文件操作", "確認檔案操作") + ', ' + T("The previewed scope will be processed. Deleted modpacks go to the recycle area first; cache clean-up and emptying the recycle area cannot be undone. Continue?", "将执行以上预览范围。整合包删除先移入回收区；缓存清理与回收区永久清除不可恢复。继续？", "將執行以上預覽範圍。整合包刪除先移入回收區；快取清理與回收區永久清除無法復原。繼續？") + ', '),
 ('files.SavePersonalCopy(task.Proposed.Name + "（个人副本）");', 'files.SavePersonalCopy(task.Proposed.Name + ' + T(" (personal copy)", "（个人副本）", "（個人副本）") + ');'),
 ('OverviewUi.ConfirmUnsaved("修改尚未保存为个人副本", ', 'OverviewUi.ConfirmUnsaved(' + T("Changes are not saved as a personal copy yet", "修改尚未保存为个人副本", "修改尚未儲存為個人副本") + ', '),
 ('var shortcuts = new[] { "打开文件", "所在文件夹", "定位文件" };',
  'var shortcuts = new[] { ' + T("Open file", "打开文件", "開啟檔案") + ', ' + T("Containing folder", "所在文件夹", "所在資料夾") + ', ' + T("Locate file", "定位文件", "定位檔案") + ' };'),
])
print('ok')
