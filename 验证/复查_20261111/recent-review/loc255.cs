#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using PEAKLib.UI;
using UnityEngine;

namespace ModConfigEnhance;

/// <summary>
/// 本模组的语言子系统。
///
/// 词条：每条一个稳定键（<c>MCE_*</c>，写进游戏语言表 <c>LocalizedText.mainTable</c>，与 PEAKLib 的 MenuAPI 同一条路），
/// 界面文字都挂 <c>LocalizedText</c> 组件或在需要时用 <see cref="Get"/> 取，切语言时游戏的 <c>RefreshAllText</c> 统一刷新。
///
/// 语言文件：<c>BepInEx\config\ModConfigEnhance\language\*.txt</c>，格式与 XUnity 词典相同（<c>键=译文</c>，// 注释，\n \= 转义，<c>r:"正则"=译文</c>）。
/// 三类行：<c>MCE_*</c> 键行是本模组界面词条（只在想改内置文字时写）；<c>r:</c> 行是正则规则；其余是各模组文字的原文 → 译文。
/// 2.5.0 起全部由本模组自己显示（侧栏、概览、说明面板、ModConfig 设置页），不再交给 XUnity。
/// 文件名不以 <c>_</c> 开头的 .txt 才算语言文件；首行可写 <c>// display: 显示名</c>。说明在根目录 <c>readme.md</c>。
///
/// 两种模式：
///   · 跟随游戏语言（默认）：en-us / zh-cn / zh-tw 三份内建文件各填进对应语言槽，其余语言回落英文；
///   · 指定文件：该文件的译文填满全部语言槽，游戏切语言不再影响本模组管的文字。
/// </summary>
internal static partial class Loc
{
    internal const string KeyPrefix = "MCE_";
    internal const string FollowGame = "";
    internal const string ReadmeFile = "readme.md";

    private const string DisplayMarker = "// display:";

    internal static string RootDirectory => Path.Combine(Paths.ConfigPath, "ModConfigEnhance");
    internal static string LanguageDirectory => Updating.MceDirectories.Language(RootDirectory);
    /// <summary>2.5.0：导出的待翻文本（原 export\，与整合包导出的 exports\ 分开）。</summary>
    internal static string ExportDirectory => Updating.MceDirectories.TranslationOutput(RootDirectory);
    /// <summary>2.5.2：运行数据（原 runtime\）、备份、说明覆盖、旧版数据都在 data\ 下；整合包导出在 output\packs。</summary>
    internal static string RuntimeDirectory => Updating.MceDirectories.Runtime(RootDirectory);
    internal static string BackupDirectory => Updating.MceDirectories.Backups(RootDirectory);
    internal static string PackOutputDirectory => Updating.MceDirectories.PackOutput(RootDirectory);

    // ── 内建词条 ──────────────────────────────────────────────────

    internal readonly struct Term
    {
        public readonly string Key;
        public readonly string English;
        public readonly string SimplifiedChinese;
        public readonly string TraditionalChinese;

        public Term(string key, string english, string simplifiedChinese, string traditionalChinese)
        {
            Key = key;
            English = english;
            SimplifiedChinese = simplifiedChinese;
            TraditionalChinese = traditionalChinese;
        }
    }

    /// <summary>界面词条（键不含前缀）。英文是回落值，也是模板里等号右边的默认内容。</summary>
    internal static readonly Term[] Terms =
    {
        // ModConfig 自己的界面文字
        new("SEARCH", "Search", "搜索", "搜尋"),
        new("SEARCH_HERE", "Search here", "在此处搜索", "在此處搜尋"),
        new("MODS", "MODS", "模组", "模組"),
        new("SECTIONS", "SECTIONS", "类目", "類目"),
        new("DEFAULT", "DEFAULT", "默认", "預設"),
        new("CLEAR", "CLEAR", "清空", "清除"),
        new("SELECT_KEY", "SELECT A KEY", "请按下按键", "請按下按鍵"),
        new("BOUND_TO", "This key is bound to: ", "此键已绑定给：", "此鍵已綁定給："),
        new("FILTER_BOOLS", "Bools", "开关", "開關"),
        new("FILTER_STRINGS", "Strings", "文本", "文字"),
        new("FILTER_NUMBERS", "Numbers", "数值", "數值"),
        new("FILTER_ENUMS", "Enums", "下拉框", "下拉框"),
        new("FILTER_CONTROLS", "Controls", "按键", "按鍵"),
        new("DD_NOTHING", "Nothing", "无", "無"),
        new("DD_EVERYTHING", "Everything", "全部", "全部"),
        new("DD_MIXED", "Mixed...", "部分", "部分"),
        // 左栏表头 / 复选框
        new("PIN", "Pin", "置顶", "置頂"),
        new("EXPORT_COL", "Export", "导出", "導出"),
        new("EXPORT_SELECTED", "Selected only", "仅导出已选", "僅導出已選"),
        new("KEYS_ONLY", "Keybind options only", "只显示快捷键选项", "只顯示快捷鍵選項"),
        new("JUMP_HOTKEYS", "Go to keybindings", "跳转到快捷键设置页", "跳轉到快捷鍵設定頁"),
        new("TIP_JUMP_HOTKEYS", "Open the overview's Keybindings tab, which lists every mod's keybinds in one place.", "打开概览的「快捷键设置」页签，集中查看与修改所有模组的快捷键。", "開啟概覽的「快捷鍵設定」頁籤，集中檢視與修改所有模組的快捷鍵。"),
        new("BTN_THEME", "Theme", "主题", "主題"),
        new("TIP_THEME", "Switch the colour theme between blue and purple. The overview follows the next time it opens.", "在蓝色与紫色主题之间切换。概览在下次打开时跟着切换。", "在藍色與紫色主題之間切換。概覽在下次開啟時跟著切換。"),
        // 工具行按钮
        new("BTN_EXPORT", "Export texts", "导出翻译", "導出翻譯"),
        new("BTN_UNTRANSLATED", "Untranslated", "补漏翻", "補漏翻"),
        new("BTN_SHOW_ORIGINAL", "Show original", "显示原文", "顯示原文"),
        new("BTN_SHOW_TRANSLATED", "Show translated", "显示译文", "顯示譯文"),
        new("BTN_RELOAD", "Reload texts", "重载翻译", "重載翻譯"),
        new("BTN_OPEN_FOLDER", "Open folder", "打开目录", "打開目錄"),
        new("BTN_REFRESH", "Refresh", "刷新", "重新整理"),
        new("BTN_GROUPS", "Groups", "分组", "分組"),
        new("GROUP_UNGROUPED", "Ungrouped", "未分组", "未分組"),
        new("NOTICE_GROUPS_UNAVAILABLE", "Could not open the group editor, see the log.", "无法打开分组编辑，详见日志。", "無法開啟分組編輯，詳見日誌。"),
        new("SORT_DEFAULT", "Load order", "加载顺序", "載入順序"),
        new("SORT_LOAD_DESC", "Load order, reversed", "加载倒序", "載入倒序"),
        new("SORT_NAME", "Name A→Z", "名称 A→Z", "名稱 A→Z"),
        new("SORT_NAME_DESC", "Name Z→A", "名称 Z→A", "名稱 Z→A"),
        new("BTN_CLEAR_UPDATES", "Clear updates", "清空更新标识", "清除更新標記"),
        new("TIP_CLEAR_UPDATES", "Mark all updates as read.", "将全部更新标识标记为已读。", "將全部更新標記為已讀。"),
        new("LANG_FOLLOW", "Follow game language", "跟随游戏语言", "跟隨遊戲語言"),
        // 说明面板
        new("META_DEFAULT", "Default", "默认", "預設"),
        new("META_RANGE", "Range", "范围", "範圍"),
        new("META_OPTIONS", "Options", "可选", "可選"),
        new("META_ON", "ON", "开", "開"),
        new("META_OFF", "OFF", "关", "關"),
        new("META_EMPTY", "(empty)", "（空）", "（空）"),
        // 提示（说明面板里显示；{0} {1} {2} 是占位符，译文里保留）
        new("NOTICE_SHOWING_TRANSLATED", "Showing translations.", "已切回译文。", "已切回譯文。"),
        new("NOTICE_SHOWING_ORIGINAL_V2",
            "Showing original texts of mods (click again to restore).",
            "正在显示模组原文（再点一次切回译文）。",
            "正在顯示模組原文（再點一次切回譯文）。"),
        new("NOTICE_RELOADED_V2",
            "Re-read the language files from disk.",
            "已从磁盘重新读取语言文件。",
            "已從磁碟重新讀取語言檔案。"),
        new("NOTICE_EXPORT_FAILED", "Export failed, see the log.", "导出失败，详见日志。", "導出失敗，詳見日誌。"),
        new("NOTICE_UNTRANSLATED", "{0} untranslated ({1} translated, {2} already CJK)", "未翻译 {0} 条（已翻 {1} 条，已是中文 {2} 条）", "未翻譯 {0} 條（已翻 {1} 條，已是中文 {2} 條）"),
        new("NOTICE_EXPORTED", "Exported {0} entries", "已导出 {0} 条", "已導出 {0} 條"),
        new("NOTICE_SELECTED_MODS", ", {0} selected mods", "，{0} 个已选模组", "，{0} 個已選模組"),
        new("NOTICE_OPEN_FOLDER_FAILED", "Could not open the folder: {0}", "打开目录失败：{0}", "打開目錄失敗：{0}"),
        new("NOTICE_LANG_APPLIED", "Language file applied: {0} ({1} entries).", "已应用语言文件：{0}（{1} 条）。", "已套用語言檔案：{0}（{1} 條）。"),
        new("NOTICE_LANG_FOLLOW", "Following the game language (built-in en-us / zh-cn / zh-tw).", "已改为跟随游戏语言（内建 en-us / zh-cn / zh-tw）。", "已改為跟隨遊戲語言（內建 en-us / zh-cn / zh-tw）。"),
        new("NOTICE_LANG_MISSING", "{0}: {1} entries missing, they fall back to English. See {2}.", "{0}：缺 {1} 条，缺的回落英文。参考 {2}。", "{0}：缺 {1} 條，缺的回落英文。參考 {2}。"),
        new("NOTICE_LANG_ERROR", "Language file error in {0}: {1}", "语言文件 {0} 有错：{1}", "語言檔案 {0} 有錯：{1}"),
        new("NOTICE_LANG_NO_ENTRIES", "no valid \"key=text\" line", "没有一行有效的「键=译文」", "沒有一行有效的「鍵=譯文」"),
        new("NOTICE_LANG_BAD_LINES", "lines without \"=\": {0}", "缺少等号的行：{0}", "缺少等號的行：{0}"),
        new("NOTICE_LANG_NOT_FOUND", "file not found (click Refresh)", "文件不存在（点刷新）", "檔案不存在（點重新整理）"),
        new("NOTICE_REFRESHED", "Language folder rescanned: {0} language group(s).", "已重新扫描语言目录：{0} 个语言组。", "已重新掃描語言目錄：{0} 個語言組。"),
        // 悬停注释（说明面板显示；首行是标题，`…` 包住的部分用标题色强调）
        new("TIP_EXPORT_V2",
            "Export texts\nExports the untranslated source dictionary of every mod, including this manager itself;\nmods unticked while `Selected only` is on are excluded.\n\nOutput: `output\\translation\\` (the folder button opens it)\nFull path: `BepInEx\\config\\ModConfigEnhance\\output\\translation\\`; usage in `readme.md` one level up from `output\\`.",
            "导出翻译\n导出包括本管理器自身在内的所有模组的待翻译原文词典;\n但不包括`仅导出已选`启用后取消选中的模组。\n\n导出位置：`output\\translation\\`（点旁边的文件夹按钮直接打开）\n完整路径：`BepInEx\\config\\ModConfigEnhance\\output\\translation\\`；用法见 `output\\` 上一级的 `readme.md`。",
            "導出翻譯\n導出包括本管理器自身在內的所有模組的待翻譯原文詞典;\n但不包括`僅導出已選`啟用後取消選中的模組。\n\n匯出位置：`output\\translation\\`（點旁邊的資料夾按鈕直接打開）\n完整路徑：`BepInEx\\config\\ModConfigEnhance\\output\\translation\\`；用法見 `output\\` 上一級的 `readme.md`。"),
        new("TIP_UNTRANSLATED_V2",
            "Untranslated\nExports only the entries the current language group doesn't cover yet: exact lines and `r:` regex rules both count as translated.\n\nOutput lands in `output\\translation\\`; fill it in, then use `Import additions` in the language row.",
            "补漏翻\n只导出当前语言组还没覆盖的原文条目：整句译文和 `r:` 正则规则命中的都算已翻。\n\n产物落在 `output\\translation\\`，填好后用语言行的`导入补翻`合进语言文件。",
            "補漏翻\n只導出目前語言組還沒覆蓋的原文條目：整句譯文和 `r:` 正則規則命中的都算已翻。\n\n產物落在 `output\\translation\\`，填好後用語言列的`匯入補翻`合進語言檔案。"),
        new("TIP_SELECTED_ONLY",
            "Selected only\nWhen ticked, every mod row in the sidebar gets an `Export` checkbox — `Export texts`/`Untranslated` cover ticked mods only.\n\nThis mod is always included (the exported file doubles as its language file).",
            "仅导出已选\n勾选后侧栏每个模组行出现`导出`复选框，`导出翻译`/`补漏翻`只包含勾选的模组。\n\n本模组始终包含（导出文件同时是本模组的语言文件）。",
            "僅導出已選\n勾選後側欄每個模組行出現`導出`複選框，`導出翻譯`/`補漏翻`只包含勾選的模組。\n\n本模組始終包含（導出檔案同時是本模組的語言檔案）。"),
        new("TIP_TOGGLE_ORIG_V2",
            "Show original / Show translated\nSwitches every mod text this manager displays (sidebar, overview, description panel, settings page) between translation and original — check the source before exporting, or compare.\n\nThis manager's own interface stays in the chosen language.",
            "显示原文 / 显示译文\n把本管理器负责显示的模组文字（侧栏、概览、说明面板、设置页）在译文和原文之间切换：导出前对照原文，或检查译文效果。\n\n管理器自己的界面文字保持所选语言。",
            "顯示原文 / 顯示譯文\n把本管理器負責顯示的模組文字（側欄、概覽、說明面板、設定頁）在譯文和原文之間切換：匯出前對照原文，或檢查譯文效果。\n\n管理器自己的介面文字保持所選語言。"),
        new("TIP_RELOAD_V2",
            "Reload texts\nRe-reads the files in `language\\` — after editing a language file this applies it without re-picking the language.",
            "重载翻译\n重新读取 `language\\` 的语言文件：改完语言文件点它即生效，无需重选语言。",
            "重載翻譯\n重新讀取 `language\\` 的語言檔案：改完語言檔案點它即生效，無需重選語言。"),
        // 2.5.5：按钮改为直接打开翻译导出目录，含义变了，换 _V3 键（旧 _V2 进 RetiredTerms）。
        // 个人数据文件夹仍叫 config\ModConfigEnhance（内部目录名，拆分时再定），提示里如实写出。
        new("TIP_OPEN_DIR_V3",
            "Open export folder\nOpens the translation export folder `output\\translation\\`: exported texts and untranslated lists are written here, and `Import additions` picks files from here.\n\nLanguage files are in the sibling `language\\` folder. Full path: `BepInEx\\config\\ModConfigEnhance\\output\\translation\\` (the folder keeps its internal name).",
            "打开导出目录\n直接打开翻译导出目录 `output\\translation\\`：`导出翻译`、`补漏翻`生成的文件都在这里，`导入补翻`也从这里选文件。\n\n语言文件在同级的 `language\\`。完整路径：`BepInEx\\config\\ModConfigEnhance\\output\\translation\\`（文件夹沿用内部名称）。",
            "打開匯出目錄\n直接打開翻譯匯出目錄 `output\\translation\\`：`導出翻譯`、`補漏翻`產生的檔案都在這裡，`匯入補翻`也從這裡選檔案。\n\n語言檔案在同級的 `language\\`。完整路徑：`BepInEx\\config\\ModConfigEnhance\\output\\translation\\`（資料夾沿用內部名稱）。"),
        new("TIP_SORT",
            "Sort\nSidebar mod order:\n\n`Default` (load order) / `Load reversed` / `Name` / `Name reversed`.",
            "排序\n侧栏模组行的排序方式：\n\n`默认`（加载顺序）/ `加载倒序` / `名称` / `名称倒序`。",
            "排序\n側欄模組行的排序方式：\n\n`默認`（載入順序）/ `載入倒序` / `名稱` / `名稱倒序`。"),
        new("TIP_LANGUAGE",
            "Language\nPick the UI language: \"Follow game language\" or a group under `language\\`; applies instantly, no restart.\n\nNaming rules `code[-variant][_part].txt` are in `readme.md`.",
            "语言\n选择界面语言：「跟随游戏语言」或 `language\\` 下的语言组，选中即应用、无需重启。\n\n命名规则「`语言码[-变体][_分包].txt`」见 `readme.md`。",
            "語言\n選擇介面語言：「跟隨遊戲語言」或 `language\\` 下的語言組，選中即套用、無需重啟。\n\n命名規則「`語言碼[-變體][_分包].txt`」見 `readme.md`。"),
        new("TIP_REFRESH",
            "Refresh\nRescans the `language\\` directory\n\n— new language files appear in the dropdown after clicking this.",
            "刷新\n重新扫描 `language\\` 目录\n\n新增语言文件后点它才会出现在下拉里。",
            "重新整理\n重新掃描 `language\\` 目錄\n\n新增語言檔案後點它才會出現在下拉里。"),
        new("BTN_IMPORT_ADDITIONS",
            "Import additions",
            "导入补翻",
            "匯入補翻"),
        new("TIP_IMPORT_ADDITIONS",
            "Import additions\nPick a filled-in export file: every line with a translation is merged into the current language file (same original → the imported one wins). Empty lines are skipped. The language file is backed up first.",
            "导入补翻\n选一个填好的导出文件：有译文的行合进当前语言文件（同一句原文以导入的为准），留空的行跳过。合并前先备份语言文件。",
            "匯入補翻\n選一個填好的匯出檔案：有譯文的行合進目前語言檔案（同一句原文以匯入的為準），留空的行略過。合併前先備份語言檔案。"),
        new("NOTICE_IMPORTED_V2",
            "Merged {0} lines into {1} ({2} new, {3} replaced; skipped {4} empty and {5} left untranslated).",
            "已合并 {0} 行到 {1}（新增 {2}，替换 {3}；跳过空行 {4}、没翻的行 {5}）。",
            "已合併 {0} 行到 {1}（新增 {2}，取代 {3}；略過空行 {4}、沒翻的行 {5}）。"),
        new("NOTICE_IMPORT_NOTHING",
            "No translated lines in that file.",
            "这个文件里没有填好译文的行。",
            "這個檔案裡沒有填好譯文的行。"),
        new("NOTICE_IMPORT_FAILED",
            "Import failed: {0}",
            "导入失败：{0}",
            "匯入失敗：{0}"),
        new("TIP_GROUPS",
            "Groups\nCreate or edit groups: name, description, color and members. Same data as the `Mod overview` groups.\n\nPinned mods stay in the pinned area at the top; their group shows as a colored bar next to the blue pin bar.",
            "分组管理\n新建或编辑分组：名称、描述、颜色和成员，与`模组概览`的分组是同一份数据。\n\n置顶的模组留在最上方的置顶区，所属分组显示为蓝色置顶条右侧的分组色条。",
            "分組管理\n新建或編輯分組：名稱、描述、顏色和成員，與`模組概覽`的分組是同一份資料。\n\n置頂的模組留在最上方的置頂區，所屬分組顯示為藍色置頂條右側的分組色條。"),
        new("TIP_GROUP_HEADER",
            "Click to collapse or expand. The collapsed state is shared with the `Mod overview`; searching temporarily expands every group.",
            "点击折叠或展开，折叠状态与`模组概览`同步；搜索时所有分组临时展开。",
            "點擊折疊或展開，折疊狀態與`模組概覽`同步；搜尋時所有分組臨時展開。"),
    };

    /// <summary>本模组 cfg 选项名 / 说明的内置译文：键是 cfg 里的英文原文，设置页与说明面板按它显示。</summary>
    internal static readonly Term[] ConfigTerms =
    {
        // 2.5.5：自身设置项此前缺内置译文（补漏翻导出里一直列出）
        new("Manage mods in-game: PEAK mods, settings, dependencies, updates and modpacks in one place, with isolated environments, profiles, localization and package export.",
            "Manage mods in-game: PEAK mods, settings, dependencies, updates and modpacks in one place, with isolated environments, profiles, localization and package export.",
            "在游戏内管理模组：PEAK 的模组、设置、依赖、更新和整合包集中在一处，支持独立环境、配置方案、本地化与打包导出。",
            "在遊戲內管理模組：PEAK 的模組、設定、依賴、更新和整合包集中在一處，支援獨立環境、配置方案、本地化與打包匯出。"),
        new("Check update metadata every 30 minutes in the main menu or Airport only. Downloads and installation require confirmation.",
            "Check update metadata every 30 minutes in the main menu or Airport only. Downloads and installation require confirmation.",
            "只在主菜单或机场每 30 分钟检查一次更新信息；下载和安装都要你确认。",
            "只在主選單或機場每 30 分鐘檢查一次更新資訊；下載和安裝都要你確認。"),
        new("Rest idle button animations", "Rest idle button animations", "闲置按钮动画休眠", "閒置按鈕動畫休眠"),
        new("Pause the manager's button animations once a non-looping Normal clip has finished. Original hover, click, selection and sounds are retained.",
            "Pause the manager's button animations once a non-looping Normal clip has finished. Original hover, click, selection and sounds are retained.",
            "管理器按钮的非循环常态动画播完后暂停，省下每帧开销；悬停、点击、选中效果和音效照旧。",
            "管理器按鈕的非循環常態動畫播完後暫停，省下每幀開銷；懸停、點擊、選中效果和音效照舊。"),
        new("On: show up to two dependency levels in name hints; deeper levels are omitted. Off: show all direct dependencies only. Dependants use a compact two-level summary. Omitted branches show an ellipsis. Long hints can be scrolled in either placement mode. Duplicate references are merged and circular branches stop expanding.",
            "On: show up to two dependency levels in name hints; deeper levels are omitted. Off: show all direct dependencies only. Dependants use a compact two-level summary. Omitted branches show an ellipsis. Long hints can be scrolled in either placement mode. Duplicate references are merged and circular branches stop expanding.",
            "开：名称提示里最多显示两层依赖，更深的省略。关：只显示全部直接依赖。被依赖方向用精简的两层摘要。省略的分支显示省略号。两种显示位置下长提示都能滚动。重复引用合并显示，循环分支不再展开。",
            "開：名稱提示裡最多顯示兩層依賴，更深的省略。關：只顯示全部直接依賴。被依賴方向用精簡的兩層摘要。省略的分支顯示省略號。兩種顯示位置下長提示都能捲動。重複引用合併顯示，循環分支不再展開。"),
        new("Appearance speed", "Appearance speed", "概览动效速度", "概覽動效速度"),
        new("Tree sidebar", "Tree sidebar", "树形侧栏", "樹形側欄"),
        new("Description panel", "Description panel", "选项说明面板", "選項說明面板"),
        new("Colour theme", "Colour theme", "配色主题", "配色主題"),
        new("Blue", "Blue", "蓝色", "藍色"),
        new("Purple", "Purple", "紫色", "紫色"),
        new("PurpleAtNight", "PurpleAtNight", "夜里自动紫色", "夜裡自動紫色"),
        new("UI localization", "UI localization", "界面本地化", "介面本地化"),
        new("Hide localization buttons", "Hide localization buttons", "隐藏本地化相关按钮", "隱藏本地化相關按鈕"),
        new("Warn about XUnity machine translation", "Warn about XUnity machine translation", "提醒 XUnity 在线机翻", "提醒 XUnity 線上機翻"),
        new("1. Layout", "1. Layout", "1. 版面", "1. 版面"),
        new("2. Localization", "2. Localization", "2. 本地化", "2. 本地化"),
        new("5. Authoring", "5. Authoring", "5. 文档编辑", "5. 文件編輯"),
        new("4. Diagnostics", "4. Diagnostics", "4. 诊断", "4. 診斷"),
        new("Debug logging", "Debug logging", "技术日志（debug）", "技術日誌（debug）"),
        new(Plugin.DescDebugLogging, Plugin.DescDebugLogging,
            "同时写入技术日志层：每条更新助手命令、队列轮询、计划文件与文件清单细节，显示在本模组的独立日志来源里，并把开关传给更新助手（助手同时写自己的 debug 行）。普通层（更新报告、模组启用情况、配置保存）始终开启。默认关闭；排查更新问题时再打开。",
            "同時寫入技術日誌層：每條更新助手命令、佇列輪詢、計畫檔案與檔案清單細節，顯示在本模組的獨立日誌來源中，並把開關傳給更新助手（助手同時寫自己的 debug 行）。一般層（更新報告、模組啟用情形、設定儲存）始終開啟。預設關閉；排查更新問題時再開啟。"),
        new("Allow editing user guide", "Allow editing user guide", "允许编辑功能说明", "允許編輯功能說明"),
        new(Plugin.DescAllowGuideEditing, Plugin.DescAllowGuideEditing,
            "允许使用与整合包介绍相同的工具栏编辑全部使用说明章节及更新日志。修改按语言保存在本地；关闭编辑后仍显示已保存的覆盖内容。默认关闭。",
            "允許使用與整合包介紹相同的工具列編輯全部使用說明章節及更新日誌。修改依語言儲存在本機；關閉編輯後仍顯示已儲存的覆寫內容。預設關閉。"),
        new(Plugin.DescTreeSidebar, Plugin.DescTreeSidebar,
            "模组列表改为左侧竖排侧栏，选中的模组在其下方展开分区，右侧显示选项。重启生效。",
            "模組列表改為左側直排側欄，選中的模組在其下方展開分區，右側顯示選項。重啟生效。"),
        new(Plugin.DescColourTheme, Plugin.DescColourTheme,
            "管理器全部界面的配色主题。蓝色跟随游戏的设置菜单；紫色是夜间风格；夜里自动紫色在装了 TimeTheme 时夜里切到紫色（只读它的昼夜状态）。模组设置页即时切换；已打开过的概览在下次打开时切换。",
            "管理器全部介面的配色主題。藍色跟隨遊戲的設定選單；紫色是夜間風格；夜裡自動紫色在裝了 TimeTheme 時夜裡切到紫色（只讀它的晝夜狀態）。模組設定頁即時切換；已開啟過的概覽在下次開啟時切換。"),
        new(Plugin.DescDescriptionPanel, Plugin.DescDescriptionPanel,
            "右上方面板显示悬停选项的说明、默认值与取值范围 / 可选项。需要树形侧栏。重启生效。",
            "右上方板面顯示懸停選項的說明、預設值與取值範圍 / 可選項。需要樹形側欄。重啟生效。"),
        new(Plugin.DescUiLocalization, Plugin.DescUiLocalization,
            "本地化 ModConfig 界面与本模组按钮和提示。英文为原文，内置简体与繁体中文；config\\ModConfigEnhance\\language\\ 中的可选语言文件覆盖内置文字。在模组列表上方选择语言。此开关重启生效。",
            "本地化 ModConfig 介面與本模組按鈕和提示。英文為原文，內建簡體與繁體中文；config\\ModConfigEnhance\\language\\ 中的選用語言檔案覆寫內建文字。在模組列表上方選擇語言。此開關重新啟動生效。"),
        new(Plugin.DescHideLocalizationButtons, Plugin.DescHideLocalizationButtons,
            "收起模组列表上方的导出与翻译工具两排按钮，只留语言下拉 + 刷新；列表上移，「导出」列同步隐藏，勾选状态保留。即时生效。",
            "收起模組列表上方的導出與翻譯工具兩排按鈕，只留語言下拉 + 重新整理；列表上移，「導出」欄同步隱藏，勾選狀態保留。即時生效。"),
        new(Plugin.DescWarnXUnity, Plugin.DescWarnXUnity,
            "装有 XUnity AutoTranslator 且它开着在线机翻（AutoTranslatorConfig.ini 的 Endpoint 不为空）时，每次启动第一次打开概览提醒一次，并可一键关掉它的在线机翻。翻译由本模组自己的语言文件完成，不再与 XUnity 联动。",
            "裝有 XUnity AutoTranslator 且它開著線上機翻（AutoTranslatorConfig.ini 的 Endpoint 不為空）時，每次啟動第一次開啟概覽提醒一次，並可一鍵關掉它的線上機翻。翻譯由本模組自己的語言檔案完成，不再與 XUnity 連動。"),
    };

    // ── 取词 ──────────────────────────────────────────────────────

    private static bool _registered;

    /// <summary>按当前语言取词；语言表里没有就退回英文。</summary>
    internal static string Get(string key)
    {
        EnsureRegistered();
        string value = LocalizedText.GetText(KeyPrefix + key, printDebug: false);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        foreach (Term term in Terms)
        {
            if (term.Key == key)
            {
                return term.English;
            }
        }

        return key;
    }

    internal static string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(Get(key), args);
        }
        catch (FormatException)
        {
            // 译文把 {0} 写坏了：退回英文
            foreach (Term term in Terms)
            {
                if (term.Key == key)
                {
                    return string.Format(term.English, args);
                }
            }

            return key;
        }
    }

    internal static string Index(string key) => KeyPrefix + key;

    /// <summary>第一次用到时把词条按当前模式写进语言表（主菜单构建页面时才会走到，避开插件加载期）。</summary>
    internal static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        try
        {
            Apply(Plugin.LanguageFile.Value, notify: false, refresh: false);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[语言] 首次注册词条失败，退回内建英文: {ex}");
            RegisterBuiltIn();
        }
    }

    /// <summary>语言表被整表重载（ReloadAll）后词条会丢，语言切换回调里补回去。</summary>
    internal static void ReRegister()
    {
        _registered = false;
        EnsureRegistered();
    }

    private static void RegisterBuiltIn()
    {
        foreach (Term term in Terms)
        {
            MenuAPI.CreateLocalization(KeyPrefix + term.Key)
                .AddLocalization(term.English, LocalizedText.Language.English)
                .AddLocalization(term.SimplifiedChinese, LocalizedText.Language.SimplifiedChinese)
                .AddLocalization(term.TraditionalChinese, LocalizedText.Language.TraditionalChinese);
        }
    }

    // ── 语言文件 ──────────────────────────────────────────────────

    internal sealed class LanguageFile
    {
        internal string Path = "";
        internal string Stem = "";
        internal string Display = "";
        internal string Code = "";
        internal string Key = "";
        internal readonly List<string> Paths = new List<string>();
        /// <summary>键 → 译文；MCE_ 键已去前缀并大写，其余键原样。</summary>
        internal readonly Dictionary<string, string> Entries = new Dictionary<string, string>(StringComparer.Ordinal);
        internal bool CompleteSnapshot;
        internal readonly List<int> BadLines = new List<int>();
        /// <summary>2.5.0：<c>r:</c> 正则规则，按文件内与组内先后排列；匹配时后写的优先。</summary>
        internal readonly List<RegexRule> Regexes = new List<RegexRule>();
        /// <summary>写坏的正则（「文件名 第 n 行：原因」），跳过不用，在应用结果里提示。</summary>
        internal readonly List<string> RegexErrors = new List<string>();

        internal string Name => System.IO.Path.GetFileName(Path);
        internal int FileCount => Paths.Count;
        /// <summary>有 // display: 用显示名，没有回落到组名（文件全名）。</summary>
        internal string DisplayOrStem => Display.Length > 0 ? Display : Stem;
    }

    /// <summary>
    /// 目录下的语言组（只看当前目录，不含 _ 开头的模板）。组名 = 第一个 _ 之前的部分：
    /// `语言码-变体` 是同码的独立分组（各自一套完整翻译），`组名_分包` 并入前缀所指的组。
    /// 同组内基础文件优先，其余按文件名排序。
    /// </summary>
    internal static List<LanguageFile> Scan()
    {
        Dictionary<string, LanguageFile> groups = new Dictionary<string, LanguageFile>(StringComparer.OrdinalIgnoreCase);
        string[] files = Overview.PublishedPack.LanguageDirectories
            .SelectMany(d => Directory.GetFiles(d, "*.txt").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            .Concat(Directory.Exists(LanguageDirectory) ? Directory.GetFiles(LanguageDirectory, "*.txt").OrderBy(p => p, StringComparer.OrdinalIgnoreCase) : Array.Empty<string>()).ToArray();
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(file);
            string groupStem = GroupStemFrom(stem);
            string key = NormalizeGroupKey(groupStem);
            if (!groups.TryGetValue(key, out LanguageFile? item))
            {
                item = new LanguageFile { Key = key, Code = LanguageCodeFromGroup(groupStem), Stem = groupStem };
                groups.Add(key, item);
            }

            item.Paths.Add(file);
        }

        List<LanguageFile> result = new List<LanguageFile>(groups.Values);
        foreach (LanguageFile item in result)
        {
            item.Paths.Sort(CompareLayeredFiles);
            item.Path = item.Paths[0];
            item.Stem = Path.GetFileNameWithoutExtension(item.Path); // 显示回落用基础文件的真实文件名
            item.Display = ReadDisplayName(item.Path) ?? "";
        }

        foreach (string code in new[] { "en-us", "zh-cn", "zh-tw" })
            if (!result.Any(g => g.Key == code)) result.Add(new LanguageFile { Key = code, Code = code, Stem = code, Display = code == "zh-cn" ? "简体中文" : code == "zh-tw" ? "繁體中文" : "English" });
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
        return result;
    }

    private static int CompareGroupFiles(string left, string right)
    {
        string leftStem = Path.GetFileNameWithoutExtension(left);
        string rightStem = Path.GetFileNameWithoutExtension(right);
        bool leftBase = leftStem.IndexOf('_') < 0;
        bool rightBase = rightStem.IndexOf('_') < 0;
        if (leftBase != rightBase)
        {
            return leftBase ? -1 : 1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(left), Path.GetFileName(right));
    }

    /// <summary>文件名（不含扩展名）→ 组名：第一个 _ 之前的部分。「语言码-变体」的变体留在组名里。</summary>
    internal static string GroupStemFrom(string stem)
    {
        int separator = stem.IndexOf('_');
        return separator < 0 ? stem : stem.Substring(0, separator);
    }

    /// <summary>组名 → 语言码：开头的 xx 或 xx-yy（zh-cn-old → zh-cn、zh-tw-old → zh-tw、en → en-us）。</summary>
    internal static string LanguageCodeFromGroup(string groupStem)
    {
        return LanguageCodeFromGroup(groupStem, out _);
    }

    /// <summary>同上，额外给出码段在组名里的原始长度（剩余部分是 -变体 后缀）。</summary>
    private static string LanguageCodeFromGroup(string groupStem, out int codeLength)
    {
        string stem = groupStem.Trim();
        int firstDash = stem.IndexOf('-');
        if (firstDash > 0)
        {
            int secondDash = stem.IndexOf('-', firstDash + 1);
            string region = secondDash < 0
                ? stem.Substring(firstDash + 1)
                : stem.Substring(firstDash + 1, secondDash - firstDash - 1);
            if (region.Length == 2)
            {
                codeLength = secondDash < 0 ? stem.Length : secondDash;
                return NormalizeLanguageCode(stem.Substring(0, codeLength));
            }
        }

        codeLength = firstDash < 0 ? stem.Length : firstDash;
        return NormalizeLanguageCode(stem.Substring(0, codeLength));
    }

    /// <summary>组名 → 归一化组标识：语言码段过别名（zh→zh-cn、en→en-us），-变体 后缀小写保留（zh-old → zh-cn-old）。</summary>
    internal static string NormalizeGroupKey(string groupStem)
    {
        string code = LanguageCodeFromGroup(groupStem, out int codeLength);
        return code + groupStem.Substring(codeLength).Trim().ToLowerInvariant();
    }

    /// <summary>文件名（不含扩展名）→ 该文件所属组的语言码（含组名里的变体归属）。</summary>
    internal static string LanguageCodeFromStem(string stem)
    {
        return LanguageCodeFromGroup(GroupStemFrom(stem));
    }

    private static string NormalizeLanguageCode(string value)
    {
        string code = value.Trim().Replace('_', '-').ToLowerInvariant();
        if (code == "zh")
        {
            return "zh-cn";
        }

        if (code == "en")
        {
            return "en-us";
        }

        return code;
    }

    /// <summary>cfg 记忆值 → 归一化组标识（旧值是语言码时同样命中对应组）。</summary>
    internal static string NormalizeSelection(string selection)
    {
        return string.IsNullOrWhiteSpace(selection) ? FollowGame : NormalizeGroupKey(GroupStemFrom(selection));
    }

    private static string? ReadDisplayName(string file)
    {
        try
        {
            using StreamReader reader = new StreamReader(file, Encoding.UTF8);
            for (int i = 0; i < 5; i++)
            {
                string? line = reader.ReadLine();
                if (line == null)
                {
                    break;
                }

                if (line.StartsWith(DisplayMarker, StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Substring(DisplayMarker.Length).Trim();
                    return value.Length > 0 ? value : null;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    internal static LanguageFile Parse(string file)
    {
        LanguageFile result = new LanguageFile { Path = file, Stem = Path.GetFileNameWithoutExtension(file) };
        result.Code = LanguageCodeFromStem(result.Stem);
        result.Display = ReadDisplayName(file) ?? result.Stem;
        Dictionary<string, int> sourceLines = new Dictionary<string, int>(StringComparer.Ordinal);
        int lineNo = 0;
        foreach (string raw in File.ReadLines(file, Encoding.UTF8))
        {
            lineNo++;
            string line = raw.TrimEnd('\r');
            if (line == "// mce-snapshot: effective-v1") result.CompleteSnapshot = true;
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int eq = FindUnescapedEquals(line);
            if (eq <= 0)
            {
                result.BadLines.Add(lineNo);
                continue;
            }

            string key = Unescape(line.Substring(0, eq)).Trim();
            string value = Unescape(line.Substring(eq + 1));
            if (key.Length == 0 || string.IsNullOrWhiteSpace(value))
            {
                continue; // 等号右边留空 = 未翻，回落
            }

            if (IsRegexKey(key))
            {
                // 2.5.4：r: 行按 XUnity 词典的转义重新读（反斜杠反斜杠 → 一个反斜杠等），从 XUnity 搬来的规则意思不变
                string rawKey = UnescapeXUnity(line.Substring(0, eq)).Trim(), rawValue = UnescapeXUnity(line.Substring(eq + 1));
                RegexRule? rule = TryCompileRegex(rawKey, rawValue, Path.GetFileName(file), lineNo, out string regexError);
                if (rule != null) result.Regexes.Add(rule);
                else result.RegexErrors.Add(F("{0} line {1}: {2}", "{0} 第 {1} 行：{2}", "{0} 第 {1} 行：{2}", Path.GetFileName(file), lineNo, regexError));
                continue;
            }

            if (key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(KeyPrefix.Length).ToUpperInvariant();
            }

            bool incomingEffective = IsEffectiveTranslation(key, value, result.Code);
            if (result.Entries.TryGetValue(key, out string? oldValue))
            {
                bool oldEffective = IsEffectiveTranslation(key, oldValue, result.Code);
                if (!result.CompleteSnapshot && (!incomingEffective || IsMceEnglishPlaceholder(key, value)) && oldEffective &&
                    !string.Equals(oldValue, value, StringComparison.Ordinal))
                {
                    continue;
                }

                if (incomingEffective && oldEffective && !string.Equals(oldValue, value, StringComparison.Ordinal))
                {
                    Plugin.Log.LogWarning($"[语言] {Path.GetFileName(file)} 键冲突「{key}」：第 {sourceLines[key]} 行 → 第 {lineNo} 行，采用后者。");
                }
            }

            result.Entries[key] = value;
            sourceLines[key] = lineNo;
        }

        ApplyCompatibilityAliases(result);
        return result;
    }

    /// <summary>先完整解析并校验语言组，再按确定顺序合并；任何文件失败都不会产生外部状态变化。</summary>
    internal static LanguageFile ParseGroup(string selection)
    {
        string key = NormalizeSelection(selection);
        LanguageFile? group = null;
        foreach (LanguageFile candidate in Scan())
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                group = candidate;
                break;
            }
        }

        if (group == null)
        {
            throw new FileNotFoundException(Get("NOTICE_LANG_NOT_FOUND"), key + ".txt");
        }

        LanguageFile merged = new LanguageFile
        {
            Code = group.Code,
            Stem = group.Stem,
            Key = group.Key,
            Path = group.Path,
            Display = group.Display,
        };
        merged.Paths.AddRange(group.Paths);
        Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Term term in Terms)
            merged.Entries[term.Key] = PickTerm(term, merged.Code);
        foreach (Term term in AllConfigTerms)
        {
            merged.Entries[term.English.Trim()] = PickTerm(term, merged.Code);
            merged.Entries[term.Key.Trim()] = PickTerm(term, merged.Code);
        }
        foreach (Term term in AllInlineTerms) merged.Entries[term.English.Trim()] = PickTerm(term, merged.Code);
        foreach (string path in group.Paths)
        {
            LanguageFile file = Parse(path);
            merged.CompleteSnapshot |= file.CompleteSnapshot;
            merged.Regexes.AddRange(file.Regexes);
            merged.RegexErrors.AddRange(file.RegexErrors);
            if (file.BadLines.Count > 0)
            {
                throw new InvalidDataException(Format("NOTICE_LANG_BAD_LINES", Path.GetFileName(path) + ": " + JoinLines(file.BadLines)));
            }

            foreach (KeyValuePair<string, string> pair in file.Entries)
            {
                if (RetiredTerm(pair.Key)) continue; // 2.5.0 改了含义的界面词条：旧译文描述的是已删除的 XUnity 行为，以内置为准
                bool incomingEffective = IsEffectiveTranslation(pair.Key, pair.Value, merged.Code);
                if (merged.Entries.TryGetValue(pair.Key, out string? oldValue))
                {
                    bool oldEffective = IsEffectiveTranslation(pair.Key, oldValue, merged.Code);
                    if (!file.CompleteSnapshot && !IsPersonalLanguage(path) && (!incomingEffective || IsMceEnglishPlaceholder(pair.Key, pair.Value)) && oldEffective &&
                        !string.Equals(oldValue, pair.Value, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (incomingEffective && oldEffective && !string.Equals(oldValue, pair.Value, StringComparison.Ordinal))
                    {
                        Diagnostics.RuntimeLogCapture.LanguageDetail($"[语言] {group.Key} 键覆盖「{pair.Key}」 -> {Path.GetFileName(path)}");
                    }
                }

                merged.Entries[pair.Key] = pair.Value;
                sources[pair.Key] = Path.GetFileName(path);
            }
        }

        if (merged.Entries.Count == 0 && merged.Regexes.Count == 0)
        {
            throw new InvalidDataException(Get("NOTICE_LANG_NO_ENTRIES"));
        }

        return merged;
    }

    /// <summary>
    /// 2.5.1：2.5.0 已删除的界面词条与旧 cfg 开关名（2.5.4 起只放真正删掉的；改了含义的词条换新键名 _V2，旧键自然不再被用到，
    /// 日文等其他语言和整合包作者给新键写的译文照常生效）。旧语言文件 / 整合包作者词典里的这些行写的是 XUnity 联动的旧说法，
    /// 继续用会把新界面的提示盖成过时文字（用户实机截图）。读取时一律跳过，回落到内置三语；以后再改含义时把键加进来。
    /// </summary>
    internal static readonly HashSet<string> RetiredTerms = new HashSet<string>(StringComparer.Ordinal)
    {
        "NOTICE_NO_XUNITY_CHECK", "NOTICE_NO_XUNITY", "NOTICE_XUNITY_SWITCHED", "NOTICE_XUNITY_SYNCED", "NOTICE_XUNITY_NO_SWITCH",
        // 2.5.4：这 7 个旧键名已换成 _V2，代码不再使用，旧文件里的译文（说的是 XUnity 联动）一并清掉
        "NOTICE_SHOWING_ORIGINAL", "NOTICE_RELOADED", "TIP_UNTRANSLATED", "TIP_TOGGLE_ORIG", "TIP_RELOAD", "TIP_OPEN_DIR", "TIP_EXPORT", "NOTICE_IMPORTED",
        // 2.5.5：文件夹按钮改为直接打开翻译导出目录
        "TIP_OPEN_DIR_V2",
        "Switch XUnity language with the language file", "Disable XUnity machine translation",
    };

    // 旧 cfg 说明原文（含 XUnity 旧说法的长句）屏幕上已不再出现，整句对不上任何文字，不用特别处理。
    internal static bool RetiredTerm(string key) => RetiredTerms.Contains(key);

    private static bool IsEffectiveTranslation(string key, string value, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IsTermKey(key))
        {
            return !string.Equals(key, value, StringComparison.Ordinal);
        }

        foreach (Term term in Terms)
        {
            if (term.Key == key)
            {
                return !string.Equals(term.English, value, StringComparison.Ordinal) ||
                       string.Equals(NormalizeLanguageCode(languageCode ?? ""), "en-us", StringComparison.Ordinal);
            }
        }

        return true;
    }

    private static bool IsMceEnglishPlaceholder(string key, string value)
    {
        foreach (Term term in Terms)
        {
            if (term.Key == key)
            {
                return string.Equals(term.English, value, StringComparison.Ordinal);
            }
        }

        return false;
    }

    /// <summary>当前生效的语言组：选了组按组标识，跟随游戏按语言码（同码多组时纯组优先）。</summary>
    private static LanguageFile? CurrentGroup()
    {
        string selection = Plugin.LanguageFile != null ? Plugin.LanguageFile.Value ?? "" : "";
        return string.IsNullOrEmpty(selection) ? ParseOptionalGroupByCode(FollowStem()) : ParseOptionalGroup(selection);
    }

    /// <summary>当前语言组里已翻的 MCE_ 词条（补漏翻用：同码变体组各算各的，不互相计入）。</summary>
    internal static HashSet<string> CurrentTranslatedTermKeys()
    {
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            LanguageFile? group = CurrentGroup();
            if (group != null)
            {
                foreach (KeyValuePair<string, string> pair in group.Entries)
                {
                    if (IsTermKey(pair.Key) && IsEffectiveTranslation(pair.Key, pair.Value, group.Code))
                    {
                        result.Add(pair.Key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[语言] 查漏翻读取当前语言组失败，MCE 词条按未翻处理: {ex.Message}");
        }

        return result;
    }

    /// <summary>当前语言组里非 MCE_ 的已翻原文（补漏翻用：只算本组自己的译文，别组同码不算）。</summary>
    internal static HashSet<string> CurrentTranslatedTexts()
    {
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            LanguageFile? group = CurrentGroup();
            if (group != null)
            {
                foreach (KeyValuePair<string, string> pair in group.Entries)
                {
                    if (!IsTermKey(pair.Key) && IsEffectiveTranslation(pair.Key, pair.Value, group.Code))
                    {
                        result.Add(pair.Key);
                        result.Add(pair.Key.Trim());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[语言] 查漏翻读取当前语言组失败，非 MCE 文本按词典判定: {ex.Message}");
        }

        return result;
    }

    // FindUnescapedEquals / Escape / Unescape 在 Loc.Escaping.cs（2.5.4 拆出，夹具直接链接）

    // ── 应用 ──────────────────────────────────────────────────────

    /// <summary>说明面板要显示的结果：正常一行，出错用警告色。Parts 非空时走结构化富文本（分段上色）。</summary>
    internal sealed class ApplyResult
    {
        internal string Message = "";
        internal bool Warning;
        internal bool Success;
        internal List<(string Text, ModConfigDescriptionPanel.NoticeRole Role)>? Parts;
    }

    /// <summary>
    /// 按 <paramref name="selection"/>（空 = 跟随游戏语言，否则是组名「语言码[-变体]」）把词条写进语言表，
    /// 需要时刷新全部 LocalizedText 组件，本模组负责显示的模组文字（侧栏、概览、说明面板、设置页）随版本号重画。
    /// </summary>
    internal static ApplyResult Apply(string selection, bool notify, bool refresh)
    {
        _registered = true;
        ApplyResult result = new ApplyResult();
        List<(string Text, ModConfigDescriptionPanel.NoticeRole Role)> parts =
            new List<(string Text, ModConfigDescriptionPanel.NoticeRole Role)>();
        bool appliedSuccessfully = false;
        var previousInlineSnapshot = _inlineSnapshot;
        try
        {
            LanguageFile? preparedGroup;
            if (string.IsNullOrEmpty(selection))
            {
                preparedGroup = ApplyFollowGame();
                _inlineSnapshot = preparedGroup?.Entries ?? new Dictionary<string, string>(StringComparer.Ordinal);
                parts.Add((Get("NOTICE_LANG_FOLLOW"), ModConfigDescriptionPanel.NoticeRole.Body));
            }
            else
            {
                LanguageFile file = ParseGroup(selection);
                preparedGroup = file;
                _inlineSnapshot = file.Entries;
                int applied = ApplyFile(file);
                int missing = Terms.Length - applied;
                parts.Add((file.DisplayOrStem + " ×" + file.FileCount
                    + T("  |  Manager dictionary: ", "  |  已加载管理器词典 ", "  |  已載入管理器詞典 ") + applied
                    + T(" entries", " 条", " 條"), ModConfigDescriptionPanel.NoticeRole.Body));
                if (missing > 0)
                {
                    result.Warning = true;
                    parts.Add((F(" ({0} untranslated → built-in text)",
                        "（漏翻 {0} 条，使用内置文本）", "（漏翻 {0} 條，使用內建文字）", missing), ModConfigDescriptionPanel.NoticeRole.Warning));
                }
            }

            parts.Insert(0, (T("Applied language file:", "已应用语言文件：", "已套用語言檔案：") + "\n", ModConfigDescriptionPanel.NoticeRole.Header));

            WriteReadme();
            if (preparedGroup != null && (preparedGroup.Regexes.Count > 0 || preparedGroup.RegexErrors.Count > 0))
            {
                parts.Add((F("  |  Regex rules: {0}", "  |  正则规则 {0} 条", "  |  正則規則 {0} 條", preparedGroup.Regexes.Count), ModConfigDescriptionPanel.NoticeRole.Body));
                foreach (string error in preparedGroup.RegexErrors.Take(5))
                {
                    result.Warning = true;
                    parts.Add(("\n" + F("Regex rule skipped: {0}", "正则规则已跳过：{0}", "正則規則已略過：{0}", error), ModConfigDescriptionPanel.NoticeRole.Warning));
                }
            }

            _inlineSnapshot = preparedGroup?.Entries ?? new Dictionary<string, string>(StringComparer.Ordinal);
            if (preparedGroup != null && preparedGroup.Paths.Count > 0)
            {
                parts.Add(("\n\n" + T("Files:", "文件列表：", "檔案列表："), ModConfigDescriptionPanel.NoticeRole.Header));
                foreach (string path in preparedGroup.Paths)
                {
                    string? display = ReadDisplayName(path);
                    string modified = File.GetLastWriteTime(path).ToString("yyyy-MM-dd");
                    parts.Add(("\n- " + Path.GetFileName(path)
                        + (display != null ? "（" + display + "）" : "")
                        + F(" (modified {0})", "（修改日期：{0}）", "（修改日期：{0}）", modified), ModConfigDescriptionPanel.NoticeRole.Body));
                }
            }

            result.Parts = parts;
            StringBuilder plain = new StringBuilder();
            foreach ((string text, _) in parts)
            {
                plain.Append(text);
            }

            result.Message = plain.ToString();
            appliedSuccessfully = true;
            result.Success = true;
        }
        catch (Exception ex)
        {
            _inlineSnapshot = previousInlineSnapshot;
            Plugin.Log.LogError($"[语言] 应用「{selection}」失败，保留原状态: {ex}");
            result.Warning = true;
            result.Message = Format("NOTICE_LANG_ERROR", selection, ex.Message);
        }
        finally
        {
            if (appliedSuccessfully) InvalidateEditingTranslations();
            if (refresh && appliedSuccessfully)
            {
                _refreshing = true;
                try
                {
                    RefreshOwnTexts();
                }
                finally
                {
                    _refreshing = false;
                }
            }
        }

        if (notify)
        {
            if (result.Parts != null && ModConfigDescriptionPanel.Current != null)
            {
                ModConfigDescriptionPanel.Current.ShowNotice(result.Parts);
            }
            else
            {
                ModConfigDescriptionPanel.Current?.ShowNotice(result.Message, result.Warning);
            }
        }

        Plugin.Log.LogInfo($"[语言] {result.Message.Replace("\n", " | ")}");
        return result;
    }

    private static bool _refreshing;

    /// <summary>
    /// 只刷本模组的 LocalizedText（索引 MCE_ 开头）。不能用 <c>LocalizedText.RefreshAllText()</c>：
    /// ModConfig 的选项单元格是原版设置单元格的克隆，文字上还挂着索引为空的 LocalizedText，全局刷一遍会把它们都变成「LOC: 」。
    /// </summary>
    private static void RefreshOwnTexts()
    {
        LocalizedText[] all = UnityEngine.Object.FindObjectsByType<LocalizedText>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (LocalizedText text in all)
        {
            if (text != null && !string.IsNullOrEmpty(text.index) && text.index.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                text.RefreshText();
            }
        }

        ModConfigLocalizationPatch.RefreshStaticTexts();
    }

    /// <summary>Apply 自己触发的 RefreshAllText 会回调 OnLanguageChanged，别在那里再 Apply 一次。</summary>
    internal static bool IsRefreshing => _refreshing;

    private static LanguageFile? ApplyFollowGame()
    {
        string code = FollowStem();
        // 只严格校验当前游戏语言组；其它语言组的坏分包不能阻止当前语言生效。
        LanguageFile? current = ParseOptionalGroupByCode(code);
        RegisterBuiltIn();
        OverlayBuiltInGroup(current, LocalizedText.CURRENT_LANGUAGE);
        return current;
    }

    /// <summary>按组标识找组（zh-cn-old 只命中它自己）。</summary>
    private static LanguageFile? ParseOptionalGroup(string key)
    {
        foreach (LanguageFile group in Scan())
        {
            if (string.Equals(group.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return ParseGroup(key);
            }
        }

        return null;
    }

    /// <summary>按语言码找组：同码多组时优先组名恰等于码的纯组，没有再取第一个变体组。</summary>
    private static LanguageFile? ParseOptionalGroupByCode(string code)
    {
        LanguageFile? variant = null;
        foreach (LanguageFile group in Scan())
        {
            if (!string.Equals(group.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(group.Key, code, StringComparison.OrdinalIgnoreCase))
            {
                return ParseGroup(group.Key);
            }

            variant ??= group;
        }

        return variant != null ? ParseGroup(variant.Key) : null;
    }

    private static void OverlayBuiltInGroup(LanguageFile? file, LocalizedText.Language language)
    {
        if (file == null)
        {
            return;
        }

        foreach (Term term in Terms)
        {
            if (file.Entries.TryGetValue(term.Key, out string? value) && IsEffectiveTranslation(term.Key, value, file.Code))
            {
                MenuAPI.CreateLocalization(KeyPrefix + term.Key).AddLocalization(value, language);
            }
        }
    }

    /// <summary>文件里的译文填满全部语言槽；文件没有的键填英文。返回命中的词条数。</summary>
    private static int ApplyFile(LanguageFile file)
    {
        int applied = 0;
        foreach (Term term in Terms)
        {
            bool hasTranslation = file.Entries.TryGetValue(term.Key, out string? translated) &&
                                  IsEffectiveTranslation(term.Key, translated, file.Code);
            string value = hasTranslation ? translated! : term.English;
            if (hasTranslation)
            {
                applied++;
            }

            List<string> row = new List<string>();
            foreach (LocalizedText.Language _ in Enum.GetValues(typeof(LocalizedText.Language)))
            {
                row.Add(value);
            }

            LocalizedText.mainTable[(KeyPrefix + term.Key).ToUpperInvariant()] = row;
        }

        return applied;
    }

    private static string JoinLines(List<int> lines)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < lines.Count && i < 8; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(lines[i]);
        }

        if (lines.Count > 8)
        {
            sb.Append(" …");
        }

        return sb.ToString();
    }

    private static bool IsTermKey(string key)
    {
        foreach (Term term in Terms)
        {
            if (term.Key == key)
            {
                return true;
            }
        }

        return false;
    }

    // ── 当前界面语言（三选一，说明文字按它挑）────────────────────

    internal enum UiLang { En, ZhCn, ZhTw }

    /// <summary>选了语言文件按文件名（zh-cn / zh-tw 前缀），跟随游戏按游戏语言；其余一律英文。</summary>
    internal static UiLang UiLanguage
    {
        get
        {
            string selection = Plugin.LanguageFile != null ? Plugin.LanguageFile.Value ?? "" : "";
            if (selection.Length > 0)
            {
                string s = selection.ToLowerInvariant().Replace('_', '-');
                if (s.StartsWith("zh-tw") || s.StartsWith("zh-hant") || s.StartsWith("zh-hk"))
                {
                    return UiLang.ZhTw;
                }

                return s.StartsWith("zh") ? UiLang.ZhCn : UiLang.En;
            }

            return LocalizedText.CURRENT_LANGUAGE switch
            {
                LocalizedText.Language.SimplifiedChinese => UiLang.ZhCn,
                LocalizedText.Language.TraditionalChinese => UiLang.ZhTw,
                _ => UiLang.En,
            };
        }
    }

    /// <summary>按当前界面语言三选一。</summary>
    internal static string T(string en, string zhCn, string zhTw)
    {
        string result = _inlineSnapshot.TryGetValue(en.Trim(), out var translated) ? translated : UiLanguage switch { UiLang.ZhCn => zhCn, UiLang.ZhTw => zhTw, _ => en };
        if (en.Length > 0 && InlineKeys.Contains(en)) KnownInline[result] = en;
        return result;
    }

    /// <summary>config\ModConfigEnhance\readme.md 按当前界面语言重写（启动时与每次切语言后）。</summary>
    internal static void WriteReadme()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            File.WriteAllText(Path.Combine(RootDirectory, ReadmeFile), BuildReadme(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[语言] 写 {ReadmeFile} 失败: {ex.Message}");
        }
    }

    internal static string FollowStem()
    {
        return LocalizedText.CURRENT_LANGUAGE switch
        {
            LocalizedText.Language.SimplifiedChinese => "zh-cn",
            LocalizedText.Language.TraditionalChinese => "zh-tw",
            _ => "en-us",
        };
    }

    /// <summary>zh/en 裸码先归一到 zh-cn/en-us；简体与繁体绝不互认。</summary>

    // ── 内建文件 ──────────────────────────────────────────────────

    /// <summary>内置词条保留在内存；只准备导出和说明目录，个人词典按需创建。</summary>
    internal static void EnsureBuiltInFiles()
    {
        try
        {
            LanguageMigration.Run();
            Directory.CreateDirectory(ExportDirectory);
            WriteReadme();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[语言] 写内建语言文件失败: {ex.Message}");
        }
    }

    private static string BuildReadme() => LanguageReadme.For(UiLanguage).Replace("{VERSION}", Plugin.PluginVersion);

}
