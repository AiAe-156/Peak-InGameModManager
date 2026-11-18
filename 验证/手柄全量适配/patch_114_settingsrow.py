# -*- coding: utf-8 -*-
"""1.1.4 settings page (user 2026-11-13, with the old MCE screenshot): one narrow button column on the right with
Default above Clear (two rows, like the old ribbons), no Save button (edits take effect at once), slider back to the
old number-box : slider proportion; only the round knob and slim track stay."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src'))
def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('SettingCellExtras.cs', [
('''    internal enum Column { Clear, Save, Default }
    private const float ButtonWidth = 84f, ButtonGap = 6f, RightEdge = 186f + 45f;
    internal static float ColumnCentre(Column column) => RightEdge - ButtonWidth / 2f - (int)column * (ButtonWidth + ButtonGap);
    /// <summary>Left edge of the reserved button area; stretched controls end a little before it.</summary>
    internal static float ButtonsLeft => RightEdge - 3 * ButtonWidth - 2 * ButtonGap;
    internal static void PlaceButton(RectTransform button, Column column)
    {
        button.anchorMin = button.anchorMax = button.pivot = new Vector2(.5f, .5f);
        button.sizeDelta = new Vector2(ButtonWidth, 28f);
        button.anchoredPosition = new Vector2(ColumnCentre(column), 0f);
    }''', '''    /// <summary>1.1.4 (user, old MCE screenshot): one button column, Default on top and Clear under it when the setting
    /// can be cleared; a row with only Default centres it. No Save button (edits take effect at once).</summary>
    internal enum Column { Default, Clear }
    private const float ButtonWidth = 76f, ButtonHeight = 18f, RightEdge = 186f + 45f;
    internal static float ColumnCentre(Column column) => RightEdge - ButtonWidth / 2f;
    /// <summary>Left edge of the reserved button area; stretched controls end a little before it.</summary>
    internal static float ButtonsLeft => RightEdge - ButtonWidth;
    internal static void PlaceButton(RectTransform button, Column column, bool stacked = false)
    {
        button.anchorMin = button.anchorMax = button.pivot = new Vector2(.5f, .5f);
        button.sizeDelta = new Vector2(ButtonWidth, stacked ? ButtonHeight : 24f);
        float y = !stacked ? 0f : column == Column.Default ? ButtonHeight / 2f + 1f : -ButtonHeight / 2f - 1f;
        button.anchoredPosition = new Vector2(ColumnCentre(column), y);
    }'''),
])

edit('ModConfigTreeLayoutPatch.cs', [
('''                // 1.1.3 (user): every row has the same three button columns, reserved even when empty:
                // Default | Save | Clear (rightmost). SettingCellExtras.Columns places them.
                SettingCellExtras.PlaceButton(button.RectTransform, button.name == "UI_MainMenuButton_ClearButton" ? SettingCellExtras.Column.Clear : SettingCellExtras.Column.Default);''',
 '''                // 1.1.4 (user): one column, Default above Clear (two rows) when the row has both; Default alone is centred.
                SettingCellExtras.PlaceButton(button.RectTransform, button.name == "UI_MainMenuButton_ClearButton" ? SettingCellExtras.Column.Clear : SettingCellExtras.Column.Default, stackedActions);'''),
('''                button.Text.fontSizeMin = 12f; button.Text.fontSizeMax = 14f; button.Text.fontSize = 14f;''',
 '''                button.Text.enableAutoSizing = true; button.Text.fontSizeMin = 9f; button.Text.fontSizeMax = stackedActions ? 12f : 13f; button.Text.fontSize = button.Text.fontSizeMax;'''),
# slider: old proportion (no stretch); FitControl still keeps it out of the button column
('''                var number = controls.GetComponentsInChildren<TMP_InputField>(true).FirstOrDefault();
                if (number != null && slider.transform is RectTransform sliderRect && number.transform is RectTransform numberRect && sliderRect.parent == numberRect.parent)
                    SliderLook.StretchAfter(sliderRect, numberRect, 10f);
                if (slider.transform is RectTransform fitted) SettingCellExtras.FitControl(fitted);''',
 '''                // 1.1.4 (user, old screenshot): keep ModConfig's own number-box : slider proportion; only make sure the
                // slider ends before the button column.'''),
('''                if ((control is TMP_InputField && !sliderNumber) || control is Slider) SettingCellExtras.FitControl(rect);''',
 '''                if (control is TMP_InputField && !sliderNumber) SettingCellExtras.FitControl(rect);'''),
])

# no Save button; edits are written at once (same as the keybindings page)
edit('Overview/NativeConfigurationDraft.cs', [
('''        var save = OverviewUi.Button(host, Loc.T("Save", "保存", "儲存"), () => {
            var repo = Repository!;
            var mods = new OverviewCatalogue().Scan(repo);
            ConfigurationSaveRequest.Run(repo, mods, ConfigurationSaveScope.ModParameters, property.Pluginfo.Metadata.GUID, result => {
                if (!result.Success) OverviewUi.ShowText(Loc.T("Save settings", "保存设置", "儲存設定"), result.Message);
            });
        }, 70, 28, UiActionRole.Commit);
        save.name = "MCE_SaveSetting";
        var rect = (UnityEngine.RectTransform)save.transform;
        // 2.3.11 (user): side by side with ModConfig's Default button instead of stacked above it. ModConfig puts
        // Default at x=+186 from the cell centre (90 wide); Save sits just left of it on the same row.
        // 1.1.3 (user): fixed middle column (Default | Save | Clear), the same on every row.
        SettingCellExtras.PlaceButton(rect, SettingCellExtras.Column.Save);
        ProfilePendingMarkers.Attach(rect, () => Repository?.GetPendingState().Parameter(property.Pluginfo.Metadata.GUID, property.ConfigBase),
            text => { }, () => { }, reserveText: false);
    }''', '''        // 1.1.4 (user): no per-row Save button; every edit is written to the mod's config at once (CommitNow).
    }
    /// <summary>1.1.4 (user: "the Save button should never have existed"): a native edit is drafted and committed for this
    /// mod straight away, so it takes effect immediately, like the keybindings page.</summary>
    private static void CommitNow(IBepInExProperty property)
    {
        var repo = Repository; if (repo == null) return;
        string guid = property.Pluginfo.Metadata.GUID;
        try
        {
            var mods = new OverviewCatalogue().Scan(repo);
            ConfigurationSaveRequest.Run(repo, mods, ConfigurationSaveScope.ModParameters, guid, result => {
                if (!result.Success) OverviewUi.ShowText(Loc.T("Save settings", "保存设置", "儲存設定"), result.Message);
            });
        }
        catch (Exception error) { Plugin.Log.LogWarning("[设置页] 立即保存失败：" + error.Message); }
    }'''),
('''        if (!Repository!.SetDraftConfig(property.Pluginfo.Metadata.GUID, property.ConfigBase, serialized, out string error))
            throw new InvalidOperationException(error);
        return false;
    }''', '''        if (!Repository!.SetDraftConfig(property.Pluginfo.Metadata.GUID, property.ConfigBase, serialized, out string error))
            throw new InvalidOperationException(error);
        CommitNow(property);
        return false;
    }'''),
('''            TomlTypeConverter.ConvertToString(property.ConfigBase.DefaultValue, property.ConfigBase.SettingType), out string error)) throw new InvalidOperationException(error);
        Refresh(__instance); return false;''', '''            TomlTypeConverter.ConvertToString(property.ConfigBase.DefaultValue, property.ConfigBase.SettingType), out string error)) throw new InvalidOperationException(error);
        Refresh(__instance); CommitNow(property); return false;'''),
])
print('ok')
