#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>Owned by a window, never calls CloseModal (which could destroy that window).</summary>
internal sealed class WindowPopover : MonoBehaviour
{
    private RectTransform? _shade;
    private CanvasGroup? _base;
    private bool _wasInteractable, _wasRaycast;
    private static WindowPopover? _active;
    private static int _closedFrame=-1;
    internal static bool TryCloseTop()
    {
        if(_active!=null&&_active._shade!=null){_active.Close();_closedFrame=Time.frameCount;return true;}
        return _closedFrame==Time.frameCount;
    }
    internal static void Adopt(RectTransform host, RectTransform shade)
    {
        var owner = host.GetComponent<WindowPopover>() ?? host.gameObject.AddComponent<WindowPopover>();
        owner.Close(); owner.Own(shade);
    }
    internal static RectTransform Open(RectTransform host, RectTransform anchor, string name, float width, float height)
    {
        var owner=host.GetComponent<WindowPopover>()??host.gameObject.AddComponent<WindowPopover>();owner.Close();
        var shade=OverviewUi.Rect("WindowPopover",host);OverviewUi.Fill(shade);shade.gameObject.AddComponent<Image>().color=ModConfigLook.Surface(new Color(0,0,0,.12f));
        owner.Own(shade);shade.gameObject.AddComponent<OverviewTextClick>().Clicked=_=>owner.Close();
        var panel=OverviewUi.Panel(name,shade,OverviewUi.Brown);panel.gameObject.AddComponent<OverviewTextClick>();
        PublicationNavigation.AttachBoundary(panel);
        Place(panel,host,anchor,width,height);panel.gameObject.AddComponent<DropdownFocusReturn>().Anchor=anchor;
        shade.SetAsLastSibling();return panel;
    }
    internal static void Place(RectTransform panel,RectTransform host,RectTransform anchor,float width,float height)
    {
        var corners=new Vector3[4];anchor.GetWorldCorners(corners);
        var lo=host.InverseTransformPoint(corners[0]);var hi=host.InverseTransformPoint(corners[2]);var area=host.rect;
        var box=UiGeometry.Popover(area.width,area.height,new UiGeometry.Box(lo.x-area.xMin,area.yMax-hi.y,hi.x-lo.x,hi.y-lo.y),width,height);
        OverviewUi.Place(panel,box.X,box.Y,box.Width,box.Height);
    }
    internal static void Menu(RectTransform host,RectTransform anchor,string title,IReadOnlyList<(string Label,string Value)> options,string current,Action<string> pick)
    {
        if (options.Count <= 16)
        {
            OverviewUi.CompactDropdown(anchor, title, options.Select(item => new UiMenuEntry {
                Label = item.Label, Checked = item.Value == current, Role = UiActionRole.Edit, Invoke = () => pick(item.Value)
            }).ToList(), host);
            return;
        }
        var panel=Open(host,anchor,"WindowMenu",300,options.Count*38+38);
        var heading=OverviewUi.Text(panel,title,16);heading.color=OverviewUi.Muted;OverviewUi.Place(heading.rectTransform,12,4,panel.rect.width-24,26);
        var viewport=OverviewUi.Rect("Choices",panel);OverviewUi.Fill(viewport);viewport.offsetMin=new Vector2(8,8);viewport.offsetMax=new Vector2(-8,-32);
        viewport.gameObject.AddComponent<RectMask2D>();viewport.gameObject.AddComponent<Image>().color=Color.clear;
        var rows=OverviewUi.Rect("Rows",viewport);rows.anchorMax=new Vector2(1,1);rows.sizeDelta=new Vector2(0,options.Count*38);
        var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=rows;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=40;
        GameObject? focus=null;
        for(int i=0;i<options.Count;i++)
        {
            var item=options[i];var button=OverviewUi.Button(rows,(item.Value==current?"✓ ":"")+item.Label,()=>{host.GetComponent<WindowPopover>()?.Close();pick(item.Value);},panel.rect.width-16,34,UiActionRole.Edit);
            OverviewUi.Place((RectTransform)button.transform,0,i*38,panel.rect.width-16,34);
            var label=button.GetComponentInChildren<TextMeshProUGUI>(true);if(label!=null){label.alignment=TextAlignmentOptions.MidlineLeft;label.overflowMode=TextOverflowModes.Ellipsis;}
            GamepadSupport.MarkSelectable(button.gameObject,scroll,(RectTransform)button.transform);if(focus==null||item.Value==current)focus=button.gameObject;
        }
        if(focus!=null)EventSystem.current?.SetSelectedGameObject(focus);
    }
    private void Own(RectTransform shade)
    {
        _shade = shade; _active = this;
        _base = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        _wasInteractable = _base.interactable; _wasRaycast = _base.blocksRaycasts;
        var layer = shade.GetComponent<CanvasGroup>() ?? shade.gameObject.AddComponent<CanvasGroup>();
        layer.ignoreParentGroups = true; layer.interactable = true; layer.blocksRaycasts = true;
        _base.interactable = false; _base.blocksRaycasts = false;
    }
    internal void Close()
    {
        if (_base != null) { _base.interactable = _wasInteractable; _base.blocksRaycasts = _wasRaycast; _base = null; }
        if (_shade != null) { _shade.gameObject.SetActive(false); Destroy(_shade.gameObject); _shade = null; }
        PublicationNavigation.Invalidate(transform);
    }
    private void Update(){if(_shade!=null&&Input.GetKeyDown(KeyCode.Escape))TryCloseTop();}
    private void OnDisable()=>Close();
}
