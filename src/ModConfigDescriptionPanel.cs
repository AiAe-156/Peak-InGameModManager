#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using PEAKLib.ModConfig;
using PEAKLib.ModConfig.Components;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.Settings;

namespace ModConfigEnhance;

/// <summary>
/// 模组设置页右侧顶部的「选项说明」面板：显示鼠标悬停（或手柄焦点）所在选项的 cfg 描述，
/// 也就是配置文件里每项上方 <c>##</c> 后面那段文字。行序：模组名 → 版本 / 选项名 / 默认 / 范围 / 可选 → 描述正文
/// （1.0.26 起元信息行提到正文上面，长说明不再把它挤出视野）。
///
/// ModConfig 把 ConfigEntry 原样带进了每个单元格（输入控件 Setup 时登记的 Setting 实现了
/// IBepInExProperty，ConfigBase 就是 BepInEx 的 ConfigEntryBase），所以七种类型都走同一条路反查，
/// 不用逐类型打补丁，也不改它任何数据。
///
/// 版面：固定高；宽度与底图都从**实际生成的第一个单元格**实时量取（2.16.0）—— 左右边与单元格对齐，
/// sprite / 材质 / 颜色照抄它的底图，圆角与褶皱质感一致，昼夜变色也跟着它走。预制体的宽度不可信（2.16.0 首版
/// 拿它算出来超出屏幕），单元格生成前先用整宽纯色顶着。
/// 描述装不下时可滚轮滚动，右侧一条细滚动条，底边用 RectMask2D 的软裁剪做渐隐。
/// 装了 TimeTheme 时底色随昼夜；没装就保持页签同款棕色。
/// KeyCode 型选项不列「可选」：一百多个键名没意义，屏幕上是按键图标。
/// 三个文本组件都关掉 TMP 的转义解析（1.0.41）：面板显示的是 cfg 描述、manifest、语言文件、Windows 路径这类原始字符串，
/// 不能让 TMP 把其中的 \n \r \t \v \uXXXX 当控制字符——`…\readme.md` 里的 \r 会被当成回车（x 归零、不换行），
/// 后半截字从行首回打在前半截上，就是 1.0.31–1.0.40 追了十个版本的「末行重叠」。
/// </summary>
internal static class ModConfigDescriptionPanel
{
    /// <summary>Compact explanation area; long descriptions retain their own wrapping and scrolling.</summary>
    internal const float PanelHeight = 144f;
    private const float PanelTop = 8f;
    private const float Padding = 8f;
    private const float PaddingLeft = 14f;
    private const float TitleFontSize = 20f;
    private const float DescriptionFontSize = 16f;
    private const float MetaFontSize = 13f;
    private const int FadeSoftness = 10;
    private const float ScrollbarWidth = 6f;

    private static readonly Color MetaColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color WarningColor = new Color(1f, 0.55f, 0.2f, 1f);

    /// <summary>通告富文本片段的角色：标题=白天白/夜里亮紫，正文=白天米白/夜里灰紫，警告=橙。</summary>
    internal enum NoticeRole { Header, Body, Warning }

    /// <summary>
    /// 把「标题\n正文」文本拆成上色片段：首行整体标题色；正文里 `…` 或 “…” 包住的段用标题色强调
    /// （反引号本身不显示，弯引号保留）。实现：在 “ 前、” 后各补一个 `，然后统一按反引号交替切分。
    /// </summary>
    internal static List<(string Text, NoticeRole Role)> SplitNotice(string text)
    {
        List<(string, NoticeRole)> parts = new List<(string, NoticeRole)>();
        int newline = text.IndexOf('\n');
        if (newline < 0)
        {
            parts.Add((text, NoticeRole.Header));
            return parts;
        }

        parts.Add((text.Substring(0, newline), NoticeRole.Header));
        string body = ("\n" + text.Substring(newline + 1)).Replace("“", "`“").Replace("”", "”`");
        int i = 0;
        bool emphasis = false;
        while (i < body.Length)
        {
            int tick = body.IndexOf('`', i);
            string segment = tick < 0 ? body.Substring(i) : body.Substring(i, tick - i);
            if (segment.Length > 0)
            {
                parts.Add((segment, emphasis ? NoticeRole.Header : NoticeRole.Body));
            }

            if (tick < 0)
            {
                break;
            }

            emphasis = !emphasis;
            i = tick + 1;
        }

        return parts;
    }

    internal static void Attach(ModSettingsMenu menu, RectTransform scrollArea)
    {
        // 选项列表让出面板的高度
        scrollArea.offsetMax = new Vector2(scrollArea.offsetMax.x, -(PanelTop + PanelHeight + Padding));

        GameObject root = new GameObject("MCE_DescPanel", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.SetParent(menu.transform, false);
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        // 先整宽纯色；第一个单元格出现后由 Panel.LateUpdate 对齐它的左右边、照抄它的底图
        rootRect.offsetMin = new Vector2(scrollArea.offsetMin.x, -(PanelTop + PanelHeight));
        rootRect.offsetMax = new Vector2(scrollArea.offsetMax.x, -PanelTop);
        Image background = root.GetComponent<Image>();
        background.color = ModConfigTreeLayoutPatch.TabBackground;
        background.raycastTarget = true; // 滚轮事件要落在 ScrollRect 自己的对象上
        ModConfigSidebarWidgets.ApplyRounded(background, 10f);

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.SetParent(rootRect, false);
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(PaddingLeft, Padding);
        viewportRect.offsetMax = new Vector2(-(Padding + ScrollbarWidth + 6f), -Padding);

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.SetParent(viewportRect, false);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup group = content.GetComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(0, 0, 0, 0);
        group.spacing = 1f;
        group.childAlignment = TextAnchor.UpperLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 自上而下：模组名 → 元信息（版本 / 选项名 / 默认值…）→ 说明正文。说明可能很长，元信息放它上面才不会被挤出视野
        TextMeshProUGUI title = MakeText(contentRect, "Title", TitleFontSize, MetaColor);
        TextMeshProUGUI meta = MakeText(contentRect, "Meta", MetaFontSize, MetaColor);
        TextMeshProUGUI description = MakeText(contentRect, "Description", DescriptionFontSize, Color.white);

        ScrollRect scroll = root.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.inertia = false;

        // 右侧细滚动条（装得下时自动隐藏）
        Scrollbar bar = ModConfigSidebarWidgets.MakeScrollbar(rootRect, scroll, 0f, ScrollbarWidth, Padding, Padding);
        RectTransform barRect = (RectTransform)bar.transform;
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(1f, 1f);
        barRect.offsetMin = new Vector2(-(Padding + ScrollbarWidth), Padding);
        barRect.offsetMax = new Vector2(-Padding, -Padding);

        root.AddComponent<ModConfigSidebarWidgets.ThemeAsOption>(); // 白字夜里变紫

        Panel panel = root.AddComponent<Panel>();
        panel.Menu = menu;
        panel.Scroll = scroll;
        panel.Mask = viewport.GetComponent<RectMask2D>();
        panel.Viewport = viewportRect;
        panel.Content = contentRect;
        panel.Title = title;
        panel.Description = description;
        panel.Meta = meta;
        panel.Background = background;
        Current = panel;
    }

    /// <summary>最近一次构建的面板（主菜单与 ESC 各一份，谁在场谁是它）；导出功能借它显示结果。</summary>
    internal static Panel? Current { get; private set; }

    private static TextMeshProUGUI MakeText(RectTransform parent, string name, float fontSize, Color color)
    {
        PeakText peak = MenuAPI.CreateText("", name).ParentTo(parent);
        TextMeshProUGUI text = peak.TextMesh;
        text.enableAutoSizing = false;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Normal;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = false;
        text.parseCtrlCharacters = false; // 原始字符串按字面显示：换行早已是真换行，路径里的 \r \n \t 不许被当转义
        text.color = color;
        text.raycastTarget = false;
        text.text = "";
        Overview.MceRegularFont.ApplyDefault(text);
        Overview.MceTextWeight.Apply(text);
        return text;
    }

    internal sealed class Panel : MonoBehaviour
    {
        internal ModSettingsMenu? Menu;
        internal ScrollRect? Scroll;
        internal RectMask2D? Mask;
        internal RectTransform? Viewport;
        internal RectTransform? Content;
        internal TextMeshProUGUI? Title;
        internal TextMeshProUGUI? Description;
        internal TextMeshProUGUI? Meta;
        internal Image? Background;

        private SettingsUICell? _shownCell;
        private SettingsUICell? _referenceCell;
        private Image? _referenceImage;
        private bool _fitLogged;
        private bool _holdNotice;
        private SettingsUICell? _noticeHovered;
        private SettingsUICell? _noticeFocused;
        private ModdedTABSButton? _overviewMod;
        private ModdedTABSButton? _overviewSection;
        private bool _overviewInitialized;
        private int _translationRevision = -1;

        /// <summary>FitToCells 每帧量单元格四角时复用，免得每帧分配。</summary>
        private static readonly Vector3[] Corners = new Vector3[4];

        private void OnEnable()
        {
            Current = this;
            _overviewInitialized = false;
            _shownCell = null;
        }

        private void ShowOverview(ModdedTABSButton? mod, ModdedTABSButton? section)
        {
            if (mod == null)
            {
                ShowNotice(Loc.T("Select a mod to view its information.", "选择左侧模组查看信息。", "選擇左側模組查看資訊。"));
                return;
            }

            string name = mod.category ?? "";
            string version = "";
            string location = "";
            string guid = "";
            if (Menu?.settings != null)
            {
                foreach (IBepInExProperty item in Menu.settings)
                {
                    if (!string.Equals(item.GetCategory(), name, StringComparison.Ordinal)) continue;
                    version = item.Pluginfo?.Metadata?.Version?.ToString() ?? "";
                    location = item.Pluginfo?.Location ?? "";
                    guid = item.Pluginfo?.Metadata?.GUID ?? "";
                    break;
                }
            }

            string category = section?.category ?? Loc.T("All", "全部", "全部");
            string categoryText = Loc.T("Category: ", "当前子分类：", "目前子分類：") + category;
            string rawDescription = ReadModDescription(location, showFallback: false);
            string description = Overview.OverviewLanguageBridge.GetDescription(guid, rawDescription);
            if (description.Length == 0) description = ReadModDescription(location);
            ShowNotice(description);
            // 介绍独占一个文本组件，和导出的原文完全一致，才能命中 XUnity 的精确词典。
            SetTexts(Overview.OverviewLanguageBridge.GetSettingName(name), description, JoinMeta(version.Length == 0 ? "" : "v" + version, categoryText));

            StyleTitle();
        }

        [Serializable]
        private sealed class ManifestInfo
        {
            public string description = "";
        }

        internal static string ReadModDescription(string pluginLocation, bool showFallback = true)
        {
            try
            {
                if (!string.IsNullOrEmpty(pluginLocation))
                {
                    string root = Path.GetFullPath(BepInEx.Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    DirectoryInfo? directory = Directory.GetParent(Path.GetFullPath(pluginLocation));
                    // 从 DLL 所在目录向上找最近的包清单，不越出 plugins，也不借用其它模组的说明。
                    while (directory != null && directory.FullName.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        string manifest = Path.Combine(directory.FullName, "manifest.json");
                        if (File.Exists(manifest))
                        {
                            ManifestInfo? info = JsonUtility.FromJson<ManifestInfo>(File.ReadAllText(manifest, Encoding.UTF8));
                            if (!string.IsNullOrWhiteSpace(info?.description)) return info!.description.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                            break;
                        }
                        directory = directory.Parent;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[说明面板] 读取模组 manifest.json 失败: {ex.Message}");
            }

            return showFallback ? Loc.T("No description provided in manifest.json.", "未提供 manifest.json 模组说明。", "未提供 manifest.json 模組說明。") : "";
        }

        /// <summary>
        /// 按文字角色定色：标题（模组名）与元信息（版本/选项名/默认值）用显眼色——白天白、夜里亮紫；
        /// 正文描述用不显眼色——白天灰、夜里灰紫；警告保留橙色。昼夜同一套逻辑。
        /// </summary>
        private void StyleTitle()
        {
            bool dark = ModConfigLook.IsDark;
            Color prominent = dark ? ModConfigLook.Prominent : Color.white;
            Color muted = dark ? Color.Lerp(prominent, new Color(0.60f, 0.60f, 0.60f, 1f), 0.65f) : MetaColor;
            if (Title != null && Title.color != prominent) Title.color = prominent;
            if (Meta != null && Meta.color != prominent) Meta.color = prominent;
            Color description = _warningShown ? WarningColor : muted;
            if (Description != null && Description.color != description) Description.color = description;
            // 富文本通告的 <color> 标签是构建时写死的，昼夜翻转后按新主题重染
            if (_noticeParts != null && dark != _noticeDark && Description != null)
            {
                _noticeDark = dark;
                Description.text = RenderNotice(_noticeParts);
            }
        }

        /// <summary>导出等功能借面板显示一条结果，下次悬停到别的选项时自然被替换。</summary>
        internal void ShowNotice(string text, bool warning = false)
        {
            _noticeParts = null;
            if (Description != null)
            {
                Description.richText = false; // 外部文本按字面渲染，不解析标签
            }

            StyleTitle();
            _shownCell = null;
            _noticeHovered = FindHoveredCell();
            _noticeFocused = FindFocusedCell();
            _holdNotice = true;
            SetNoticeColor(warning);
            SetTexts("", text, "");
        }

        private List<(string Text, NoticeRole Role)>? _noticeParts;
        private bool _noticeDark;

        /// <summary>结构化通告：按片段角色分别上色（标题白 / 正文米白 / 警告橙），昼夜随主题重染。</summary>
        internal void ShowNotice(List<(string Text, NoticeRole Role)> parts)
        {
            _noticeParts = parts;
            _noticeDark = ModConfigLook.IsDark;
            StyleTitle();
            _shownCell = null;
            _noticeHovered = FindHoveredCell();
            _noticeFocused = FindFocusedCell();
            _holdNotice = true;
            _warningShown = false;
            SetTexts("", RenderNotice(parts), "", rich: true);
        }

        private static Color NoticeColor(NoticeRole role)
        {
            bool dark = ModConfigLook.IsDark;
            Color prominent = dark ? ModConfigLook.Prominent : Color.white;
            return role switch
            {
                NoticeRole.Header => prominent,
                NoticeRole.Warning => WarningColor,
                _ => dark ? Color.Lerp(prominent, new Color(0.60f, 0.60f, 0.60f, 1f), 0.65f) : ModConfigSidebarWidgets.Cream,
            };
        }

        private static string RenderNotice(List<(string Text, NoticeRole Role)> parts)
        {
            StringBuilder sb = new StringBuilder();
            foreach ((string text, NoticeRole role) in parts)
            {
                // 文本里 < > 转义掉：我们的内容不含 TMP 标签，纯文本字符按字面显示（文件系统也不允许 < >）
                string safe = text.Replace("<", "﹤").Replace(">", "﹥");
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(NoticeColor(role))).Append('>').Append(safe).Append("</color>");
            }

            return sb.ToString();
        }

        private bool _warningShown;

        /// <summary>警告保留橙色，其余文字按正文/辅助信息的角色统一恢复。</summary>
        private void SetNoticeColor(bool warning)
        {
            _warningShown = warning;
            StyleTitle();
        }

        private void Update()
        {
            if (Menu == null || Description == null)
            {
                return;
            }

            ModdedTABSButton? selectedMod = Menu.ModTabs != null ? Menu.ModTabs.selectedButton : null;
            ModdedTABSButton? selectedSection = null;
            if (Menu.SectionTabController != null)
            {
                foreach (GameObject row in Menu.SectionTabController.Tabs)
                {
                    ModdedTABSButton? button = row != null ? row.GetComponent<ModdedTABSButton>() : null;
                    if (button != null && button.Selected) { selectedSection = button; break; }
                }
            }

            if (_translationRevision != Loc.TranslationRevision)
            {
                _translationRevision = Loc.TranslationRevision;
                _overviewInitialized = false; _shownCell = null;
            }
            if (!_overviewInitialized || selectedMod != _overviewMod || selectedSection != _overviewSection)
            {
                _overviewInitialized = true;
                _overviewMod = selectedMod;
                _overviewSection = selectedSection;
                ShowOverview(selectedMod, selectedSection);
            }

            SettingsUICell? mouseCell = FindHoveredCell();
            SettingsUICell? focusedCell = FindFocusedCell();
            if (_holdNotice)
            {
                // 留下完整加载结果供阅读；旧手柄焦点不能在下一帧把它顶掉。
                bool newHover = mouseCell != null && mouseCell != _noticeHovered;
                bool newFocus = focusedCell != null && focusedCell != _noticeFocused;
                _noticeHovered = mouseCell;
                _noticeFocused = focusedCell;
                if (!newHover && !newFocus)
                {
                    return;
                }

                _holdNotice = false;
            }

            SettingsUICell? hovered = mouseCell ?? focusedCell;
            if (hovered == null || hovered == _shownCell)
            {
                return;
            }

            _shownCell = hovered;
            _noticeParts = null; // 离开通告回到普通说明：后续 SetTexts 按字面渲染

            StyleTitle();
            SetNoticeColor(false);
            IBepInExProperty? property = ResolveProperty(hovered);
            if (property == null)
            {
                SetTexts("", "", "");
                return;
            }

            string optionName = hovered.m_text != null ? hovered.m_text.text : "";
            SetTexts(FormatHeader(property), FormatDescription(property.ConfigBase),
                JoinMeta(FormatVersion(property), optionName, FormatMeta(property.ConfigBase)));
        }

        private void LateUpdate()
        {
            FitToCells();
            StyleTitle();
            if (Mask == null || Viewport == null || Content == null)
            {
                return;
            }

            // 只有装不下时才渐隐；装得下的短描述干净显示
            bool overflow = Content.rect.height > Viewport.rect.height + 0.5f;
            Vector2Int softness = overflow ? new Vector2Int(0, FadeSoftness) : Vector2Int.zero;
            if (Mask.softness != softness)
            {
                Mask.softness = softness;
            }
        }

        /// <summary>
        /// 对齐第一个活着的单元格：左右边取它在本面板父级坐标系里的位置；底图 sprite / 材质 / 类型照抄它最大的那张
        /// 背景 Image，颜色每帧同步（TimeTheme 会改它的颜色，跟着走就自动昼夜）。单元格换了才重新量。
        /// </summary>
        private void FitToCells()
        {
            if (Menu == null || Background == null || transform.parent is not RectTransform parent)
            {
                return;
            }

            SettingsUICell? cell = null;
            foreach (SettingsUICell candidate in Menu.m_spawnedCells)
            {
                if (candidate != null && candidate.gameObject.activeInHierarchy)
                {
                    cell = candidate;
                    break;
                }
            }

            if (cell == null)
            {
                return;
            }

            if (cell != _referenceCell)
            {
                _referenceCell = cell;
                _referenceImage = FindBackdrop(cell);
                if (_referenceImage != null && _referenceImage.sprite != null)
                {
                    ModConfigPaperStyle.CopyFrom(_referenceImage, Background);
                }

                // 单元格刚生成那一帧布局还没排（Canvas 在 LateUpdate 之后才重排），世界坐标还是预制体的旧位置；
                // 换了参考单元格就先把它所在的列表布局强制算一遍，量出来的才是最终位置，不会闪一帧
                if (Menu != null && Menu.Content is RectTransform content)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                }
            }

            try
            {
                Vector3[] corners = Corners;
                ((RectTransform)cell.transform).GetWorldCorners(corners);
                float left = parent.InverseTransformPoint(corners[0]).x - parent.rect.xMin;
                float right = parent.InverseTransformPoint(corners[2]).x - parent.rect.xMax;
                RectTransform rect = (RectTransform)transform;
                if (Mathf.Abs(rect.offsetMin.x - left) > 0.5f || Mathf.Abs(rect.offsetMax.x - right) > 0.5f)
                {
                    rect.offsetMin = new Vector2(left, rect.offsetMin.y);
                    rect.offsetMax = new Vector2(right, rect.offsetMax.y);
                }

                if (!_fitLogged)
                {
                    _fitLogged = true;
                    Plugin.Log.LogDebug($"[面板布局] 说明面板已对齐单元格：left {left:0.#} right {right:0.#}，底图 {(_referenceImage != null ? _referenceImage.name + "/" + (_referenceImage.sprite != null ? _referenceImage.sprite.name : "无 sprite") : "未找到")}");
                }
            }
            catch (Exception ex)
            {
                if (!_fitLogged)
                {
                    _fitLogged = true;
                    Plugin.Log.LogWarning($"[面板布局] 说明面板对齐单元格失败: {ex.Message}");
                }
            }

            if (_referenceImage != null && Background.color != _referenceImage.color)
            {
                Background.color = _referenceImage.color;
            }
        }

        /// <summary>单元格自己的底图：不在选项内容容器里、面积最大的那张 Image。</summary>
        private static Image? FindBackdrop(SettingsUICell cell)
        {
            Image? best = null;
            float bestArea = 0f;
            foreach (Image image in cell.GetComponentsInChildren<Image>(true))
            {
                if (cell.m_settingsContentParent != null && image.transform.IsChildOf(cell.m_settingsContentParent))
                {
                    continue;
                }

                Rect r = image.rectTransform.rect;
                float area = r.width * r.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = image;
                }
            }

            return best;
        }

        private void SetTexts(string title, string description, string meta, bool rich = false)
        {
            if (Title != null)
            {
                Title.text = title;
                Title.gameObject.SetActive(title.Length > 0);
            }

            if (Description != null)
            {
                Description.richText = rich;
                Description.text = description;
                Description.gameObject.SetActive(description.Length > 0);
            }

            if (Meta != null)
            {
                Meta.text = meta;
                Meta.gameObject.SetActive(meta.Length > 0);
            }

            if (Scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                Scroll.verticalNormalizedPosition = 1f;
            }
        }

        private SettingsUICell? FindHoveredCell()
        {
            if (Menu == null)
            {
                return null;
            }

            Camera? eventCamera = null;
            Canvas? canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                eventCamera = canvas.worldCamera;
            }

            Vector2 mouse = Input.mousePosition;
            foreach (SettingsUICell cell in Menu.m_spawnedCells)
            {
                if (cell == null || !cell.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)cell.transform, mouse, eventCamera))
                {
                    return cell;
                }
            }

            return null;
        }

        private static SettingsUICell? FindFocusedCell()
        {
            EventSystem? system = EventSystem.current;
            GameObject? selected = system != null ? system.currentSelectedGameObject : null;
            return selected != null ? selected.GetComponentInParent<SettingsUICell>() : null;
        }

        private static IBepInExProperty? ResolveProperty(SettingsUICell cell)
        {
            try
            {
                if (cell.m_settingsContentParent == null)
                {
                    return null;
                }

                SettingInputUICell? input = cell.m_settingsContentParent.GetComponentInChildren<SettingInputUICell>(true);
                Setting? setting = input != null ? input._listeningSetting : null;
                return setting as IBepInExProperty;
            }
            catch
            {
                return null;
            }
        }
    }

    // ── 文案 ────────────────────────────────────────────────────────

    /// <summary>标题行：模组显示名（GetCategory = 列表里的行名）。</summary>
    private static string FormatHeader(IBepInExProperty property)
    {
        try
        {
            return Overview.OverviewLanguageBridge.GetSettingName(property.GetCategory() ?? "");
        }
        catch
        {
            return "";
        }
    }

    /// <summary>元信息行的版本号段（v1.2.3）；拿不到就空。</summary>
    private static string FormatVersion(IBepInExProperty property)
    {
        try
        {
            string? version = property.Pluginfo?.Metadata?.Version?.ToString();
            return string.IsNullOrEmpty(version) ? "" : "v" + version;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>元信息行各段以四个空格相连，空段跳过。</summary>
    private static string JoinMeta(params string?[] parts)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string? part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            Separator(sb).Append(part);
        }

        return sb.ToString();
    }

    internal static string FormatDescription(ConfigEntryBase entry)
    {
        string? text = entry.Description?.Description;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        return Overview.OverviewLanguageBridge.Translate(text!.Replace("\r\n", "\n").Replace('\r', '\n').Trim());
    }

    internal static string FormatMeta(ConfigEntryBase entry)
    {
        StringBuilder sb = new StringBuilder();
        string? defaultText = FormatValue(entry.DefaultValue, entry.SettingType);
        if (defaultText != null)
        {
            sb.Append(Loc.Get("META_DEFAULT")).Append(": ").Append(defaultText);
        }

        AcceptableValueBase? acceptable = entry.Description?.AcceptableValues;
        if (acceptable != null)
        {
            Type type = acceptable.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AcceptableValueRange<>))
            {
                object? min = type.GetProperty("MinValue")?.GetValue(acceptable);
                object? max = type.GetProperty("MaxValue")?.GetValue(acceptable);
                Separator(sb).Append(Loc.Get("META_RANGE")).Append(": ")
                    .Append(FormatValue(min, entry.SettingType)).Append(" ~ ").Append(FormatValue(max, entry.SettingType));
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AcceptableValueList<>))
            {
                if (type.GetProperty("AcceptableValues")?.GetValue(acceptable) is Array values && values.Length > 0)
                {
                    List<string> items = new List<string>();
                    foreach (object? value in values)
                    {
                        items.Add(FormatValue(value, entry.SettingType) ?? "");
                    }

                    Separator(sb).Append(Loc.Get("META_OPTIONS")).Append(": ").Append(string.Join(" / ", items));
                }
            }
        }
        else if (entry.SettingType.IsEnum && entry.SettingType != typeof(KeyCode))
        {
            Separator(sb).Append(Loc.Get("META_OPTIONS")).Append(": ").Append(string.Join(" / ", Enum.GetNames(entry.SettingType)));
        }

        return sb.ToString();
    }

    private static StringBuilder Separator(StringBuilder sb)
    {
        return sb.Length > 0 ? sb.Append("    ") : sb;
    }

    private static string? FormatValue(object? value, Type settingType)
    {
        if (value == null)
        {
            return null;
        }

        if (value is bool flag)
        {
            return flag ? Loc.Get("META_ON") : Loc.Get("META_OFF");
        }

        if (value is float f)
        {
            return f.ToString("0.###");
        }

        if (value is double d)
        {
            return d.ToString("0.###");
        }

        string text = value.ToString() ?? "";
        return text.Length == 0 && settingType == typeof(string) ? Loc.Get("META_EMPTY") : text;
    }
}
