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

edit('ModConfigSidebarWidgets.cs', [
('''    private const string KeysOnlyKey = "MCE_KEYS_ONLY";
''', ''),
('''        internal CheckBox? KeysOnlyBox;
''', ''),
('''        /// <summary>「只看按键」：走 ModConfig 自己的筛选下拉（同 2.13.1 的 KeysOnlyToggle），复选框状态在 Update 里跟 FilterValue 走。</summary>
        private void ToggleKeysOnly()
        {
            if (Menu == null || Menu.FilterDropdown == null || Menu.FilterDropdown.Dropdown == null)
            {
                return;
            }

            int target = Menu.FilterValue == ModConfigTreeLayoutPatch.ControlsFilter ? ModConfigTreeLayoutPatch.AllFilter : ModConfigTreeLayoutPatch.ControlsFilter;
            if (Menu.FilterDropdown.Dropdown.value == target)
            {
                Menu.SetFilter(target);
            }
            else
            {
                Menu.FilterDropdown.Dropdown.value = target;
            }
        }

''', ''),
('''                    if (row.Text != null) row.Text.text = Overview.OverviewLanguageBridge.GetSettingName(row.Name);
            }
            if (KeysOnlyBox != null && Menu != null)
            {
                bool active = Menu.FilterValue == ModConfigTreeLayoutPatch.ControlsFilter;
                if (KeysOnlyBox.Checked != active)
                {
                    KeysOnlyBox.SetSilently(active);
                }
            }
        }''',
'''                    if (row.Text != null) row.Text.text = Overview.OverviewLanguageBridge.GetSettingName(row.Name);
            }
        }'''),
])
print('ok')
