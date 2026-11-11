#nullable enable
using System;
using System.IO;
using ModConfigEnhance.Overview.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.UI;

namespace ModConfigEnhance.Overview.Help;
internal sealed partial class HelpPage
{
    private RectTransform _readingHost = null!;
    private IntroductionEditorView? _editorView;
    private HelpDocumentSource? _editorSource;
    private Button? _restoreBuiltin;
    private bool _editing, _modeChangePending, _fontRootChanged, _guideResourcesStale;
    private string? _previousFontDirectory;
    private string _loadedOverrideStamp = "";
    private HelpDocumentSource Source
    {
        get { _editorSource ??= new HelpDocumentSource(); return _editorSource; }
    }
    private void RememberFontDirectory() { if (!_fontRootChanged) _previousFontDirectory = IntroductionFontRegistry.PackFontsDirectory; }
    private void RestoreFontDirectory()
    {
        if (!_fontRootChanged) return;
        IntroductionFontRegistry.PackFontsDirectory = _previousFontDirectory ?? ""; _fontRootChanged = false;
        _guideResourcesStale = true;
        if (_parent is OverviewPage page) page.GuideFontsChanged();
    }
    private void ConfigureHelpAssets()
    {
        if (!_editing && !Source.HasOverride && !Directory.Exists(Source.FontsDirectory)) return;
        IntroductionFontRegistry.PackFontsDirectory = Source.FontsDirectory; _fontRootChanged = true;
    }
    private string ResolveHelpMedia(string relative) => Source.ResolveMedia(relative);
    private void RequestDocumentLeave(Action action) { if (_editing && _editorView != null) _editorView.RequestLeave(action); else action(); }
    private void LeaveToParent() => _parent.GetComponentInParent<UIPageHandler>()?.TransistionToPage(_parent, new SetActivePageTransistion());
    private string OverrideStamp()
    {
        string Stamp(string extension) { var file = new FileInfo(Path.Combine(Source.RootDirectory, _page + extension)); return file.Exists ? file.Length + ":" + file.LastWriteTimeUtc.Ticks : ""; }
        return _language + "/" + _page + "/" + Stamp(".mcedoc.json") + "/" + Stamp(".md");
    }
    private void LoadEditorPage()
    {
        Source.PageId = _page; Source.Language = _language;
        ConfigureHelpAssets(); _loadedOverrideStamp = OverrideStamp(); _guideResourcesStale = false;
        if (_editing && _editorView != null) { _editorView.Reload(); _editorView.SetAuthorMode(true); }
    }
    private void UpdateEditingMode()
    {
        bool requested = Plugin.AllowGuideEditing.Value;
        if (requested == _editing) { _modeChangePending = false; return; }
        if (_editorView?.IsImporting == true) { _status.text = Loc.T("Wait for the asset import to finish.", "请等待素材导入完成。", "請等待素材匯入完成。"); return; }
        if (_modeChangePending)
        {
            if (!OverviewUi.HasModal && _editorView?.IsDirty != true) { _modeChangePending = false; SetEditing(false); }
            return;
        }
        if (_editing && _editorView?.IsDirty == true)
        {
            _modeChangePending = true;
            _editorView.RequestLeave(() => { _modeChangePending = false; SetEditing(false); }, () => { Plugin.AllowGuideEditing.Value = true; _modeChangePending = false; });
            return;
        }
        SetEditing(requested);
    }
    private void SetEditing(bool editing)
    {
        RememberPosition(); float position = _positions.TryGetValue(PositionKey(_page), out float stored) ? stored : 0;
        _editing = editing;
        if (editing && _editorView == null)
        {
            Source.PageId = _page; Source.Language = _language;
            _editorView = new IntroductionEditorView(_articleHost, Source, FixedText);
            _editorView.StatusChanged += message => _status.text = message;
            _editorView.DraftChanged += RefreshRestoreButton; _editorView.FollowLink = FollowLink;
        }
        _readingHost.gameObject.SetActive(!editing);
        if (_editorView != null)
        {
            if (editing) { _editorView.Root.SetActive(true); _editorView.SetAuthorMode(true); }
            else { _editorView.SetAuthorMode(false); _editorView.Root.SetActive(false); }
        }
        if (editing) LoadEditorPage(); else LoadPage(_page, null, position);
        _pendingOffset = position; _settleFrames = 3; RefreshChrome(); Layout();
        PublicationNavigation.Attach(_frame);
        if (editing && !GamepadSupport.IsGamepad) _editorView?.ActivateEditor();
    }
    private string GuideSourceLabel() => (_language.Length > 0 ? _language + " · " : "") + (Source.HasOverride
        ? Loc.T("Local changes", "本地修改", "本機修改") : Loc.T("Built-in guide", "内置说明", "內建說明"));
    private void RefreshRestoreButton()
    {
        if (_restoreBuiltin != null)
        {
            _restoreBuiltin.gameObject.SetActive(_editing);
            _restoreBuiltin.interactable = Source.HasOverride || _editorView?.IsDirty == true;
        }
    }
    private void LayoutRestoreButton(float sideWidth, float height)
    {
        if (_editing && _restoreBuiltin == null) _restoreBuiltin = HelpButton(_sidebar, Loc.T("Restore this page", "恢复本页内置", "還原本頁內建內容"), RestoreCurrentPage, sideWidth - 16, 34, UiActionRole.Danger);
        if (_restoreBuiltin != null) OverviewUi.Place((RectTransform)_restoreBuiltin.transform, 8, height - 152, sideWidth - 16, 34);
        ((RectTransform)_chapterRows.parent.parent).offsetMin = new Vector2(0, _editing ? 46 : 0);
        RefreshRestoreButton();
    }
    private void RestoreCurrentPage()
    {
        RequestDocumentLeave(() => OverviewUi.ConfirmText(Loc.T("Restore the built-in guide?", "恢复内置说明？", "還原內建說明？"),
            Loc.T("Restore this page in the current language. Saved overrides are moved to backups; other pages and languages are kept.", "恢复当前语言的当前章节。已保存的覆盖资料移入备份，其他章节和语言保留。", "還原目前語言的目前章節。已儲存的覆寫資料移入備份，保留其他章節和語言。"),
            () => { Source.RestoreBuiltin(); LoadPage(_page, null, 0); _status.text = Loc.T("Built-in guide restored. Previous changes are in backups.", "已恢复内置说明，原修改保留在备份中。", "已還原內建說明，原修改保留在備份中。"); }, UiActionRole.Danger));
    }
    private void UpdateEditorScroll()
    {
        if (_editorView == null) return;
        if (_settleFrames > 0)
        {
            Canvas.ForceUpdateCanvases();
            if (_pendingAnchor != null && _article.Anchors.TryGetValue(_pendingAnchor, out int index)) _editorView.ScrollToBlock(index);
            else if (_pendingOffset.HasValue) _editorView.ScrollOffset = _pendingOffset.Value;
            if (--_settleFrames == 0) { _pendingAnchor = null; _pendingOffset = null; }
        }
        if (!OverviewUi.HasModal && GamepadSupport.IsGamepad && UnityEngine.InputSystem.Gamepad.current != null)
        {
            float stick = UnityEngine.InputSystem.Gamepad.current.rightStick.ReadValue().y;
            if (Mathf.Abs(stick) > .15f) _editorView.ScrollOffset -= stick * 650 * Time.unscaledDeltaTime;
        }
    }
}
