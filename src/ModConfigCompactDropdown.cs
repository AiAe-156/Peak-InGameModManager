#nullable enable
using System;
using System.Linq;
using HarmonyLib;
using ModConfigEnhance.Overview;
using PEAKLib.ModConfig.Components;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance;

/// <summary>
/// Display-only adapter: native option indices/events remain authoritative. Do not replace the
/// multi-select filter, searchable/custom templates, image choices or long lists.
/// </summary>
internal static class ModConfigCompactDropdown
{
    internal const int MaximumOptions = 12;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(TMP_Dropdown), nameof(TMP_Dropdown.Show)),
            prefix: new HarmonyMethod(typeof(ModConfigCompactDropdown), nameof(ShowPrefix)));
        harmony.Patch(AccessTools.Method(typeof(PeakChildPage), "GetParentPage", Type.EmptyTypes),
            prefix: new HarmonyMethod(typeof(ModConfigCompactDropdown), nameof(BackPrefix)) { priority = Priority.First });
    }

    internal static bool IsSimple(TMP_Dropdown dropdown)
    {
        if (dropdown.MultiSelect || dropdown.options.Count < 1 || dropdown.options.Count > MaximumOptions
            || dropdown.options.Any(option => option.image != null || string.IsNullOrWhiteSpace(option.text))) return false;
        // Search/custom editors may live beside the TMP component or inside its inactive template.
        if (dropdown.GetComponentInChildren<TMP_InputField>(true) != null
            || dropdown.GetComponentInChildren<InputField>(true) != null) return false;
        var template = dropdown.template;
        return template != null && template.GetComponentsInChildren<Toggle>(true).Length == 1
            && template.GetComponentsInChildren<Selectable>(true).All(control => control is Toggle or Scrollbar);
    }

    private static bool ShowPrefix(TMP_Dropdown __instance)
    {
        var dropdown = __instance;
        if (!dropdown.IsActive() || !dropdown.IsInteractable() || dropdown.IsExpanded || !IsSimple(dropdown)) return true;
        var page = dropdown.GetComponentInParent<PeakChildPage>();
        if (page == null) return true;
        // Sidebar controls are siblings of ModSettingsMenu, not children of that component.
        var menu = page.GetComponentInChildren<ModSettingsMenu>(true);
        if (menu == null || menu.MainPage == null || menu.MainPage.transform != page.transform
            || dropdown == menu.FilterDropdown?.Dropdown || OverviewUi.HasModal) return true;
        var owner = page.GetComponent<ModConfigDropdownHost>() ?? page.gameObject.AddComponent<ModConfigDropdownHost>();
        try { return !owner.Open(dropdown); }
        catch (Exception ex)
        {
            owner.Close();
            Plugin.Log.LogWarning("[设置下拉] 紧凑菜单不可用，保留原生下拉：" + ex.Message);
            return true;
        }
    }

    private static bool BackPrefix(PeakChildPage __instance, ref (Zorro.UI.UIPage, Zorro.UI.PageTransistion) __result)
    {
        var owner = __instance.GetComponent<ModConfigDropdownHost>();
        if (owner == null || !owner.IsOpen) return true;
        owner.Close();
        __result = (__instance, GamepadSupport.NoopTransistion.Shared);
        return false;
    }
}

/// <summary>Owns the temporary modal parent only while this settings dropdown is open.</summary>
internal sealed class ModConfigDropdownHost : MonoBehaviour
{
    private RectTransform? _host, _panel;
    private Transform? _previousParent;
    private TMP_Dropdown? _dropdown;
    private bool _ownsParent;
    internal bool IsOpen => _panel != null && _panel.gameObject.activeInHierarchy;

    internal bool Open(TMP_Dropdown dropdown)
    {
        if (_host == null)
        {
            _host = OverviewUi.Rect("MCE_SettingsDropdownHost", transform);
            _host.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            OverviewUi.Fill(_host);
        }
        _host.SetAsLastSibling();
        _previousParent = OverviewUi.ModalParent;
        OverviewUi.ModalParent = _host;
        _ownsParent = true;
        _dropdown = dropdown;
        int selected = dropdown.value;
        // Snapshot option objects as well as indices: rebuilding settings while open must never
        // write an old menu index into a different option list.
        var options = dropdown.options.ToArray();
        var entries = options.Select((option, index) => new SourceToolOption {
            Entry = new UiMenuEntry {
                Label = option.text, Checked = index == selected,
                CanInvoke = () => dropdown != null && dropdown.IsActive() && dropdown.IsInteractable()
                    && dropdown.options.Count == options.Length && ReferenceEquals(dropdown.options[index], option),
                Invoke = () => {
                    dropdown.value = index; // uses native change listeners / settings persistence
                    if (dropdown != null) dropdown.RefreshShownValue();
                }
            }
        }).ToArray();
        string heading = dropdown.GetComponentInParent<SettingsUICell>()?.m_text?.text
            ?? Loc.T("Options", "选项", "選項");
        _panel = OverviewUi.SourceToolsDropdown((RectTransform)dropdown.transform,
            new[] { new SourceToolGroup(heading, OverviewUi.Ivory, entries) }, (_, __) => { });
        if (_panel == null) { ReleaseParent(); return false; }
        // Shared rows already supply hover/focus tint, scroll-to-selection and explicit Up/Down
        // navigation. Begin on the selected value rather than always on the first setting option.
        var rows = _panel.GetComponentsInChildren<Button>(false);
        if (selected >= 0 && selected < rows.Length)
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(rows[selected].gameObject);
        return true;
    }

    internal void Close()
    {
        if (_panel != null && OverviewUi.ModalParent == _host) OverviewUi.CloseModal();
        ReleaseParent();
    }

    private void ReleaseParent()
    {
        if (_host != null && OverviewUi.ModalParent == _host) OverviewUi.ModalParent = _previousParent;
        _panel = null; _dropdown = null; _previousParent = null; _ownsParent = false;
    }

    private void LateUpdate()
    {
        if (_panel == null) { if (_ownsParent) ReleaseParent(); return; }
        if (_dropdown == null || !_dropdown.IsActive() || !_dropdown.IsInteractable()) Close();
    }
    private void OnDisable() => Close();
}
