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

T = 'ModConfigTreeLayoutPatch.cs'
edit(T, [
# patch registration: always
('''        // 页签文字色：跟随游戏新配色时接管（选中深蓝字 / 未选中米色字），TimeTheme 在场时再管夜间色
        Plugin.Log.LogInfo("[配色] " + ModConfigLook.State);
        if (TimeThemeBridge.Available || !ModConfigLook.Classic)
        {''',
'''        // 页签文字色与选中底图：按 MCE 主题（蓝色主题 = 游戏 3.1 页签的米色 / 深蓝，紫色主题 = 淡紫 / 紫）
        Plugin.Log.LogInfo("[配色] " + ModConfigLook.State);
        {'''),
('''                Plugin.Log.LogWarning("[面板布局] 找不到 ModdedTABSButton.Update，夜间模式下页签文字保持白色。");''',
'''                Plugin.Log.LogWarning("[面板布局] 找不到 ModdedTABSButton.Update，页签文字保持 ModConfig 原色。");'''),
('''        State = $"已挂载（ModConfig {ModConfigSupport.InstalledVersion}{(TimeThemeBridge.Available ? "，随 TimeTheme 昼夜跟色" : "")}）";''',
'''        State = $"已挂载（ModConfig {ModConfigSupport.InstalledVersion}，主题 {Plugin.ColourTheme.Value}）";'''),
# search box
('''        // 3d. 搜索框：夜间按 TimeTheme 的输入框 ColorBlock 上色（它自己收不到这个后建的控件）
        if (TimeThemeBridge.Available)
        {
            Transform? search = page.Find("SearchInput");
            if (search != null)
            {
                TimeThemeBridge.ApplyOptionsTheme(search, TimeThemeBridge.IsDark);
            }
        }''',
'''        // 3d. 搜索框：按主题上色（ColorBlock）
        ModConfigLook.ApplyOptions(page.Find("SearchInput"));'''),
# page theme follower
('''        // 5. 昼夜主题：让 TimeTheme 把这页收进它的表（之后的切换才覆盖得到），再把此刻该有的颜色刷上去
        if (TimeThemeBridge.Available)
        {
            if (inPauseMenu)
            {
                TimeThemeBridge.RefreshLists();
            }

            TimeThemeBridge.ApplyCurrentTheme(page);
            TreeThemeFollower.ApplyOptionMarkers(page, TimeThemeBridge.IsDark);
            TreeThemeFollower themeFollower = list.gameObject.AddComponent<TreeThemeFollower>();
            themeFollower.Page = page;
            themeFollower.Menu = menu;
        }''',
'''        // 5. 主题：把此刻该有的颜色刷上去，之后主题变了（手动切换或「夜里自动紫色」）由跟随器重刷
        {
            ModConfigLook.ApplyMain(page);
            TreeThemeFollower.ApplyOptionMarkers(page);
            TreeThemeFollower themeFollower = list.gameObject.AddComponent<TreeThemeFollower>();
            themeFollower.Page = page;
            themeFollower.Menu = menu;
        }'''),
# cells after ShowSettings
('''                    ModConfigSidebarWidgets.StyleCellButtons(cell); // 亮蓝换可换肤蓝 + 虚线归位，须先于 TimeTheme 换色
                }
            }

            if (TimeThemeBridge.Available)
            {
                TimeThemeBridge.ApplyCurrentTheme(menu.Content);
                TimeThemeBridge.ApplyOptionsTheme(menu.Content, TimeThemeBridge.IsDark);
            }''',
'''                    ModConfigSidebarWidgets.StyleCellButtons(cell); // 「默认」钮挂主题色 + 虚线归位
                }
            }

            ModConfigLook.ApplyMain(menu.Content);
            ModConfigLook.ApplyOptions(menu.Content);'''),
('''            TimeThemeBridge.ApplyCurrentTheme(sectionRoot);''',
'''            ModConfigLook.ApplyMain(sectionRoot);'''),
# SettleSelection
('''        bool dark = TimeThemeBridge.IsDark;
        if (button.SelectedGraphic != null)
        {
            button.SelectedGraphic.SetActive(button.Selected);
            TimeThemeBridge.TintSelectedGraphic(button.SelectedGraphic, dark);
        }

        if (button.text != null)
        {
            button.text.color = button.Selected ? ModConfigLook.TextSelected : (dark ? TimeThemeBridge.DarkTabText : ModConfigLook.TextDefault);
        }''',
'''        if (button.SelectedGraphic != null)
        {
            button.SelectedGraphic.SetActive(button.Selected);
            TintSelectedGraphic(button.SelectedGraphic);
        }

        if (button.text != null)
        {
            button.text.color = button.Selected ? ModConfigLook.TextSelected : ModConfigLook.TextDefault;
        }'''),
# follower
('''    /// <summary>
    /// 挂在模组列表上：打开页面时按当前主题把整页刷一遍，页面开着时主题变了也刷
    /// （分区行、复选框、滚动条、说明面板、工具行都是后建的，不在 TimeTheme 的表里）。
    /// 选项控件（搜索框、单元格里的下拉框 / 输入框）走 ColorBlock，另刷一遍。
    /// </summary>''',
'''    /// <summary>
    /// 挂在模组列表上：打开页面时按当前主题把整页刷一遍，页面开着时主题变了也刷。
    /// 选项控件（搜索框、单元格里的下拉框 / 输入框）走 ColorBlock，另刷一遍。
    /// </summary>'''),
('''        /// <summary>挂了 ThemeAsOption 的子树（标题、搜索标签、工具行、表头、说明面板）多刷一遍选项表，让白字变紫 / 变回白。</summary>
        internal static void ApplyOptionMarkers(Transform page, bool dark)
        {
            foreach (ModConfigSidebarWidgets.ThemeAsOption marker in page.GetComponentsInChildren<ModConfigSidebarWidgets.ThemeAsOption>(true))
            {
                TimeThemeBridge.ApplyOptionsTheme(marker.transform, dark);
            }
        }

        private void OnEnable()
        {
            _dark = TimeThemeBridge.IsDark;
            Apply(_dark);
        }

        private void Update()
        {
            bool dark = TimeThemeBridge.IsDark;
            if (dark == _dark)
            {
                return;
            }

            _dark = dark;
            Apply(dark);
        }

        private void Apply(bool dark)
        {
            Transform root = Page != null ? Page : transform;
            TimeThemeBridge.ApplyTheme(root, dark);
            if (Page != null)
            {
                Transform? search = Page.Find("SearchInput");
                if (search != null)
                {
                    TimeThemeBridge.ApplyOptionsTheme(search, dark);
                }

                ApplyOptionMarkers(Page, dark);
            }

            ModSettingsMenu? menu = Menu;
            if (menu != null && menu.Content != null)
            {
                TimeThemeBridge.ApplyTheme(menu.Content, dark);
                TimeThemeBridge.ApplyOptionsTheme(menu.Content, dark);
            }
        }''',
'''        /// <summary>挂了 ThemeAsOption 的子树（标题、搜索标签、工具行、表头、说明面板）多刷一遍选项表，让白字变紫 / 变回白。</summary>
        internal static void ApplyOptionMarkers(Transform page)
        {
            foreach (ModConfigSidebarWidgets.ThemeAsOption marker in page.GetComponentsInChildren<ModConfigSidebarWidgets.ThemeAsOption>(true))
            {
                ModConfigLook.ApplyOptions(marker.transform);
            }
        }

        private void OnEnable()
        {
            _dark = ModConfigLook.IsDark;
            Apply();
        }

        private void Update()
        {
            bool dark = ModConfigLook.IsDark;
            if (dark == _dark)
            {
                return;
            }

            _dark = dark;
            Apply();
        }

        private void Apply()
        {
            Transform root = Page != null ? Page : transform;
            ModConfigLook.ApplyMain(root);
            if (Page != null)
            {
                ModConfigLook.ApplyOptions(Page.Find("SearchInput"));
                ApplyOptionMarkers(Page);
            }

            ModSettingsMenu? menu = Menu;
            if (menu != null && menu.Content != null)
            {
                ModConfigLook.ApplyMain(menu.Content);
                ModConfigLook.ApplyOptions(menu.Content);
            }
        }'''),
# tab update prefix
('''    /// <summary>
    /// 夜间模式下页签行的配色（对应 TimeTheme 给原版 SettingsTABSButton 打的那个补丁，再多管一个选中态）：
    ///   · 未选中：文字用它的夜间页签字色 DarkTabText，选中底图隐藏；
    ///   · 选中：底图从 ModConfig 的纯白改成它的夜间选中紫（DarkSelectedFill），文字仍走原逻辑的黑 ——
    ///     夜里整栏黑底紫字，中间突然一条白底黑字太扎眼（2.13.3）。
    /// 白天：底图还原纯白，其余全交回 ModConfig 原 Update。
    /// 本前缀只在 TimeTheme 在场时才注册；IsDark 在它缺席时恒为 false，TintSelectedGraphic 也直接返回。
    /// </summary>
    private static bool TabButtonUpdatePrefix(object __instance)
    {
        if (__instance is not ModdedTABSButton button)
        {
            return true;
        }

        bool dark = TimeThemeBridge.IsDark;
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

        button.text.color = Color.Lerp(button.text.color, TimeThemeBridge.DarkTabText, Time.unscaledDeltaTime * 7f);
        if (button.SelectedGraphic != null)
        {
            button.SelectedGraphic.SetActive(false);
        }

        return false;
    }''',
'''    /// <summary>
    /// 页签行的配色，替代 ModConfig 原 Update（白字 / 选中白底黑字）：与原版 SettingsTABSButton.Update 同一套渐变，
    /// 目标色按 MCE 主题 —— 蓝色主题是游戏 3.1 页签的米色字 / 选中米底深蓝字，紫色主题是淡紫字 / 选中紫底深紫字。
    /// </summary>
    private static bool TabButtonUpdatePrefix(object __instance)
    {
        if (__instance is not ModdedTABSButton button)
        {
            return true;
        }

        if (button.SelectedGraphic != null)
        {
            TintSelectedGraphic(button.SelectedGraphic);
            button.SelectedGraphic.SetActive(button.Selected);
        }

        if (button.text != null)
        {
            Color target = button.Selected ? ModConfigLook.TextSelected : ModConfigLook.TextDefault;
            button.text.color = Color.Lerp(button.text.color, target, Time.unscaledDeltaTime * 7f);
        }

        return false;
    }

    /// <summary>选中底图上主题色：只比 RGB、保留原透明度，颜色已对时不写。</summary>
    private static void TintSelectedGraphic(GameObject selectedGraphic)
    {
        Image? image = selectedGraphic.GetComponent<Image>();
        if (image == null)
        {
            return;
        }

        Color target = ModConfigLook.SelectedFill, current = image.color;
        if (current.r != target.r || current.g != target.g || current.b != target.b)
        {
            image.color = new Color(target.r, target.g, target.b, current.a);
        }
    }'''),
])

edit('ModConfigSidebarWidgets.Groups.cs', [
('''            if (TimeThemeBridge.Available)
            {
                TimeThemeBridge.ApplyCurrentTheme(row.transform);
            }''',
'''            ModConfigLook.ApplyMain(row.transform);'''),
])
print('ok')
