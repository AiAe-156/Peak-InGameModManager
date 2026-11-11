#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal static class PackConflictUi
{
    internal static readonly Color AdviceColor = new(.91f, .85f, .60f);
    private static readonly Color ModColor = new(.98f, .97f, .91f);
    internal static string ModeCaption(PackConflictRule rule) => rule.mode == PackConflictRule.All
        ? Loc.T("All listed mods enabled together", "全部所列模组同时启用", "全部所列模組同時啟用")
        : Loc.T("Any two listed mods enabled together", "所列模组中任意两项同时启用", "所列模組中任意兩項同時啟用");
    private static string State(bool value) => value ? Loc.T("Conflict", "有冲突建议", "有衝突建議") : Loc.T("No match", "未触发", "未觸發");
    private static string Enabled(bool value) => value ? Loc.T("Enabled", "启用", "啟用") : Loc.T("Disabled", "停用", "停用");
    private static string Escape(string value) => value.Replace("<", "‹").Replace(">", "›");
    internal static string Hint(IEnumerable<PackConflictMatch> matches)
    {
        return string.Join("\n\n", matches.Select(match => "<color=#E8D99A>" + Escape(match.Title) + "</color>\n" +
            string.Join(" / ", match.Members.Select(m => "<color=#FAF7E8>" + Escape(m.Name) + "</color>")) + "\n" +
            Escape(match.Rule.reason) + "\n" +
            Loc.T("Profile target: ", "方案目标：", "方案目標：") + State(match.Planned) + " · " +
            Loc.T("Loaded now: ", "本次加载：", "本次載入：") + State(match.Loaded) + " · " +
            Loc.T("Next launch: ", "下次启动：", "下次啟動：") + State(match.NextLaunch) + "\n" +
            "<color=#B3B3A3>" + Escape(match.PackName) + " · " + (match.Ignored
                ? Loc.T("Active reminders ignored on this PC", "本机已忽略主动提醒", "本機已忽略主動提醒") : ModeCaption(match.Rule)) + "</color>"));
    }
    private static float Line(Transform parent, string text, float x, float y, float width, float size, Color color, bool bold = false)
    {
        var label = OverviewUi.Text(parent, text, size); label.richText = false; label.color = color;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal; label.textWrappingMode = TextWrappingModes.Normal;
        float height = Mathf.Max(size + 8, Mathf.Ceil(label.GetPreferredValues(text, width, Mathf.Infinity).y) + 6);
        OverviewUi.Place(label.rectTransform, x, y, width, height); return height;
    }
    // Shared by the overview details and the categorized apply review, so the same advice has one presentation.
    internal static float Card(Transform parent, PackConflictMatch match, float x, float y, float width, Action? ignore = null, bool showTitle = true)
    {
        var card = OverviewUi.Panel("PackConflict", parent, ModConfigLook.Surface(new Color(.12f, .115f, .075f, .83f)));
        OverviewUi.Place(card, x, y, width, 100); float cursor = 10, available = width - 28;
        if (showTitle) cursor += Line(card, match.Title, 14, cursor, available, 22, AdviceColor, true) + 3;
        cursor += Line(card, ModeCaption(match.Rule), 14, cursor, available, 16, AdviceColor);
        cursor += Line(card, Loc.T("From modpack: ", "来自整合包：", "來自整合包：") + match.PackName, 14, cursor, available, 15, OverviewUi.Muted) + 4;
        foreach (var member in match.Members)
        {
            cursor += Line(card, member.Name, 22, cursor, available - 8, 19, ModColor, true);
            string detail = member.Missing ? Loc.T("Not installed; the rule member is retained", "未安装，保留规则成员", "未安裝，保留規則成員") :
                Loc.T("Target: ", "目标：", "目標：") + Enabled(member.Planned) + "  ·  " +
                Loc.T("This session: ", "本次：", "本次：") + (member.Loaded ? Loc.T("Loaded", "已加载", "已載入") : Loc.T("Not loaded", "未加载", "未載入")) + "  ·  " +
                Loc.T("Next-launch files: ", "下次文件：", "下次檔案：") + Enabled(member.NextLaunch);
            cursor += Line(card, detail, 22, cursor, available - 8, 15, OverviewUi.Muted) + 4;
        }
        cursor += Line(card, match.Rule.reason, 14, cursor + 4, available, 18, OverviewUi.Ivory) + 6;
        cursor += Line(card, Loc.T("Profile target: ", "方案目标：", "方案目標：") + State(match.Planned) + "\n" +
            Loc.T("Loaded this session: ", "本次已加载：", "本次已載入：") + State(match.Loaded) + "\n" +
            Loc.T("Next-launch files: ", "下次启动文件：", "下次啟動檔案：") + State(match.NextLaunch), 14, cursor, available, 16, AdviceColor);
        if (match.Ignored) cursor += Line(card, Loc.T("Active reminders ignored on this PC; this badge remains available.", "本机已忽略主动提醒，角标仍保留详情。", "本機已忽略主動提醒，角標仍保留詳情。"), 14, cursor + 3, available, 15, OverviewUi.Muted);
        else if (ignore != null)
        {
            var button = OverviewUi.Button(card, Loc.T("Ignore on this PC", "本机忽略", "本機忽略"), ignore, 145, 32, UiActionRole.Neutral);
            OverviewUi.Place((RectTransform)button.transform, 14, cursor + 5, 145, 32); cursor += 43;
        }
        card.sizeDelta = new Vector2(width, cursor + 10); return cursor + 20;
    }
    internal static void Show(OverviewRepository repo, IReadOnlyList<PackConflictMatch> matches, Action changed)
    {
        var panel = OverviewUi.CreateModalPanel("MCE_ConflictAdvice", 900, 720); if (panel == null) return;
        var title = OverviewUi.Text(panel, Loc.T("Mod compatibility advice", "模组冲突建议", "模組衝突建議"), 27); title.color = AdviceColor;
        OverviewUi.Place(title.rectTransform, 24, 16, panel.rect.width - 48, 40);
        var scope = OverviewUi.Text(panel, Loc.T("Advice from installed modpacks applies to every profile. You may continue using these mods.", "已安装整合包的建议适用于所有方案，仍可继续使用这些模组。", "已安裝整合包的建議適用於所有方案，仍可繼續使用這些模組。"), 17);
        scope.color = OverviewUi.Muted; scope.textWrappingMode = TextWrappingModes.Normal;
        float scopeHeight = Mathf.Max(35, scope.GetPreferredValues(scope.text, panel.rect.width - 48, Mathf.Infinity).y + 6);
        OverviewUi.Place(scope.rectTransform, 24, 62, panel.rect.width - 48, scopeHeight);
        float top = 72 + scopeHeight, height = panel.rect.height - top - 70;
        var host = OverviewUi.Rect("AdviceBody", panel); OverviewUi.Place(host, 18, top, panel.rect.width - 36, height);
        var content = SourceUi.Rows(host, 0, height, out var scroll); ManagementUi.Scrollbar(host, scroll);
        void Draw()
        {
            foreach (Transform child in content) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            float y = 0, width = host.rect.width - 50;
            foreach (var match in matches.OrderBy(m => m.Ignored).ThenBy(m => m.PackName, StringComparer.OrdinalIgnoreCase))
                y += Card(content, match, 0, y, width, match.Ignored ? null : () => {
                    try { PackConflictService.Ignore(repo, match); changed(); Draw(); }
                    catch (Exception e) { scope.text = Loc.LocalizeMessage(e.Message); scope.color = AdviceColor; }
                });
            content.sizeDelta = new Vector2(0, y + 8);
        }
        Draw();
        var close = OverviewUi.Button(panel, Loc.T("Close", "关闭", "關閉"), OverviewUi.CloseModal, 130, 36);
        OverviewUi.Bottom((RectTransform)close.transform, 24, 18); EventSystem.current?.SetSelectedGameObject(close.gameObject);
    }
}
