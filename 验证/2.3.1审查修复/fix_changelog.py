import sys
sys.path.insert(0, r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\ModConfigEnhance\验证\2.3.1审查修复")
import io
from patchlib import ROOT

path = ROOT + r"\CHANGELOG.md"
raw = io.open(path, "rb").read().decode("utf-8")
text = raw.replace("\r\n", "\n")
start = text.index("## 2.3.1 ")
end = text.index("## 2.3.0 ")
entry = """## 2.3.1 2.3.0 审查修复；目录整理窗口加大、空文件夹改复选框随整理放进回收站

- **方案版本号：** `applied-profile.json` 与草稿恢复读取接受 schemaVersion 1/2（含嵌套分组的方案应用后快照写 2，旧逻辑只认 1，重启后概览加载失败）；worker 非完整整合包导入 `ProjectDefaults` 接受默认方案为 2（配置快照仍只认 1）。
- **分组归属落盘：** `SavePackInformation` 先 `MergeEditedRules`，`GetRule` 发出的临时规则（智能分组、分组编辑器设置的成员）不再在分组保存时丢失。
- **停用过的模组：** 目录整理采集、智能分组扫描、嵌套判定都把 `manifest.json.old` 视为模组清单（MCE 停用时会把 manifest 改名），停用模组不再被当成分类文件夹或拆成两半。
- **嵌套启停：** `OverviewEnablementDraft.Planned` 改为「自身计划开 且 外层计划开」，只记了外层停用的方案落盘时内层也随之停用；不可启停的内层（受保护库、身份冲突）不连带、不挡外层，也不显示被外层挡住。`nestedEnabled` 是本机记忆，方案导出与配置预设导出时剥离。
- **分组菜单回滚：** 概览分组标题菜单的新建/删除/挂接保存失败时恢复分组、成员与折叠状态。
- **整理回退：** 提交时记录本次新建的目录与被复用的空目录（`Transaction.CreatedDirectories` / `ReplacedEmptyDirectories`），回退只删前者、恢复后者；用户原有的空分组文件夹不再被删。旧事务没有这份记录时，回退不删任何目录。
- **空文件夹：** 预览窗口去掉逐个「删除」按钮与弹窗，改为分区总复选框 + 每行复选框（默认勾选）；确认时 `LayoutQueueRequest.RecycleEmpty` 带上勾选项，worker 只接受预览里列为空文件夹的路径（按改名后的位置），全部移动成功后由 worker 侧新的 `RecycleBin` 放进回收站，放之前逐个复查（仍限 plugins 内、无 DLL/manifest/链接），不合格的跳过并写入任务提示，不算整理失败。插件侧 `RecycleBin.cs` 移除。规划器跟踪空文件夹改名后的位置；会被移入模组的空分组文件夹不再列为空文件夹。
- **整理窗口：** 窗口 1680×1300（按屏幕封顶），标题区少一行；状态与助手说明并为一行；总数、计数标签与筛选按钮同一行（放不下时换行）。
- **其他：** 删除分组时上移的子分组连续占据原分组的位置；编辑器删除确认文案改为「上移一层」；棕色系与金色系重复的 `#E0B29B` 改为 `#D4A88A`。README 标题的版本号自 2.2.11 起没更新，已改。
- 验证：分组树 38、嵌套启停 26（含现场）、整理规划 27、智能分组 11（含现场）、InstallerChecks 132（新增 S38：空文件夹回收与回退保留原有目录）。专项 `验证/2.3.1审查修复/`。

"""
text = text[:start] + entry + text[end:]
io.open(path, "wb").write(text.replace("\n", "\r\n").encode("utf-8"))
print("ok")
