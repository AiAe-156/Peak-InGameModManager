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
# ── 1. dropdown arrow smaller, caption gets the space back
('''        Tint(dropdown.Arrow, SelfRowTint.Selected, SelfRowTint.NightSelected);
        ModConfigPaperStyle.Apply(dropdown.Background, 8f);''',
'''        Tint(dropdown.Arrow, SelfRowTint.Selected, SelfRowTint.NightSelected);
        ModConfigPaperStyle.Apply(dropdown.Background, 8f);
        // 2.4.1：模板箭头约 30×19，窄下拉里把文字挤没了（「简体…」只剩两个字）。缩到 14 见方、贴右 8。
        if (dropdown.Arrow != null)
        {
            RectTransform arrowRect = dropdown.Arrow.rectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0.5f);
            arrowRect.anchorMax = new Vector2(1f, 0.5f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.sizeDelta = new Vector2(DropdownArrowSize, DropdownArrowSize * 0.62f);
            arrowRect.anchoredPosition = new Vector2(-8f, 0f);
            dropdown.Arrow.preserveAspect = true;
        }'''),
('''            float arrowSpace = dropdown.Arrow != null
                ? Mathf.Max(dropdown.Arrow.rectTransform.sizeDelta.x, dropdown.Arrow.rectTransform.rect.width)
                : 0f;
            capRect.offsetMax = new Vector2(-(Mathf.Max(arrowSpace, 14f) + 4f), capRect.offsetMax.y);''',
'''            float arrowSpace = dropdown.Arrow != null ? DropdownArrowSize + 8f : 0f;
            capRect.offsetMax = new Vector2(-(arrowSpace + 4f), capRect.offsetMax.y);'''),
# ── 2. square icon buttons: same icon box for all three; clear-updates uses a drawn check
('''        iconRect.sizeDelta = new Vector2(height - 14f, height - 14f);''',
'''        // 2.4.1：三个方钮的图标统一占 60% 见方（原先 height-14，加上各自 sprite 留白不同，刷新图标明显偏小）
        iconRect.sizeDelta = new Vector2(Mathf.Round(height * IconBoxRatio), Mathf.Round(height * IconBoxRatio));'''),
('''        var clear = ModConfigTranslationExport.MakeButton(row, "MCE_ClearUpdates", "BTN_CLEAR_UPDATES", RefreshColor, refreshLeft + (refreshWidth + rowGap) * (openGroups != null ? 2 : 1), 0f, refreshWidth, height, Overview.ChangeLedger.Clear);
        StyleSquareIconButton(clear, RefreshIconSprite(), height).SetActive(false);
        var clearText = Overview.OverviewUi.Text(clear.transform, "✓", 20); clearText.alignment = TextAlignmentOptions.Midline; Overview.OverviewUi.Fill(clearText.rectTransform, 0);''',
'''        var clear = ModConfigTranslationExport.MakeButton(row, "MCE_ClearUpdates", "BTN_CLEAR_UPDATES", RefreshColor, refreshLeft + (refreshWidth + rowGap) * (openGroups != null ? 2 : 1), 0f, refreshWidth, height, Overview.ChangeLedger.Clear);
        // 2.4.1：原先是字体里的「✓」，笔画细、颜色不跟图标走；改成与刷新 / 分组同一套粗笔画金色 sprite
        StyleSquareIconButton(clear, CheckIconSprite(), height);'''),
# ── 3. drawn icons: refresh ring thicker so its stroke weight matches the group bars; check sprite; theme sprite
('''        const float radius = 19f, halfThick = 4.5f, startDeg = 55f, sweep = 285f;
        const float arrowLen = 16f, arrowWing = 8.5f;''',
'''        // 2.4.1：环带加粗、外径贴近 64 画布边，与分组图标（5 宽横条、48 宽）的笔画和占地一致
        const float radius = 22f, halfThick = 5f, startDeg = 55f, sweep = 285f;
        const float arrowLen = 17f, arrowWing = 9.5f;'''),
('''    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)''',
'''    private const float IconBoxRatio = 0.6f;
    private const float DropdownArrowSize = 14f;

    private static Sprite? _checkIcon, _themeIcon;

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
    internal static void MakeThemeButton(RectTransform page, float left, float top, float size)
    {
        RectTransform host = MakeRowContainer(page, "MCE_ThemeToggle", left, top, size, size);
        PeakMenuButton button = ModConfigTranslationExport.MakeButton(host, "MCE_ThemeButton", "BTN_THEME", RefreshColor, 0f, 0f, size, size, ToggleTheme);
        StyleSquareIconButton(button, ThemeIconSprite(), size);
        button.gameObject.AddComponent<TipTarget>().Key = "TIP_THEME";
    }

    internal static void ToggleTheme()
    {
        Plugin.ColourTheme.Value = ModConfigLook.IsDark ? ModConfigLook.ThemeMode.Blue : ModConfigLook.ThemeMode.Purple;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)'''),
# ── 4. header: "keybind options only" checkbox → jump to the overview's keybindings page
('''            // 右侧：「只看按键」+ 复选框
            float boxLeft = ModConfigTreeLayoutPatch.SidebarWidth - 8f - CheckBoxSize - 6f;
            KeysOnlyBox = MakeCheckBox("KeysOnly", row.transform, new Vector2(boxLeft, 0f), false, _ => ToggleKeysOnly());
            PeakText label = MenuAPI.CreateText("", "Text (TMP)").ParentTo(row.transform);
            label.SetLocalizationIndex(KeysOnlyKey);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + ColumnWidth * 2f + 4f, 0f);
            labelRect.offsetMax = new Vector2(-(ModConfigTreeLayoutPatch.SidebarWidth - boxLeft) - 4f, 0f);
            StyleLabel(label.TextMesh, TextAlignmentOptions.MidlineRight);

            // 只有「只显示快捷键选项」文字 + 复选框这一段能点，别压到左边的导出 / 置顶标题''',
'''            // 右侧（2.4.1 用户改）：「跳转到快捷键设置页」，点了打开概览并切到快捷键设置页签，替代原「只显示快捷键选项」复选框
            float boxLeft = ModConfigTreeLayoutPatch.SidebarWidth - 8f;
            PeakText label = MenuAPI.CreateText("", "Text (TMP)").ParentTo(row.transform);
            label.SetLocalizationIndex(JumpHotkeysKey);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(ModConfigTreeLayoutPatch.ModTextIndent + ColumnWidth * 2f + 4f, 0f);
            labelRect.offsetMax = new Vector2(-(ModConfigTreeLayoutPatch.SidebarWidth - boxLeft) - 22f, 0f);
            StyleLabel(label.TextMesh, TextAlignmentOptions.MidlineRight);
            label.TextMesh.color = SelfRowTint.Day;
            Tint(label.TextMesh, SelfRowTint.Day, SelfRowTint.Night);
            Image jumpArrow = MakeBox("JumpArrow", (RectTransform)row.transform, SelfRowTint.Day, 0f, paper: false);
            jumpArrow.sprite = ArrowIconSprite();
            jumpArrow.type = Image.Type.Simple;
            RectTransform jumpRect = jumpArrow.rectTransform;
            jumpRect.anchorMin = jumpRect.anchorMax = new Vector2(1f, 0.5f);
            jumpRect.pivot = new Vector2(1f, 0.5f);
            jumpRect.sizeDelta = new Vector2(12f, 12f);
            jumpRect.anchoredPosition = new Vector2(-8f, 0f);
            jumpRect.localRotation = Quaternion.Euler(0f, 0f, 90f); // 朝下的三角转成朝右
            Tint(jumpArrow, SelfRowTint.Day, SelfRowTint.Night);

            // 只有右侧这一段文字能点，别压到左边的导出 / 置顶标题'''),
('''            Button button = hit.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => KeysOnlyBox?.Toggle());
            row.AddComponent<ThemeAsOption>();
            // 手柄：表头的命中区与复选框都能拿焦点，选中时把表头滚回视野
            GamepadSupport.MarkSelectable(hit, Scroll, (RectTransform)row.transform);
            if (KeysOnlyBox != null)
            {
                GamepadSupport.MarkSelectable(KeysOnlyBox.gameObject, Scroll, (RectTransform)row.transform);
            }
            Header = row;''',
'''            Button button = hit.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Overview.OverviewController.OpenHotkeys(Menu != null ? Menu.MainPage : null));
            hit.AddComponent<TipTarget>().Key = "TIP_JUMP_HOTKEYS";
            row.AddComponent<ThemeAsOption>();
            // 手柄：表头的命中区能拿焦点，选中时把表头滚回视野
            GamepadSupport.MarkSelectable(hit, Scroll, (RectTransform)row.transform);
            Header = row;'''),
('''    private const string KeysOnlyKey = "MCE_KEYS_ONLY";''',
'''    private const string KeysOnlyKey = "MCE_KEYS_ONLY";
    private const string JumpHotkeysKey = "MCE_JUMP_HOTKEYS";'''),
])

# Theme button on the settings page: right of the big 「搜索」 title, above the search box.
edit('ModConfigTreeLayoutPatch.cs', [
('''        // 3d. 搜索框：按主题上色（ColorBlock）
        ModConfigLook.ApplyOptions(page.Find("SearchInput"));''',
'''        // 3d. 搜索框：按主题上色（ColorBlock）
        ModConfigLook.ApplyOptions(page.Find("SearchInput"));

        // 3e. 主题切换钮：大「搜索」标题右侧、搜索白框上方右端（ModConfig 的搜索框 300 宽、左缘 65、顶 240）
        try
        {
            ModConfigSidebarWidgets.MakeThemeButton(page, SidebarLeft + SidebarWidth - ThemeButtonSize, SearchBoxTop - ThemeButtonSize - 6f, ThemeButtonSize);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[配色] 主题切换钮构建失败，跳过: {ex.Message}");
        }'''),
('''    internal const float SidebarLeft = 65f;''',
'''    internal const float SidebarLeft = 65f;

    /// <summary>ModConfig 的搜索框：SetPosition(215,-275)、高 70 → 顶边 240（页面左上角坐标系）。</summary>
    private const float SearchBoxTop = 240f;
    private const float ThemeButtonSize = 36f;'''),
])

# Strings.
edit('Loc.cs', [
('''        new("KEYS_ONLY", "Keybind options only", "只显示快捷键选项", "只顯示快捷鍵選項"),''',
'''        new("KEYS_ONLY", "Keybind options only", "只显示快捷键选项", "只顯示快捷鍵選項"),
        new("JUMP_HOTKEYS", "Go to keybindings", "跳转到快捷键设置页", "跳轉到快捷鍵設定頁"),
        new("TIP_JUMP_HOTKEYS", "Open the overview's Keybindings tab, which lists every mod's keybinds in one place.", "打开概览的「快捷键设置」页签，集中查看与修改所有模组的快捷键。", "開啟概覽的「快捷鍵設定」頁籤，集中檢視與修改所有模組的快捷鍵。"),
        new("BTN_THEME", "Theme", "主题", "主題"),
        new("TIP_THEME", "Switch the colour theme between blue and purple. The overview follows the next time it opens.", "在蓝色与紫色主题之间切换。概览在下次打开时跟着切换。", "在藍色與紫色主題之間切換。概覽在下次開啟時跟著切換。"),'''),
])
print('ok')
