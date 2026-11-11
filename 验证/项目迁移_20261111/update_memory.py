# -*- coding: utf-8 -*-
"""更新记忆：路径改到新工程文件夹，新增 igmm-project-folder。"""
import io
import os

MEM = r"C:\Users\AiAe\.claude\projects\F--Games-SteamLibrary-steamapps-common-PEAK\memory"
BS = "\\"
os.chdir(MEM)
for f in ["mce-nested-groups-layout.md", "mce-no-overdefense-layout.md", "mce-ops-gotchas.md", "peak-mce-cascade-vanilla-queue.md", "peak-stale-manifest-copies.md"]:
    s = io.open(f, encoding="utf-8").read()
    n = s.replace("MODs/ModConfigEnhance/", "MODs/InGameModManager/").replace("MODs" + BS + "ModConfigEnhance" + BS, "MODs" + BS + "InGameModManager" + BS)
    if n != s:
        io.open(f, "w", encoding="utf-8", newline="\n").write(n)
        print("updated", f)

f = "mce-store-public-version.md"
s = io.open(f, encoding="utf-8").read()
a = "现在的 `MODs/ModConfigEnhance/` 工程里，内容其实是 IGMM（2.4.5），但还沿用 MCE 的 GUID、包名和目录。"
b = "IGMM 工程 2026-11-11 起在 `MODs/InGameModManager/`（新 git 仓库，旧 `MODs/ModConfigEnhance/` 只读留档），但还沿用 MCE 的 GUID、包名、csproj 名和配置目录。"
assert a in s
s = s.replace(a, b)
a2 = "- IGMM 连同项目文件夹整个建成**新的 git 项目**。\n- 拆分和新建仓库都**以后再做**，当时只是先同步理念。"
if a2 in s:
    s = s.replace(a2, "- IGMM 连同项目文件夹整个建成**新的 git 项目**：文件夹和仓库 2026-11-11 已建（见 [[igmm-project-folder]]）；GUID、包名等拆分以后再做。")
io.open(f, "w", encoding="utf-8", newline="\n").write(s)
print("updated", f)

io.open("igmm-project-folder.md", "w", encoding="utf-8", newline="\n").write("""---
name: igmm-project-folder
description: 游戏内模组管理器工程 2026-11-11 起在 MODs/InGameModManager（新 git 仓库）；旧 MODs/ModConfigEnhance 只读留档（备份、发布 ZIP 只在那里）
metadata:
  type: project
---

2026-11-11 用户：「早就该建个新名称的项目文件夹了」。选定文件夹名 `InGameModManager`、只搬工程（旧的留档）、新建 git 仓库。

- 工程：`F:/Games/SteamLibrary/steamapps/common/PEAK/MODs/InGameModManager/`，git `main`，首个提交 `09182a7`，没设远程（等用户建好 GitHub 仓库再说）。
- 旧文件夹 `MODs/ModConfigEnhance/` 只读留档：`备份/`（历次部署前安装备份、改前源码快照）、`发布/`（旧 ZIP）、历史验证产物只在那里；旧 `.git` 连着 `github.com/AiAe-156/Peak-ModConfigEnhance`，留给以后的 MCE 简化版。
- 没改：csproj 名 `ModConfigEnhance.csproj`、`DeployDir`、GUID、DLL 名、商店包名、`config/ModConfigEnhance/`。
- `验证/` 夹具源码进了新仓库（`.gitignore` 排除 bin/obj、deployment_*、runs、日志、dll/exe/zip）；`图片/` 不进仓库。

**Why:** 显示名早已改成游戏内模组管理器，又决定 IGMM 作为新模组首发、与 MCE 拆开。

**How to apply:** 所有改动在新文件夹做，不要再写旧文件夹；记录里写 `ModConfigEnhance/备份/...` 的去旧文件夹找，`ModConfigEnhance/验证/...` 两边都有。相关：[[mce-store-public-version]]
""")
p = "MEMORY.md"
s = io.open(p, encoding="utf-8").read().rstrip("\n")
s += "\n- [IGMM 工程文件夹](igmm-project-folder.md) — 2026-11-11 起在 MODs/InGameModManager（新 git）；旧 MODs/ModConfigEnhance 只读留档，备份和旧 ZIP 只在那里\n"
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("ok")
