#nullable enable
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Independent save/apply/restart indicators. Green update dots retain their own state and interactions.</summary>
internal sealed class ProfilePendingMarkers : MonoBehaviour
{
    // 2.3.6: "pending restart" was the same blue as the unread-change chip beside it; it is violet now.
    private static readonly Color[] Colors = { new Color(1f, .55f, .12f, 1f), new Color(1f, .55f, .12f, 1f), StatusChip.Restart };
    private readonly GameObject?[] _dots = new GameObject?[3];
    private Func<ProfileItemPending?>? _state;
    private Action<string>? _show;
    private Action? _hide;
    private ProfileItemPending? _last;
    private int _language = -1;
    private bool _hovered;
    private int? _hoverSlot;
    private bool _enablement;
    private int _firstSlot = -1;
    private OverviewHotkeyFocus? _focus;
    private Func<string>? _detail;
    private Func<Color>? _statusColor;

    internal static void Attach(Transform host, Func<ProfileItemPending?> state, Action<string> show, Action hide, GameObject? focusTarget = null, bool reserveText = true, bool enablement = false, Func<string>? detail = null, Func<Color>? statusColor = null, int firstSlot = -1)
    {
        var marks = host.GetComponent<ProfilePendingMarkers>() ?? host.gameObject.AddComponent<ProfilePendingMarkers>();
        marks._enablement = enablement; marks._detail=detail; marks._statusColor=statusColor; marks._firstSlot = firstSlot;
        marks._state = state; marks._show = show; marks._hide = hide;
        if (reserveText)
        {
            var text = host.GetComponent<TMP_Text>() ?? host.GetComponentInChildren<TMP_Text>(true);
            if (text != null) { var margin = text.margin; margin.z = Mathf.Max(margin.z, 46); text.margin = margin; }
        }
        var target = focusTarget ?? host.gameObject;
        var focus = target.GetComponent<OverviewHotkeyFocus>() ?? target.AddComponent<OverviewHotkeyFocus>();
        if(marks._focus!=null) { marks._focus.Selected-=marks.ShowCombined;marks._focus.Deselected-=marks.Hide; }
        marks._focus=focus;
        focus.Selected += marks.ShowCombined;
        focus.Deselected += marks.Hide;
    }
    private void OnEnable()=>OverviewMaintenance.Register(this,Refresh);
    private void OnDestroy() { OverviewMaintenance.Remove(this);if(_focus!=null) { _focus.Selected-=ShowCombined;_focus.Deselected-=Hide; } }
    internal void RefreshNow() { _language=-1;Refresh(); }
    private void Refresh()
    {
        var state = _state?.Invoke();
        if (ReferenceEquals(state, _last) && _language == OverviewLanguageBridge.Revision) return;
        _last = state; _language = OverviewLanguageBridge.Revision;
        bool[] visible = new[] { state?.NeedsSave == true, false, state?.NeedsRestart == true };
        for (int i = 0; i < visible.Length; i++)
        {
            if (visible[i] && _dots[i] == null)
            {
                int slot = i;
                // Rows pass firstSlot so the pending markers join the shared chip lane beside the update and
                // unread chips; every other host keeps its own compact row.
                var rect = _firstSlot >= 0 ? StatusChip.Create(transform, i == 0 ? StatusChip.FirstPendingSlot : StatusChip.RestartSlot, Colors[i], raycast: true) : Standalone();
                RectTransform Standalone()
                {
                    var box = OverviewUi.Rect("Pending_" + i, transform);
                    box.anchorMin = box.anchorMax = box.pivot = Vector2.one;
                    box.anchoredPosition = new Vector2(-14 - i * 11, -3); box.sizeDelta = new Vector2(7, 7);
                    var graphic = box.gameObject.AddComponent<RoundedChip>(); graphic.color = Colors[i]; graphic.raycastTarget = true;
                    return box;
                }
                var pointer = rect.gameObject.AddComponent<PendingDotPointer>(); pointer.Enter = () => Show(slot); pointer.Exit = Hide;
                _dots[i] = rect.gameObject;
            }
            if (_dots[i] != null) _dots[i]!.SetActive(visible[i]);
        }
        if (_hovered) { string detail = Detail(_hoverSlot); if (detail.Length > 0) _show?.Invoke(detail); }
    }
    private string Value(string text)
    {
        if (text.Length == 0) return Loc.T("(empty)", "（空）", "（空）");
        if (bool.TryParse(text, out bool value)) return _enablement ? value ? Loc.T("Enabled", "启用", "啟用") : Loc.T("Disabled", "禁用", "停用") : value ? Loc.T("On", "开启", "開啟") : Loc.T("Off", "关闭", "關閉");
        return text.Replace("<", "＜").Replace(">", "＞");
    }
    private string Detail(int? slot)
    {
        if(_detail!=null)
        {
            // 2.3.6: the enablement detail says what the state is, not what the hovered chip means. Name it first.
            string chip = slot == 0 ? "<color=#" + ColorUtility.ToHtmlStringRGB(Colors[0]) + ">" + Loc.T("Orange: this enable/disable change is not saved yet. Next: Save configuration.", "橙色：这项启停改动还没保存。下一步：点「保存配置」。", "橙色：這項啟停變更還沒儲存。下一步：按「儲存設定」。") + "</color>\n\n"
                : slot == 2 ? "<color=#" + ColorUtility.ToHtmlStringRGB(Colors[2]) + ">" + Loc.T("Violet: applied; takes effect after restarting PEAK.", "紫色：已应用，重启 PEAK 后生效。", "紫色：已套用，重新啟動 PEAK 後生效。") + "</color>\n\n" : "";
            return chip + _detail();
        }
        var item = _state?.Invoke(); if (item == null) return "";
        string title = slot == 0 ? Loc.T("Pending save", "待保存", "待儲存") : slot == 1 ? Loc.T("Pending apply", "待应用", "待套用") : slot == 2 ? OverviewRuntimeSettingsBridge.TimingLabel(item.EffectTiming == ConfigurationEffectTiming.NextRound ? ConfigurationEffectTiming.NextRound : ConfigurationEffectTiming.GameRestart)
            : string.Join(" · ", new[] { item.NeedsSave ? Loc.T("Pending save", "待保存", "待儲存") : "", "", item.NeedsRestart ? OverviewRuntimeSettingsBridge.TimingLabel(item.EffectTiming == ConfigurationEffectTiming.NextRound ? ConfigurationEffectTiming.NextRound : ConfigurationEffectTiming.GameRestart) : "" }.Where(text => text.Length > 0));
        if (title.Length == 0) return "";
        string notRecorded = Loc.T("Not recorded", "未记录", "未記錄");
        string Legend(int i) => "<color=#" + ColorUtility.ToHtmlStringRGB(Colors[i]) + ">" + (i == 0 ? Loc.T("Orange: unsaved", "橙色：尚未保存", "橙色：尚未儲存") : i == 1 ? Loc.T("Yellow: not applied", "黄色：目标尚未应用", "黃色：目標尚未套用") : Loc.T("Violet: ", "紫色：", "紫色：") + OverviewRuntimeSettingsBridge.TimingLabel(item.EffectTiming == ConfigurationEffectTiming.NextRound ? ConfigurationEffectTiming.NextRound : ConfigurationEffectTiming.GameRestart)) + "</color>";
        string explanation = slot.HasValue ? Legend(slot.Value) : string.Join("\n", new[] { item.NeedsSave ? Legend(0) : "", "", item.NeedsRestart ? Legend(2) : "" }.Where(s => s.Length > 0));
        string action = slot == 0 ? Loc.T("Next: Save configuration to write and use this change.", "下一步：点击“保存配置”，写入并使用此项修改。", "下一步：按「儲存設定」，寫入並使用此項變更。") : slot == 1 ? Loc.T("Next: Apply configuration to write the target to the game.", "下一步：点击“应用配置”，将目标写入实际配置。", "下一步：按「套用設定」，將目標寫入實際設定。") : slot == 2 ? (item.EffectTiming == ConfigurationEffectTiming.NextRound ? Loc.T("Next: start a new round to use the saved value.", "下一步：重新开始对局，使用已保存的配置。", "下一步：重新開始對局，使用已儲存的設定。") : Loc.T("Next: restart PEAK to use the saved value.", "下一步：重启 PEAK，使用已保存的配置。", "下一步：重新啟動 PEAK，使用已儲存的設定。")) : "";
        return explanation + (action.Length > 0 ? "\n" + action : "") + "\n\n" + "<color=#EDAA4D>" + Loc.T("Saved in profile: ", "方案已保存：", "方案已儲存：") + "</color>" + (item.SavedRecorded ? Value(item.Saved) : notRecorded) + "\n" +
            "<color=#6EADF0>" + Loc.T("Temporary target: ", "当前方案目标：", "目前方案目標：") + "</color>" + (item.DraftRecorded ? Value(item.Draft) : notRecorded) + "\n" +
            Loc.T("Current runtime: ", "当前实际生效：", "目前實際生效：") + (item.CurrentAvailable ? Value(item.NeedsRestart && item.RestartPrevious.Length > 0 ? item.RestartPrevious : item.Current) : Loc.T("Not loaded", "未加载", "未載入")) +
            (item.UnavailableReason.Length > 0 ? "\n" + Loc.LocalizeMessage(item.UnavailableReason) : "");
    }
    private void Show(int slot) { _hovered = true; _hoverSlot = slot; string text = Detail(slot); if (text.Length > 0) _show?.Invoke(text); }
    private void ShowCombined() { _hoverSlot = null; string text = Detail(null); if (text.Length > 0) _show?.Invoke(text); }
    private void Hide() { _hovered = false; _hoverSlot = null; _hide?.Invoke(); }
    private void OnDisable() { OverviewMaintenance.Remove(this);if (_hovered) Hide(); }
}

// Stops a status dot click from being treated as reading the neighboring green update dot or editing a value.
internal sealed class PendingDotPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
{
    internal Action? Enter, Exit;
    public void OnPointerEnter(PointerEventData data) => Enter?.Invoke();
    public void OnPointerExit(PointerEventData data) => Exit?.Invoke();
    public void OnPointerDown(PointerEventData data) => data.Use();
    public void OnPointerClick(PointerEventData data) => data.Use();
}
