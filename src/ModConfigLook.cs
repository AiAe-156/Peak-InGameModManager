#nullable enable
using System;
using HarmonyLib;
using UnityEngine;

namespace ModConfigEnhance;

/// <summary>
/// 2.4.0：MCE 全部界面的配色主题，唯一来源。
///
/// 两套主题：
///   · 蓝色：跟随游戏 3.1 的设置菜单（<c>Peak.UI.SettingsMenu.SettingsColors</c>，运行时反射读取，读不到用同值常量）；
///   · 紫色：沿用早先 TimeTheme 夜间的风格 —— 近黑的紫底、淡紫文字、紫色选中。
/// 第三个选项「夜里自动紫色」只读 TimeTheme 的昼夜状态（白天蓝、夜里紫），不再读它的配色表、不再让它重新收表。
///
/// 模组设置页：自建元素在建的时候直接取这里的颜色；主题切换时由 <see cref="ApplyMain"/> / <see cref="ApplyOptions"/>
/// 按「另一套主题的同一格颜色 → 当前主题」逐个换色（只认这里登记的颜色，不会染到别的东西）。
/// 概览页：颜色统一过 <see cref="Tone"/>：暖色中性（旧的棕、金、象牙）与灰色转成主题色相，内容色（状态角标、分组色、
/// 警告橙、绿/红按钮）原样保留。概览页在主题变化后下次打开时重建（<see cref="Epoch"/>）。
/// </summary>
internal static class ModConfigLook
{
    internal enum ThemeMode
    {
        Blue,
        Purple,
        PurpleAtNight,
    }

    private const string SettingsColorsTypeName = "Peak.UI.SettingsMenu.SettingsColors";

    // ── 两套主题的固定格 ──────────────────────────────────────────

    /// <summary>游戏 3.1 SettingsColors 的同值常量（读不到时用）。</summary>
    private static readonly Color GameBox = new Color(0.07075472f, 0.1818232f, 0.2830189f, 62f / 85f);
    private static readonly Color GameText = new Color(0.874f, 0.857f, 0.762f, 1f);
    private static readonly Color GameTextSelected = new Color(0.071f, 0.182f, 0.283f, 1f);

    /// <summary>紫色主题：底色近黑偏紫（保留原透明度），选中底是压暗一档的紫，文字淡紫。</summary>
    private static readonly Color PurpleBox = new Color(0.086f, 0.043f, 0.137f, 62f / 85f);
    private static readonly Color PurpleCream = new Color(0.651f, 0.376f, 0.753f, 1f);
    private static readonly Color PurpleProminent = new Color(0.784f, 0.498f, 0.976f, 1f);
    private static readonly Color PurpleTextSelected = new Color(0.102f, 0.039f, 0.161f, 1f);
    private static readonly Color PurpleSelectedFill = new Color(0.611f, 0.380f, 0.769f, 1f);

    /// <summary>米白：复选框描边、工具钮底、滚动条滑块（蓝色主题与游戏米色同系）。</summary>
    internal static readonly Color BlueCream = new Color(0.8745098f, 0.854902f, 0.7607843f, 1f);

    /// <summary>旧版棕底（PeakHorizontalTabs 默认值）：只作为「需要换掉的旧颜色」登记，不再用来上色。</summary>
    private static readonly Color LegacyBrown = new Color(0.1792453f, 0.1253449f, 0.09046815f, 62f / 85f);

    /// <summary>置顶竖线：两套主题都不用的青柠黄绿（用户定 #C8F05A）。</summary>
    internal static readonly Color PinAccent = new Color(0.784f, 0.941f, 0.353f, 1f);

    private static bool _resolved;
    private static Color _gameBox = GameBox, _gameText = GameText, _gameTextSelected = GameTextSelected;

    // ── 当前主题 ──────────────────────────────────────────────────

    private static int _darkFrame = -1;
    private static bool _darkCache;
    private static bool? _lastDark;
    private static int _epoch;

    /// <summary>当前是否紫色主题。每帧最多算一次。</summary>
    internal static bool IsDark
    {
        get
        {
            int frame = Time.frameCount;
            if (frame == _darkFrame)
            {
                return _darkCache;
            }

            _darkFrame = frame;
            ThemeMode mode = Plugin.ColourTheme != null ? Plugin.ColourTheme.Value : ThemeMode.Blue;
            _darkCache = mode switch
            {
                ThemeMode.Purple => true,
                ThemeMode.PurpleAtNight => TimeThemeBridge.IsDark,
                _ => false,
            };
            if (_lastDark != _darkCache)
            {
                _lastDark = _darkCache;
                _epoch++;
            }

            return _darkCache;
        }
    }

    /// <summary>主题每变一次加一。概览页记下建页时的值，不一致就在下次打开前重建。</summary>
    internal static int Epoch
    {
        get
        {
            _ = IsDark;
            return _epoch;
        }
    }

    internal static string State
    {
        get
        {
            Resolve();
            ThemeMode mode = Plugin.ColourTheme != null ? Plugin.ColourTheme.Value : ThemeMode.Blue;
            return $"{mode}（当前{(IsDark ? "紫" : "蓝")}；游戏配色{(_resolvedFromGame ? "已读取" : "未找到，用同值常量")}；TimeTheme {(TimeThemeBridge.Available ? "在场" : "未装")}）";
        }
    }

    /// <summary>行 / 面板 / 页签 / 选项单元格的底色（半透明）。</summary>
    internal static Color Box => BoxFor(IsDark);

    /// <summary>指定主题的底色 / 米白（给 ThemeTint 这种同时要两套颜色的地方）。</summary>
    internal static Color BoxOf(bool dark) => BoxFor(dark);
    internal static Color CreamOf(bool dark) => dark ? PurpleCream : BlueCream;

    /// <summary>强调色（ModConfig 筛选下拉）：蓝色主题用游戏「模组设置」按钮的蓝，紫色主题用紫。</summary>
    internal static Color Accent => IsDark ? new Color(0.392f, 0.184f, 0.624f, 1f) : new Color(0.185f, 0.394f, 0.6226f, 1f);

    /// <summary>米白格：复选框描边、工具钮底、滚动条滑块。</summary>
    internal static Color Cream => IsDark ? PurpleCream : BlueCream;

    /// <summary>显眼文字（标题、选项名、说明面板标题）：蓝色主题白，紫色主题亮紫。</summary>
    internal static Color Prominent => IsDark ? PurpleProminent : Color.white;

    /// <summary>页签未选中的文字色。</summary>
    internal static Color TextDefault => IsDark ? PurpleProminent : GameTextColor;

    /// <summary>页签选中的文字色。</summary>
    internal static Color TextSelected
    {
        get
        {
            Resolve();
            return IsDark ? PurpleTextSelected : _gameTextSelected;
        }
    }

    /// <summary>页签选中底图（「Selected」）：蓝色主题与原版选中页签一样是米色，紫色主题是紫。</summary>
    internal static Color SelectedFill => IsDark ? PurpleSelectedFill : GameTextColor;

    private static Color GameTextColor
    {
        get
        {
            Resolve();
            return _gameText;
        }
    }

    private static Color BoxFor(bool dark)
    {
        Resolve();
        return dark ? PurpleBox : _gameBox;
    }

    private static bool _resolvedFromGame;

    private static void Resolve()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        try
        {
            Type? type = AccessTools.TypeByName(SettingsColorsTypeName);
            if (type == null)
            {
                return;
            }

            Color? Read(string name) => AccessTools.Field(type, name)?.GetValue(null) is Color c ? c : (Color?)null;
            Color? box = Read("BoxColor"), text = Read("TextDefaultColor"), selected = Read("TextSelectedColor");
            if (box == null || text == null || selected == null)
            {
                Plugin.Log.LogWarning("[配色] 游戏的 SettingsColors 结构变了，蓝色主题用内置同值常量。");
                return;
            }

            _gameBox = box.Value;
            _gameText = text.Value;
            _gameTextSelected = selected.Value;
            _resolvedFromGame = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[配色] 读取游戏配色失败，蓝色主题用内置同值常量：" + ex.Message);
        }
    }

    // ── 模组设置页：按登记的颜色换主题 ───────────────────────────────

    /// <summary>主表：底色与米白格（页面上所有自建元素、页签行、选项单元格底）。旧棕色也登记成「要换掉的颜色」。</summary>
    internal static void ApplyMain(Transform? root)
    {
        if (root == null)
        {
            return;
        }

        bool dark = IsDark;
        Color box = BoxFor(dark), boxOther = BoxFor(!dark);
        Color cream = dark ? PurpleCream : BlueCream, creamOther = dark ? BlueCream : PurpleCream;
        foreach (UnityEngine.UI.Graphic graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            if (graphic == null)
            {
                continue;
            }

            Color current = graphic.color;
            if (SameRgb(current, boxOther) || SameRgb(current, LegacyBrown))
            {
                graphic.color = new Color(box.r, box.g, box.b, current.a);
            }
            else if (SameRgb(current, creamOther))
            {
                graphic.color = new Color(cream.r, cream.g, cream.b, current.a);
            }
        }
    }

    /// <summary>
    /// 选项表：标题、搜索标签、工具行、表头、说明面板与选项单元格里的白字（紫色主题变亮紫），
    /// 以及输入框 / 下拉框的 ColorBlock（Selectable 的着色不走 Graphic.color，主表碰不到）。
    /// 控件自己的底图保持白色，具体颜色交给 ColorBlock，避免两层紫色相乘。
    /// </summary>
    internal static void ApplyOptions(Transform? root)
    {
        if (root == null)
        {
            return;
        }

        bool dark = IsDark;
        Color prominent = dark ? PurpleProminent : Color.white, other = dark ? Color.white : PurpleProminent;
        var controlTargets = new System.Collections.Generic.HashSet<UnityEngine.UI.Graphic>();
        foreach (TMPro.TMP_InputField input in root.GetComponentsInChildren<TMPro.TMP_InputField>(true))
        {
            PrepareControlTarget(input, controlTargets);
            input.colors = OptionBlock(dark);
        }

        foreach (TMPro.TMP_Dropdown dropdown in root.GetComponentsInChildren<TMPro.TMP_Dropdown>(true))
        {
            PrepareControlTarget(dropdown, controlTargets);
            dropdown.colors = OptionBlock(dark);
        }

        foreach (UnityEngine.UI.Graphic graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            if (graphic == null || controlTargets.Contains(graphic))
            {
                continue;
            }

            Color current = graphic.color;
            if (SameRgb(current, other))
            {
                graphic.color = new Color(prominent.r, prominent.g, prominent.b, current.a);
            }
        }
    }

    private static void PrepareControlTarget(UnityEngine.UI.Selectable control, System.Collections.Generic.HashSet<UnityEngine.UI.Graphic> targets)
    {
        UnityEngine.UI.Graphic target = control.targetGraphic;
        if (target == null)
        {
            return;
        }

        targets.Add(target);
        Color current = target.color;
        if (SameRgb(current, PurpleProminent) || SameRgb(current, Dim(PurpleProminent)))
        {
            target.color = new Color(1f, 1f, 1f, current.a);
        }
    }

    /// <summary>输入框 / 下拉框的配色块：蓝色主题是 Unity 默认的白系，紫色主题照早先夜间风格压暗一档的紫。</summary>
    private static UnityEngine.UI.ColorBlock OptionBlock(bool dark)
    {
        UnityEngine.UI.ColorBlock block = UnityEngine.UI.ColorBlock.defaultColorBlock;
        if (dark)
        {
            block.normalColor = Dim(PurpleProminent);
            block.highlightedColor = Dim(new Color(0.745f, 0.463f, 0.937f, 1f));
            block.selectedColor = block.highlightedColor;
            block.pressedColor = new Color(0.572f, 0.290f, 0.753f, 1f);
            block.disabledColor = new Color(0.572f, 0.290f, 0.753f, 0.5f);
        }
        else
        {
            block.normalColor = Color.white;
            block.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
            block.selectedColor = block.highlightedColor;
            block.pressedColor = new Color(0.784f, 0.784f, 0.784f, 1f);
            block.disabledColor = new Color(0.784f, 0.784f, 0.784f, 0.5f);
        }

        block.colorMultiplier = 1f;
        block.fadeDuration = 0.1f;
        return block;
    }

    private static Color Dim(Color color) => new Color(color.r * 0.82f, color.g * 0.82f, color.b * 0.82f, color.a);

    /// <summary>
    /// 近似比色：游戏预制体里的 BoxColor 是 (0.071,0.182,0.283)，SettingsColors 里是 (0.07075,0.18182,0.28302)，
    /// TimeTheme 的夜间紫也只到小数点后四位 —— 精确比较认不出同一格颜色。每通道差 0.004 以内算同色。
    /// </summary>
    private static bool SameRgb(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

    // ── 概览页：统一换色 ──────────────────────────────────────────

    /// <summary>蓝色主题的色相（与游戏 BoxColor 同系）、紫色主题的色相。</summary>
    internal const float BlueHue = 207f / 360f, PurpleHue = 272f / 360f;

    /// <summary>
    /// 把一个「旧设计」颜色换成当前主题：
    ///   · 已经在主题色相附近的不动（反复调用结果不变）；
    ///   · 浅色中性（文字、象牙、米白线条）：蓝色主题不动，紫色主题转淡紫；
    ///   · 暖色中性（棕、金、象牙暗部）、灰色：转主题色相，饱和度略提；
    ///   · 冷色（蓝、青灰）：蓝色主题不动，紫色主题转紫；
    ///   · 其余（绿、红、橙、粉、高饱和的金等内容色）原样。
    /// </summary>
    internal static Color Tone(Color color) => ToneFor(color, IsDark);

    /// <summary>界面「骨架」颜色（面板、行底、滚动条、分隔线、中性按钮）：同 <see cref="Tone"/>，名字上区分用途。</summary>
    internal static Color Surface(Color color) => ToneFor(color, IsDark);
    internal static Color SurfaceFor(Color color, bool dark) => ToneFor(color, dark);

    internal static Color ToneFor(Color color, bool dark)
    {
        Color.RGBToHSV(color, out float h, out float s, out float v);
        float hue = dark ? PurpleHue : BlueHue;
        if (s >= 0.08f && HueDistance(h, hue) < 15f / 360f)
        {
            return color;
        }

        Color result;
        if (v > 0.7f && s < 0.32f)
        {
            if (!dark)
            {
                return color;
            }

            result = Color.HSVToRGB(hue, Mathf.Min(1f, s * 0.6f + 0.22f), v);
        }
        else
        {
            bool grey = s < 0.08f;
            bool warm = h >= 15f / 360f && h <= 65f / 360f && s < 0.7f && v < 0.8f; // 亮的橙 / 金是内容色（警告、提示），不换
            bool cool = h >= 165f / 360f && h <= 232f / 360f && s < 0.8f;
            if (!(grey || warm || cool))
            {
                return color;
            }

            if (cool && !dark)
            {
                return color;
            }

            float saturation = grey ? (dark ? 0.30f : 0.34f) : Mathf.Clamp(s * (dark ? 1.15f : 1.35f), 0f, 0.8f);
            result = Color.HSVToRGB(hue, saturation, v);
        }

        result.a = color.a;
        return result;
    }

    private static float HueDistance(float a, float b)
    {
        float d = Mathf.Abs(a - b);
        return Mathf.Min(d, 1f - d);
    }
}
