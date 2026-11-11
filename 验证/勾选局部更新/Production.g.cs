partial class Harness {
    private void Toggle(OverviewMod mod, bool enabled)
    {
        if (_building || _disposed) return;
        try
        {
            // 勾选框直接作用于所在行；只有该行本身处于多选中时才批量作用于整个选区。
            var selected = _selected.Contains(mod.Id) ? Selected(mod) : new List<OverviewMod> { mod };
            _repo.SetPlannedEnabled(selected, enabled);
            _status(Loc.T("Enable target changed. Save only keeps it; Apply configuration and restart the game to change enablement.", "启用目标已修改。“仅保存”记住目标；点击“应用配置”并重启游戏后生效。", "啟用目標已修改。「僅儲存」記住目標；按「套用設定」並重新啟動遊戲後生效。"));
            RefreshEnablementRows();
        }
        catch (Exception ex) { _status(T("Could not change state: ", "启停失败：") + Loc.LocalizeMessage(ex.Message)); RefreshEnablementRows(); }
    }

}
