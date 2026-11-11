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

W = 'ModConfigSidebarWidgets.cs'
edit(W, [
('''/// 颜色全部取自 TimeTheme 配色表里已有的那几格（米白 ↔ 夜间紫、页签棕 ↔ 黑），
/// 于是 <see cref="TimeThemeBridge.ApplyTheme"/> 扫一遍就自动跟昼夜，不用另写夜间分支；没装 TimeTheme 就是原版米白。''',
'''/// 颜色全部取自 <see cref="ModConfigLook"/> 登记的主题格（底色、米白），主题切换时由
/// <see cref="ModConfigLook.ApplyMain"/> 扫一遍换色；不在登记表里的颜色（✓、行名金色、「默认」钮）由 <see cref="ThemeTint"/> 自己切。'''),
('''    /// <summary>TimeTheme 主表第 19 组的白天色（米白 #DFDAC2），夜间对应紫 (0.65,0.38,0.75)。</summary>
    internal static readonly Color Cream = new Color(0.8745098f, 0.854902f, 0.7607843f, 1f);''',
'''    /// <summary>米白（#DFDAC2）：当前主题的同一格见 <see cref="ModConfigLook.Cream"/>，建元素时用那个。</summary>
    internal static Color Cream => ModConfigLook.Cream;'''),
('''    /// <summary>✓ 与置顶竖条的颜色：白天低饱和天蓝，夜间深一档的蓝（用户定）。</summary>''',
'''    /// <summary>✓ 的颜色：蓝色主题低饱和天蓝，紫色主题深一档的蓝（用户定）。置顶竖线另用 <see cref="ModConfigLook.PinAccent"/>。</summary>'''),
('''    /// <summary>不在 TimeTheme 表里的颜色自己按昼夜切（✓、置顶竖条）。IsDark 每帧有缓存，Update 很便宜。</summary>''',
'''    /// <summary>不在主题登记表里的颜色自己按主题切（✓、行名金色、「默认」钮）。IsDark 每帧有缓存，Update 很便宜。</summary>'''),
('''        /// <summary>置顶行左缘 4 宽的竖条，颜色与 ✓ 同（白天天蓝 / 夜间深蓝），一眼能认出哪些是钉住的。</summary>''',
'''        /// <summary>置顶行左缘的细竖线：青柠黄绿（两套主题与分组色都不用的颜色，用户定），一眼能认出哪些是钉住的。</summary>'''),
('''            rect.offsetMin = new Vector2(0f, 4f);
            rect.offsetMax = new Vector2(4f, -4f);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            ApplyRounded(image, 2f);
            Tint(image, MarkDay, MarkNight);
            go.SetActive(active);''',
'''            rect.offsetMin = new Vector2(0f, 4f);
            rect.offsetMax = new Vector2(PinAccentWidth, -4f);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = ModConfigLook.PinAccent;
            go.SetActive(active);'''),
])

G = 'ModConfigSidebarWidgets.Groups.cs'
edit(G, [
('''    private const float RailWidth = 4f;
    private const float PinnedRailLeft = RailWidth + 1f;''',
'''    private const float RailWidth = 4f;

    /// <summary>置顶竖线（2.4.0 改细）：2 宽，分组色条紧贴它右侧。</summary>
    internal const float PinAccentWidth = 2f;
    private const float PinnedRailLeft = PinAccentWidth + 1f;'''),
])
print('ok')
