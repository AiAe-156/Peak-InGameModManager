"""Builds Help/zh-tw/<page>.md from the rewritten Help/zh-cn pages (2.3.10).

OpenCC s2twp gives Taiwan wording; FIX then forces the exact Traditional labels the UI uses (from the Loc catalogue,
see labels.txt) and a few MCE terms, so the guide names buttons exactly as they appear.
Pages: overview, mods, profiles, sources, packs, faq, changelog (rewritten). translation/authoring/export keep their
existing zh-tw text, with only the same intro edit applied by hand.
"""
import os, sys
import opencc
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
conv = opencc.OpenCC('s2twp')
PAGES = ['overview', 'mods', 'profiles', 'sources', 'packs', 'faq', 'changelog']
# Exact UI labels (Traditional as shipped in the Loc catalogue) and MCE terms.
FIX = [
 ('儲存設定', '儲存設定'), ('儲存配置', '儲存設定'), ('變更檢查目錄', '變更檢查目錄'),
 ('標籤篩選', '標簽篩選'), ('隱藏已停用', '隱藏已停用'), ('來源與更新', '來源與更新'), ('管理中心', '管理中心'),
 ('管理配置方案…', '管理設定方案…'), ('管理設定方案…', '管理設定方案…'),
 ('預覽切換影響', '預覽切換影響'), ('存入整合包庫（不改動遊戲）', '存入整合包庫（不改動遊戲）'),
 ('同意下載並補齊', '同意下載並補齊'), ('儲存當前配置為臨時整合包', '儲存目前配置為臨時整合包'), ('儲存目前配置為臨時整合包', '儲存目前配置為臨時整合包'),
 ('儲存當前配置並切換', '儲存目前設定並切換'), ('儲存目前配置並切換', '儲存目前設定並切換'), ('直接切換', '直接切換'),
 ('重試首次預設方案應用', '重試首次默認方案應用'), ('查看更新變動', '查看更新變動'), ('所有小角標標記為已讀', '所有小角標標記為已讀'),
 ('批次查詢缺失來源', '批量查找缺失来源'), ('批量查詢缺失來源', '批量查找缺失来源'), ('批次查找缺失來源', '批量查找缺失来源'),
 ('整理到分組資料夾', '整理到分組資料夾'), ('新建子分組', '新增子分組'), ('移到頂層', '移到頂層'), ('刪除分組', '刪除分組'),
 ('標記為本地', '標記為本機'), ('手動識別', '手動識別'), ('更新選中', '更新選取'), ('全部更新', '全部更新'),
 ('改譯名', '改譯名'), ('卸載基礎元件', '解除安裝基礎組件'), ('解除安裝基礎元件', '解除安裝基礎組件'),
 ('還原原版預設到草稿', '還原原版預設到草稿'), ('恢復原版預設到草稿', '還原原版預設到草稿'),
 ('重新整理', '重新整理'), ('過載翻譯', '重載翻譯'), ('重灌翻譯', '重載翻譯'),
 # MCE terms
 ('基礎元件', '基礎組件'), ('元件', '組件'), ('解除安裝', '解除安裝'), ('卸載', '解除安裝'),
 ('外掛', '插件'), ('嵌套', '巢狀'), ('上級分組', '上級分組'), ('資源包', '資源包'), ('散放 DLL', '散放 DLL'),
 ('本地', '本機'), ('預設方案', '預設方案'), ('作者預設', '作者預設'),
 ('模組概覽', '模組概覽'), ('目錄整理', '目錄整理'), ('更新助手', '更新助手'),
 ('Steam Deck', 'Steam Deck'),
 # OpenCC s2twp over-corrections and terms that must match the UI (UI uses 參數/設定/發佈/工具列/名稱欄)
 ('引數', '參數'), ('釋出', '發佈'), ('配置方案', '設定方案'), ('配置與快捷鍵', '設定與快捷鍵'), ('配置儲存', '設定儲存'),
 ('當前配置', '目前設定'), ('實際配置', '實際設定'), ('個人配置', '個人設定'), ('配置檔案', '設定檔'),
 ('工具條', '工具列'), ('名稱列', '名稱欄'), ('手柄', '控制器'), ('當前', '目前'), ('預設只讀', '預設唯讀'),
 ('恢復原版', '還原原版'), ('重啟遊戲', '重新啟動遊戲'), ('重啟 PEAK', '重新啟動 PEAK'), ('重啟後', '重新啟動後'), ('待重啟', '待重新啟動'), ('等重啟', '等重新啟動'), ('／重啟', '／重新啟動'),
 ('批量', '批次'), ('批次查找缺失来源', '批量查找缺失来源'),
]
for page in PAGES:
    src = open(os.path.join(ROOT, 'Help', 'zh-cn', page + '.md'), encoding='utf-8').read()
    out = conv.convert(src)
    for a, b in FIX: out = out.replace(a, b)
    # keep media paths and anchors exactly
    out = out.replace('media/', 'media/')
    open(os.path.join(ROOT, 'Help', 'zh-tw', page + '.md'), 'w', encoding='utf-8', newline='\n').write(out)
    print('wrote', page)
