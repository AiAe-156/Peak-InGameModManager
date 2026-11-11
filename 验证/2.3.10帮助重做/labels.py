"""Looks up the exact English / Simplified / Traditional UI labels so the help pages name buttons exactly as the UI does."""
import os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'src')
cn = sys.argv[1:] or ['重试首次默认方案应用','保存当前配置为临时整合包','预览切换影响','同意下载并补齐','导入整合包','存入整合包库（不改动游戏）','空间管理','查看更新变动',
  '所有小角标标记为已读','批量查找缺失来源','整理到分组文件夹','管理配置方案…','保存配置','变更检查目录','标签筛选','隐藏已停用','来源与更新','管理中心',
  '保存当前配置并切换','直接切换','新建子分组','移到顶层','删除分组','标记为本地','手动识别','更新选中','全部更新','模组概览','快捷键','改译名','卸载基础组件',
  '重载翻译','导出翻译','补漏翻','仅导出已选','显示原文','日志','更多','刷新','编辑模式','恢复原版预设到草稿','检查更新','使用说明','保存快捷键','模组商店',
  '导入本地 DLL / ZIP / 文件夹','导入商店模组','整合包库','切换到此方案','网络','工具','方案','分组','发布','语言']
LIT = r'"((?:[^"\\]|\\.)*)"'
term = re.compile(r'new\(\s*' + LIT + r'\s*,\s*' + LIT + r'\s*,\s*' + LIT + r'\s*,\s*' + LIT + r'\s*\)')
tri = re.compile(r'(?:Loc\.)?(?:T|F)\(\s*' + LIT + r'\s*,\s*' + LIT + r'\s*,\s*' + LIT)
found = {}
for d, _, fs in os.walk(SRC):
    if os.sep + 'bin' in d or os.sep + 'obj' in d: continue
    for f in fs:
        if not f.endswith('.cs'): continue
        t = open(os.path.join(d, f), encoding='utf-8').read()
        for m in term.finditer(t): found.setdefault(m.group(3), (m.group(2), m.group(4)))
        for m in tri.finditer(t): found.setdefault(m.group(2), (m.group(1), m.group(3)))
for c in cn: print(c, '=>', found.get(c, ('??', '??')))
