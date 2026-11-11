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

edit('Plugin.cs', [
('''    internal static ConfigEntry<bool> ClassicBrownMenu = null!;
    internal const string DescClassicBrownMenu = "The mod settings page follows the game's settings colours (dark blue since PEAK 3.1). Turn this on to keep the classic brown look of earlier versions. Restart to apply.";''',
'''    internal static ConfigEntry<ModConfigLook.ThemeMode> ColourTheme = null!;
    internal const string DescColourTheme = "Colour theme of every MCE screen. Blue follows the game's settings menu; Purple is the night style; PurpleAtNight switches to purple at night when TimeTheme is installed (only its day/night state is read). The mod settings page switches at once; an open overview switches the next time it is opened.";'''),
('''        ClassicBrownMenu = Config.Bind(secLayout, "Classic brown colours", false, DescClassicBrownMenu);''',
'''        ColourTheme = Config.Bind(secLayout, "Colour theme", ModConfigLook.ThemeMode.Blue, DescColourTheme);'''),
])

edit('Loc.cs', [
('''        new("Classic brown colours", "Classic brown colours", "经典棕色配色", "經典棕色配色"),''',
'''        new("Colour theme", "Colour theme", "配色主题", "配色主題"),
        new("Blue", "Blue", "蓝色", "藍色"),
        new("Purple", "Purple", "紫色", "紫色"),
        new("PurpleAtNight", "PurpleAtNight", "夜里自动紫色", "夜裡自動紫色"),'''),
('''        new(Plugin.DescClassicBrownMenu, Plugin.DescClassicBrownMenu,
            "模组设置页跟随游戏设置菜单的配色（PEAK 3.1 起为深蓝）。打开后保持旧版的棕色外观。重启生效。",
            "模組設定頁跟隨遊戲設定選單的配色（PEAK 3.1 起為深藍）。開啟後保持舊版的棕色外觀。重新啟動生效。"),''',
'''        new(Plugin.DescColourTheme, Plugin.DescColourTheme,
            "MCE 全部界面的配色主题。蓝色跟随游戏的设置菜单；紫色是夜间风格；夜里自动紫色在装了 TimeTheme 时夜里切到紫色（只读它的昼夜状态）。模组设置页即时切换；已打开过的概览在下次打开时切换。",
            "MCE 全部介面的配色主題。藍色跟隨遊戲的設定選單；紫色是夜間風格；夜裡自動紫色在裝了 TimeTheme 時夜裡切到紫色（只讀它的晝夜狀態）。模組設定頁即時切換；已開啟過的概覽在下次開啟時切換。"),'''),
])
print('ok')
