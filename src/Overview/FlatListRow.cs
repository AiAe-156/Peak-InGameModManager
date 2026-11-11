#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
internal static class FlatListRow
{
    internal static Button Create(Transform parent,Action clicked,float width,float height,Color background)
    {
        var rect=OverviewUi.Rect("ListRow",parent);OverviewUi.Place(rect,0,0,width,height);
        var image=rect.gameObject.AddComponent<Image>();image.color=background;
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.transition=Selectable.Transition.ColorTint;
        var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.15f,1.15f,1.15f,1);colors.selectedColor=colors.highlightedColor;button.colors=colors;
        button.onClick.AddListener(()=>{try{clicked();}catch(Exception e){Plugin.Log.LogError(e);}});return button;
    }
}
