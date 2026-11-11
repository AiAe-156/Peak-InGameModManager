#nullable enable
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewListView
{
    private void RefreshEnablementRows()
    {
        // Header recomputes the pending snapshot; invalidate the list's short cache afterwards.
        _headersChanged();_markerState=null;_markerStateAt=-1;
        _dependencyHints.InvalidateDisplay();
        foreach(var row in _virtualRows)
        {
            if(row.Mod==null || row.View==null)continue;
            var binding=row.View.GetComponent<OverviewEnablementBinding>();
            if(binding!=null)binding.Refresh(MarkerState()?.Mod(row.Mod.Guid));
        }
        var conflicts=PackConflictService.Evaluate(_repo,_mods).Where(c=>c.Any).ToList();
        RefreshCompatibilityAdvice(conflicts);
        RefreshConflictBadges(conflicts);
    }
    private void BuildEnablementStatus(Toggle checkbox, OverviewMod mod, ProfileItemPending? pending)
    {
        bool target=_repo.PlannedEnabled(mod);
        Color tint=ModEnablementPresentation.Tint(mod,target,pending);
        var colors=checkbox.colors;colors.normalColor=colors.highlightedColor=colors.selectedColor=colors.pressedColor=colors.disabledColor=Color.white;checkbox.colors=colors;
        var border=checkbox.gameObject.AddComponent<EnablementBorderTint>();border.Target=checkbox.targetGraphic;border.Color=tint;
        if(checkbox.targetGraphic!=null)checkbox.targetGraphic.color=tint;
        AddHint(checkbox.gameObject,()=>ModEnablementPresentation.Detail(mod,_repo.PlannedEnabled(mod),MarkerState()?.Mod(mod.Guid)));
    }
}
