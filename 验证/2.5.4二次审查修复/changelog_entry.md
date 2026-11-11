## 2.5.4 二次审查修复（2.4.6–2.5.3）

审查报告 18 条，逐条核实后全部修复；记录 `验证/2.5.4二次审查修复/结果.md`。

- **目录搬家重写（高危 2–6）：**
  - `MceDirectories` 逐目录回退：旧目录在、新目录不在时继续用旧目录，搬家没做或失败都不会另起一份空数据（原来失败后会新建默认文件，下次重试把真实数据挤成 `.old-layout`）。
  - `MceDirectoryMigration` 每次启动都检查（不再靠标记），旧目录还在就并入：整合包自带旧版 MCE 写出的旧目录切回来后会并回来。并入时较新的一份留在新位置，另一份改名 `.old-layout` 保留。
  - 单个文件失败（被占用、新路径超过 259 字符）只记警告、留在原处，其余照搬；现场两份旧整合包导出里有 6 个超长路径文件，会留在 `exports/` 原处。
  - 等更新写入结束后才搬（移到 `WaitForPendingUpdateWrite` 之后），更新助手在跑就推迟到下次启动。
  - 备份改到 `BepInEx/MCE/layout-backups/`（原 `MCE/backups/` 会被助手 14 天清理），沿用第一次的备份目录、已备份的不重复复制。
  - 改写 JSON 旧路径支持转义反斜杠、正斜杠、根目录用正斜杠三种写法；补扫根目录 `pack-information.json`、`data/legacy`、旧 `overview/`。
- **作者整合包覆盖个人配置（高危 1）：** `PublishedPack` 判断已有个人安装时，未托管改查 `config/ModConfigEnhance/profiles/index.json`（2.5.2 后原写法永远找不到）。
- **词条：** 改了含义的 7 个界面词条（及导入结果提示）换新键名 `_V2`，其他语言和作者词典可以照常翻；旧键名列为退役并从个人文件里清掉。`DropRetired` 只删精确清单（MCE 退役键与旧版 MCE 自己的旧 cfg 说明原文），不再按「含 XUnity」模糊删。
- **导入补翻：** 跳过译文等于原文、或 MCE_ 键译文等于内置英文的行（完整导出预填的英文不再盖掉内置中文），结果里报「没翻的行」数。
- **正则：** 与 XUnity 5.6.1 一致：取第一个匹配、`Match.Result` 作整句结果、命中即停；`r:` 行按 XUnity 词典的转义读（双反斜杠、换行、回车、等号、Unicode 码点、`%3D`）。
- **转义：** `Escape` 在反斜杠后跟等号、`n`、反斜杠或位于末尾时写两个反斜杠，`FindUnescapedEquals` 按转义状态扫描，原文以反斜杠结尾不再让整个语言组加载失败；导出改用同一套。拆出 `Loc.Escaping.cs` 供夹具直接测。
- **跳转：** 中途离开设置页（`OpenDriver.OnDisable`）取消这次打开并复位 `_driven` / `_openHotkeys`；所有取消与失败路径都复位。
- **其余：** cfg 两个旧 XUnity 开关真正删掉（BepInEx `OrphanedEntries`）；语言合并改为先写临时文件再挪原文件；关 XUnity 机翻的提示写明重启前别动 XUnity 设置；未托管时个人语言目录不再扫两遍；`OverviewRepository` 夹具分支与生产同布局；`OverviewProfileStore.RuntimeDirectory` 改比完整路径。
- **夹具：** `layout-checks` 28 项（新增占用、超长、旧版写回、助手在跑、三种 JSON 写法）；`regex-checks` 28 项（新增 XUnity 语义、转义往返）。

