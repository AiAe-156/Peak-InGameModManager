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


prepend("README.md", "## 2.3.2 ", [
    "## 2.3.3 退出游戏后的整理/更新会弹出完成窗口；基础组件找回 MonoDetour；小窗口不再铺满屏幕；子分组与新建分组同一个编辑器",
    "",
    "**退出游戏后有结果了。** 等游戏退出再执行的目录整理、更新、回退，原先在游戏还没完全关掉时就悄悄做完，没有任何提示。现在只要这次真的改了文件，结束后会弹出结果窗口：全部完成写「已完成，下次启动生效」，20 秒后自动关闭；有失败写「已完成，有问题」，停留 10 分钟方便查看。",
    "",
    "**基础组件少了两个，找回来了。** MonoDetour 与 MonoDetour_BepInEx_5 的 DLL 直接放在 `core`/`patchers` 根下，说明文件放在各自的文件夹里；概览只在文件夹里找 DLL，于是这两项一直没列出来（文件本身完好、照常加载）。现在按这两个包自己的文件名认出来，基础组件恢复为 5 项。",
    "",
    "**窗口按内容大小。** 新建分组/改名/输入地址这类只有一两个输入框的窗口、确认框、选择列表、颜色输入、删除确认原先都按屏幕比例铺开（最大的占屏幕 66%×80%），现在按内容定高、居中显示；标题较长的确认框会自动换行。",
    "",
    "**新建子分组与新建分组一样。** 分组标题右键「新建子分组」和概览里的「新建分组」现在打开同一个分组编辑器，带颜色、说明和直接勾选模组，上级分组已预先选好。",
    "",
    "**空分组也显示。** 刚建好的子分组、或成员都移进了子分组的分组，原先在非编辑模式下整个看不见，像没建成一样；现在都会显示（计数为 0）。搜索、标签筛选、相关筛选时仍只显示有匹配的分组。",
])
prepend("CHANGELOG.md", "## 2.3.2 ", [
    "## 2.3.3 退出后完成窗口；基础组件识别 MonoDetour 平铺布局；对话框按内容定尺寸；新建子分组走完整编辑器；空分组可见",
    "",
    "- **退出后无提示：** 等退出的任务在游戏进程尚在退出时开始执行，`Files.PeakRunning()` 为真，助手不开窗（避免盖在全屏游戏上），做完即退出，用户看不到任何结果（现场 11-09 12:46 整理 13 处移动、8 个空文件夹回收全部成功，但无提示）。`QueueWorker.WroteFiles` 记录本进程进入 Deploying/RollingBack 的任务；`Run` 结束后若有记录且游戏已退出，`Program` 打开（或沿用）窗口调用 `WorkerWindow.ShowFinished`：标题与摘要写完成/有问题、停止按钮改「关闭」、置前一次、倒计时自动关闭（成功 20 秒，有失败 10 分钟），`--once` 不弹。`PublishStatus` 的卡片投影抽成 `QueueWorker.View` 供复用。",
    "- **基础组件少两项：** MonoDetour 包把 DLL 平铺在 `core/`、`patchers/` 根下，只把 manifest/说明放进 `MonoDetour-MonoDetour/`、`MonoDetour-MonoDetour_BepInEx_5/`；`InfrastructurePackages.Scan` 只在 manifest 目录内找运行时文件，找不到即丢弃该包。新增 `LooseAssemblies`（仅这两个已知包、按包自带文件名）在区域根目录补认。现场只读探针：扫描 4→6 行、概览 3→5 行。11-02 两次 MonoDetour「同版本重装」曾把根目录的 MonoDetour DLL 归档进 `MCE/backups`（`9f0964f3`、`8a43e351`）后由 17:06 的更新放回，现装文件完整、`LogOutput.log` 显示 `0.com.github.MonoDetour.BepInEx.5 0.7.17.0` 正常加载；该归档规则（`InstallPlanner` 对 core/patchers 同名程序集归档）本轮未改。",
    "- **对话框过大：** `OverviewUi.AskValidated`（新建分组、改名、地址/路径输入、`Confirm`）由 66%×80% 屏幕比例改为按标题与字段定高、居中、宽 820（无字段 660），标题可换行、调色板列数按实际宽度；`ConfirmUpdatePolicy` 改 820×360、`Choose` 选择列表按选项数定高（宽 640）；分组编辑器二级框 `AskColor` 760×330、`Secondary` 确认 760×250（原 64%×68% / 64%×46%）。全部以屏幕为上限。",
    "- **新建子分组：** 列表分组菜单「新建分组/新建子分组」改走 `OverviewListView.NewGroupIn(parentId)` → `NewGroupRequested` → 管理中心分组页 `_managementNewGroupParent`，以 `OverviewGroupEditor.NewGroupParent` 预设上级打开完整编辑器（颜色、说明、成员勾选）；删除旧的单输入框 `NewGroup`。",
    "- **空分组不显示：** `HeaderOrder` 只在编辑模式把无成员分组列入；改为无搜索/标签/相关筛选时始终列出全部分组。",
    "- **部署与引用路径：** 用户 11-09 12:46 执行整理后 MCE 现装移到 `plugins/0基础模组/ModManager_InGame_2.0.0_20261029_215224/`，`DeployDir` 随之更新；PEAKLib.Core/UI 引用移到 `plugins/依赖模组/`，ModConfig 引用移到 `plugins/0基础模组/`（旧路径编译报 112 个类型缺失）。",
])
print("ok")
