import io
import os

PROJECT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")


def prepend(name, anchor, lines):
    path = os.path.join(PROJECT, name)
    s = io.open(path, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in s else "\n"
    assert s.count(anchor) == 1, (name, anchor)
    s = s.replace(anchor, nl.join(lines) + nl + nl + anchor, 1)
    io.open(path, "w", encoding="utf-8", newline="").write(s)


prepend("README.md", "## 2.3.3 ", [
    "## 2.3.4 识别更全：资源包、任意后缀停用的 DLL、散放 DLL 都会显示",
    "",
    "**只有 manifest、没有 DLL 的包（资源包）现在会显示。** 比如 Aeralis 的雾效和光追资源包，里面只有 .bundle 和贴图，以前概览里根本看不到；现在作为普通一行显示，名称旁有绿色小方块角标「资源包」，可以启停（改名它的 manifest）、分组、整理。",
    "",
    "**DLL 后面接任何字符都认作已停用。** 以前只认 `xxx.dll.<后缀>`，像 `ModConfigEnhance.dll1`、`xxx.dllx` 这种认不出来，整个模组就消失了；现在都显示为已停用，可以直接在概览里启用（改回 `.dll`）。`.dll.config`、`.dll.mdb`、`.dll.pdb`、`.dll.xml` 这类附属文件不算。MCE 自己停用时仍写 `.d`。",
    "",
    "**直接放在 plugins 根下的单个 DLL** 有紫色小方块角标「散放」，悬停说明目录整理会把它收进单独的文件夹。",
    "",
    "**开发侧：** 本分区新增部署前路径检查 `MODs/_tools/check_deploy_dir.py`，部署目标与现装位置不一致就停下；MCE 部署脚本已内置。整理后失效的 11 个工程部署路径已改到现装位置。",
])
prepend("CHANGELOG.md", "## 2.3.3 ", [
    "## 2.3.4 识别：资源包、任意后缀停用、散放 DLL；部署前路径检查",
    "",
    "- **资源包（用户拍板：正常显示）：** `OverviewScanInventory.Manifests` 收集 plugins 下全部 `manifest.json(.old)`；`OverviewCatalogue.AddResourcePacks` 为没有任何程序集、也不是别的包一部分的 manifest 文件夹建行（`OverviewMod.ResourcePack`，ID `resource:<Team-Name>`），排除：打包结构里的 `x/plugins/manifest.json`、已有行的包根、其下有别的包、其下有程序集、已确认为基础组件的同名包（MonoDetour 的 plugins 那一半）。`ModToggleService` 对资源包跳过程序集身份核对，只改名 manifest。扫描结果改为显式传入（`DiscoverScan`），不读缓存字段。",
    "- **任意后缀停用（用户拍板）：** `PluginFileNames.DisabledOffset` 由「`.dll.` 后有字符」改为「最后一个 `.dll` 后有字符」，排除 `.config/.mdb/.pdb/.xml` 附属文件；`.mce-part`/`.mce-candidate` 更新产物照旧排除。现场 `ModConfigEnhance.dll1` 由此被识别。",
    "- **散放 DLL：** `OverviewMod.LooseFile`（plugins 根下的程序集）。",
    "- **角标：** `StatusChip.ResourcePack`（绿）、`StatusChip.LooseFile`（紫），排在待处理与嵌套角标之后，标题预留按实际数量计算。",
    "- **部署前路径检查（分区规则 6.2.1）：** 新 `MODs/_tools/check_deploy_dir.py`（只读；OK / MOVED / ELSEWHERE / NEW，后两类不一致退出码 1）；MCE `deploy.py` 模板内置，不一致即中止。随用户授权修正 11 个工程 `DeployDir`（BetterPing、Better_MicIndicator、LocalFix、MicEnhance、BetterPeakVoiceFix→1.1.8、RecentlyPlayedWith→1.0.2、ZombieTweaks→3玩法模组、BetterReviveTeleport、PlantholderOverhaul_ZH→0.1.7、SoFly_ZH、RandomTerrainEnhance）；3 个本机从未安装的保持原样。",
    "- 验证：三项构建 0/0；只读形态探针 18 项（真实 `PluginFileNames` + `OverviewScanInventory` 对现装：111 个程序集、63 个 manifest，认出 2 个 Aeralis 资源包与 `.dll1`，打包结构不误判）；旧夹具 文件启停冲突 70、扫描证据 24、下拉后缀 105 通过；完整回归见结果记录。",
])
print("ok")
