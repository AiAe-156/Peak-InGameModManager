#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ModConfigEnhance.Overview;
using PEAKLib.ModConfig.Components;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.UI;

namespace ModConfigEnhance;

/// <summary>
/// 侧栏分组：数据就是模组概览的分组（state.json 里的 groups / mods[].groupId / collapsedGroups），两边共用一份。
///   · 列表顺序：自身行 → 置顶区（不加标题）→ 各分组（标题 + 成员）→ 未分组；置顶的模组只在置顶区出现一次。
///   · 左缘竖条：置顶 = 蓝条 [0,4] 紧挨一条分组色细条 [5,9]；非置顶的分组成员只画分组色条 [0,4]。
///   · 折叠与概览同步；搜索或类型筛选生效时，折叠的分组临时展开，没有匹配成员的分组标题隐藏。
///   · 第三行的「分组管理」钮在设置页上直接弹出概览同款分组编辑窗口。
/// </summary>
internal static partial class ModConfigSidebarWidgets
{
    /// <summary>2.4.6：分组行是树里最上一级，比模组行（30）高（用户：原 26 比模组行 36 还小，主次反了）。</summary>
    private const float GroupHeaderHeight = 36f;
    private const float RailWidth = 4f;

    /// <summary>置顶竖线（2.4.0 改细）：2 宽，分组色条紧贴它右侧。</summary>
    internal const float PinAccentWidth = 2f;
    private const float PinnedRailLeft = PinAccentWidth + 1f;

    /// <summary>设置页独立持有一份仓库；概览那份保存后这份会变「过期」，下次用时重读。</summary>
    internal static class SidebarGroupStore
    {
        private static OverviewRepository? _repository;
        private static int _contentVersion = -1;

        internal static bool NeedsReload => _repository == null || _repository.IsStale || _contentVersion != OverviewPageCache.Content;

        internal static OverviewRepository? Get()
        {
            try
            {
                if (_repository == null)
                {
                    _repository = new OverviewRepository(appliedView: true);
                }
                else if (NeedsReload)
                {
                    _repository.Reload();
                }

                _contentVersion = OverviewPageCache.Content;
                return _repository;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[侧栏分组] 读取概览分组失败，侧栏按不分组显示：" + ex.Message);
                return null;
            }
        }
    }

    private static OverviewGroupEditor? _groupEditor;
    private static Transform? _groupEditorPage;
    private static RectTransform? _groupEditorHost;

    /// <summary>编辑窗口与它的确认框挂在这个全屏容器里：不受页面布局组摆布，并压在页面其它内容之上。</summary>
    private static RectTransform ModalHost(Transform page)
    {
        Transform? existing = page.Find("MCE_SidebarModalHost");
        RectTransform host = existing as RectTransform ?? OverviewUi.Rect("MCE_SidebarModalHost", page);
        if (host.GetComponent<LayoutElement>() == null)
        {
            host.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        OverviewUi.Fill(host);
        host.SetAsLastSibling();
        return host;
    }

    /// <summary>返回键（Esc / B / 返回按钮）先关分组编辑窗口的最上层，不翻页。由 GetParentPage 前缀调用。</summary>
    internal static bool GroupEditorBackPrefix(PeakChildPage __instance, ref (UIPage, PageTransistion) __result)
    {
        if (_groupEditor == null || !_groupEditor.IsOpen || _groupEditorPage != __instance.transform)
        {
            return true;
        }

        // 放弃修改的确认框是 OverviewUi 的公共层，先关它；否则交给编辑器（有未保存修改时它会弹确认）
        if (!OverviewUi.TryCloseTopModal())
        {
            _groupEditor.TryCloseTop();
        }

        __result = (__instance, GamepadSupport.NoopTransistion.Shared);
        return false;
    }

    internal static void PatchGroupEditorBack(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(PeakChildPage), "GetParentPage", Type.EmptyTypes);
        if (method == null)
        {
            Plugin.Log.LogWarning("[侧栏分组] 找不到 PeakChildPage.GetParentPage，分组编辑窗口只能用按钮关闭。");
            return;
        }

        // 要早于手柄的分级返回与概览跳转的返回改写，所以排最前
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(ModConfigSidebarWidgets), nameof(GroupEditorBackPrefix)) { priority = Priority.First });
    }

    private static void CloseGroupEditor()
    {
        if (_groupEditor == null)
        {
            return;
        }

        if (_groupEditorHost != null && OverviewUi.ModalParent == _groupEditorHost)
        {
            OverviewUi.CloseModal();
        }

        _groupEditor.Close();
        _groupEditor = null;
        _groupEditorPage = null;
        _groupEditorHost = null;
    }

    private static Color ParseGroupColor(string? hex)
    {
        return ColorUtility.TryParseHtmlString(hex ?? "", out Color color) ? color : OverviewUi.Blue;
    }

    private static Image MakeRail(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = new Vector2(0f, 4f);
        rect.offsetMax = new Vector2(RailWidth, -4f);
        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        ApplyRounded(image, 2f);
        return image;
    }

    private static Image MakeGroupRail(Transform row)
    {
        Image rail = MakeRail(row, "MCE_GroupRail");
        rail.gameObject.SetActive(false);
        return rail;
    }

    /// <summary>分组标题上的悬停 / 手柄聚焦注释：分组名 + 描述 + 操作说明，显示在说明面板。</summary>
    private sealed class GroupTip : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        internal string Title = "";
        internal string Description = "";

        public void OnPointerEnter(PointerEventData eventData) => Show();

        public void OnSelect(BaseEventData eventData) => Show();

        private void Show()
        {
            string body = Description.Length > 0 ? Description + "\n\n" + Loc.Get("TIP_GROUP_HEADER") : Loc.Get("TIP_GROUP_HEADER");
            ModConfigDescriptionPanel.Current?.ShowNotice(ModConfigDescriptionPanel.SplitNotice(Title + "\n" + body));
        }
    }

    internal sealed partial class SidebarController
    {
        private sealed class GroupSlot
        {
            internal string Id = "";
            internal string Name = "";
            internal Color Color;
            internal int Depth = 1;
            /// <summary>Ancestors first, then this group: used for the rails and to fold the header under a collapsed ancestor.</summary>
            internal List<string> Chain = new List<string>();
            internal GameObject Row = null!;
            internal TextMeshProUGUI? Label;
            internal RectTransform? Arrow;
            internal GroupTip? Tip;
        }

        private readonly List<GroupSlot> _groupSlots = new List<GroupSlot>();
        private readonly Dictionary<string, Color> _groupColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        /// <summary>2.3.0 nested groups: the tree the slots were built from.</summary>
        private GroupTree _groupTree = new GroupTree(Array.Empty<OverviewGroup>());
        private readonly HashSet<GameObject> _collapsedHidden = new HashSet<GameObject>();
        private bool _built;
        private int _groupLanguageRevision = -1, _groupSettingsCount = -1, _groupPluginsCount = -1;
        private string _lastGroupSummary = "";

        private bool IsPinnedRow(RowWidgets widgets) => widgets.IsSelf || Pinned.Contains(widgets.Name);

        /// <summary>行所在的分组段；没有对应标题的组按未分组处理，保证每一行都落进某一段。</summary>
        private string RowGroup(GameObject row)
        {
            foreach (RowWidgets widgets in _rows)
            {
                if (widgets.Row == row)
                {
                    return _groupColors.ContainsKey(widgets.GroupId) ? widgets.GroupId : "";
                }
            }

            return "";
        }

        /// <summary>
        /// 重读分组数据：模组行按插件 GUID 对到概览规则（ModConfig 的行名就是设置分类名，同名插件也能分清），
        /// 再按概览顺序重建分组标题。数据没变也可以重复调用。
        /// </summary>
        internal void RefreshGroups(bool reorder)
        {
            if (!_built || ListContent == null)
            {
                return;
            }

            OverviewRepository? repository = SidebarGroupStore.Get();
            _groupLanguageRevision = Loc.TranslationRevision;
            _groupSettingsCount = Menu?.settings?.Count() ?? -1;
            _groupPluginsCount = BepInEx.Bootstrap.Chainloader.PluginInfos.Count;
            Dictionary<string, List<string>> guidsByCategory = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (Menu != null && Menu.settings != null)
            {
                foreach (var setting in Menu.settings)
                {
                    string? category = setting?.GetCategory();
                    string? guid = setting?.Pluginfo?.Metadata.GUID;
                    if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(guid))
                    {
                        continue;
                    }

                    if (!guidsByCategory.TryGetValue(category!, out List<string>? guids))
                    {
                        guidsByCategory[category!] = guids = new List<string>();
                    }

                    if (!guids.Contains(guid!))
                    {
                        guids.Add(guid!);
                    }
                }
            }

            // 设置表在首次建页 / 进页的那一刻可能还是空的；ModConfig 的行名就是 FixNaming(插件名)，用已加载插件兜底
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                if (info?.Metadata == null || string.IsNullOrEmpty(info.Metadata.GUID))
                {
                    continue;
                }

                string category = Plugin.FixNaming(info.Metadata.Name);
                if (!guidsByCategory.TryGetValue(category, out List<string>? guids))
                {
                    guidsByCategory[category] = guids = new List<string>();
                }

                if (!guids.Contains(info.Metadata.GUID))
                {
                    guids.Add(info.Metadata.GUID);
                }
            }

            int matched = 0;
            foreach (RowWidgets widgets in _rows)
            {
                widgets.GroupId = "";
                if (repository != null && guidsByCategory.TryGetValue(widgets.Name, out List<string>? guids))
                {
                    widgets.GroupId = guids.Select(repository.GroupOf).FirstOrDefault(id => id.Length > 0) ?? "";
                    if (widgets.GroupId.Length > 0)
                    {
                        matched++;
                    }
                }
            }

            string summary = Loc.F("{0} groups, {1}/{2} rows grouped", "{0} 个分组，{1}/{2} 行属于分组", "{0} 個分組，{1}/{2} 列屬於分組", repository?.State.groups.Count ?? 0, matched, _rows.Count);
            if (summary != _lastGroupSummary)
            {
                _lastGroupSummary = summary;
                Plugin.Log.LogInfo("[侧栏分组] " + summary + "。");
            }

            foreach (GroupSlot slot in _groupSlots)
            {
                if (slot.Row != null)
                {
                    slot.Row.SetActive(false);
                    Destroy(slot.Row);
                }
            }

            _groupSlots.Clear();
            _groupColors.Clear();
            if (repository != null)
            {
                _groupTree = new GroupTree(repository.State.groups);
                var wanted = new HashSet<string>(StringComparer.Ordinal);
                foreach (RowWidgets row in _rows)
                {
                    if (!row.IsSelf && _groupTree.Contains(row.GroupId))
                    {
                        foreach (OverviewGroup member in _groupTree.Chain(row.GroupId)) wanted.Add(member.id);
                    }
                }

                foreach (OverviewGroup group in _groupTree.Flat)
                {
                    if (!wanted.Contains(group.id) || _groupColors.ContainsKey(group.id))
                    {
                        continue;
                    }

                    Color color = ParseGroupColor(group.color);
                    _groupColors[group.id] = color;
                    GroupSlot slot = MakeGroupHeader(group.id, group.name, group.description ?? "", color,
                        _groupTree.Depth(group.id), _groupTree.Chain(group.id).Select(g => g.id).ToList());
                    slot.Tip!.Title = _groupTree.Path(group.id);
                    _groupSlots.Add(slot);
                }

                if (_groupSlots.Count > 0 && _rows.Any(r => !r.IsSelf && r.GroupId.Length == 0))
                {
                    _groupColors[""] = new Color(Cream.r, Cream.g, Cream.b, 0.75f);
                    _groupSlots.Add(MakeGroupHeader("", Loc.Get("GROUP_UNGROUPED"), "", _groupColors[""], 1, new List<string>()));
                }
            }

            foreach (RowWidgets widgets in _rows)
            {
                UpdateRowMarks(widgets);
            }

            RefreshGroupLabels();
            if (reorder)
            {
                ApplyPinOrder();
                ApplyGroupVisibility(null);
            }
        }

        /// <summary>置顶蓝条 + 分组色条：置顶时分组条紧贴蓝条右侧，否则分组条占蓝条的位置。未分组不画分组条。</summary>
        private void UpdateRowMarks(RowWidgets widgets)
        {
            bool pinned = IsPinnedRow(widgets);
            if (widgets.Accent != null && widgets.Accent.activeSelf != pinned)
            {
                widgets.Accent.SetActive(pinned);
            }

            if (widgets.GroupRail == null)
            {
                return;
            }

            bool grouped = widgets.GroupId.Length > 0 && _groupColors.TryGetValue(widgets.GroupId, out Color color);
            widgets.GroupRail.gameObject.SetActive(grouped);
            if (!grouped)
            {
                return;
            }

            widgets.GroupRail.color = _groupColors[widgets.GroupId];
            float left = pinned ? PinnedRailLeft : 0f;
            // Ancestors' rails sit to the left of the own-group rail, like the overview list. Each deeper level is
            // thinner and shorter (bottom-aligned), see GroupRailShape.
            List<OverviewGroup> chain = _groupTree.Chain(widgets.GroupId);
            int ancestors = Math.Max(0, chain.Count - 1);
            GroupRailShape.Stretch(widgets.GroupRail.rectTransform, GroupRailShape.X(left, RailWidth, 1f, ancestors),
                GroupRailShape.Width(RailWidth, ancestors), 4f, ancestors);
            SyncAncestorRails(widgets, chain, left);
        }

        /// <summary>标题文字 = 分组名 + 段内（不含置顶）成员数。</summary>
        private void RefreshGroupLabels()
        {
            foreach (GroupSlot slot in _groupSlots)
            {
                if (slot.Label == null)
                {
                    continue;
                }

                HashSet<string>? subtree = slot.Id.Length > 0 ? _groupTree.Subtree(slot.Id) : null;
                int count = _rows.Count(r => !IsPinnedRow(r) && (subtree != null ? subtree.Contains(r.GroupId) : !_groupColors.ContainsKey(r.GroupId) || r.GroupId.Length == 0));
                slot.Label.text = EscapeRich(slot.Name) + "  <alpha=#99><size=85%>" + count + "</size>";
            }
        }

        private static string EscapeRich(string value) => value.Replace("<", "＜").Replace(">", "＞");

        private Color GroupColorOf(string id) => _groupColors.TryGetValue(id, out Color color) ? color : ParseGroupColor(_groupTree.Get(id)?.color);

        /// <summary>Extra rails to the left of a member row's own rail, one per ancestor group, created on demand.</summary>
        private void SyncAncestorRails(RowWidgets widgets, List<OverviewGroup> chain, float left)
        {
            if (widgets.GroupRail == null) return;
            Transform row = widgets.GroupRail.transform.parent;
            int needed = Math.Max(0, chain.Count - 1);
            for (int i = 0; ; i++)
            {
                Transform? existing = row.Find("MCE_AncestorRail" + i);
                if (i >= needed) { if (existing == null) break; existing.gameObject.SetActive(false); continue; }
                Image rail = existing != null ? existing.GetComponent<Image>() : MakeRail(row, "MCE_AncestorRail" + i);
                rail.gameObject.SetActive(true);
                rail.color = GroupColorOf(chain[i].id);
                GroupRailShape.Stretch(rail.rectTransform, GroupRailShape.X(left, RailWidth, 1f, i), GroupRailShape.Width(RailWidth, i), 4f, i);
            }
        }

        private GroupSlot MakeGroupHeader(string id, string name, string description, Color color, int depth, List<string> chain)
        {
            GroupSlot slot = new GroupSlot { Id = id, Name = name, Color = color, Depth = Math.Max(1, depth), Chain = chain };
            float indent = (slot.Depth - 1) * 12f;
            GameObject row = new GameObject("MCE_Group_" + (id.Length > 0 ? id : "ungrouped"), typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(ListContent, false);
            ModConfigTreeLayoutPatch.StyleRowFrame(row, GroupHeaderHeight);
            slot.Row = row;

            Image background = row.GetComponent<Image>();
            background.color = ModConfigTreeLayoutPatch.TabBackground;
            background.raycastTarget = true;
            ModConfigPaperStyle.Apply(background, 8f);

            // One rail per level, ancestors first (the overview list draws them the same way).
            for (int level = 0; level < Math.Max(1, chain.Count); level++)
            {
                Image rail = MakeRail(row.transform, "MCE_GroupRail");
                rail.color = level < chain.Count - 1 ? GroupColorOf(chain[level]) : color;
                GroupRailShape.Stretch(rail.rectTransform, GroupRailShape.X(0f, RailWidth, 1f, level), GroupRailShape.Width(RailWidth, level), 3f, level);
            }

            GameObject arrow = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            RectTransform arrowRect = (RectTransform)arrow.transform;
            arrowRect.SetParent(row.transform, false);
            arrowRect.anchorMin = new Vector2(0f, 0.5f);
            arrowRect.anchorMax = new Vector2(0f, 0.5f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.sizeDelta = new Vector2(14f, 14f);
            arrowRect.anchoredPosition = new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + 4f + indent, 0f);
            Image arrowImage = arrow.GetComponent<Image>();
            arrowImage.sprite = ArrowIconSprite();
            arrowImage.color = color;
            arrowImage.raycastTarget = false;
            slot.Arrow = arrowRect;

            PeakText label = MenuAPI.CreateText("", "Label").ParentTo(row.transform);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + 16f + indent, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            TextMeshProUGUI text = label.TextMesh;
            StyleLabel(text, TextAlignmentOptions.MidlineLeft);
            text.enableAutoSizing = true;
            text.fontSizeMin = 13f;
            text.fontSizeMax = slot.Depth <= 1 ? 18f : 16f;
            text.fontSize = slot.Depth <= 1 ? 18f : 16f;
            text.richText = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.color = color;
            slot.Label = text;

            Button button = row.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => ToggleCollapse(id));

            slot.Tip = row.AddComponent<GroupTip>();
            slot.Tip.Title = name;
            slot.Tip.Description = description;
            GamepadSupport.MarkSelectable(row, Scroll, (RectTransform)row.transform);
            ModConfigLook.ApplyMain(row.transform);

            return slot;
        }

        private void ToggleCollapse(string id)
        {
            bool stale = SidebarGroupStore.NeedsReload;
            OverviewRepository? repository = SidebarGroupStore.Get();
            if (repository == null)
            {
                return;
            }

            if (stale)
            {
                // 数据在别处被改过：先按新数据重建，免得写回旧状态；这次点击照常生效
                RefreshGroups(reorder: true);
            }

            if (!repository.State.collapsedGroups.Remove(id))
            {
                repository.State.collapsedGroups.Add(id);
            }

            try
            {
                repository.Save();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[侧栏分组] 折叠状态保存失败：" + ex.Message);
            }

            ApplyGroupVisibility(null);
        }

        private bool Searching => Menu != null && (!string.IsNullOrWhiteSpace(Menu.search) || Menu.FilterValue != ModConfigTreeLayoutPatch.AllFilter);

        /// <summary>
        /// 折叠 = 把分组成员行 SetActive(false) 并记进 _collapsedHidden；筛选隐藏仍归 ModConfig / ApplyRowVisibility 管。
        /// filterVisible 非空 = 刚按筛选重算过显隐（此时 activeSelf 就是筛选结果），否则由 activeSelf + 已折叠记录推回筛选结果。
        /// 选中的行不折叠（概览跳转、手柄切换落到折叠组里时仍能看到它）。
        /// </summary>
        internal void ApplyGroupVisibility(HashSet<string>? filterVisible)
        {
            if (filterVisible != null)
            {
                _collapsedHidden.Clear();
            }

            OverviewRepository? repository = _groupSlots.Count > 0 ? SidebarGroupStore.Get() : null;
            bool searching = Searching;
            GameObject? selected = Menu != null && Menu.ModTabs != null && Menu.ModTabs.selectedButton != null ? Menu.ModTabs.selectedButton.gameObject : null;
            HashSet<string> shown = new HashSet<string>(StringComparer.Ordinal);
            bool changed = false;
            foreach (RowWidgets widgets in _rows)
            {
                GameObject row = widgets.Row;
                if (row == null)
                {
                    continue;
                }

                bool wanted = filterVisible != null ? filterVisible.Contains(widgets.Name) : row.activeSelf || _collapsedHidden.Contains(row);
                if (repository == null || IsPinnedRow(widgets))
                {
                    if (_collapsedHidden.Remove(row) && wanted && !row.activeSelf)
                    {
                        row.SetActive(true);
                        changed = true;
                    }

                    continue;
                }

                string group = _groupColors.ContainsKey(widgets.GroupId) ? widgets.GroupId : "";
                if (wanted)
                {
                    shown.Add(group);
                    // A subgroup's member keeps every ancestor header visible.
                    foreach (OverviewGroup ancestor in _groupTree.Ancestors(group)) shown.Add(ancestor.id);
                }

                bool hide = wanted && !searching && row != selected && (group.Length > 0 ? _groupTree.HiddenByCollapse(group, repository.State.collapsedGroups) : repository.State.collapsedGroups.Contains(group));
                if (hide)
                {
                    _collapsedHidden.Add(row);
                    if (row.activeSelf)
                    {
                        row.SetActive(false);
                        changed = true;
                    }
                }
                else if (_collapsedHidden.Remove(row) && wanted && !row.activeSelf)
                {
                    row.SetActive(true);
                    changed = true;
                }
            }

            foreach (GroupSlot slot in _groupSlots)
            {
                bool visible = shown.Contains(slot.Id) && (searching || repository == null || slot.Id.Length == 0 || !_groupTree.HeaderHidden(slot.Id, repository.State.collapsedGroups));
                if (slot.Row.activeSelf != visible)
                {
                    slot.Row.SetActive(visible);
                    changed = true;
                }

                bool collapsed = !searching && repository != null && repository.State.collapsedGroups.Contains(slot.Id);
                if (slot.Arrow != null)
                {
                    slot.Arrow.localRotation = Quaternion.Euler(0f, 0f, collapsed ? 90f : 0f);
                }
            }

            if (changed && ListContent != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(ListContent);
            }
        }

        /// <summary>别的路径（整表重新激活、跳转还原、换选中行）改了行显隐后，下一帧把折叠补回来。</summary>
        private void LateUpdate()
        {
            if (_built && _groupSlots.Count > 0)
            {
                ApplyGroupVisibility(null);
            }
        }

        internal void OpenGroupEditor()
        {
            if (Menu == null || Menu.MainPage == null)
            {
                return;
            }

            try
            {
                OverviewRepository repository = new OverviewRepository();

                CloseGroupEditor();
                List<OverviewMod> mods = new OverviewCatalogue().Scan(repository);
                _groupEditorHost = ModalHost(Menu.MainPage.transform);
                OverviewUi.ModalParent = _groupEditorHost;
                _groupEditorPage = Menu.MainPage.transform;
                _groupEditor = new OverviewGroupEditor(repository, mods,
                    message => ModConfigDescriptionPanel.Current?.ShowNotice(message),
                    () => { RefreshGroups(reorder: true); ModConfigDescriptionPanel.Current?.ShowNotice(Loc.T("Group draft saved. Apply the profile in the overview to synchronize the settings sidebar.", "分组方案草稿已保存；在概览应用方案后同步到设置侧栏。", "分組方案草稿已保存；在概覽套用方案後同步至設定側欄。")); });
                _groupEditor.Show(null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[侧栏分组] 打开分组编辑失败：" + ex);
                ModConfigDescriptionPanel.Current?.ShowNotice(Loc.Get("NOTICE_GROUPS_UNAVAILABLE"), true);
            }
        }
    }

    // ── 图标 ────────────────────────────────────────────────────────

    private static Sprite? _groupIcon;
    private static Sprite? _arrowIcon;

    /// <summary>分组管理图标：三行「小方块 + 横条」，像一张分好组的清单。</summary>
    private static Sprite GroupIconSprite()
    {
        if (_groupIcon != null)
        {
            return _groupIcon;
        }

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color clear = new Color(0f, 0f, 0f, 0f);
        float[] rows = { 15f, 32f, 49f };
        const float half = 5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                bool on = false;
                foreach (float cy in rows)
                {
                    if (Mathf.Abs(py - cy) > half)
                    {
                        continue;
                    }

                    on = (px >= 8f && px <= 18f) || (px >= 24f && px <= 56f);
                    break;
                }

                tex.SetPixel(x, y, on ? Color.white : clear);
            }
        }

        tex.Apply();
        _groupIcon = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _groupIcon;
    }

    /// <summary>折叠箭头：朝下的实心三角；折叠时整体转 90° 朝右。不依赖字体字形。</summary>
    private static Sprite ArrowIconSprite()
    {
        if (_arrowIcon != null)
        {
            return _arrowIcon;
        }

        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color clear = new Color(0f, 0f, 0f, 0f);
        Vector2 a = new Vector2(4f, 24f), b = new Vector2(28f, 24f), c = new Vector2(16f, 8f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, PointInTriangle(new Vector2(x + 0.5f, y + 0.5f), a, b, c) ? Color.white : clear);
            }
        }

        tex.Apply();
        _arrowIcon = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _arrowIcon;
    }
}

internal static partial class ModConfigSidebarWidgets
{
    /// <summary>
    /// 2.5.1：表头「跳转到快捷键设置页」按文字宽度收紧、贴右；文字随语言变，变了就重算。
    /// 宽度 = 方框 + 文字（按 13 号测）+ 左右留白，最宽不越过导出 / 置顶两列标题（<see cref="MinLeft"/>）。
    /// </summary>
    internal sealed class JumpWidth : MonoBehaviour
    {
        internal TMPro.TextMeshProUGUI? Label;
        internal RectTransform? Row;
        internal float MinLeft;
        private string? _text;
        private float _rowWidth = -1f;

        private void LateUpdate()
        {
            if (Label == null || Row == null) return;
            float rowWidth = Row.rect.width;
            if (_text == Label.text && Mathf.Approximately(_rowWidth, rowWidth)) return;
            _text = Label.text;
            _rowWidth = rowWidth;
            float textWidth = Label.GetPreferredValues(Label.text, 10000f, 100f).x;
            float wanted = CheckBoxSize + 8f + textWidth + 10f;
            float widest = Mathf.Max(60f, rowWidth - MinLeft - 4f);
            RectTransform rect = (RectTransform)transform;
            rect.offsetMin = new Vector2(-4f - Mathf.Min(wanted, widest), rect.offsetMin.y);
        }
    }
}
