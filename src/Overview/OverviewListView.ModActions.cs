#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewListView
{
    private void ShowModActions(OverviewMod mod, PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left) return;
        HideAllHints();
        OverviewUi.PointerDropdown(data.position, data.pressEventCamera, mod.Name, ModActionEntries(mod, false).ToArray());
    }

    /// <summary>1.1.0 手柄：名称格按 A、行内任意格按 Y 打开的菜单。代替双击和右键；角标对应的动作（看变动并标已读、
    /// 看版本、冲突建议、跳到外层模组）也放在这里，角标本身不单独落焦（用户 2026-11-12 拍板）。</summary>
    internal void ShowGamepadRowMenu(OverviewMod mod, RectTransform anchor)
    {
        HideAllHints();
        OverviewUi.CompactDropdown(anchor, OverviewLanguageBridge.GetDisplayName(mod), ModActionEntries(mod, true));
    }

    private List<UiMenuEntry> ModActionEntries(OverviewMod mod, bool gamepad)
    {
        var entries = new List<UiMenuEntry>();
        if (gamepad)
        {
            if (ChangeLedger.FindModUpdate("mod:" + mod.Guid) != null)
                entries.Add(new UiMenuEntry { Label = Loc.T("Blue · View changes and mark as read", "蓝色 · 查看变动并标为已读", "藍色 · 檢視變動並標為已讀"), Role = UiActionRole.Neutral,
                    Invoke = () => ChangeLedger.OpenModUpdate("mod:" + mod.Guid) });
            if (mod.HasUpdate)
                entries.Add(new UiMenuEntry { Label = Loc.T("Green · Version and update", "绿色 · 版本与更新", "綠色 · 版本與更新"), Role = UiActionRole.Neutral, Invoke = () => ShowVersion(mod) });
            var involved = _currentConflicts.Where(c => c.Any && c.Involves(mod)).ToList();
            if (involved.Count > 0)
                entries.Add(new UiMenuEntry { Label = Loc.T("Compatibility advice", "冲突建议", "衝突建議"), Role = UiActionRole.Neutral,
                    Invoke = () => PackConflictUi.Show(_repo, involved, () => { Render(); _headersChanged(); }) });
            if (mod.Outer != null)
            {
                var outer = mod.Outer;
                entries.Add(new UiMenuEntry { Label = Loc.T("Amber · Go to the outer mod", "琥珀色 · 跳到外层模组", "琥珀色 · 跳到外層模組"), Role = UiActionRole.Neutral, Invoke = () => FocusMod(outer) });
            }
        }
        entries.Add(new UiMenuEntry { Label = Loc.T("Change mod information", "更改模组信息", "變更模組資訊"), Role = UiActionRole.Edit, Invoke = () => ModInformationRequested?.Invoke(mod) });
        entries.Add(new UiMenuEntry { Label = Loc.T("View mod dependencies", "查看模组依赖项", "檢視模組依賴項"), Role = UiActionRole.Neutral,
            Invoke = () => ComponentDetailsView.Open(_repo, _mods, null, OverviewDependencies.Identity(mod), _infrastructureGraph, _infrastructure) });
        if (gamepad && !mod.HasUpdate)
            entries.Add(new UiMenuEntry { Label = Loc.T("Version and update", "版本与更新", "版本與更新"), Role = UiActionRole.Neutral, Invoke = () => ShowVersion(mod) });
        return entries;
    }
}
internal static partial class OverviewUi
{
    internal static void PointerDropdown(Vector2 position, Camera camera, string title, UiMenuEntry[] entries)
    {
        if (ModalParent is not RectTransform host) return;
        var anchor = Rect("PointerMenuAnchor", host);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(host, position, camera, out var local);
        Place(anchor, local.x - host.rect.xMin, host.rect.yMax - local.y, 1, 1);
        var menu = CompactDropdown(anchor, title, entries);
        if (menu == null) { Object.Destroy(anchor.gameObject); return; }
        menu.gameObject.AddComponent<PointerMenuAnchorLifetime>().Anchor = anchor.gameObject;
    }
}
internal sealed class PointerMenuAnchorLifetime : MonoBehaviour
{
    internal GameObject? Anchor;
    private void OnDestroy() { if (Anchor != null) Object.Destroy(Anchor); }
}
