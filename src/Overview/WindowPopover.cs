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
    /// <summary>同步自 MCE 1.1.0：窗口内的下拉层开着时，概览页的 LB / RB 不跳工具行。</summary>
    internal static bool AnyOpen => _active != null && _active._shade != null;
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
        // 实测修正（2026-11-16，下拉样式统一）：不论选项多少都用共用紧凑下拉（可滚动）；原来超过 16 项时是另一套外观。
        OverviewUi.CompactDropdown(anchor, title, options.Select(item => new UiMenuEntry {
            Label = item.Label, Checked = item.Value == current, Role = UiActionRole.Edit, Invoke = () => pick(item.Value)
        }).ToList(), host);
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
