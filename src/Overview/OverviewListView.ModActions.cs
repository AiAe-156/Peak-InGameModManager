#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewListView
{
    private void ShowModActions(OverviewMod mod, PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left) return;
        HideAllHints();
        OverviewUi.PointerDropdown(data.position, data.pressEventCamera, mod.Name, new[] {
            new UiMenuEntry { Label = Loc.T("Change mod information", "更改模组信息", "變更模組資訊"), Role = UiActionRole.Edit, Invoke = () => ModInformationRequested?.Invoke(mod) },
            new UiMenuEntry { Label = Loc.T("View mod dependencies", "查看模组依赖项", "檢視模組依賴項"), Role = UiActionRole.Neutral,
                Invoke = () => ComponentDetailsView.Open(_repo, _mods, null, OverviewDependencies.Identity(mod), _infrastructureGraph, _infrastructure) }
        });
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
