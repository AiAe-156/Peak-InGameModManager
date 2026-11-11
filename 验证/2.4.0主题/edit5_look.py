import io, sys
SRC = sys.argv[1]

def edit(p, pairs):
    p = SRC + '/' + p
    s = io.open(p, encoding='utf-8').read()
    for a, b in pairs:
        n = s.count(a)
        assert n == 1, (p, a[:70], n)
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('ModConfigLook.cs', [
('''    private static bool SameRgb(Color a, Color b) => new Vector3(a.r, a.g, a.b) == new Vector3(b.r, b.g, b.b);''',
'''    /// <summary>
    /// 近似比色：游戏预制体里的 BoxColor 是 (0.071,0.182,0.283)，SettingsColors 里是 (0.07075,0.18182,0.28302)，
    /// TimeTheme 的夜间紫也只到小数点后四位 —— 精确比较认不出同一格颜色。每通道差 0.004 以内算同色。
    /// </summary>
    private static bool SameRgb(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;'''),
('''    /// <summary>行 / 面板 / 页签 / 选项单元格的底色（半透明）。</summary>
    internal static Color Box => BoxFor(IsDark);''',
'''    /// <summary>行 / 面板 / 页签 / 选项单元格的底色（半透明）。</summary>
    internal static Color Box => BoxFor(IsDark);

    /// <summary>指定主题的底色 / 米白（给 ThemeTint 这种同时要两套颜色的地方）。</summary>
    internal static Color BoxOf(bool dark) => BoxFor(dark);
    internal static Color CreamOf(bool dark) => dark ? PurpleCream : BlueCream;

    /// <summary>强调色（ModConfig 筛选下拉）：蓝色主题用游戏「模组设置」按钮的蓝，紫色主题用紫。</summary>
    internal static Color Accent => IsDark ? new Color(0.392f, 0.184f, 0.624f, 1f) : new Color(0.185f, 0.394f, 0.6226f, 1f);'''),
('''    internal static Color Tone(Color color) => ToneFor(color, IsDark);''',
'''    internal static Color Tone(Color color) => ToneFor(color, IsDark);

    /// <summary>界面「骨架」颜色（面板、行底、滚动条、分隔线、中性按钮）：同 <see cref="Tone"/>，名字上区分用途。</summary>
    internal static Color Surface(Color color) => ToneFor(color, IsDark);'''),
])
print('ok')
