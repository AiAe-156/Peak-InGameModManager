#nullable enable
using PEAKLib.UI.Elements;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance;

/// <summary>Only MCE-owned operation buttons use these roles; content colors remain independent.</summary>
internal sealed class UiActionStyle : MonoBehaviour
{
    internal UiActionRole Role;
    private PeakMenuButton? _peak;
    private bool? _dark, _enabled;
    private bool _selected, _lastSelected;
    private bool _muted;
    private Color? _contentTint;
    internal static void Tint(Button button, Color tint)
    { var style = button.GetComponent<UiActionStyle>(); if (style != null) { style._contentTint = tint; style._dark = null; style.Refresh(); } }
    /// <summary>
    /// 2.4.2：按钮按功能分色系，蓝 / 紫两套主题各给一组固定色，不再过 <see cref="ModConfigLook.Surface"/>
    /// （2.4.0 把中性 / 编辑 / 跳转三种都转成主题色相，夜里几乎同色，功能色系丢了）。
    /// 提交绿、危险红两套主题共用，紫色主题压暗一档。
    /// </summary>
    internal static Color ColorFor(UiActionRole role, bool dark = false)
    {
        Color color = role switch
        {
            UiActionRole.Edit => dark ? new Color32(0x33, 0x35, 0x7a, 255) : new Color32(0x26, 0x4b, 0x6e, 255),
            UiActionRole.Commit => new Color32(0x26, 0x4d, 0x33, 255),
            UiActionRole.Danger => new Color32(0x66, 0x1a, 0x21, 255),
            UiActionRole.Overview => dark ? new Color32(0x1f, 0x55, 0x60, 255) : new Color32(0x2f, 0x5f, 0x5a, 255),
            UiActionRole.Guide => dark ? new Color32(0x6a, 0x2a, 0x55, 255) : new Color32(0x6a, 0x4a, 0x1c, 255),
            _ => dark ? new Color32(0x3e, 0x33, 0x50, 255) : new Color32(0x3a, 0x47, 0x56, 255)
        };
        if (dark && role is UiActionRole.Commit or UiActionRole.Danger) { color.r *= .82f; color.g *= .82f; color.b *= .82f; }
        return color;
    }
    internal static void Set(Button button, UiActionRole role, bool muted = false)
    {
        var style = button.GetComponent<UiActionStyle>() ?? button.gameObject.AddComponent<UiActionStyle>();
        style._peak = button.GetComponent<PeakMenuButton>(); style.Role = role; style._muted = muted; style._dark = null; style.Refresh();
    }
    internal static void Select(Button button, bool selected)
    { var style = button.GetComponent<UiActionStyle>(); if (style != null) { style._selected = selected; style.Refresh(); } }
    private void OnEnable() { _dark = null;Overview.OverviewMaintenance.Register(this,Refresh); }
    private void OnDisable()=>Overview.OverviewMaintenance.Remove(this);
    private void Refresh()
    {
        if (_peak == null) return;
        bool dark = ModConfigLook.IsDark, enabled = _peak.Button.interactable && !_muted;
        if (_dark == dark && _enabled == enabled && _lastSelected == _selected) return;
        _dark = dark; _enabled = enabled; _lastSelected = _selected;
        Color color = ColorFor(Role, dark);
        if (_contentTint.HasValue) color = Color.Lerp(color, _contentTint.Value, .35f);
        if (_selected) color = dark ? ModConfigLook.SurfaceFor(new Color32(0x28, 0x68, 0x83, 255), true) : new Color32(0x32, 0x7b, 0x98, 255);
        if (!enabled) color = Color.Lerp(color, ModConfigLook.SurfaceFor(new Color(.23f, .22f, .20f), dark), .7f);
        _peak.SetColor(color);
    }
}
