import io, sys
SRC = sys.argv[1]

def edit(p, pairs):
    p = SRC + '/' + p
    s = io.open(p, encoding='utf-8').read()
    for a, b in pairs:
        assert s.count(a) == 1, (p, a[:60], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('ModConfigTreeLayoutPatch.cs', [
('''    /// <summary>PeakHorizontalTabs 给页签底色用的默认色，我们自己加的行沿用它，TimeTheme 也认这个色。</summary>
    internal static readonly Color TabBackground = new Color(0.1792453f, 0.1253449f, 0.09046815f, 62f / 85f);''',
'''    /// <summary>自建行 / 面板的底色。2.3.12 起默认跟随游戏 3.1 的 SettingsColors.BoxColor（深蓝），
    /// 「经典棕色」开关或旧版游戏用 PeakHorizontalTabs 的棕色（TimeTheme 夜里认这个色）。见 <see cref="ModConfigLook"/>。</summary>
    internal static Color TabBackground => ModConfigLook.Box;'''),
('''        // 页签文字的夜间色：只在 TimeTheme 在场时接管（对应它给原版 SettingsTABSButton.Update 打的补丁）
        if (TimeThemeBridge.Available)''',
'''        // 页签文字色：跟随游戏新配色时接管（选中深蓝字 / 未选中米色字），TimeTheme 在场时再管夜间色
        Plugin.Log.LogInfo("[配色] " + ModConfigLook.State);
        if (TimeThemeBridge.Available || !ModConfigLook.Classic)'''),
('''            ModConfigPaperStyle.Apply(image, 8f);
            if (backgroundAlpha.HasValue)
            {
                Color color = image.color;
                color.a = backgroundAlpha.Value;
                image.color = color;
            }''',
'''            ModConfigPaperStyle.Apply(image, 8f);
            // 2.3.12：PeakHorizontalTabs.AddTab 写死棕色，改成当前配色（保留各自透明度）
            Color box = ModConfigLook.Box;
            image.color = new Color(box.r, box.g, box.b, backgroundAlpha ?? image.color.a);'''),
('''            ModConfigPaperStyle.Apply(selected, 8f);
        }
    }''',
'''            ModConfigPaperStyle.Apply(selected, 8f);
            Color fill = ModConfigLook.SelectedFill;
            selected.color = new Color(fill.r, fill.g, fill.b, selected.color.a);
        }
    }'''),
('''            button.text.color = button.Selected ? Color.black : (dark ? TimeThemeBridge.DarkTabText : Color.white);''',
'''            button.text.color = button.Selected ? ModConfigLook.TextSelected : (dark ? TimeThemeBridge.DarkTabText : ModConfigLook.TextDefault);'''),
('''        bool dark = TimeThemeBridge.IsDark;
        TimeThemeBridge.TintSelectedGraphic(button.SelectedGraphic, dark);

        if (!dark || button.text == null || button.Selected)
        {
            return true;
        }
''',
'''        bool dark = TimeThemeBridge.IsDark;
        TimeThemeBridge.TintSelectedGraphic(button.SelectedGraphic, dark);

        if (!dark || button.text == null || button.Selected)
        {
            if (ModConfigLook.Classic || button.text == null)
            {
                return true;
            }

            // 跟随游戏 3.1：与原版 SettingsTABSButton.Update 同一套渐变，目标色换成 SettingsColors
            Color target = button.Selected ? ModConfigLook.TextSelected : ModConfigLook.TextDefault;
            button.text.color = Color.Lerp(button.text.color, target, Time.unscaledDeltaTime * 7f);
            if (button.SelectedGraphic != null)
            {
                button.SelectedGraphic.SetActive(button.Selected);
            }

            return false;
        }
'''),
])

edit('TimeThemeBridge.cs', [
('''        if (selectedGraphic == null || !Available)
        {
            return;
        }''',
'''        // TimeTheme 不在场且是经典配色：底图从未被改过，无需处理。跟随游戏配色时白天要涂米色，与它无关
        if (selectedGraphic == null || (!Available && ModConfigLook.Classic))
        {
            return;
        }'''),
('''            Color target = dark ? DarkSelectedFill : Color.white;''',
'''            Color target = dark ? DarkSelectedFill : ModConfigLook.SelectedFill;'''),
])

edit('Plugin.cs', [
('''    internal static ConfigEntry<bool> EnableDescriptionPanel = null!;''',
'''    internal static ConfigEntry<bool> EnableDescriptionPanel = null!;
    internal static ConfigEntry<bool> ClassicBrownMenu = null!;
    internal const string DescClassicBrownMenu = "The mod settings page follows the game's settings colours (dark blue since PEAK 3.1). Turn this on to keep the classic brown look of earlier versions. Restart to apply.";'''),
('''        EnableDescriptionPanel = Config.Bind(secLayout, "Description panel", true, DescDescriptionPanel);''',
'''        EnableDescriptionPanel = Config.Bind(secLayout, "Description panel", true, DescDescriptionPanel);
        ClassicBrownMenu = Config.Bind(secLayout, "Classic brown colours", false, DescClassicBrownMenu);'''),
])

edit('Loc.cs', [
('''        new("Description panel", "Description panel", "选项说明面板", "選項說明面板"),''',
'''        new("Description panel", "Description panel", "选项说明面板", "選項說明面板"),
        new("Classic brown colours", "Classic brown colours", "经典棕色配色", "經典棕色配色"),'''),
('''        new(Plugin.DescDescriptionPanel, Plugin.DescDescriptionPanel,''',
'''        new(Plugin.DescClassicBrownMenu, Plugin.DescClassicBrownMenu,
            "模组设置页跟随游戏设置菜单的配色（PEAK 3.1 起为深蓝）。打开后保持旧版的棕色外观。重启生效。",
            "模組設定頁跟隨遊戲設定選單的配色（PEAK 3.1 起為深藍）。開啟後保持舊版的棕色外觀。重新啟動生效。"),
        new(Plugin.DescDescriptionPanel, Plugin.DescDescriptionPanel,'''),
])
print("ok")
