"""Release check: hard-coded Chinese UI text in the MCE plugin that is not three-language (2.3.8).

Covered and therefore not reported:
  * T/F/L/Loc.T/Loc.F with literal arguments, and Loc.T(variable, "简", "繁");
  * calls with three or more literal arguments (term catalogues) and ["中文"] = "English" maps;
  * comments, log calls, exceptions, Regex patterns;
  * LOG_ONLY files: their Chinese only reaches the BepInEx log, file/folder names or matching keys
    (checked by hand 2026-11-09);
  * ALLOWED lines: per-line exceptions with the reason, keyed by a fragment of the line.
Exit code 1 when anything remains, so a new feature that forgets Loc fails the regression.
"""
import os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'src')
LOG_ONLY = {
    'ModConfigTreeLayoutPatch.cs', 'ModConfigLocalizationPatch.cs', 'TimeThemeBridge.cs', 'XUnityBridge.cs', 'Plugin.cs',
    'LogExport.cs', 'ManagerLog.cs', 'ModConfigPaperStyle.cs', 'PublicationFilePolicy.cs', 'GroupFolderPlanner.cs',
    'DiagnosticUiLanguage.cs', 'UpdateMessageCatalog.cs',
    # 2.5.4: directory-migration report lines go to the BepInEx log only (runs before Loc is set up).
    'MceDirectoryMigration.cs',
    # ModConfigLook.State is only written to the BepInEx log at startup ([配色] line).
    'ModConfigLook.cs',
    # Job-state labels and queue notes feed ManagerLog from the background read thread (Loc is not thread-safe);
    # the update list draws its own three-language labels (UpdateQueueView.cs).
    'OverviewUpdateQueue.cs',
    # Built off the main thread from worker data; translated on display where it matters. Worker-side
    # localisation of pack reports is a separate task (审查结论.md).
    'PackReportText.cs', 'StoredPackDetails.cs',
    # Simplified phrases translated at draw time by StorageText.Display / DiagnosticUiLanguage.Text / the editor's
    # own English+Traditional label maps.
    'PackStorageBrowser.cs', 'LogWindow.cs', 'OverviewDocumentView.cs',
    # Folder-name filters and data keys.
    'OverviewScanInventory.cs', 'OverviewRepository.cs', 'OverviewCatalogue.cs', 'ShopInstalled.cs', 'PublishedPack.cs',
    'PackContracts.cs', 'IntroductionDocument.cs', 'OverviewRepository.Diagnostics.cs', 'ModConfigSupport.cs', 'LogRepeatIndex.cs',
}
ALLOWED = {
    '"Language changes / 语言变更详情"': 'log channel name shown bilingually in the log window source list (unchanged since 2.x; line moved by the 1.0.0 rename)',
    'Loc.T(tag, ': 'English is the store tag itself (variable); three languages given',
    'Loc.T(EnablementNote, ': 'English comes from a constant; three languages given',
    'KeyCode / 按键名清单': 'translation-export file header writes both Chinese variants on purpose',
    'KeyCode / 按鍵名清單': 'translation-export file header writes both Chinese variants on purpose',
    '仅导出已选：左栏': 'translation-export file header writes both Chinese variants on purpose',
    '僅導出已選：左欄': 'translation-export file header writes both Chinese variants on purpose',
    '"国").y': 'measurement sample glyph, not shown',
    '缺字「': 'font diagnostics written to the log',
    'IndexOf("依赖"': 'matches worker message text (Chinese and English both checked)',
    'IndexOf("副本"': 'matches worker message text (Chinese and English both checked)',
    'IndexOf("路径"': 'matches worker message text (Chinese and English both checked)',
    'StartsWith("无独立整合包身份"': 'matches a worker detail line',
    'f.Group == "配置文件"': 'compares a worker group tag',
    'CategoryNames = {': 'internal category keys; captions are Loc.T below',
    'category switch { "模组" =>': 'switch on internal keys; captions are Loc.T',
    'Excluded attachment / 已确认排除附带内容': 'bilingual warning stored in the published pack manifest',
    'MissingTargets(OverviewRepository repo': 'bilingual warning stored in the published pack manifest',
    'profile.name != "现有配置"': 'recognises legacy default profile names',
    'cfg.Bind("模组总览 - 依赖提示"': 'BepInEx config section/key names (kept for existing cfg files)',
    '+ "（已移除）"': 'mods.yml sync summary written to the log',
    '"→启用" : "→停用"': 'enable/disable summary written to the log',
    'Core components 核心组件 核心組件': 'search keywords in all three languages',
    'Startup patchers 启动补丁 啟動補丁': 'search keywords in all three languages',
}
LIT = r'"(?:[^"\\\n]|\\.)*"'
HAN = re.compile(r'"[^"\n]*[一-鿿][^"\n]*"')
CALL = re.compile(r'(?<![\w.])(?:Loc\.)?(?:T|F|L)\(\s*' + LIT + r'\s*,\s*' + LIT + r'(?:\s*,\s*' + LIT + r')?', re.S)
TRI = re.compile(r'\(\s*' + LIT + r'(?:\s*,\s*' + LIT + r'){2,}', re.S)
MAP = re.compile(r'\[\s*' + LIT + r'\s*\]\s*=\s*' + LIT)
SKIP = re.compile(r'Log(?:Info|Warning|Error|Debug|Message)\(|Plugin\.Log\.|ManagerLog\.|throw new |Exception\(|Regex\(|IsMatch\(')
def blank(m): return re.sub(r'[^\n]', ' ', m.group(0))
found = []
for d, _, fs in os.walk(SRC):
    if os.sep + 'bin' in d or os.sep + 'obj' in d: continue
    for f in fs:
        if not f.endswith('.cs') or f.startswith('Loc') or f in LOG_ONLY: continue
        p = os.path.join(d, f)
        text = open(p, encoding='utf-8').read(); lines = text.split('\n')
        work = '\n'.join(l if not l.strip().startswith('//') else '' for l in lines)
        work = CALL.sub(blank, work); work = TRI.sub(blank, work); work = MAP.sub(blank, work)
        for n, line in enumerate(work.split('\n'), 1):
            raw = lines[n - 1]
            if not HAN.search(line) or SKIP.search(raw) or any(k in raw for k in ALLOWED): continue
            found.append(f'{os.path.relpath(p, ROOT)}:{n}: {raw.strip()[:180]}')
for x in found: print(x)
print(f'-- {len(found)} line(s)')
sys.exit(1 if found else 0)
