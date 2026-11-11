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

edit('Overview/OverviewUi.cs', [
('''    internal static readonly Color Ivory = new(.94f, .91f, .81f);
    internal static readonly Color Muted = new(.70f, .70f, .64f);
    internal static readonly Color Blue = new(.40f, .72f, .82f);
    internal static readonly Color Green = new(.50f, .77f, .49f);
    internal static readonly Color Brown = new(.16f, .13f, .10f, .96f);
    internal static readonly Color Gold = new(.70f, .60f, .37f);''',
'''    // 2.4.0：骨架色随主题（ModConfigLook.Surface）；Blue / Green 是内容色，两套主题都不变。
    internal static Color Ivory => ModConfigLook.Surface(new(.94f, .91f, .81f));
    internal static Color Muted => ModConfigLook.Surface(new(.70f, .70f, .64f));
    internal static readonly Color Blue = new(.40f, .72f, .82f);
    internal static readonly Color Green = new(.50f, .77f, .49f);
    internal static Color Brown => ModConfigLook.Surface(new(.16f, .13f, .10f, .96f));
    internal static Color Gold => ModConfigLook.Surface(new(.70f, .60f, .37f));'''),
])
edit('Overview/OverviewUi.SaveDialogs.cs', [
('''    private static readonly Color DialogSurface = new(.105f, .12f, .135f, 1);
    private static readonly Color DialogInset = new(.065f, .08f, .095f, 1);''',
'''    private static Color DialogSurface => ModConfigLook.Surface(new(.105f, .12f, .135f, 1));
    private static Color DialogInset => ModConfigLook.Surface(new(.065f, .08f, .095f, 1));'''),
])
edit('Overview/LayoutReviewView.cs', [
('''    private static readonly Color CardColor = new(.13f, .15f, .15f, .97f), RowColor = new(.09f, .10f, .10f, .92f), HostColor = new(.065f, .08f, .095f, 1);''',
'''    private static Color CardColor => ModConfigLook.Surface(new(.13f, .15f, .15f, .97f));
    private static Color RowColor => ModConfigLook.Surface(new(.09f, .10f, .10f, .92f));
    private static Color HostColor => ModConfigLook.Surface(new(.065f, .08f, .095f, 1));'''),
])
edit('Overview/Editor/DocToolbar.cs', [
('''    internal static readonly Color Normal = new(.23f, .19f, .14f, .96f), Hover = new(.33f, .27f, .19f, .98f), Active = new(.56f, .45f, .22f, 1), Strip = new(.13f, .11f, .08f, .92f);''',
'''    internal static Color Normal => ModConfigLook.Surface(new(.23f, .19f, .14f, .96f));
    internal static Color Hover => ModConfigLook.Surface(new(.33f, .27f, .19f, .98f));
    internal static Color Active => ModConfigLook.Surface(new(.56f, .45f, .22f, 1));
    internal static Color Strip => ModConfigLook.Surface(new(.13f, .11f, .08f, .92f));'''),
])
edit('Overview/OverviewRowActions.cs', [
('''    private static readonly Color Disabled = new(.37f, .39f, .37f);''',
'''    private static Color Disabled => ModConfigLook.Surface(new(.37f, .39f, .37f));'''),
])

# Neutral action-button role (brown) follows the theme; content roles (edit blue, commit green, danger red, overview teal) stay.
edit('UiActionStyle.cs', [
('''            _ => new Color32(0x4a, 0x3b, 0x29, 255)
        };''',
'''            _ => ModConfigLook.SurfaceFor(new Color32(0x4a, 0x3b, 0x29, 255), dark)
        };'''),
('''        if (!enabled) color = Color.Lerp(color, new Color(.23f, .22f, .20f), .7f);''',
'''        if (!enabled) color = Color.Lerp(color, ModConfigLook.SurfaceFor(new Color(.23f, .22f, .20f), dark), .7f);'''),
])
edit('ModConfigLook.cs', [
('''    internal static Color Surface(Color color) => ToneFor(color, IsDark);''',
'''    internal static Color Surface(Color color) => ToneFor(color, IsDark);
    internal static Color SurfaceFor(Color color, bool dark) => ToneFor(color, dark);'''),
])
edit('ModConfigTranslationExport.cs', [
('''    private static readonly Color DisabledColor = new Color(0.3f, 0.3f, 0.3f);
    private static readonly Color FolderColor = new Color(0.55f, 0.36f, 0.16f);''',
'''    private static readonly Color DisabledColor = new Color(0.3f, 0.3f, 0.3f);
    private static Color FolderColor => ModConfigLook.Surface(new Color(0.55f, 0.36f, 0.16f));'''),
])
print('ok')
