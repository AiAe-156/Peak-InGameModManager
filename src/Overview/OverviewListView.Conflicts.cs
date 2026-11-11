#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewListView
{
    private readonly List<Action<IReadOnlyList<PackConflictMatch>>> _conflictRefresh=new();
    private void RefreshConflictBadges(IReadOnlyList<PackConflictMatch> conflicts)
    { foreach(var refresh in _conflictRefresh)refresh(conflicts); }
    private void BuildConflictBadge(RectTransform markers, OverviewMod mod, IReadOnlyList<PackConflictMatch> conflicts,float rightInset=49)
    {
        var involved = conflicts.Where(c => c.Any && c.Involves(mod)).ToList();
        var badge = OverviewUi.Rect("CompatibilityAdvice", markers);
        badge.anchorMin = badge.anchorMax = badge.pivot = Vector2.one; badge.anchoredPosition = new Vector2(-rightInset, 1); badge.sizeDelta = new Vector2(14, 14);
        var graphic = badge.gameObject.AddComponent<RoundedChip>(); graphic.color = PackConflictUi.AdviceColor; graphic.raycastTarget = true;
        var button = badge.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
        var mark = OverviewUi.Text(badge, "!", 12); mark.color = ModConfigLook.Surface(new Color(.19f, .17f, .09f)); mark.fontStyle = FontStyles.Bold; mark.alignment = TextAlignmentOptions.Midline;
        OverviewUi.Fill(mark.rectTransform); mark.raycastTarget = false;
        string hint = involved.Count>0?PackConflictUi.Hint(involved):"";
        badge.gameObject.SetActive(involved.Count>0);
        AddHint(badge.gameObject,()=>hint);
        _conflictRefresh.Add(latest=> {
            if(badge==null)return;
            involved=latest.Where(c=>c.Any&&c.Involves(mod)).ToList();
            hint=involved.Count>0?PackConflictUi.Hint(involved):"";badge.gameObject.SetActive(involved.Count>0);
        });
        var focus = badge.gameObject.AddComponent<OverviewHotkeyFocus>(); focus.Selected += () => ShowHint(hint); focus.Deselected += HideExternalHint;
        button.onClick.AddListener(() => { HideAllHints(); PackConflictUi.Show(_repo, involved, () => { Render(); _headersChanged(); }); });
    }
}
