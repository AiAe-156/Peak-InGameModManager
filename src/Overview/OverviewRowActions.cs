#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Shared row icon geometry and presentation; callers supply every business action and hint.</summary>
internal static class OverviewRowActions
{
    internal const float Size = 30;
    private static Color Disabled => ModConfigLook.Surface(new(.37f, .39f, .37f));

    internal static Button Settings(Transform parent, float x, float y, Action clicked, bool enabled,
        string hint, Action<GameObject, string> bindHint)
        => Create(parent, "gear", x, y, clicked, enabled, OverviewUi.Ivory, hint, bindHint);

    internal static Button Home(Transform parent, float x, float y, Action clicked, bool enabled,
        string hint, Action<GameObject, string> bindHint, bool identified = true)
        => Create(parent, "home", x, y, clicked, enabled, identified ? OverviewUi.Blue : Disabled, hint, bindHint);

    internal static Button Folder(Transform parent, float x, float y, Action clicked, bool enabled,
        string hint, Action<GameObject, string> bindHint)
        => Create(parent, "folder", x, y, clicked, enabled, OverviewUi.Ivory, hint, bindHint);

    internal static void HighlightSelf(TMP_Text text)
    {
        text.color = ModConfigSidebarWidgets.SelfRowColor();
        if (text is not TextMeshProUGUI caption) return;
        var tint = caption.GetComponent<ModConfigSidebarWidgets.SelfRowTint>()
            ?? caption.gameObject.AddComponent<ModConfigSidebarWidgets.SelfRowTint>();
        tint.Text = caption;
    }

    private static Button Create(Transform parent, string icon, float x, float y, Action clicked, bool enabled,
        Color activeColor, string hint, Action<GameObject, string> bindHint)
    {
        var button = OverviewUi.IconButton(parent, icon, clicked, Size, Size);
        OverviewUi.Place((RectTransform)button.transform, x, y, Size, Size);
        button.interactable = enabled;
        button.gameObject.AddComponent<OverviewRowColumn>().Column = icon == "gear" ? ListColumnPolicy.Settings : icon == "home" ? ListColumnPolicy.Home : ListColumnPolicy.Folder;
        Color color = enabled ? activeColor : Disabled;
        foreach (var text in button.GetComponentsInChildren<TextMeshProUGUI>(true)) text.color = color;
        foreach (var glyph in button.GetComponentsInChildren<OverviewIconGraphic>(true)) glyph.color = color;
        if (!string.IsNullOrWhiteSpace(hint)) bindHint(button.gameObject, hint);
        return button;
    }

}
