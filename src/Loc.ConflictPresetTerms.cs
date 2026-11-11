#nullable enable
namespace ModConfigEnhance;

internal static partial class Loc
{
    private static readonly Term[] ConflictPresetTerms =
    {
        new("All listed mods enabled together", "All listed mods enabled together", "全部所列模组同时启用", "全部所列模組同時啟用"),
        new("Any two listed mods enabled together", "Any two listed mods enabled together", "所列模组中任意两项同时启用", "所列模組中任意兩項同時啟用"),
        new("Conflict", "Conflict", "有冲突建议", "有衝突建議"),
        new("No match", "No match", "未触发", "未觸發"),
        new("Loaded now: ", "Loaded now: ", "本次加载：", "本次載入："),
        new("Next launch: ", "Next launch: ", "下次启动：", "下次啟動："),
        new("Active reminders ignored on this PC", "Active reminders ignored on this PC", "本机已忽略主动提醒", "本機已忽略主動提醒"),
        new("From modpack: ", "From modpack: ", "来自整合包：", "來自整合包："),
        new("Not installed; the rule member is retained", "Not installed; the rule member is retained", "未安装，保留规则成员", "未安裝，保留規則成員"),
        new("This session: ", "This session: ", "本次：", "本次："),
        new("Loaded this session: ", "Loaded this session: ", "本次已加载：", "本次已載入："),
        new("Active reminders ignored on this PC; this badge remains available.", "Active reminders ignored on this PC; this badge remains available.", "本机已忽略主动提醒，角标仍保留详情。", "本機已忽略主動提醒，角標仍保留詳情。"),
        new("Ignore on this PC", "Ignore on this PC", "本机忽略", "本機忽略"),
        new("Mod compatibility advice", "Mod compatibility advice", "模组冲突建议", "模組衝突建議"),
        new("Advice from installed modpacks applies to every profile. You may continue using these mods.", "Advice from installed modpacks applies to every profile. You may continue using these mods.", "已安装整合包的建议适用于所有方案，仍可继续使用这些模组。", "已安裝整合包的建議適用於所有方案，仍可繼續使用這些模組。"),
        new("Conflict presets exceed the 512 KB limit.", "Conflict presets exceed the 512 KB limit.", "冲突预设超过 512 KB 上限。", "衝突預設超過 512 KB 上限。"),
        new("The pack needs a stable identity before editing conflict presets.", "The pack needs a stable identity before editing conflict presets.", "编辑冲突预设前需要稳定的整合包标识。", "編輯衝突預設前需要穩定的整合包識別碼。"),
        new("Invalid conflict preset JSON: ", "Invalid conflict preset JSON: ", "冲突预设 JSON 无效：", "衝突預設 JSON 無效："),
        new("Unsupported conflict preset format.", "Unsupported conflict preset format.", "不支持的冲突预设格式。", "不支援的衝突預設格式。"),
        new("Use at most 256 conflict rules.", "Use at most 256 conflict rules.", "冲突规则最多 256 条，且规则列表不能为空。", "衝突規則最多 256 條，且規則清單不能為空。"),
        new("Rule {0}: ", "Rule {0}: ", "规则 {0}：", "規則 {0}："),
        new("The rule is empty.", "The rule is empty.", "规则为空。", "規則為空。"),
        new("Use a unique rule ID of at most 128 characters.", "Use a unique rule ID of at most 128 characters.", "规则标识不能为空、重复或超过 128 字。", "規則識別碼不能為空、重複或超過 128 字。"),
        new("Select a supported trigger mode.", "Select a supported trigger mode.", "请选择有效的触发模式。", "請選擇有效的觸發模式。"),
        new("Keep the title within 160 characters.", "Keep the title within 160 characters.", "标题最多 160 字。", "標題最多 160 字。"),
        new("Enter a reason within 4,000 characters.", "Enter a reason within 4,000 characters.", "请填写冲突原因，最多 4000 字。", "請填寫衝突原因，最多 4000 字。"),
        new("Select 2 to 32 mods.", "Select 2 to 32 mods.", "请选择 2 至 32 个模组。", "請選擇 2 至 32 個模組。"),
        new("A mod identity or fallback name is invalid.", "A mod identity or fallback name is invalid.", "模组标识或备用名称无效。", "模組識別碼或備用名稱無效。"),
        new("The same mod occurs more than once.", "The same mod occurs more than once.", "同一个模组重复出现。", "同一個模組重複出現。"),
        new("These plugins toggle together. Select at least two independent mod packages.", "These plugins toggle together. Select at least two independent mod packages.", "这些插件随包联动，请至少选择两个可独立启停的模组包。", "這些外掛隨包連動，請至少選擇兩個可獨立啟停的模組包。"),
        new("Compatibility · {0}", "Compatibility · {0}", "冲突建议 · {0}", "衝突建議 · {0}"),
        new("Compatibility advice", "Compatibility advice", "冲突建议", "衝突建議"),
        new("Apply anyway", "Apply anyway", "仍然应用", "仍然套用"),
        new("Return to adjust", "Return to adjust", "返回调整", "返回調整"),
        new("The modpack author advises against these combinations. You can still apply them, or ignore active reminders on this PC; the pale-yellow badge keeps the details.", "The modpack author advises against these combinations. You can still apply them, or ignore active reminders on this PC; the pale-yellow badge keeps the details.", "整合包作者建议避免以下组合。你仍可应用，或在本机忽略主动提醒；浅黄色角标会保留详情。", "整合包作者建議避免以下組合。你仍可套用，或在本機忽略主動提醒；淺黃色角標會保留詳情。"),
        new("Replaced by a later local build", "Replaced by a later local build", "已由后续本机安装接替", "已由後續本機安裝接替"),
    };
}
