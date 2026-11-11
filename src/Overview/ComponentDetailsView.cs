#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance.Updating;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>A bounded component page with normal actions and an expandable relationship tree.</summary>
internal static class ComponentDetailsView
{
    internal static void Open(OverviewRepository repo, IReadOnlyList<OverviewMod> mods,
        InfrastructureOverviewModel? owner, string key, ComponentDependencyGraph? rawGraph,
        IReadOnlyList<InfrastructureOverviewModel> components, Action? changed = null)
    {
        var graph=SharedRelationshipSnapshot.Get(mods,components,rawGraph);
        components=SharedRelationshipSnapshot.Components(graph);
        string requestedKey=key;
        key=SharedRelationshipSnapshot.Canonical(graph,key);
        var rootMod=SharedRelationshipSnapshot.Metadata(graph,key)?.Mod;
        var panel = OverviewUi.CreateModalPanel("MCE_ComponentDetails", 940, 680); if (panel == null) return;
        var labels = new ComponentRelationshipLabels(graph, mods, components);
        string alias = owner != null ? repo.GetRule(OverviewListView.ComponentRuleId(owner)).alias : "";
        string name = alias.Length > 0 ? alias + "（" + owner!.Name + "）" : owner?.Name ?? labels?.Name(key) ?? key;
        var heading = OverviewUi.Text(panel, name, 22);
        OverviewUi.PlaceClipped(heading, 22, 10, panel.rect.width - 44, 32);
        var content = SourceUi.Rows(panel, 50, Mathf.Max(28, panel.rect.height - 110), out var scroll);
        float width = panel.rect.width - 40;
        var body = OverviewUi.Rect("ComponentBody", content);
        Button? folderButton = null;
        bool reverse = graph.Edges.All(e=>!e.From.Equals(key,StringComparison.OrdinalIgnoreCase))
            &&graph.Edges.Any(e=>e.To.Equals(key,StringComparison.OrdinalIgnoreCase));
        var collapsed = new HashSet<string>(StringComparer.Ordinal);
        var allRows = Array.Empty<ComponentRelationshipTree.Row>();
        bool defaultExpansion = true;
        void Clear()
        {
            for (int i = body.childCount - 1; i >= 0; i--) {
                var child = body.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child);
            }
        }
        TextMeshProUGUI Text(string text, float x, float y, float w, float height, float size = 17, bool wrap = false, bool rich = false)
        {
            var label = OverviewUi.Text(body, text, size);
            label.richText = rich;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            // TMP suppresses the entire first line in Ellipsis mode when its actual font
            // metrics exceed the box. Localized fallback fonts can be taller than the base font.
            float textWidth = Mathf.Max(20, w);
            float measuredHeight = Mathf.Ceil(label.GetPreferredValues(text, textWidth, 0).y) + 4;
            OverviewUi.Place(label.rectTransform, x, y, textWidth, Mathf.Max(height, measuredHeight));
            return label;
        }
        Button Action(string title, Action clicked, float x, float y, float w, bool enabled = true, UiActionRole role = UiActionRole.Neutral)
        {
            var button = OverviewUi.Button(body, title, clicked, w, 32, role);
            button.gameObject.name = "ComponentAction_" + title;
            OverviewUi.Place((RectTransform)button.transform, x, y, w, 32); button.interactable = enabled;
            var caption = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (caption != null) { caption.alignment = TextAlignmentOptions.Midline; caption.overflowMode = TextOverflowModes.Ellipsis; }
            return button;
        }
        void OpenLocations()
        {
            if (owner == null || folderButton == null) return;
            FolderShortcuts.Open(owner,(RectTransform)folderButton.transform,_=>{});
        }
        void Render(bool resetScroll = false)
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            string? focus = selected != null && selected.transform.IsChildOf(body) ? selected.name : null;
            float position = scroll.verticalNormalizedPosition; Clear();
            float y = 0;
            string version = owner?.Version ?? labels?.Version(key) ?? "";
            var metadata = Text((owner?.TypeTitle ?? Loc.T("Component details", "组件详情", "組件詳情")) + " · " + version,
                6, y, width - 12, 24, 15);
            metadata.color = OverviewUi.Muted; y += metadata.rectTransform.rect.height + 4;
            bool identified = owner?.IdentityConfirmed ?? ShopInstalled.Key(key);
            if (owner != null)
            {
                int columns = width >= 720 ? 4 : 2;
                float buttonWidth = Mathf.Min(185, (width - (columns - 1) * 10 - 12) / columns);
                Button PlaceAction(int i, string title, Action clicked, bool enabled = true, UiActionRole role = UiActionRole.Neutral)
                    => Action(title, clicked, 6 + (i % columns) * (buttonWidth + 10), y + (i / columns) * 36, buttonWidth, enabled, role);
                PlaceAction(0, Loc.T("Mod store", "模组商店", "模組商店"), () => ShopWindow.Open(repo, mods, false, key), identified);
                PlaceAction(1, Loc.T("Store page", "商店网页", "商店網頁"), () => {
                    int at = key.IndexOf('-'); Application.OpenURL(ThunderstoreUpdateService.HomeUrl(key.Substring(0, at), key.Substring(at + 1)));
                }, identified);
                folderButton = PlaceAction(2, Loc.T("Open folder", "打开文件夹", "開啟資料夾"), OpenLocations);
                PlaceAction(3, Loc.T("Uninstall (safe preview)", "卸载（安全预览）", "解除安裝（安全預覽）"), () => {
                    if (!identified || SourceUi.UninstallReason(key).Length > 0) { Render(); return; }
                    // 2.3.9 (user): base components get one extra reminder — other mods often share them.
                    OverviewUi.ConfirmText(Loc.T("Uninstall a base component", "卸载基础组件", "解除安裝基礎組件"),
                        (owner?.Name ?? key) + "\n\n" + Loc.T("Base components are shared libraries; other mods may stop working without it. The preview still checks file ownership and dependencies, and nothing is removed until you confirm it.",
                            "基础组件是共用的运行库，其他模组可能因此无法工作。接下来仍会先核对文件归属与依赖，确认预览内容前不会删除任何文件。",
                            "基礎組件是共用的執行庫，其他模組可能因此無法運作。接下來仍會先核對檔案歸屬與依賴，確認預覽內容前不會刪除任何檔案。"),
                        () => ShopWindow.Open(repo, mods, false, key, true), UiActionRole.Danger);
                }, identified && SourceUi.UninstallReason(key).Length == 0, UiActionRole.Danger);
                // 2.3.4 (user): components can be renamed in the overview (display alias) and enabled/disabled like mods,
                // except the ones MCE runs on. They stay in the components group and never take part in folder organization.
                var component = owner;
                PlaceAction(4, Loc.T("Display name", "改译名", "改譯名"), () => {
                    var rule = repo.GetRule(OverviewListView.ComponentRuleId(component));
                    OverviewUi.Ask(Loc.T("Display name for ", "显示名称：", "顯示名稱：") + component.Name,
                        new[] { (Loc.T("Display name (blank uses the package name)", "显示名称（留空使用包名）", "顯示名稱（留空使用套件名）"), rule.alias) }, values => {
                        repo.EnsureEditableProfile(); var target = repo.GetRule(OverviewListView.ComponentRuleId(component));
                        string previous = target.alias; target.alias = values[0].Trim();
                        try { repo.SavePackInformation(ConfigurationSaveScope.ModInformation, target.id); }
                        catch { target.alias = previous; throw; }
                        changed?.Invoke(); OverviewUi.CloseModal();
                    });
                });
                bool on = component.Enabled;
                string toggleReason = ComponentToggle.BlockReason(component, !on);
                var toggle = PlaceAction(5, on ? Loc.T("Disable", "停用", "停用") : Loc.T("Enable", "启用", "啟用"), () => {
                    OverviewUi.ConfirmText(on ? Loc.T("Disable component", "停用组件", "停用組件") : Loc.T("Enable component", "启用组件", "啟用組件"),
                        component.Name + "\n\n" + (on
                            ? Loc.T("Its assemblies are renamed to .dll.d and are not loaded from the next launch. Mods that need this component may stop working.",
                                "它的程序集会改名为 .dll.d，从下次启动起不再加载。依赖此组件的模组可能无法工作。", "它的程式集會改名為 .dll.d，從下次啟動起不再載入。依賴此組件的模組可能無法運作。")
                            : Loc.T("Its assemblies are renamed back to .dll and load from the next launch.",
                                "它的程序集会改回 .dll，从下次启动起加载。", "它的程式集會改回 .dll，從下次啟動起載入。")),
                        () => {
                            try { ComponentToggle.Set(component, !on); OverviewCatalogue.InvalidateCaches(); OverviewPageCache.Invalidate(); changed?.Invoke(); OverviewUi.CloseModal(); }
                            catch (Exception error) { OverviewUi.ShowText(Loc.T("Could not change the component", "组件启停未完成", "組件啟停未完成"), Loc.LocalizeMessage(error.Message)); }
                        }, on ? UiActionRole.Danger : UiActionRole.Commit);
                }, toggleReason.Length == 0, on ? UiActionRole.Danger : UiActionRole.Commit);
                if (toggleReason.Length > 0) { var hover = toggle.gameObject.AddComponent<OverviewHover>(); var tip = toggleReason; hover.Enter = () => OverviewUi.ShowText(Loc.T("Enable / disable", "启停", "啟停"), tip); }
                y += (6 + columns - 1) / columns * 36 + 2;
            }
            string status = !identified ? Loc.T("Source identity is unconfirmed. Store actions are unavailable.",
                "来源身份尚未确认，商店操作暂不可用。", "來源身分尚未確認，商店操作暫不可用。")
                : SourceUi.DependencyDiagnostic(key).Summary;
            if (status.Length > 0) {
                var statusText = Text(status, 6, y, width - 12, 24, 14, true);
                statusText.color = OverviewUi.Muted; y += statusText.rectTransform.rect.height + 4;
            }
            float tabWidth = Mathf.Min(195, (width - 22) / 2);
            void SwitchDirection(bool incoming)
            {
                reverse = incoming; collapsed.Clear(); defaultExpansion = true;
                Render(true);
            }
            var dependencies = Action(Loc.T("Dependencies", "依赖的模组", "依賴的模組"), () => SwitchDirection(false), 6, y, tabWidth);
            var dependants = Action(Loc.T("Installed dependants", "依赖它的已安装模组", "依賴它的已安裝模組"), () => SwitchDirection(true), 16 + tabWidth, y, tabWidth);
            UiActionStyle.Select(dependencies, !reverse); UiActionStyle.Select(dependants, reverse);
            bool inlineActions = width >= 720;
            float bulkX = inlineActions ? 26 + tabWidth * 2 : 6;
            float bulkY = inlineActions ? y : y + 36;
            Action(Loc.T("Expand all", "全部展开", "全部展開"), () => { collapsed.Clear(); defaultExpansion = false; Render(); }, bulkX, bulkY, 105);
            Action(Loc.T("Collapse all", "全部折叠", "全部摺疊"), () => { defaultExpansion = false; foreach (var row in allRows.Where(r => r.HasChildren)) collapsed.Add(row.Path); Render(); }, bulkX + 115, bulkY, 105);
            y += inlineActions ? 36 : 72;
            if (graph == null || labels == null) {
                var reading = Text(Loc.T("Reading relationships…", "正在读取依赖关系……", "正在讀取依賴關係……"), 10, y, width - 20, 30);
                y += reading.rectTransform.rect.height + 6;
            }
            else
            {
                allRows = ComponentRelationshipTree.Build(graph, key, reverse, 16, 2048, labels.Canonical).ToArray();
                if (defaultExpansion)
                {
                    foreach (var row in allRows.Where(r => r.Depth >= 2 && r.HasChildren)) collapsed.Add(row.Path);
                    defaultExpansion = false;
                }
                var hidden = new HashSet<string>(StringComparer.Ordinal);
                if (allRows.Length == 0) {
                    var empty = Text(Loc.T("No relationships found.", "暂无依赖关系。", "暫無依賴關係。"), 10, y, width - 20, 30);
                    y += empty.rectTransform.rect.height + 6;
                }
                foreach (var row in allRows)
                {
                    if (hidden.Contains(row.Parent)) { hidden.Add(row.Path); continue; }
                    bool closed = collapsed.Contains(row.Path);
                    if (closed) hidden.Add(row.Path);
                    float indent = Mathf.Min(row.Depth - 1, Mathf.Max(0, Mathf.Min(8, (width - 400) / 14))) * 14;
                    var background = OverviewUi.Panel("Relationship", body, ModConfigLook.Surface(new Color(.08f, .11f, .12f, .50f)));
                    float stateWidth = Mathf.Clamp(width * .19f, 100, 165);
                    float nameWidth = Mathf.Max(60, width - 58 - indent - stateWidth);
                    var rowTitle = Text("", 34 + indent, y + 2, nameWidth, 24, 16, rich: true);
                    string fitted = DependencyRowText.FitName(labels.Name(row.Id), labels.Original(row.Id), labels.Version(row.Id),
                        nameWidth - 20, value => rowTitle.GetPreferredValues(value).x);
                    rowTitle.text = DependencyDepthStyle.Wrap(OverviewDependencyHints.TreeArrow() + " " + fitted, row.Depth - 1);
                    float titleHeight = Mathf.Max(24, Mathf.Ceil(rowTitle.GetPreferredValues(rowTitle.text, nameWidth, 0).y) + 2);
                    OverviewUi.Place(rowTitle.rectTransform, 34 + indent, y + 2, nameWidth, titleHeight);
                    var rowState = Text(labels.State(row), width - stateWidth - 12, y + 2, stateWidth, 24, 13);
                    rowState.color = OverviewUi.Muted;
                    float rowHeight = Mathf.Max(titleHeight, rowState.rectTransform.rect.height) + 4;
                    rowTitle.alignment = rowState.alignment = TextAlignmentOptions.MidlineLeft;
                    OverviewUi.Place(rowTitle.rectTransform, 34 + indent, y + 2, nameWidth, rowHeight - 4);
                    OverviewUi.Place(rowState.rectTransform, width - stateWidth - 12, y + 2, stateWidth, rowHeight - 4);
                    OverviewUi.Place(background, 6, y, width - 12, rowHeight);
                    if (row.HasChildren && !row.Truncated)
                    {
                        var toggle = OverviewUi.IconButton(body, closed ? "triangle-right" : "triangle-down", () => {
                            if (!collapsed.Add(row.Path)) collapsed.Remove(row.Path); Render();
                        }, 18, 20);
                        if (ColorUtility.TryParseHtmlString(DependencyDepthStyle.Ink(row.Depth - 1), out var depthInk))
                            foreach (var glyph in toggle.GetComponentsInChildren<OverviewIconGraphic>(true)) glyph.color = depthInk;
                        toggle.gameObject.name = "Relationship_" + row.Path;
                        OverviewUi.Place((RectTransform)toggle.transform, 10 + indent, y + (rowHeight - 20) / 2, 18, 20);
                        GamepadSupport.MarkSelectable(toggle.gameObject, scroll, (RectTransform)toggle.transform);
                    }
                    y += rowHeight + 1;
                }
                if (allRows.Any(r => r.Truncated)) {
                    var limit = Text(Loc.T("Relationship limit reached.", "其余关系已省略。", "其餘關係已省略。"), 10, y, width - 20, 28, 15);
                    y += limit.rectTransform.rect.height + 4;
                }
            }
            y += 4;
            var note = Text(Loc.T("These relationships describe installed files. Uninstall still requires a fresh safety check.",
                "此处显示已安装文件的关系；卸载前会重新核查。", "此處顯示已安裝檔案的關係；解除安裝前會重新核查。"), 10, y, width - 20, 22, 12, true);
            note.color = OverviewUi.Muted;
            y += note.rectTransform.rect.height + 4;
            OverviewUi.Place(body, 0, 0, width, y); content.sizeDelta = new Vector2(0, y);
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = resetScroll ? 1 : position;
            if (focus != null) {
                var replacement = body.GetComponentsInChildren<Selectable>(false).FirstOrDefault(s => s.gameObject.name == focus && s.IsInteractable());
                if (replacement != null) EventSystem.current?.SetSelectedGameObject(replacement.gameObject);
            }
            PublicationNavigation.Attach(panel);
        }
        // First and second levels are visible; deeper branches start collapsed.
        Render(true);
        var close = OverviewUi.Button(panel, Loc.T("Close", "关闭", "關閉"), OverviewUi.CloseModal, 115, 34);
        OverviewUi.Bottom((RectTransform)close.transform, panel.rect.width - 137, 16);
        int language = Loc.TranslationRevision;
        string context = SourceUi.DependencyContext;
        panel.gameObject.AddComponent<ComponentDetailsRefresh>().Tick = () => {
            if (context != SourceUi.DependencyContext) { OverviewUi.CloseModal(); return; }
            var current = SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),SourceUi.DependencyGraph);
            if (ReferenceEquals(current, graph) && language == Loc.TranslationRevision) return;
            bool changedGraph = !ReferenceEquals(current, graph);
            graph = current; language = Loc.TranslationRevision;
            key=rootMod!=null ? SharedRelationshipSnapshot.RootId(rootMod,graph) : SharedRelationshipSnapshot.Canonical(graph,requestedKey);
            components=SharedRelationshipSnapshot.Components(graph);
            labels = new ComponentRelationshipLabels(graph, mods, components);
            if (changedGraph) { collapsed.Clear(); defaultExpansion = true; }
            heading.text = owner?.Name ?? labels?.Name(key) ?? key;
            Render();
        };
        PublicationNavigation.Attach(panel);
    }
}

internal sealed class ComponentDetailsRefresh : MonoBehaviour
{
    internal Action? Tick;
    private void Update() => Tick?.Invoke();
    private void OnDestroy() => Tick = null;
}
