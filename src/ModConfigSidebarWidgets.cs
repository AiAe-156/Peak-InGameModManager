#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PEAKLib.ModConfig;
using PEAKLib.ModConfig.Components;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance;

/// <summary>
/// 树形侧栏上自己画的小部件（2.16.0）：
///   · 复选框：深色圆角方块 + 米白描边，未勾描边半透明、勾上描边点亮并压一个天蓝 ✓（两根旋转的细条，不依赖字体）；
///     ✓ 的蓝（白天 #C2E4FF / 夜间 #238CE0）不在 TimeTheme 表里，由 <see cref="ThemeTint"/> 自己按昼夜切；
///   · 置顶的行左缘一条同色竖条；
///   · 列表右侧的竖向滚动条：细、圆角，深棕轨道 + 米白滑块，内容装得下时自动隐藏；
///   · 圆角来自运行时画的一张 32×32 圆角 sprite（九宫格），不依赖游戏资源；
///   · 列表上方的工具行：导出翻译 / 查漏翻 / 「仅导出已选」开关；
///   · 列表首行的表头：两列复选框的标题（导出 / 置顶）+ 右侧的「只显示快捷键选项」开关；
///   · 每个模组行左侧的两列复选框：导出列（只在「仅导出已选」勾上时显示）、置顶列（勾上的行排到列表最上面）。
/// 颜色全部取自 <see cref="ModConfigLook"/> 登记的主题格（底色、米白），主题切换时由
/// <see cref="ModConfigLook.ApplyMain"/> 扫一遍换色；不在登记表里的颜色（✓、行名金色、「默认」钮）由 <see cref="ThemeTint"/> 自己切。
/// 置顶集合、不导出集合、「仅导出已选」开关存在本模组 cfg 里（带 Hidden 标签，ModConfig 界面不显示），
/// 键是 ModConfig 给模组行的名字（它的 FixNaming 结果），与导出文件里的模组名一致。
/// </summary>
internal static partial class ModConfigSidebarWidgets
{
    internal const float CheckBoxSize = 22f;
    internal const float ColumnWidth = 30f;
    internal const float ToolbarHeight = 40f;
    internal const float ToolbarRowGap = 6f;
    internal const int ToolbarRows = 3;

    /// <summary>工具行 + 表头这一块的文字统一 13 号。</summary>
    private const float HeaderFontSize = 13f;

    /// <summary>
    /// 悬停/手柄聚焦时在说明面板显示词条注释：Key 是 MCE_TIP_* 词条，首行标题色、正文米白、
    /// `…` 包住的部分用标题色强调。离开后不清，等悬停到别处自然被替换（与通告同语义）。
    /// </summary>
    internal sealed class TipTarget : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        internal string Key = "";

        public void OnPointerEnter(PointerEventData eventData)
        {
            Show();
        }

        public void OnSelect(BaseEventData eventData)
        {
            Show();
        }

        private void Show()
        {
            if (Key.Length == 0)
            {
                return;
            }

            ModConfigDescriptionPanel.Panel? panel = ModConfigDescriptionPanel.Current;
            if (panel == null)
            {
                return;
            }

            panel.ShowNotice(ModConfigDescriptionPanel.SplitNotice(Loc.Get(Key)));
        }
    }



    /// <summary>复选框点击区比方块大一圈（28），在 30 宽的列里居中要往右挪 1。</summary>
    private const float ColumnInset = (ColumnWidth - (CheckBoxSize + 6f)) / 2f;
    private const float ToolbarGap = 6f;

    /// <summary>米白（#DFDAC2）：当前主题的同一格见 <see cref="ModConfigLook.Cream"/>，建元素时用那个。</summary>
    internal static Color Cream => ModConfigLook.Cream;
    private const float UncheckedBorderAlpha = 0.45f;

    /// <summary>✓ 的颜色：蓝色主题低饱和天蓝，紫色主题深一档的蓝（用户定）。置顶竖线另用 <see cref="ModConfigLook.PinAccent"/>。</summary>
    internal static readonly Color MarkDay = new Color(0.7607843f, 0.8941177f, 1f, 1f);
    internal static readonly Color MarkNight = new Color(0.1372549f, 0.5490196f, 0.8784314f, 1f);

    private const int SpriteSize = 32;
    private const int SpriteRadius = 10;
    private const int SpriteBorder = 12;

    private static string PinKey => Loc.Index("PIN"); // 1.1.3: was the pre-split "MCE_" index → showed "LOC: MCE_PIN"
    private static string ExportColumnKey => Loc.Index("EXPORT_COL");
    private static string ExportSelectedKey => Loc.Index("EXPORT_SELECTED");
    private static string JumpHotkeysKey => Loc.Index("JUMP_HOTKEYS");

    private static Sprite? _roundedSprite;

    // ── 持久化 ──────────────────────────────────────────────────────

    internal static readonly HashSet<string> Pinned = new HashSet<string>(StringComparer.Ordinal);
    internal static readonly HashSet<string> ExcludedFromExport = new HashSet<string>(StringComparer.Ordinal);

    private const char Separator = '|';

    internal static bool ExportSelectedOnly
    {
        get => Plugin.ExportSelectedOnly != null && Plugin.ExportSelectedOnly.Value;
        set
        {
            if (Plugin.ExportSelectedOnly != null)
            {
                Plugin.ExportSelectedOnly.Value = value;
            }
        }
    }

    /// <summary>导出列当前是否可见：开关记得的状态 × 完整工具行在场（隐藏本地化按钮时连列一起收，此时它唯一的开关也看不到）。</summary>
    internal static bool ExportColumnVisible =>
        ExportSelectedOnly && !Plugin.HideLocalizationButtons.Value;

    internal static void LoadState()
    {
        Pinned.Clear();
        ExcludedFromExport.Clear();
        Split(Plugin.PinnedMods?.Value, Pinned);
        Split(Plugin.ExportExcludedMods?.Value, ExcludedFromExport);
    }

    private static void Split(string? raw, HashSet<string> into)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        foreach (string part in raw!.Split(Separator))
        {
            string name = part.Trim();
            if (name.Length > 0)
            {
                into.Add(name);
            }
        }
    }

    private static void SavePinned()
    {
        if (Plugin.PinnedMods != null)
        {
            Plugin.PinnedMods.Value = string.Join(Separator.ToString(), Pinned);
        }
    }

    private static void SaveExcluded()
    {
        if (Plugin.ExportExcludedMods != null)
        {
            Plugin.ExportExcludedMods.Value = string.Join(Separator.ToString(), ExcludedFromExport);
        }
    }

    /// <summary>导出时是否包含这个模组（按 ModConfig 的模组行名字）。</summary>
    internal static bool ShouldExport(string modName)
    {
        return !ExportSelectedOnly || !ExcludedFromExport.Contains(modName);
    }

    // ── 词条 ────────────────────────────────────────────────────────

    /// <summary>每次建页都重注册（语言表整表重载后词条会丢；MenuAPI 对已有键只是覆盖同值）。</summary>
    private static void EnsureTerms() => Loc.EnsureRegistered();

    // ── 圆角底图 ────────────────────────────────────────────────────

    /// <summary>
    /// 运行时画一张 32×32、圆角半径 10 的白色圆角矩形（边缘一像素抗锯齿），九宫格边距 12。
    /// 之前借搜索框 sprite 的路子拿到的是 null（输入框的 targetGraphic 不是那张底图），改为自绘，稳定且任意尺寸可用。
    /// </summary>
    internal static Sprite RoundedSprite
    {
        get
        {
            if (_roundedSprite == null)
            {
                _roundedSprite = BuildRoundedSprite();
            }

            return _roundedSprite;
        }
    }

    private static Sprite BuildRoundedSprite()
    {
        Texture2D texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "MCE_Rounded",
        };
        Color32[] pixels = new Color32[SpriteSize * SpriteSize];
        float r = SpriteRadius;
        for (int y = 0; y < SpriteSize; y++)
        {
            for (int x = 0; x < SpriteSize; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                // 到圆角矩形边界的有符号距离：角落按圆算，其余按直边算
                float cx = Mathf.Clamp(px, r, SpriteSize - r);
                float cy = Mathf.Clamp(py, r, SpriteSize - r);
                float d = r - Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                float alpha = Mathf.Clamp01(d + 0.5f);
                pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        UnityEngine.Object.DontDestroyOnLoad(texture);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(SpriteBorder, SpriteBorder, SpriteBorder, SpriteBorder));
        sprite.name = "MCE_Rounded";
        UnityEngine.Object.DontDestroyOnLoad(sprite);
        return sprite;
    }

    /// <summary>给 Image 套上圆角 sprite，圆角在屏幕上约 cornerPixels 像素（九宫格边距 12 ÷ 倍率）。</summary>
    internal static void ApplyRounded(Image image, float cornerPixels)
    {
        image.sprite = RoundedSprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = SpriteBorder / Mathf.Max(1f, cornerPixels);
    }

    /// <summary>paper=true 用原版纸面（带抖动），false 用自绘圆角（滚动条这种细条纸面糊不出形状）。</summary>
    private static Image MakeBox(string name, Transform parent, Color color, float cornerPixels, bool paper = true)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (paper)
        {
            ModConfigPaperStyle.Apply(image, cornerPixels);
        }
        else
        {
            ApplyRounded(image, cornerPixels);
        }

        return image;
    }

    /// <summary>不在主题登记表里的颜色自己按主题切（✓、行名金色、「默认」钮）。IsDark 每帧有缓存，Update 很便宜。</summary>
    internal sealed class ThemeTint : MonoBehaviour
    {
        internal Graphic? Target;
        internal Color Day;
        internal Color Night;
        private bool? _dark;

        private void OnEnable()
        {
            _dark = null;
        }

        private void Update()
        {
            bool dark = ModConfigLook.IsDark;
            if (_dark == dark || Target == null)
            {
                return;
            }

            _dark = dark;
            Color target = dark ? Night : Day;
            Target.color = new Color(target.r, target.g, target.b, Target.color.a);
        }
    }

    internal static ThemeTint Tint(Graphic graphic, Color day, Color night)
    {
        ThemeTint tint = graphic.gameObject.AddComponent<ThemeTint>();
        tint.Target = graphic;
        tint.Day = day;
        tint.Night = night;
        graphic.color = new Color(day.r, day.g, day.b, graphic.color.a);
        return tint;
    }

    /// <summary>
    /// 自身模组行的行名色：白天低饱和橙（用户定的 #EFC95F）、夜间暖金（#C9A55B，压得住深底又与夜间紫字同区）；
    /// 选中态换深琥珀（#8A6207，白天白底、夜里紫底都压得住，不回黑）。
    /// <c>ModdedTABSButton.Update</c> 每帧把文字往黑 / 白 Lerp，所以本组件必须在 LateUpdate 里每帧断言颜色。
    /// 分区行也挂它（仅当自身模组被选中时，见 UpdateSectionTabsPostfix）。
    /// </summary>
    internal sealed class SelfRowTint : MonoBehaviour
    {
        internal static readonly Color Day = new Color(0.9372549f, 0.7882353f, 0.372549f);      // #EFC95F
        internal static readonly Color Night = new Color(0.7882353f, 0.6470588f, 0.3568628f);    // #C9A55B
        internal static readonly Color Selected = new Color(0.6705883f, 0.5058824f, 0.0392157f);  // #AB810A
        internal static readonly Color NightSelected = new Color(0.96f, 0.83f, 0.48f); // 夜间紫底上的浅金色

        internal TextMeshProUGUI? Text;
        internal ModdedTABSButton? Button;

        private void LateUpdate()
        {
            if (Text == null)
            {
                return;
            }

            bool selected = Button != null && Button.Selected;
            Color target = SelfRowColor(selected);
            if (Text.color != target)
            {
                Text.color = target;
            }
        }
    }

    internal static Color SelfRowColor(bool selected = false)
    {
        bool dark = ModConfigLook.IsDark;
        return selected ? (dark ? SelfRowTint.NightSelected : SelfRowTint.Selected)
            : (dark ? SelfRowTint.Night : SelfRowTint.Day);
    }

    // ── 复选框 ──────────────────────────────────────────────────────

    /// <summary>
    /// 结构：Button 根（透明、接点击）→ Border（米白圆角，做描边）→ Box（深色圆角，内缩 2）→ Mark（两根天蓝细条拼成 ✓）。
    /// 未勾：描边 45% 透明、无 ✓；勾上：描边不透明、✓ 显示。深色与米白都是 TimeTheme 表里的格，夜里自动变黑 / 紫；
    /// ✓ 的蓝由 ThemeTint 切。任何底色（含选中行的白底）上深色方块都看得见。
    /// </summary>
    internal sealed class CheckBox : MonoBehaviour
    {
        internal Image? Border;
        internal GameObject? Mark;
        internal Action<bool>? Changed;
        private bool _checked;

        internal bool Checked
        {
            get => _checked;
            set
            {
                _checked = value;
                Refresh();
            }
        }

        internal void SetSilently(bool value)
        {
            _checked = value;
            Refresh();
        }

        private void Refresh()
        {
            if (Border != null)
            {
                Color color = Border.color;
                color.a = _checked ? 1f : UncheckedBorderAlpha;
                Border.color = color;
            }

            if (Mark != null && Mark.activeSelf != _checked)
            {
                Mark.SetActive(_checked);
            }
        }

        internal void Toggle()
        {
            Checked = !Checked;
            Changed?.Invoke(Checked);
        }
    }

    internal static CheckBox MakeCheckBox(string name, Transform parent, Vector2 anchoredPosition, bool initial, Action<bool>? changed)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(CheckBoxSize + 6f, CheckBoxSize + 6f); // 比方块大一圈，好点
        rect.anchoredPosition = anchoredPosition;

        Image hit = root.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f); // 只接射线，不画
        hit.raycastTarget = true;

        Image border = MakeBox("Border", rect, Cream, 6f);
        RectTransform borderRect = border.rectTransform;
        borderRect.anchorMin = new Vector2(0.5f, 0.5f);
        borderRect.anchorMax = new Vector2(0.5f, 0.5f);
        borderRect.sizeDelta = new Vector2(CheckBoxSize, CheckBoxSize);
        borderRect.anchoredPosition = Vector2.zero;

        Image box = MakeBox("Box", borderRect, new Color(ModConfigTreeLayoutPatch.TabBackground.r, ModConfigTreeLayoutPatch.TabBackground.g, ModConfigTreeLayoutPatch.TabBackground.b, 0.92f), 4f);
        RectTransform boxRect = box.rectTransform;
        boxRect.anchorMin = Vector2.zero;
        boxRect.anchorMax = Vector2.one;
        boxRect.offsetMin = new Vector2(2f, 2f);
        boxRect.offsetMax = new Vector2(-2f, -2f);

        GameObject mark = new GameObject("Mark", typeof(RectTransform));
        RectTransform markRect = (RectTransform)mark.transform;
        markRect.SetParent(boxRect, false);
        markRect.anchorMin = Vector2.zero;
        markRect.anchorMax = Vector2.one;
        markRect.offsetMin = Vector2.zero;
        markRect.offsetMax = Vector2.zero;
        MakeBar(markRect, new Vector2(-3.2f, -1.2f), new Vector2(2.4f, 6f), 45f);
        MakeBar(markRect, new Vector2(1.4f, 0.4f), new Vector2(2.4f, 10.5f), -45f);

        Button button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = border;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        CheckBox checkBox = root.AddComponent<CheckBox>();
        checkBox.Border = border;
        checkBox.Mark = mark;
        checkBox.Changed = changed;
        checkBox.SetSilently(initial);
        button.onClick.AddListener(checkBox.Toggle);
        root.AddComponent<GamepadSupport.FocusHighlight>(); // 手柄焦点框（1.2x 描边色太弱，补一圈外扩描边）
        return checkBox;
    }

    /// <summary>✓ 的一根细条：天蓝、绕中心旋转、圆头。</summary>
    private static void MakeBar(RectTransform parent, Vector2 center, Vector2 size, float angle)
    {
        GameObject go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = center;
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        ApplyRounded(image, 1.5f);
        Tint(image, MarkDay, MarkNight);
    }

    // ── 滚动条 ──────────────────────────────────────────────────────

    /// <summary>
    /// 立在列表右侧的竖向滚动条：轨道用页签棕（更淡）、滑块用米白，都是 TimeTheme 表里的色；两者都圆角。
    /// 挂到 ScrollRect.verticalScrollbar 上由它驱动；AutoHide 让内容装得下时整条隐藏。
    /// 滑块不叫「Handle」：TimeTheme 对这个名字有特殊处理。
    /// </summary>
    internal static Scrollbar MakeScrollbar(RectTransform page, ScrollRect scroll, float left, float width, float top, float bottom)
    {
        GameObject root = new GameObject("MCE_Scrollbar", typeof(RectTransform), typeof(Scrollbar));
        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(page, false);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(left + width, -top);

        Image track = MakeBox("MCE_ScrollTrack", rect, new Color(ModConfigTreeLayoutPatch.TabBackground.r, ModConfigTreeLayoutPatch.TabBackground.g, ModConfigTreeLayoutPatch.TabBackground.b, 0.4f), width / 2f, paper: false);
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = Vector2.zero;
        trackRect.anchorMax = Vector2.one;
        trackRect.offsetMin = Vector2.zero;
        trackRect.offsetMax = Vector2.zero;

        GameObject area = new GameObject("Sliding Area", typeof(RectTransform));
        RectTransform areaRect = (RectTransform)area.transform;
        areaRect.SetParent(rect, false);
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(1f, 1f);
        areaRect.offsetMax = new Vector2(-1f, -1f);

        Image thumb = MakeBox("Thumb", areaRect, new Color(Cream.r, Cream.g, Cream.b, 0.85f), (width - 2f) / 2f, paper: false);
        thumb.raycastTarget = true;
        RectTransform thumbRect = thumb.rectTransform;
        thumbRect.anchorMin = Vector2.zero;
        thumbRect.anchorMax = Vector2.one;
        thumbRect.offsetMin = Vector2.zero;
        thumbRect.offsetMax = Vector2.zero;

        Scrollbar bar = root.GetComponent<Scrollbar>();
        bar.handleRect = thumbRect;
        bar.targetGraphic = thumb;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 1f;
        bar.colors = colors;

        // 手柄焦点不许落到滚动条上（行滚动由 FocusScrollIntoView 代劳；鼠标拖拽不受影响）
        bar.navigation = new Navigation { mode = Navigation.Mode.None };

        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return bar;
    }

    // ── 工具行（列表上方）───────────────────────────────────────────

    /// <summary>工具行句柄：前两排收进 <see cref="ToolbarRefs.Collapsible"/>，语言行独立容器 ——「隐藏本地化相关按钮」即时切换 = 容器 SetActive + 语言行上移。</summary>
    internal sealed class ToolbarRefs
    {
        internal RectTransform Collapsible = null!;
        internal RectTransform LanguageRow = null!;
        internal CheckBox? ExportSelectedBox;
    }

    /// <summary>容器不占绘制：只作排版与整体开关的挂点。</summary>
    private static RectTransform MakeRowContainer(RectTransform page, string name, float left, float top, float width, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(page, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    /// <summary>
    /// 三格等分 300 宽：导出翻译（蓝）、查漏翻（绿；没装 XUnity 灰）、「仅导出已选」开关（页签棕底 + 复选框）。
    /// 前两排建在 Collapsible 容器里、语言行在自己的容器里，容器内一律 0 起相对坐标。
    /// </summary>
    internal static ToolbarRefs MakeToolbar(RectTransform page, float left, float width, float top, Action<bool> exportSelectedChanged, Action? openGroups = null)
    {
        EnsureTerms();
        ToolbarRefs refs = new ToolbarRefs();
        RectTransform collapsible = MakeRowContainer(page, "MCE_ToolbarCollapsible", left, top, width, ToolbarHeight * 2f + ToolbarRowGap);
        refs.Collapsible = collapsible;

        // 2.4.6：三格平分（原先前两格固定 92，「仅导出已选」英文放不下）；文字放不下时自动缩到 10 号
        float third = Mathf.Floor((width - ToolbarGap * 2f) / 3f);
        float x = 0f;
        FitCaption(ModConfigTranslationExport.MakeToolbarButton(collapsible, "MCE_ExportTexts", false, x, 0f, third, ToolbarHeight).Text);
        x += third + ToolbarGap;
        FitCaption(ModConfigTranslationExport.MakeToolbarButton(collapsible, "MCE_ExportUntranslated", true, x, 0f, third, ToolbarHeight).Text);
        x += third + ToolbarGap;

        float toggleWidth = width - x;
        GameObject row = new GameObject("MCE_ExportSelectedOnly", typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)row.transform;
        rect.SetParent(collapsible, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(toggleWidth, ToolbarHeight);
        Image background = row.GetComponent<Image>();
        background.color = ModConfigTreeLayoutPatch.TabBackground;
        ModConfigPaperStyle.Apply(background, 8f);

        CheckBox box = MakeCheckBox("Check", rect, new Vector2(4f, 0f), ExportSelectedOnly, value =>
        {
            ExportSelectedOnly = value;
            exportSelectedChanged(value);
        });

        PeakText label = MenuAPI.CreateText("", "Label").ParentTo(rect);
        label.SetLocalizationIndex(ExportSelectedKey);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(CheckBoxSize + 10f, 0f);
        labelRect.offsetMax = new Vector2(-3f, 0f);
        StyleLabel(label.TextMesh, TextAlignmentOptions.MidlineLeft);
        FitCaption(label.TextMesh);

        row.GetComponent<Button>().transition = Selectable.Transition.None;
        row.GetComponent<Button>().onClick.AddListener(box.Toggle);
        row.AddComponent<ThemeAsOption>();
        row.AddComponent<TipTarget>().Key = "TIP_SELECTED_ONLY";
        GamepadSupport.MarkSelectable(row, null, null); // 不在滚动区：只要焦点框

        // 第二行：XUnity 的「显示原文 / 译文」「重载翻译」+「打开目录」
        ModConfigTranslationExport.MakeTranslationTools(collapsible, 0f, ToolbarHeight + ToolbarRowGap, width, ToolbarHeight, ToolbarGap);
        // 第三行：语言文件下拉 + 刷新（独立容器，隐藏前两排时整体移到第一排的位置）
        refs.LanguageRow = MakeLanguageRow(page, left, top + (ToolbarHeight + ToolbarRowGap) * 2f, width, ToolbarHeight, ToolbarGap, openGroups);
        refs.ExportSelectedBox = box;
        return refs;
    }

    // ── 语言行（第三行）────────────────────────────────────────────

    private static readonly Color RefreshColor = new Color(0.185f, 0.394f, 0.6226f);

    /// <summary>
    /// 第三行：排序下拉 + 语言下拉（等分剩余宽度）+ 方形刷新图标钮 + 方形分组管理钮（各一个行高见方），间隙 8。
    /// 语言下拉第一项「跟随游戏语言」，其后是 language 目录下的语言组（显示名取首行 // display: 或文件名），
    /// 选中即应用、全页面实时刷新，出错在说明面板用警告色提示；「刷新」重扫目录，新建的文件点一下就能选。
    /// 方形钮换掉蓝缎带样式：纸面底（白天米白、夜里与下拉同深纸面）+ 金色图标。
    /// 整行装进独立容器并返回：隐藏前两排时整容器上移到第一排的位置。
    /// </summary>
    private static RectTransform MakeLanguageRow(RectTransform page, float left, float top, float width, float height, float gap, Action? openGroups)
    {
        RectTransform row = MakeRowContainer(page, "MCE_LanguageRow", left, top, width, height);
        const float rowGap = 8f;
        float refreshWidth = height;
        int squares = openGroups != null ? 3 : 2;
        // 排序项都很短（「加载顺序」「名称 A→Z」），语言项带文件名，宽度按 42 : 58 分
        float dropdownSpace = width - refreshWidth * squares - rowGap * (squares + 1);
        float sortWidth = Mathf.Round(dropdownSpace * 0.42f);
        float languageWidth = dropdownSpace - sortWidth;

        PeakDropdown sort = MakeStyledDropdown(row, "MCE_Sort", 0f, sortWidth, height);
        SortDropdown sortController = sort.gameObject.AddComponent<SortDropdown>();
        sortController.Dropdown = sort;
        sort.OnValueChanged(sortController.OnPicked);
        sort.gameObject.AddComponent<TipTarget>().Key = "TIP_SORT";

        PeakDropdown language = MakeStyledDropdown(row, "MCE_Language", sortWidth + rowGap, languageWidth, height);
        LanguageDropdown controller = language.gameObject.AddComponent<LanguageDropdown>();
        controller.Dropdown = language;
        controller.Rescan(notify: false);
        language.OnValueChanged(controller.OnPicked);
        language.gameObject.AddComponent<TipTarget>().Key = "TIP_LANGUAGE";

        RefreshSpin? spinner = null;
        float refreshLeft = dropdownSpace + rowGap * 2f;
        PeakMenuButton refresh = ModConfigTranslationExport.MakeButton(row, "MCE_LanguageRefresh", "BTN_REFRESH", RefreshColor, refreshLeft, 0f, refreshWidth, height, () =>
        {
            controller.Rescan(notify: true);
            spinner?.Kick();
        });
        spinner = StyleSquareIconButton(refresh, RefreshIconSprite(), height).AddComponent<RefreshSpin>();
        refresh.gameObject.AddComponent<TipTarget>().Key = "TIP_REFRESH";

        if (openGroups != null)
        {
            PeakMenuButton groups = ModConfigTranslationExport.MakeButton(row, "MCE_GroupManager", "BTN_GROUPS", RefreshColor, refreshLeft + refreshWidth + rowGap, 0f, refreshWidth, height, openGroups, UiActionRole.Edit);
            StyleSquareIconButton(groups, GroupIconSprite(), height);
            groups.gameObject.AddComponent<TipTarget>().Key = "TIP_GROUPS";
        }

        var clear = ModConfigTranslationExport.MakeButton(row, "MCE_ClearUpdates", "BTN_CLEAR_UPDATES", RefreshColor, refreshLeft + (refreshWidth + rowGap) * (openGroups != null ? 2 : 1), 0f, refreshWidth, height, Overview.ChangeLedger.Clear);
        // 2.4.1：原先是字体里的「✓」，笔画细、颜色不跟图标走；改成与刷新 / 分组同一套粗笔画金色 sprite
        StyleSquareIconButton(clear, CheckIconSprite(), height);
        clear.gameObject.AddComponent<TipTarget>().Key = "TIP_CLEAR_UPDATES";
        return row;
    }

    /// <summary>
    /// 缎带钮改成方形图标钮：文字关掉、缎带虚线边藏起来、底图换纸面，中间放金色图标；返回图标对象。
    /// 注意根对象上没有 Image，真正的底图是 Button.targetGraphic（之前改色没生效就是改错了对象）。
    /// </summary>
    private static GameObject StyleSquareIconButton(PeakMenuButton button, Sprite sprite, float height)
    {
        button.Text.gameObject.SetActive(false);
        if (button.BorderTop != null)
        {
            button.BorderTop.gameObject.SetActive(false);
        }

        if (button.BorderBottom != null)
        {
            button.BorderBottom.gameObject.SetActive(false);
        }

        Button? uiButton = button.Button;
        Image? background = uiButton != null ? uiButton.targetGraphic as Image : null;
        if (background != null)
        {
            // 底图还挂着缎带模板的原尺寸（比行高出一截），拉回贴满按钮
            RectTransform bgRect = background.rectTransform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            ModConfigPaperStyle.Apply(background, 8f);
            // 白天米白纸面、夜里跟下拉一样深纸面；图标金色在两个底上都压得住
            Tint(background, ModConfigLook.CreamOf(false), ModConfigLook.BoxOf(true));
            // 不能用 ColorTint：ThemeTint 每帧断言底色会把变色盖回去。模板本来就是 Animation 过渡，
            // 由根的 Animator 驱动放大动画、顺带开关 SFX Hover/Click 子对象播音效——改底色不冲突。
            if (uiButton != null)
            {
                uiButton.transition = Selectable.Transition.Animation;
            }
        }

        // Pressed 动画会翻出模板的 Glow 辉光（缎带上是装饰，方钮上看着像漏斗）——方钮用不上，建钮时销毁；
        // Panel 底、Border 虚线、Text、SFX 子对象保留（模板子对象：Shadow Panel Border Border Glow Text SFX Appear/Hover/Click）。
        List<GameObject> ornaments = new List<GameObject>();
        foreach (Transform child in button.transform)
        {
            if (child.name == "Glow")
            {
                ornaments.Add(child.gameObject);
            }
        }

        foreach (GameObject ornament in ornaments)
        {
            UnityEngine.Object.Destroy(ornament);
        }

        GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(button.transform, false);
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        // 2.4.1：三个方钮的图标统一占 60% 见方（原先 height-14，加上各自 sprite 留白不同，刷新图标明显偏小）
        iconRect.sizeDelta = new Vector2(Mathf.Round(height * IconBoxRatio), Mathf.Round(height * IconBoxRatio));
        iconRect.anchoredPosition = Vector2.zero;
        Image iconImage = icon.GetComponent<Image>();
        iconImage.sprite = sprite;
        iconImage.color = SelfRowTint.Selected;
        iconImage.raycastTarget = false;
        Tint(iconImage, SelfRowTint.Selected, SelfRowTint.NightSelected);
        return icon;
    }

    /// <summary>
    /// 刷新钮的点击反馈：Animator 只给缎带模板做了缩放/装饰动效，没有"重扫"语义的反馈——
    /// 按下改为 ↻ 图标原地转一圈，0.4 秒，不依赖 Button 状态。
    /// </summary>
    private sealed class RefreshSpin : MonoBehaviour
    {
        private const float Duration = 0.4f;
        private Coroutine? _run;

        internal void Kick()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_run != null)
            {
                StopCoroutine(_run);
            }

            _run = StartCoroutine(Run());
        }

        private System.Collections.IEnumerator Run()
        {
            for (float t = 0f; t < Duration; t += Time.unscaledDeltaTime)
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, -360f * t / Duration);
                yield return null;
            }

            transform.localRotation = Quaternion.identity;
            _run = null;
        }
    }

    /// <summary>工具行用的小下拉：纸面白底、白天暗金/夜里亮金的字与箭头、焦点框，与语言下拉同一套样式。</summary>
    private static PeakDropdown MakeStyledDropdown(RectTransform parent, string name, float left, float width, float height)
    {
        // 直接实例化到 page 下时页面还没激活，PeakDropdown.Awake 不跑、RectTransform 为空（ModConfig 自己也是先挂活动节点再 ParentTo）
        PeakDropdown dropdown = MenuAPI.CreateDropdown(name, null!).ParentTo(parent);
        RectTransform cell = (RectTransform)dropdown.transform;
        cell.anchorMin = new Vector2(0f, 1f);
        cell.anchorMax = new Vector2(0f, 1f);
        cell.pivot = new Vector2(0f, 1f);
        cell.sizeDelta = new Vector2(width, height);
        cell.anchoredPosition = new Vector2(left, 0f);
        dropdown.SetSize(new Vector2(width, height));
        dropdown.RectTransform.anchorMin = new Vector2(0f, 1f);
        dropdown.RectTransform.anchorMax = new Vector2(0f, 1f);
        dropdown.RectTransform.pivot = new Vector2(0f, 1f);
        // RectTransform 就是 cell 本身：这里归零会把 left 抹掉，排序下拉曾因此被语言下拉整个盖住
        dropdown.RectTransform.anchoredPosition = new Vector2(left, 0f);
        dropdown.SetBackgroundColor(ModConfigTreeLayoutPatch.TabBackground)
            .SetLabelColor(SelfRowTint.Selected)
            .SetArrowColor(SelfRowTint.Selected);
        Tint(dropdown.Dropdown.captionText, SelfRowTint.Selected, SelfRowTint.NightSelected);
        Tint(dropdown.Arrow, SelfRowTint.Selected, SelfRowTint.NightSelected);
        ModConfigPaperStyle.Apply(dropdown.Background, 8f);
        // 2.4.1：模板箭头约 30×19，窄下拉里把文字挤没了（「简体…」只剩两个字）。缩到 14 见方。
        // 2.4.2：模板里的箭头是转过角度的，原先以右缘为轴心贴右 8，旋转后图形一半伸出框外（实机截图）。
        // 改为中心轴心，按旋转后的实际宽度把中心放在离右缘 DropdownArrowPad + 半宽处，转多少度都在框内。
        float arrowVisibleWidth = 0f;
        if (dropdown.Arrow != null)
        {
            RectTransform arrowRect = dropdown.Arrow.rectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0.5f);
            arrowRect.anchorMax = new Vector2(1f, 0.5f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.sizeDelta = new Vector2(DropdownArrowSize, DropdownArrowSize * 0.62f);
            float angle = arrowRect.localEulerAngles.z * Mathf.Deg2Rad;
            arrowVisibleWidth = Mathf.Abs(Mathf.Cos(angle)) * arrowRect.sizeDelta.x + Mathf.Abs(Mathf.Sin(angle)) * arrowRect.sizeDelta.y;
            arrowVisibleWidth *= Mathf.Abs(arrowRect.localScale.x);
            arrowRect.anchoredPosition = new Vector2(-(DropdownArrowPad + arrowVisibleWidth / 2f), 0f);
            dropdown.Arrow.preserveAspect = true;
        }
        TMP_Text caption = dropdown.Dropdown.captionText;
        if (caption != null)
        {
            caption.enableAutoSizing = false;
            caption.fontSize = HeaderFontSize;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            caption.overflowMode = TextOverflowModes.Ellipsis;
            // 原模板右缘在箭头左侧留了一大截，122px 的窄框里「Name A…」就截断了——拉到贴箭头
            RectTransform capRect = caption.rectTransform;
            float arrowSpace = dropdown.Arrow != null ? arrowVisibleWidth + DropdownArrowPad : 0f;
            capRect.offsetMax = new Vector2(-(arrowSpace + 4f), capRect.offsetMax.y);
            capRect.offsetMin = new Vector2(Mathf.Min(capRect.offsetMin.x, 8f), capRect.offsetMin.y);
            // 模板还可能在 TMP margin 里再藏一层左右内边距，清成 0 让 rect 说了算
            Vector4 capMargin = caption.margin;
            capMargin.x = 0f;
            capMargin.z = 0f;
            caption.margin = capMargin;
        }

        if (dropdown.Dropdown.itemText != null)
        {
            // 展开项：预制体开了 autoSize 会把它放大到占满行高（实测看不清还截断），关掉再定字号
            dropdown.Dropdown.itemText.enableAutoSizing = false;
            dropdown.Dropdown.itemText.fontSize = HeaderFontSize;
            dropdown.Dropdown.itemText.textWrappingMode = TextWrappingModes.NoWrap;
            dropdown.Dropdown.itemText.overflowMode = TextOverflowModes.Ellipsis;
            // 项内左侧的勾选色块占掉小半行，收成窄条、标签左缘跟上；
            // 真正的缩进在 TMP 的 margin.x 里（改 rect 偏移没用），一并清掉
            dropdown.Dropdown.itemText.rectTransform.offsetMin = new Vector2(12f, dropdown.Dropdown.itemText.rectTransform.offsetMin.y);
            Vector4 itemMargin = dropdown.Dropdown.itemText.margin;
            itemMargin.x = 0f;
            dropdown.Dropdown.itemText.margin = itemMargin;
        }

        Toggle? itemToggle = dropdown.Dropdown.template != null
            ? dropdown.Dropdown.template.GetComponentInChildren<Toggle>(true)
            : null;
        if (itemToggle != null && itemToggle.graphic != null)
        {
            RectTransform check = itemToggle.graphic.rectTransform;
            check.sizeDelta = new Vector2(5f, check.sizeDelta.y);
            check.anchoredPosition = new Vector2(4f, check.anchoredPosition.y);
        }

        dropdown.gameObject.AddComponent<ThemeAsOption>();
        GamepadSupport.MarkSelectable(dropdown.Dropdown.gameObject, null, null); // 手柄焦点框
        return dropdown;
    }

    /// <summary>程序化生成 ↻ 图标：环带一段弧（缺口朝右上）+ 弧尾一个三角箭头，顺时针。</summary>
    private static Sprite? _refreshIcon;

    private static Sprite RefreshIconSprite()
    {
        if (_refreshIcon != null)
        {
            return _refreshIcon;
        }

        const int size = 64;
        // 2.4.1：环带加粗、外径贴近 64 画布边，与分组图标（5 宽横条、48 宽）的笔画和占地一致
        const float radius = 22f, halfThick = 5f, startDeg = 55f, sweep = 285f;
        const float arrowLen = 17f, arrowWing = 9.5f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color clear = new Color(0f, 0f, 0f, 0f);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float endRad = (startDeg - sweep) * Mathf.Deg2Rad;
        Vector2 end = center + new Vector2(Mathf.Cos(endRad), Mathf.Sin(endRad)) * radius;
        Vector2 tangent = new Vector2(Mathf.Sin(endRad), -Mathf.Cos(endRad)); // 顺时针切线
        Vector2 normal = new Vector2(tangent.y, -tangent.x);
        Vector2 tip = end + tangent * arrowLen;
        Vector2 wingA = end + normal * arrowWing;
        Vector2 wingB = end - normal * arrowWing;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                Vector2 d = p - center;
                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float swept = Mathf.Repeat(startDeg - angle, 360f); // 顺时针已扫过的角度
                bool onRing = Mathf.Abs(d.magnitude - radius) <= halfThick && swept <= sweep;
                tex.SetPixel(x, y, onRing || PointInTriangle(p, tip, wingA, wingB) ? Color.white : clear);
            }
        }

        tex.Apply();
        _refreshIcon = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _refreshIcon;
    }

    private const float IconBoxRatio = 0.6f;
    private const float DropdownArrowSize = 14f;
    /// <summary>箭头右缘离下拉右缘的距离；纸面底图边缘是手绘毛边，留得比文字左距（8）略大。</summary>
    private const float DropdownArrowPad = 10f;

    private static Sprite? _checkIcon, _themeIcon, _folderIcon;

    /// <summary>2.5.0：「打开目录」改成方形图标钮（第二排让位给「导入补翻」），与刷新 / 分组同一套纸面 + 金色图标。</summary>
    internal static void StyleFolderButton(PeakMenuButton button, float height) => StyleSquareIconButton(button, FolderIconSprite(), height);

    /// <summary>文件夹轮廓：圆头粗线（半宽 5），左上一个标签，占地与另外几个图标一致。</summary>
    private static Sprite FolderIconSprite()
    {
        if (_folderIcon != null)
        {
            return _folderIcon;
        }

        const int size = 64;
        Vector2[] path =
        {
            new Vector2(9f, 13f), new Vector2(55f, 13f), new Vector2(55f, 43f), new Vector2(30f, 43f),
            new Vector2(26f, 51f), new Vector2(9f, 51f), new Vector2(9f, 13f),
        };
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = float.MaxValue;
                for (int i = 0; i + 1 < path.Length; i++) d = Mathf.Min(d, SegmentDistance(p, path[i], path[i + 1]));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(5.5f - d)));
            }
        }

        tex.Apply();
        _folderIcon = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _folderIcon;
    }

    /// <summary>✓：两段圆头粗线（半宽 5，与刷新环、分组横条同一笔画），占地与另外两个图标一致。</summary>
    private static Sprite CheckIconSprite()
    {
        if (_checkIcon != null)
        {
            return _checkIcon;
        }

        const int size = 64;
        Vector2 a = new Vector2(11f, 33f), b = new Vector2(26f, 17f), c = new Vector2(53f, 47f);
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(5.5f - d)));
            }
        }

        tex.Apply();
        _checkIcon = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _checkIcon;
    }

    /// <summary>主题图标：半月（实心圆挖掉偏右上的一个圆），配色主题切换钮用。</summary>
    internal static Sprite ThemeIconSprite()
    {
        if (_themeIcon != null)
        {
            return _themeIcon;
        }

        const int size = 64;
        Vector2 center = new Vector2(30f, 32f), bite = new Vector2(42f, 41f);
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float inside = Mathf.Clamp01(23f - Vector2.Distance(p, center) + 0.5f);
                float cut = Mathf.Clamp01(18f - Vector2.Distance(p, bite) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, inside * (1f - cut)));
            }
        }

        tex.Apply();
        _themeIcon = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _themeIcon;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>
    /// 主题切换小方钮（模组设置页，「搜索」标题右侧、搜索框上方）：与工具行方钮同一套纸面 + 金色图标，点一下在蓝 / 紫之间切换。
    /// 「夜里自动紫色」时点击改成固定的另一套（用户手动选了就以手动为准）。
    /// </summary>
    internal static void MakeThemeButton(RectTransform page, float left, float top, float size, TMP_Text? alignTo = null)
    {
        RectTransform host = MakeRowContainer(page, "MCE_ThemeToggle", left, top, size, size);
        if (alignTo != null)
        {
            ThemeButtonBaseline baseline = host.gameObject.AddComponent<ThemeButtonBaseline>();
            baseline.Label = alignTo;
            baseline.Page = page;
            baseline.Size = size;
        }

        PeakMenuButton button = ModConfigTranslationExport.MakeButton(host, "MCE_ThemeButton", "BTN_THEME", RefreshColor, 0f, 0f, size, size, ToggleTheme);
        StyleSquareIconButton(button, ThemeIconSprite(), size);
        button.gameObject.AddComponent<TipTarget>().Key = "TIP_THEME";
    }

    /// <summary>
    /// 2.4.2：主题钮底边与「搜索」两个字的字形底部水平对齐（用户实机截图：原先按搜索框顶边上推 6 定位，钮比字低一截）。
    /// 字形底部取 TMP 的 textBounds（字形框，不含行高留白），换算到页面坐标；只在文字、字号或页面尺寸变化时重算。
    /// </summary>
    internal sealed class ThemeButtonBaseline : MonoBehaviour
    {
        internal TMP_Text? Label;
        internal RectTransform? Page;
        internal float Size;
        private string? _text;
        private float _fontSize = -1f, _pageHeight = -1f;

        private void OnEnable() => _text = null;

        private void LateUpdate()
        {
            if (Label == null || Page == null || !Label.isActiveAndEnabled)
            {
                return;
            }

            float pageHeight = Page.rect.height;
            if (_text == Label.text && Mathf.Approximately(_fontSize, Label.fontSize) && Mathf.Approximately(_pageHeight, pageHeight))
            {
                return;
            }

            Bounds bounds = Label.textBounds;
            if (bounds.size.y <= 0.01f)
            {
                return; // 还没生成网格，下一帧再试
            }

            _text = Label.text;
            _fontSize = Label.fontSize;
            _pageHeight = pageHeight;
            Vector3 world = Label.rectTransform.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, 0f));
            float bottom = Page.InverseTransformPoint(world).y - Page.rect.yMax; // 相对页面顶边，向下为负
            RectTransform host = (RectTransform)transform;
            host.anchoredPosition = new Vector2(host.anchoredPosition.x, bottom + Size);
        }
    }

    internal static void ToggleTheme()
    {
        Plugin.ColourTheme.Value = ModConfigLook.IsDark ? ModConfigLook.ThemeMode.Blue : ModConfigLook.ThemeMode.Purple;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = TriangleSign(p, a, b), d2 = TriangleSign(p, b, c), d3 = TriangleSign(p, c, a);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }

    private static float TriangleSign(Vector2 p, Vector2 a, Vector2 b)
    {
        return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }

    /// <summary>下拉框的状态：索引 0 = 跟随游戏，其后按 <see cref="Loc.Scan"/> 的顺序。</summary>
    internal sealed class LanguageDropdown : MonoBehaviour
    {
        internal PeakDropdown? Dropdown;
        private readonly List<Loc.LanguageFile> _files = new List<Loc.LanguageFile>();
        private int _languageRevision = -1;
        private bool _updating;

        internal void Rescan(bool notify)
        {
            if (Dropdown == null || Dropdown.Dropdown == null)
            {
                return;
            }

            _files.Clear();
            _files.AddRange(Loc.Scan());
            List<string> options = new List<string> { Loc.Get("LANG_FOLLOW") };
            int selected = 0;
            string current = Plugin.LanguageFile.Value ?? "";
            string normalizedCurrent = Loc.NormalizeSelection(current);
            for (int i = 0; i < _files.Count; i++)
            {
                // 有 // display: 显示「名字 (文件名)」，没有直接显示文件名；多文件组末尾带 ×数量
                string label = _files[i].Display.Length > 0
                    ? _files[i].Display + " (" + _files[i].Stem + ")"
                    : _files[i].Stem;
                if (_files[i].FileCount > 1)
                {
                    label += " ×" + _files[i].FileCount;
                }

                options.Add(label);
                if (string.Equals(_files[i].Key, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i + 1;
                }
            }

            _updating = true;
            try
            {
                Dropdown.SetOptions(options);
                Dropdown.Dropdown.SetValueWithoutNotify(selected);
                Dropdown.Dropdown.RefreshShownValue();
            }
            finally
            {
                _updating = false;
            }

            if (notify)
            {
                ModConfigDescriptionPanel.Current?.ShowNotice(Loc.Format("NOTICE_REFRESHED", _files.Count));
            }

            if (selected == 0 && current.Length > 0)
            {
                // 记忆里的文件已不在：提示但不改记忆（用户可能只是暂时移走）
                ModConfigDescriptionPanel.Current?.ShowNotice(Loc.Format("NOTICE_LANG_ERROR", current + ".txt", Loc.Get("NOTICE_LANG_NOT_FOUND")), true);
            }
        }

        internal void OnPicked(int index)
        {
            if (_updating)
            {
                return;
            }

            string selection = index <= 0 || index > _files.Count ? Loc.FollowGame : _files[index - 1].Key;
            string previous = Plugin.LanguageFile.Value ?? "";
            Plugin.LanguageFile.Value = selection;
            Loc.ApplyResult result = Loc.Apply(selection, notify: true, refresh: true);
            if (!result.Success)
            {
                Plugin.LanguageFile.Value = previous;
            }

            Rescan(notify: false); // 「跟随游戏语言」这一项的文字本身也要换语言
        }

        private void Update()
        {
            if (_languageRevision == Loc.TranslationRevision) return;
            _languageRevision = Loc.TranslationRevision;
            Rescan(notify: false);
        }

        /// <summary>页面重开时下拉文字按当前语言重刷（LANG_FOLLOW 是纯文本选项，不带 LocalizedText 组件）。</summary>
        private void OnEnable()
        {
            if (Dropdown != null && Dropdown.Dropdown != null && Dropdown.Dropdown.options.Count > 0)
            {
                Dropdown.Dropdown.options[0].text = Loc.Get("LANG_FOLLOW");
                Dropdown.Dropdown.RefreshShownValue();
            }
        }
    }

    /// <summary>
    /// 排序下拉：三档固定（默认顺序 / 名称 / 选项数），选中即写配置，侧栏即时重排。
    /// 选项文字跟随语言切换——Update 里拿首项译文当快照比对，变了就重建。
    /// </summary>
    internal sealed class SortDropdown : MonoBehaviour
    {
        internal PeakDropdown? Dropdown;
        private bool _updating;
        private string _snapshot = "";

        private static readonly (string Key, Plugin.SidebarSortMode Mode)[] Entries =
        {
            ("SORT_DEFAULT", Plugin.SidebarSortMode.Default),
            ("SORT_LOAD_DESC", Plugin.SidebarSortMode.LoadReversed),
            ("SORT_NAME", Plugin.SidebarSortMode.Name),
            ("SORT_NAME_DESC", Plugin.SidebarSortMode.NameDesc),
        };

        private void OnEnable()
        {
            Build();
        }

        private void Update()
        {
            if (Loc.Get(Entries[0].Key) != _snapshot)
            {
                Build();
            }
        }

        private void Build()
        {
            if (Dropdown == null || Dropdown.Dropdown == null)
            {
                return;
            }

            _updating = true;
            try
            {
                List<string> options = new List<string>();
                foreach ((string key, _) in Entries)
                {
                    options.Add(Loc.Get(key));
                }

                _snapshot = Loc.Get(Entries[0].Key);
                Dropdown.SetOptions(options);
                int selected = 0;
                for (int i = 0; i < Entries.Length; i++)
                {
                    if (Entries[i].Mode == Plugin.SidebarSort.Value)
                    {
                        selected = i;
                    }
                }

                Dropdown.Dropdown.SetValueWithoutNotify(selected);
                Dropdown.Dropdown.RefreshShownValue();
            }
            finally
            {
                _updating = false;
            }
        }

        internal void OnPicked(int index)
        {
            if (_updating || index < 0 || index >= Entries.Length)
            {
                return;
            }

            Plugin.SidebarSort.Value = Entries[index].Mode;
        }
    }

    /// <summary>
    /// 标记：这棵子树里的白色文字夜里要变紫。TimeTheme 主表不含纯白（白字不动），选项表第 0 组是「白 → 夜间紫」，
    /// 标题、搜索标签、工具行、表头、说明面板这些我们自己的（或 ModConfig 的固定）文字挂上它，主题跟随器多刷一遍选项表。
    /// </summary>
    internal sealed class ThemeAsOption : MonoBehaviour
    {
    }

    // ── 右栏「默认 / 清空」缎带钮 ──────────────────────────────────

    private const string DefaultsButtonName = "UI_MainMenuButton_DefaultsButton";
    private const string ClearButtonName = "UI_MainMenuButton_ClearButton";

    /// <summary>
    /// 「默认」钮的夜间色：TimeTheme 按钮表第 17 组的夜间格 (0, 0.08, 0.29)，与「清空」的夜间暗红同一档。
    /// 白天保持 ModConfig 原版 Color.dodgerBlue —— 1.0.26 起全天换成 #2F649F 是为了让 TimeTheme 夜间能查表，
    /// 但把白天也一并压暗了；现在昼夜两色都写死，由 ThemeTint 按 IsDark 切换，不再依赖配色表匹配。
    /// </summary>
    private static readonly Color DefaultsNightBlue = new Color(0.251f, 0f, 0.482f, 1f); // 2.4.0 紫色主题：深紫

    /// <summary>
    /// 每次 ShowSettings 后过一遍单元格里的「默认」「清空」钮：「默认」挂 ThemeTint（白天原版亮蓝 / 夜间藏蓝），
    /// 两条虚线按模板等比归位。只认名字 + 原色 + 未挂过组件，重复调用无副作用；昼夜切换由 ThemeTint 每帧自己追，
    /// 与 TimeTheme 扫表的先后无关（dodgerBlue 与藏蓝都不是任何一张表里的「白天色」，扫表不会误判）。
    /// </summary>
    internal static void StyleCellButtons(SettingsUICell cell)
    {
        Transform? content = cell.m_settingsContentParent;
        if (content == null)
        {
            return;
        }

        foreach (PeakMenuButton button in content.GetComponentsInChildren<PeakMenuButton>(true))
        {
            if (button.name != DefaultsButtonName && button.name != ClearButtonName)
            {
                continue;
            }

            if (button.name == DefaultsButtonName && button.Panel != null &&
                button.Panel.GetComponent<ThemeTint>() == null && SameRgb(button.Panel.color, Color.dodgerBlue))
            {
                Tint(button.Panel, Color.dodgerBlue, DefaultsNightBlue); // 不动 SetColor：白色虚线与透明度原样保留
            }

            FitBorders(button);
        }
    }

    private static bool SameRgb(Color a, Color b)
    {
        return new Vector3(a.r, a.g, a.b) == new Vector3(b.r, b.g, b.b);
    }

    /// <summary>2.4.6：工具行文字放不下时缩字（13 → 10），不折行、不出框。</summary>
    internal static void FitCaption(TMP_Text text)
    {
        text.enableAutoSizing = true;
        text.fontSizeMax = text.fontSize > 0f ? text.fontSize : HeaderFontSize;
        text.fontSizeMin = 10f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void StyleLabel(TextMeshProUGUI text, TextAlignmentOptions alignment)
    {
        Overview.MceRegularFont.ApplyDefault(text);
        Overview.MceTextWeight.Apply(text);
        text.enableAutoSizing = false;
        text.fontSize = HeaderFontSize;
        text.fontStyle = FontStyles.Normal;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    // ── 表头行 + 模组行的两列 ──────────────────────────────────────

    /// <summary>
    /// 挂在模组列表上，管两列复选框：建行、显示/隐藏导出列、置顶重排、把文字缩进让开列宽。
    /// 分区子行不带复选框。
    /// </summary>
    internal sealed partial class SidebarController : MonoBehaviour
    {
        internal ModSettingsMenu? Menu;
        internal PeakHorizontalTabs? ModTabs;
        internal RectTransform? ListContent;
        internal RectTransform? SectionRoot;
        internal ScrollRect? Scroll;
        internal GameObject? Header;
        internal TextMeshProUGUI? ExportCaption;
        internal TextMeshProUGUI? PinCaption;
        internal CheckBox? ExportSelectedBox;
        internal ToolbarRefs? Toolbar;
        internal RectTransform? ScrollbarRect;

        private readonly List<RowWidgets> _rows = new List<RowWidgets>();
        private int _translationRevision = -1;

        private sealed class RowWidgets
        {
            internal GameObject Row = null!;
            internal string Name = "";
            internal TextMeshProUGUI? Text;
            internal CheckBox Export = null!;
            internal CheckBox Pin = null!;
            internal GameObject Accent = null!;
            internal Image? GroupRail;
            internal string GroupId = "";
            internal bool IsSelf;
        }

        internal float TextIndent => ModConfigTreeLayoutPatch.ModTextIndent + ColumnWidth + (ExportColumnVisible ? ColumnWidth : 0f);

        internal void Build()
        {
            if (ModTabs == null || ListContent == null)
            {
                return;
            }

            EnsureTerms();
            LoadState();
            BuildHeader();
            foreach (GameObject row in ModTabs.Tabs)
            {
                Decorate(row);
            }

            RefreshColumns();
            _built = true;
            RefreshGroups(reorder: false);
            ApplyPinOrder();
            ApplyGroupVisibility(null);
        }

        private void BuildHeader()
        {
            if (Menu == null || ListContent == null)
            {
                return;
            }

            GameObject row = new GameObject("MCE_Header", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(ListContent, false);
            row.transform.SetAsFirstSibling();
            GameObject background = new GameObject("Image", typeof(RectTransform), typeof(Image)).ParentTo(row.transform).ExpandToParent();
            background.GetComponent<Image>().color = ModConfigTreeLayoutPatch.TabBackground;
            background.GetComponent<Image>().raycastTarget = false;
            ModConfigTreeLayoutPatch.StyleRowFrame(row, ModConfigTreeLayoutPatch.ModRowHeight);
            ModConfigPaperStyle.Apply(background.GetComponent<Image>(), 8f);

            // 两列标题：压在下面复选框列的正上方
            ExportCaption = MakeCaption(row.transform, ExportColumnKey, ModConfigTreeLayoutPatch.ModTextIndent);
            PinCaption = MakeCaption(row.transform, PinKey, ModConfigTreeLayoutPatch.ModTextIndent + ColumnWidth);

            // 右侧（2.4.1 用户改）：「跳转到快捷键设置页」，点了打开概览并切到快捷键设置页签。
            // 2.4.7（用户改）：方框 + 文字，方框里画 →。
            // 2.5.1（用户改）：靠右、按文字宽度收紧；底色换成语言行方形钮同款纸面（白天米白、夜里深纸面），字和箭头用金色。
            GameObject hit = new GameObject("KeysOnlyHit", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform hitRect = (RectTransform)hit.transform;
            hitRect.SetParent(row.transform, false);
            hitRect.anchorMin = new Vector2(1f, 0f);
            hitRect.anchorMax = new Vector2(1f, 1f);
            hitRect.pivot = new Vector2(1f, 0.5f);
            hitRect.offsetMin = new Vector2(-160f, 3f);
            hitRect.offsetMax = new Vector2(-4f, -3f);
            Image hitBackground = hit.GetComponent<Image>();
            ModConfigPaperStyle.Apply(hitBackground, 8f);
            Tint(hitBackground, ModConfigLook.CreamOf(false), ModConfigLook.BoxOf(true));

            Image jumpBorder = MakeBox("JumpBorder", hitRect, SelfRowTint.Selected, 6f);
            Tint(jumpBorder, SelfRowTint.Selected, SelfRowTint.NightSelected);
            RectTransform jumpBorderRect = jumpBorder.rectTransform;
            jumpBorderRect.anchorMin = jumpBorderRect.anchorMax = new Vector2(0f, 0.5f);
            jumpBorderRect.pivot = new Vector2(0f, 0.5f);
            jumpBorderRect.sizeDelta = new Vector2(CheckBoxSize - 4f, CheckBoxSize - 4f);
            jumpBorderRect.anchoredPosition = new Vector2(6f, 0f);
            Image jumpBox = MakeBox("JumpBox", jumpBorderRect, ModConfigLook.CreamOf(false), 4f);
            Tint(jumpBox, ModConfigLook.CreamOf(false), ModConfigLook.BoxOf(true));
            jumpBox.rectTransform.anchorMin = Vector2.zero;
            jumpBox.rectTransform.anchorMax = Vector2.one;
            jumpBox.rectTransform.offsetMin = new Vector2(2f, 2f);
            jumpBox.rectTransform.offsetMax = new Vector2(-2f, -2f);
            GameObject jumpGlyph = new GameObject("JumpGlyph", typeof(RectTransform), typeof(Overview.OverviewIconGraphic));
            RectTransform jumpGlyphRect = (RectTransform)jumpGlyph.transform;
            jumpGlyphRect.SetParent(jumpBox.rectTransform, false);
            jumpGlyphRect.anchorMin = Vector2.zero;
            jumpGlyphRect.anchorMax = Vector2.one;
            jumpGlyphRect.offsetMin = new Vector2(1f, 1f);
            jumpGlyphRect.offsetMax = new Vector2(-1f, -1f);
            Overview.OverviewIconGraphic glyph = jumpGlyph.GetComponent<Overview.OverviewIconGraphic>();
            glyph.Kind = "arrow-right";
            glyph.raycastTarget = false;
            Tint(glyph, SelfRowTint.Selected, SelfRowTint.NightSelected);

            PeakText label = MenuAPI.CreateText("", "Text (TMP)").ParentTo(hitRect);
            label.SetLocalizationIndex(JumpHotkeysKey);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(CheckBoxSize + 8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            StyleLabel(label.TextMesh, TextAlignmentOptions.MidlineLeft);
            FitCaption(label.TextMesh);
            Tint(label.TextMesh, SelfRowTint.Selected, SelfRowTint.NightSelected);
            JumpWidth fit = hit.AddComponent<JumpWidth>();
            fit.Label = label.TextMesh;
            fit.Row = (RectTransform)row.transform;
            fit.MinLeft = ModConfigTreeLayoutPatch.ModTextIndent + ColumnWidth * 2f + 12f;

            Button button = hit.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = jumpBorder;
            ColorBlock jumpColors = ColorBlock.defaultColorBlock;
            jumpColors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            jumpColors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            jumpColors.fadeDuration = 0.08f;
            button.colors = jumpColors;
            button.onClick.AddListener(() => Overview.OverviewController.OpenHotkeys(Menu != null ? Menu.MainPage : null));
            hit.AddComponent<TipTarget>().Key = "TIP_JUMP_HOTKEYS";
            row.AddComponent<ThemeAsOption>();
            // 手柄：表头的命中区能拿焦点，选中时把表头滚回视野
            GamepadSupport.MarkSelectable(hit, Scroll, (RectTransform)row.transform);
            Header = row;
        }

        private static TextMeshProUGUI MakeCaption(Transform parent, string key, float left)
        {
            PeakText caption = MenuAPI.CreateText("", "Caption").ParentTo(parent);
            caption.SetLocalizationIndex(key);
            RectTransform rect = caption.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(left - 4f, 0f);
            rect.offsetMax = new Vector2(left + ColumnWidth + 4f, 0f);
            TextMeshProUGUI text = caption.TextMesh;
            StyleLabel(text, TextAlignmentOptions.Center);
            return text;
        }

        private void Update()
        {
            if (_translationRevision != Loc.TranslationRevision)
            {
                _translationRevision = Loc.TranslationRevision;
                foreach (RowWidgets row in _rows)
                    if (row.Text != null) row.Text.text = Overview.OverviewLanguageBridge.GetSettingName(row.Name);
            }
        }

        private void Decorate(GameObject row)
        {
            string name = row.name;
            bool isSelf = name == Plugin.SelfRowName;
            var identities = BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Where(p => Plugin.FixNaming(p.Metadata.Name) == name).ToArray();
            if (identities.Length == 1) Overview.ChangeLedger.Dot(row.transform, "mod:" + identities[0].Metadata.GUID);
            RowWidgets widgets = new RowWidgets
            {
                Row = row,
                Name = name,
                Text = row.GetComponentInChildren<TextMeshProUGUI>(true),
            };
            widgets.Export = MakeCheckBox("MCE_Export", row.transform, new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + ColumnInset, 0f),
                !ExcludedFromExport.Contains(name), value =>
                {
                    if (value)
                    {
                        ExcludedFromExport.Remove(name);
                    }
                    else
                    {
                        ExcludedFromExport.Add(name);
                    }

                    SaveExcluded();
                });
            widgets.Pin = MakeCheckBox("MCE_Pin", row.transform, new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + ColumnWidth + ColumnInset, 0f),
                Pinned.Contains(name) || isSelf, value =>
                {
                    if (value)
                    {
                        Pinned.Add(name);
                    }
                    else
                    {
                        Pinned.Remove(name);
                    }

                    SavePinned();
                    UpdateRowMarks(widgets);
                    RefreshGroupLabels();
                    ApplyPinOrder();
                    ApplyGroupVisibility(null);
                });
            if (isSelf)
            {
                // 自身行固定最顶，复选框没有意义；置顶竖条常亮，行名上色（LateUpdate 每帧断言，见 SelfRowTint）
                widgets.Pin.gameObject.SetActive(false);
                if (widgets.Text != null)
                {
                    SelfRowTint tint = widgets.Text.gameObject.AddComponent<SelfRowTint>();
                    tint.Text = widgets.Text;
                    tint.Button = row.GetComponent<ModdedTABSButton>();
                }
            }

            widgets.IsSelf = isSelf;
            widgets.Accent = MakeAccent(row.transform, isSelf || Pinned.Contains(name));
            widgets.GroupRail = MakeGroupRail(row.transform);
            if (widgets.Text != null) widgets.Text.text = Overview.OverviewLanguageBridge.GetSettingName(name);

            // 手柄：行与两列复选框都可聚焦；焦点框亮起 + 选中时把整行滚回视野（复选框对准整行而非小方块）；
            // A 键语义（选中 / 已选中再往分区·右栏钻）在 WireModRow
            GamepadSupport.MarkSelectable(row, Scroll, (RectTransform)row.transform);
            GamepadSupport.MarkSelectable(widgets.Export.gameObject, Scroll, (RectTransform)row.transform);
            GamepadSupport.MarkSelectable(widgets.Pin.gameObject, Scroll, (RectTransform)row.transform);
            GamepadSupport.WireModRow(row);
            _rows.Add(widgets);
        }

        /// <summary>置顶行左缘的细竖线：青柠黄绿（两套主题与分组色都不用的颜色，用户定），一眼能认出哪些是钉住的。</summary>
        private static GameObject MakeAccent(Transform row, bool active)
        {
            GameObject go = new GameObject("MCE_PinAccent", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(row, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(0f, 4f);
            rect.offsetMax = new Vector2(PinAccentWidth, -4f);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = ModConfigLook.PinAccent;
            go.SetActive(active);
            return go;
        }

        /// <summary>「隐藏本地化相关按钮」「排序方式」即时生效：选项变化不用重启。</summary>
        private void OnEnable()
        {
            Plugin.HideLocalizationButtons.SettingChanged += OnToolbarOptionChanged;
            Plugin.SidebarSort.SettingChanged += OnSortChanged;
            // Keep existing headers when neither the grouping nor its settings/language changed.
            if (_built && (SidebarGroupStore.NeedsReload || _groupLanguageRevision != Loc.TranslationRevision ||
                _groupSettingsCount != (Menu?.settings?.Count() ?? -1) || _groupPluginsCount != BepInEx.Bootstrap.Chainloader.PluginInfos.Count))
            {
                RefreshGroups(reorder: true);
            }
        }

        private void OnDisable()
        {
            Plugin.HideLocalizationButtons.SettingChanged -= OnToolbarOptionChanged;
            Plugin.SidebarSort.SettingChanged -= OnSortChanged;
            CloseGroupEditor();
        }

        private void OnToolbarOptionChanged(object? sender, EventArgs args)
        {
            ApplyToolbarCollapsed();
        }

        private void OnSortChanged(object? sender, EventArgs args)
        {
            ApplyPinOrder();
        }

        /// <summary>
        /// 隐藏 = 前两排容器 SetActive(false) + 语言行移到第一排 + 列表与滚动条顶边上移。
        /// 建行时同样走这条路：容器先按展开位置建好，这里统一收拢到当前选项值。
        /// </summary>
        internal void ApplyToolbarCollapsed()
        {
            bool hide = Plugin.HideLocalizationButtons.Value;
            if (Toolbar?.Collapsible != null && Toolbar.Collapsible.gameObject.activeSelf == hide)
            {
                Toolbar.Collapsible.gameObject.SetActive(!hide);
            }

            if (Toolbar?.LanguageRow != null)
            {
                Vector2 pos = Toolbar.LanguageRow.anchoredPosition;
                pos.y = -(ModConfigTreeLayoutPatch.ToolbarTop + (hide ? 0f : (ToolbarHeight + ToolbarRowGap) * 2f));
                Toolbar.LanguageRow.anchoredPosition = pos;
            }

            float rows = hide ? 1f : ToolbarRows;
            float top = ModConfigTreeLayoutPatch.ToolbarTop + rows * ToolbarHeight + (rows - 1f) * ToolbarRowGap + 8f;
            RectTransform list = (RectTransform)transform;
            list.offsetMax = new Vector2(list.offsetMax.x, -top);
            if (ScrollbarRect != null)
            {
                ScrollbarRect.offsetMax = new Vector2(ScrollbarRect.offsetMax.x, -top);
            }

            RefreshColumns();
        }

        /// <summary>导出列随「仅导出已选」显示/隐藏（隐藏本地化按钮时强制收掉）；隐藏时置顶列左移、文字跟着左移，不留空档。</summary>
        internal void RefreshColumns()
        {
            bool showExport = ExportColumnVisible;
            float pinLeft = ModConfigTreeLayoutPatch.ModTextIndent + (showExport ? ColumnWidth : 0f) + ColumnInset;
            float indent = TextIndent;
            foreach (RowWidgets widgets in _rows)
            {
                if (widgets.Export.gameObject.activeSelf != showExport)
                {
                    widgets.Export.gameObject.SetActive(showExport);
                }

                ((RectTransform)widgets.Pin.transform).anchoredPosition = new Vector2(pinLeft, 0f);
                if (widgets.Text != null)
                {
                    Vector4 margin = widgets.Text.margin;
                    margin.x = indent;
                    widgets.Text.margin = margin;
                }
            }

            if (ExportCaption != null && ExportCaption.gameObject.activeSelf != showExport)
            {
                ExportCaption.gameObject.SetActive(showExport);
            }

            if (PinCaption != null)
            {
                // 表头里置顶标题跟着列走
                RectTransform rect = PinCaption.rectTransform;
                float left = ModConfigTreeLayoutPatch.ModTextIndent + (showExport ? ColumnWidth : 0f);
                rect.offsetMin = new Vector2(left - 4f, 0f);
                rect.offsetMax = new Vector2(left + ColumnWidth + 4f, 0f);
            }
        }

        /// <summary>
        /// 自身行永远钉在最顶（表头之下第一位），其后才是置顶行（按它们在 ModConfig 列表里的原顺序——置顶是手动意愿，
        /// 排序方式不碰它），其余行按「排序方式」排（默认 = ModConfig 原序），分区容器再贴回当前选中行下面。
        /// 有分组时其余行按概览的分组顺序分段：每段先放分组标题，段内再按「排序方式」排，未分组段在最后。
        /// 只动 sibling 顺序，不碰 ModConfig 的 Tabs / buttons 列表。
        /// </summary>
        internal void ApplyPinOrder()
        {
            if (ModTabs == null || ListContent == null)
            {
                return;
            }

            int index = Header != null ? 1 : 0;
            foreach (GameObject row in ModTabs.Tabs)
            {
                if (row != null && row.name == Plugin.SelfRowName)
                {
                    row.transform.SetSiblingIndex(index++);
                }
            }

            foreach (GameObject row in ModTabs.Tabs)
            {
                if (row != null && row.name != Plugin.SelfRowName && Pinned.Contains(row.name))
                {
                    row.transform.SetSiblingIndex(index++);
                }
            }

            List<GameObject> rest = new List<GameObject>();
            foreach (GameObject row in ModTabs.Tabs)
            {
                if (row != null && row.name != Plugin.SelfRowName && !Pinned.Contains(row.name))
                {
                    rest.Add(row);
                }
            }

            if (_groupSlots.Count == 0)
            {
                SortRest(rest);
                foreach (GameObject row in rest)
                {
                    row.transform.SetSiblingIndex(index++);
                }
            }
            else
            {
                foreach (GroupSlot slot in _groupSlots)
                {
                    slot.Row.transform.SetSiblingIndex(index++);
                    List<GameObject> members = rest.FindAll(row => RowGroup(row) == slot.Id);
                    SortRest(members);
                    foreach (GameObject row in members)
                    {
                        row.transform.SetSiblingIndex(index++);
                    }
                }
            }

            ModdedTABSButton? selected = Menu != null && Menu.ModTabs != null ? Menu.ModTabs.selectedButton : null;
            if (SectionRoot != null && selected != null && selected.transform.parent == SectionRoot.parent)
            {
                SectionRoot.SetAsLastSibling();
                SectionRoot.SetSiblingIndex(selected.transform.GetSiblingIndex() + 1);
            }

            LayoutRebuilder.MarkLayoutForRebuild(ListContent);
        }

        /// <summary>名称排序用 zh-Hans 规则：中文按拼音、拉丁按字母；同键再按 row.name 字典序兜底稳定。</summary>
        private static readonly StringComparer NameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("zh-Hans"), ignoreCase: true);

        private void SortRest(List<GameObject> rest)
        {
            switch (Plugin.SidebarSort.Value)
            {
                case Plugin.SidebarSortMode.LoadReversed:
                    rest.Reverse();
                    break;
                case Plugin.SidebarSortMode.Name:
                case Plugin.SidebarSortMode.NameDesc:
                    bool desc = Plugin.SidebarSort.Value == Plugin.SidebarSortMode.NameDesc;
                    rest.Sort((a, b) =>
                    {
                        int byName = NameComparer.Compare(RowLabel(a), RowLabel(b));
                        if (desc)
                        {
                            byName = -byName;
                        }

                        return byName != 0 ? byName : string.CompareOrdinal(a.name, b.name);
                    });
                    break;
            }
        }

        /// <summary>行的显示名（本地化后的行标签），拿不到退回 row.name。</summary>
        private string RowLabel(GameObject row)
        {
            foreach (RowWidgets widgets in _rows)
            {
                if (widgets.Row == row)
                {
                    return widgets.Text != null && widgets.Text.text.Length > 0 ? widgets.Text.text : row.name;
                }
            }

            return row.name;
        }
    }

    // ── 按钮虚线位置 ────────────────────────────────────────────────

    /// <summary>
    /// PeakMenuButton 模板高 67，上下两条虚线（BorderTop / BorderBottom）锚在顶/底边、各向内偏 15 / 14；
    /// 按钮压矮之后两条线没跟着缩：30 高的「默认」钮上两条正好都落在中线上叠成一条（20 高的则上下互换、
    /// 仍各贴一边，所以只有数值 / 枚举格看得出来）。按「实高 / 模板高」把模板里的偏移等比缩回去；
    /// 直接从 PEAKLib 的模板读原值，重复调用得到同一结果。上下拉伸式锚点的偏移本来就贴边，不动。
    /// </summary>
    internal static void FitBorders(PeakMenuButton button)
    {
        if (!ResolveBorderTemplate() || button.RectTransform == null)
        {
            return;
        }

        // Stretch-layout buttons have sizeDelta.y == 0 even when their visible height is nonzero.
        // Use the resolved rectangle so settings-page entries receive the same fit as fixed-size buttons.
        float height = button.RectTransform.rect.height;
        if (height <= 0f)
        {
            return;
        }

        float ratio = height / _templateHeight;
        Apply(button.BorderTop, _templateTopY);
        Apply(button.BorderBottom, _templateBottomY);

        void Apply(Image? border, float templateY)
        {
            if (border == null)
            {
                return;
            }

            RectTransform rect = border.rectTransform;
            if (Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            {
                Vector2 position = rect.anchoredPosition;
                position.y = templateY * ratio;
                rect.anchoredPosition = position;
            }
        }
    }

    private static bool _templateResolved;
    private static float _templateHeight;
    private static float _templateTopY;
    private static float _templateBottomY;

    /// <summary>从 Templates.ButtonTemplate 读一次模板高与两条虚线的锚点偏移（与 PeakMenuButton.Awake 同一套找法）。</summary>
    private static bool ResolveBorderTemplate()
    {
        if (_templateResolved)
        {
            return _templateHeight > 0f;
        }

        GameObject? template = Templates.ButtonTemplate;
        if (template == null)
        {
            return false; // 还没到 PEAKLib 建模板的时候，下次再试
        }

        _templateResolved = true;
        try
        {
            Transform? top = template.transform.Find("Border");
            Transform? bottom = top != null && top.GetSiblingIndex() + 1 < template.transform.childCount
                ? template.transform.GetChild(top.GetSiblingIndex() + 1)
                : null;
            if (top is RectTransform topRect && bottom is RectTransform bottomRect)
            {
                _templateHeight = template.GetComponent<RectTransform>().rect.height;
                _templateTopY = topRect.anchoredPosition.y;
                _templateBottomY = bottomRect.anchoredPosition.y;
                Plugin.Log.LogDebug($"[面板布局] 按钮虚线模板: 高 {_templateHeight}，上 {_templateTopY} 下 {_templateBottomY}");
            }
            else
            {
                Plugin.Log.LogWarning("[面板布局] PeakMenuButton 模板里找不到两条虚线，按钮虚线归位跳过。");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[面板布局] 读按钮模板失败，虚线归位跳过: {ex.Message}");
        }

        return _templateHeight > 0f;
    }
}
