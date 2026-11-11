#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewListView
{
    // The parent list reserves this column only in author mode.
    private void BuildUpdateHeader(Transform parent, float x, float width)
    {
        if (!_authorMode) return;
        var refresh = OverviewUi.IconButton(parent, "refresh", () => { }, 30, 32);
        OverviewUi.Place((RectTransform)refresh.transform, x + 2, 2, 30, 32); Tint(refresh, OverviewUi.Ivory); refresh.interactable = false;
    }

    private void BuildUpdateCell(Transform parent, OverviewMod mod, float x, float width)
    {
        if (!_authorMode) return;
        var permission = OverviewUi.Check(parent, _repo.GetModUpdateAllowed(mod.Rule), value => SetModUpdateAllowed(mod, value));
        TintPermission(permission, PermissionColor(mod));
        OverviewEditableFrame.Set((RectTransform)permission.transform, true);
        OverviewUi.Place((RectTransform)permission.transform, x + 4, (RowHeight - 28) / 2, 28, 28);
        AddHint(permission.gameObject, T("Allow this mod's manual and automatic update checks", "允许此模组手动和自动检查更新"));
    }

    public void SetGlobalUpdateAllowed(bool value)
    {
        if (_disposed) return;
        if (value == _repo.GetGlobalUpdateAllowed()) return;
        RequestUpdatePermission(null, value, suppress => ApplyGlobalUpdateAllowed(value, suppress));
    }

    public void SetModUpdateAllowed(OverviewMod mod, bool value)
    {
        if (_disposed || value == _repo.GetModUpdateAllowed(mod.Rule)) return;
        RequestUpdatePermission(mod, value, suppress => ApplyModUpdateAllowed(mod, value, suppress));
    }

    public Color PermissionColor(OverviewMod? mod = null)
    {
        if (!_repo.HasPublishedPack) return OverviewUi.Ivory;
        bool overridden = mod == null ? _repo.State.globalUpdateOverridden : mod.Rule.updateOverridden;
        return overridden ? OverviewUi.Blue : OverviewUi.Green;
    }

    public static void TintPermission(Toggle toggle, Color color)
    {
        // Ordinary players retain Enhance's cream border and blue tick.
        if (color == OverviewUi.Ivory) return;
        var box = toggle.GetComponent<ModConfigSidebarWidgets.CheckBox>();
        if (box == null) return;
        if (box.Border != null) { var borderColor = color; borderColor.a = box.Border.color.a; box.Border.color = borderColor; }
        if (box.Mark != null)
            foreach (var image in box.Mark.GetComponentsInChildren<Image>(true))
            {
                image.color = color;
                var theme = image.GetComponent<ModConfigSidebarWidgets.ThemeTint>();
                if (theme != null) theme.Day = theme.Night = color;
            }
    }

    private void RequestUpdatePermission(OverviewMod? mod, bool value, Action<bool> apply)
    {
        if (!value || !_repo.NeedsUpdateWarning(mod?.Rule)) { apply(false); return; }
        // Restore both toggles immediately. A modal confirmation is the only operation that
        // mutates the saved preference, so Cancel/Escape/page close leave it unchanged.
        Render(); _headersChanged();
        string warning = mod == null
            ? T("The pack author disabled global update checks. Enabling them manually may cause multiplayer issues.", "该整合包作者关闭了全局的更新检查，如果您要手动开启，可能会导致联机问题。")
            : T("The pack author disabled update checks for this mod. Enabling them manually may cause multiplayer issues.", "该整合包作者关闭了该模组的更新检查，如果您要手动开启，可能会导致联机问题。");
        OverviewUi.ConfirmUpdatePolicy(warning, suppress => { if (!_disposed && _opened) apply(suppress); });
    }

    private void ApplyGlobalUpdateAllowed(bool value, bool suppress)
    {
        bool previous = _repo.State.globalUpdateAllowed, previousOverride = _repo.State.globalUpdateOverridden, previousSuppression = _repo.State.updateWarningsDismissed;
        _repo.State.globalUpdateAllowed = value;
        if (_repo.HasPublishedPack) _repo.State.globalUpdateOverridden = true;
        if (suppress) _repo.State.updateWarningsDismissed = true;
        try { _repo.Save(); }
        catch (Exception ex)
        {
            _repo.State.globalUpdateAllowed = previous; _repo.State.globalUpdateOverridden = previousOverride; _repo.State.updateWarningsDismissed = previousSuppression;
            Render(); _headersChanged(); _status(T("Save failed: ", "保存失败：") + ex.Message); throw;
        }
        if (!value)
        {
            _round?.Cancel();
            foreach (var mod in _mods) mod.HasUpdate = false;
            _reopenPending = false;
        }
        Render(); _headersChanged();
        _status(value ? T("Global update checks are allowed.", "已允许全局检查更新。") : T("Global update checks are disabled, including manual requests.", "已关闭全局检查更新，手动请求同样受限。"));
    }

    private void ApplyModUpdateAllowed(OverviewMod mod, bool value, bool suppress)
    {
        bool previous = mod.Rule.updateAllowed, previousOverride = mod.Rule.updateOverridden, previousSuppression = _repo.State.updateWarningsDismissed;
        mod.Rule.updateAllowed = value;
        if (_repo.HasPublishedPack) mod.Rule.updateOverridden = true;
        if (suppress) _repo.State.updateWarningsDismissed = true;
        try { _repo.Save(); }
        catch (Exception ex)
        {
            mod.Rule.updateAllowed = previous; mod.Rule.updateOverridden = previousOverride; _repo.State.updateWarningsDismissed = previousSuppression;
            Render(); _headersChanged(); _status(T("Save failed: ", "保存失败：") + ex.Message); throw;
        }
        if (!value) { mod.HasUpdate = false; _round?.Cancel(); }
        Render(); _headersChanged();
    }

    private void CheckSingleUpdate(OverviewMod mod)
    {
        if (_disposed || !_opened || _checking) return;
        if (!_repo.IsUpdateAllowed(mod.Rule)) { _status(T("This mod is not permitted to check for updates.", "此模组未获更新检查许可。")); return; }
        // The result belongs to a fixed source snapshot. No live row is mutated on a worker thread.
        var snapshot = new OverviewMod
        {
            Id = mod.Id, Guid = mod.Guid, Name = mod.Name, Rule = mod.Rule,
            PackageVersion = mod.PackageVersion, PluginVersion = mod.PluginVersion,
            RawName = mod.RawName, PackageName = mod.PackageName, PackageRoot = mod.PackageRoot, Location = mod.Location,
            Author = mod.Author, AuthorSource = mod.AuthorSource, HomepageUrl = mod.HomepageUrl, IdentityConflict = mod.IdentityConflict,
            SourceConfirmed = mod.SourceConfirmed, SourceNamespace = mod.SourceNamespace, SourceName = mod.SourceName
        };
        _round?.Dispose(); _round = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationToken token = _round.Token;
        _checking = true;
        _status(T("Checking ", "正在检查 ") + OverviewLanguageBridge.GetDisplayName(mod));
        Render(); _checkTask = RunSingleUpdate(snapshot, token);
    }

    private async Task RunSingleUpdate(OverviewMod snapshot, CancellationToken token)
    {
        // Own a fresh client for this request; never dispose the page's shared client from here.
        try
        {
            using var service = new ThunderstoreUpdateService(_repo, TimeSpan.Zero);
            await service.CheckAsync(new[] { snapshot }, token).ConfigureAwait(false);
            _pump.Enqueue(() =>
            {
                if (_disposed || !_opened || token.IsCancellationRequested) return;
                var current = _mods.FirstOrDefault(m => m.Id == snapshot.Id);
                if (current == null || current.SourceKey != snapshot.SourceKey || current.PackageVersion != snapshot.PackageVersion || !_repo.IsUpdateAllowed(current.Rule)) return;
                current.LatestVersion = snapshot.LatestVersion; current.LastCheckedUtc = snapshot.LastCheckedUtc;
                current.HasUpdate = snapshot.HasUpdate; current.CheckMessage = snapshot.CheckMessage; current.UpdateNotes = snapshot.UpdateNotes;
                _status(OverviewLanguageBridge.GetDisplayName(current) + "：" + current.CheckMessage); Render(); _headersChanged();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _pump.Enqueue(() => { if (!_disposed && _opened && !token.IsCancellationRequested) _status(T("This mod's update check failed: ", "此模组检查失败：") + Loc.LocalizeMessage(ex.Message)); });
        }
        finally
        {
            _pump.Enqueue(() =>
            {
                _checking = false; bool retry = _reopenPending; _reopenPending = false;
                if (_disposed) return;
                if (_opened) { Render(); _headersChanged(); }
                if (retry && _opened && _repo.State.automaticUpdateChecks && AnyUpdateAllowed) CheckUpdates();
            });
        }
    }

    internal void PopupSource(OverviewMod mod) { if (!_disposed) SourceUi.Menu(_repo, mod, Reload, _status, _mods); }
}
