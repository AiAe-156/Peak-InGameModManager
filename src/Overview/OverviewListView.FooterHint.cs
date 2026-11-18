#nullable enable
using TMPro;
using UnityEngine;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 同步自 MCE 1.1.0（实测修正，用户 2026-11-16）：列表最后一行下面还有空白时，在空白里用淡色小字提示操作
/// （IGMM：铅笔 = 编辑模式，开着才能拖动排序、双击名称或介绍编辑）。只在键鼠模式显示，手柄模式不显示；空白不够就藏起来，不挡列表。
/// 提示挂在视口上、贴视口底边，不进行容器（不占滚动高度、不参与虚拟行与出场动画），也不接收点击。
/// </summary>
internal sealed partial class OverviewListView
{
    private const float FooterHintHeight = 28, FooterHintBottom = 10, FooterHintGap = 12;
    private TextMeshProUGUI? _footerHint;
    private Loc.UiLang _footerHintLanguage;

    private static string FooterHintText => Loc.T(
        "Turn on the pencil (edit mode), then drag a row to reorder or double-click a name or description to edit it.",
        "打开铅笔（编辑模式）后，按住拖动可排序，双击名称或介绍可编辑",
        "開啟鉛筆（編輯模式）後，按住拖曳可排序，雙擊名稱或介紹可編輯");

    /// <summary>每帧（列表可见时）由 pump 调用；只改显隐，文字在语言变了时才换。</summary>
    private void UpdateFooterHint()
    {
        float free = _viewport.rect.height - _content.rect.height;
        bool show = !GamepadSupport.IsGamepad && !_building && _virtualRows.Count > 0
            && free >= FooterHintHeight + FooterHintBottom + FooterHintGap;
        if (!show)
        {
            if (_footerHint != null && _footerHint.gameObject.activeSelf) _footerHint.gameObject.SetActive(false);
            return;
        }
        if (_footerHint == null)
        {
            _footerHint = OverviewUi.Text(_viewport, FooterHintText, 16);
            _footerHintLanguage = Loc.UiLanguage;
            var rect = _footerHint.rectTransform;
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, FooterHintBottom); rect.sizeDelta = new Vector2(-40, FooterHintHeight);
            _footerHint.alignment = TextAlignmentOptions.Midline;
            _footerHint.overflowMode = TextOverflowModes.Ellipsis;
            _footerHint.raycastTarget = false;
            Color ink = OverviewUi.Muted; ink.a = .6f; _footerHint.color = ink;
            _footerHint.name = "FooterHint";
        }
        else if (_footerHintLanguage != Loc.UiLanguage)
        {
            _footerHintLanguage = Loc.UiLanguage;
            _footerHint.text = FooterHintText;
        }
        if (!_footerHint.gameObject.activeSelf) _footerHint.gameObject.SetActive(true);
    }
}
