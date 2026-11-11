#nullable enable
using System;
using System.IO;
using System.Text;
using ModConfigEnhance.Overview.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed class PublicationContentView : IDisposable
{
    private readonly RectTransform _host;
    private readonly OverviewRepository _repo;
    private readonly IContentFiles _drafts;
    private readonly bool _publication;
    private readonly Action<string> _status;
    private TMP_InputField? _input;
    private TextInputUndoBinding? _undo;
    private string _initial = "", _relative = "";
    private IntroductionEditorView? _editor;
    private IntroductionMedia? _media;
    internal bool HasDirty => _editor?.IsDirty == true || _input != null && _input.text != _initial;
    internal bool TryCloseLayer() => _media?.CloseEnlarged() == true || _editor?.TryCloseLayer() == true;
    internal bool SaveCurrent() => !HasDirty || (_editor != null ? _editor.TrySave() : Save());
    internal PublicationContentView(RectTransform host, OverviewRepository repo, IContentFiles drafts, Action<string> status)
    { _host = host; _repo = repo; _drafts = drafts; _publication = drafts is PublicationDrafts; _status = status; Hint(Loc.T("Select a file to preview its content.", "选择文件，直接在这里预览内容。", "選擇檔案，直接在此預覽內容。")); }
    internal void RequestLeave(Action next)
    {
        _media?.CloseEnlarged();
        if (_editor != null) { _editor.RequestLeave(next); return; }
        if (!HasDirty) { next(); return; }
        OverviewUi.ConfirmUnsaved(Loc.T("Publication text has unsaved changes", "发布文本有未保存的修改", "發布文字有未儲存的修改"), () => { if (Save()) next(); }, next, () => _input?.ActivateInputField());
    }
    internal void Open(string relative, Action? opened = null) => RequestLeave(() => { Show(relative); opened?.Invoke(); });
    private void Show(string relative, bool sourceMode = false)
    {
        Reset(); _relative = relative;
        try
        {
            string? path = _drafts.Resolve(relative);
            if (path == null) { Hint(Loc.T("This record is generated when the package is built.", "这项资料将在生成发布包时自动写入。", "此資料會在產生發布套件時自動寫入。")); return; }
            var title = OverviewUi.Text(_host, Path.GetFileName(relative), 19); OverviewUi.PlaceClipped(title, 0, 0, _host.rect.width, 32);
            float top = 42;
            if (_publication && _drafts.IsIntroduction(relative) && !sourceMode)
            {
                var note = OverviewUi.Text(_host, Loc.T("Introduction edits save to the current profile.", "介绍修改保存到当前方案。", "介紹修改儲存至目前方案。"), 14); OverviewUi.PlaceClipped(note, 0, top, _host.rect.width, 30); top += 36;
                var body = Body(top); _editor = new IntroductionEditorView(body, _repo, OverviewUi.Text); _editor.StatusChanged += _status; _editor.Reload(); _editor.SetAuthorMode(false);
                AddButton(Loc.T("Edit formatting", "编辑排版", "編輯排版"), _host.rect.width / 2, 0, () => _editor?.SetAuthorMode(true));
                return;
            }
            string ext = Path.GetExtension(relative).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".mp4" or ".webm")
            {
                var body = Body(top); string type = ext == ".gif" ? "gif" : ext is ".mp4" or ".webm" ? "video" : "image";
                _media = new IntroductionMedia(body, type, path, OverviewUi.Text, _status, null, body, OverviewUi.ModalParent ?? _host, new IntroductionMediaState()); return;
            }
            bool text = ext is ".md" or ".txt" or ".json" or ".cfg" or ".ini" or ".xml" or ".yaml" or ".yml" or ".toml" or ".csv" || Path.GetFileName(relative).Equals("LICENSE", StringComparison.OrdinalIgnoreCase);
            if (!text) { Hint(Loc.T("This file is included as a binary file.", "此文件作为二进制文件随包附带。", "此檔案以二進位檔隨套件附帶。")); return; }
            if (new FileInfo(path).Length > 2 * 1024 * 1024) { Hint(Loc.T("Text preview supports files up to 2 MB.", "文本预览支持 2 MB 以内的文件。", "文字預覽支援 2 MB 以內的檔案。")); return; }
            string source = File.ReadAllText(path, Encoding.UTF8); bool editable = _drafts.CanEdit(relative);
            if (ext == ".md" && !sourceMode) { ShowMarkdown(relative, path, editable); return; }
            if (ext == ".md")
            {
                AddButton(Loc.T("Read / format", "阅读／排版", "閱讀／排版"), 0, top, () => RequestLeave(() => ShowMarkdown(relative, _drafts.Resolve(relative) ?? path, editable)));
                top += 42;
            }
            var help = OverviewUi.Text(_host, editable ? _drafts.Notice : Loc.T("Edit this record in its management tab.", "请在对应管理标签里编辑这项资料。", "請在對應管理標籤編輯這項資料。"), 13);
            help.textWrappingMode = TextWrappingModes.Normal;
            float noteHeight = Mathf.Max(36, help.GetPreferredValues(help.text, _host.rect.width, 0).y + 8);
            OverviewUi.Place(help.rectTransform, 0, top, _host.rect.width, noteHeight); top += noteHeight + 8;
            _initial = source; _input = OverviewUi.Input(_host, source, true); _input.characterLimit = 2 * 1024 * 1024; _input.textComponent.fontSize = 16; _input.readOnly = !editable;
            OverviewUi.Place((RectTransform)_input.transform, 0, top, _host.rect.width - 16, Mathf.Max(70, _host.rect.height - top - 52));
            AttachInputScrollbar(_input);
            if (editable) _undo = TextInputUndoBinding.Attach(_input, source);
            if (editable)
            {
                AddButton(_drafts.SaveLabel, 0, _host.rect.height - 40, () => Save(true), UiActionRole.Commit);
                AddButton(Loc.T("Undo edits", "撤销修改", "復原修改"), 166, _host.rect.height - 40, () => { if (_undo != null) _undo.ReplaceText(_initial); else _input.SetTextWithoutNotify(_initial); }, UiActionRole.Neutral);
            }
        }
        catch (Exception error) { Hint(Loc.LocalizeMessage(error.Message)); _status(Loc.LocalizeMessage(error.Message)); }
    }
    private void ShowMarkdown(string relative, string path, bool editable)
    {
        Reset(); _relative = relative;
        AddButton(Loc.T("Source text", "查看源码", "檢視原始碼"), 0, 0, () => RequestLeave(() => Show(relative, true)), UiActionRole.Neutral);
        if (editable) AddButton(Loc.T("Edit formatting", "编辑排版", "編輯排版"), 166, 0, () => _editor?.SetAuthorMode(true));
        var body = Body(42); _editor = new IntroductionEditorView(body, new PublicationDocumentSource(_drafts, relative, path,
            () => { if (_publication) OverviewUi.ShowText(Loc.T("Publication changes stored", "本次发布修改已存储", "本次發布修改已儲存"), _drafts.Notice); else _status(Loc.T("File changes are saved in the draft. Select Save as personal copy to commit them.", "文件修改已保存到草稿，点击“保存为个人副本”提交。", "檔案修改已儲存至草稿，請按「另存為個人副本」提交。")); }), OverviewUi.Text);
        _editor.StatusChanged += _status; _editor.Reload();
    }
    private void AttachInputScrollbar(TMP_InputField input)
    {
        var track = OverviewUi.Panel("TextScrollbar", _host, ModConfigLook.Surface(new Color(.07f, .07f, .06f, 1)));
        var inputRect = (RectTransform)input.transform; OverviewUi.Place(track, _host.rect.width - 10, -inputRect.anchoredPosition.y, 10, inputRect.rect.height);
        var handle = OverviewUi.Panel("Handle", track, OverviewUi.Gold); OverviewUi.Fill(handle);
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.targetGraphic = handle.GetComponent<Image>(); bar.handleRect = handle; bar.direction = Scrollbar.Direction.BottomToTop; input.verticalScrollbar = bar;
    }
    private RectTransform Body(float top) { var body = OverviewUi.Rect("ContentBody", _host); OverviewUi.Place(body, 0, top, _host.rect.width, Mathf.Max(70, _host.rect.height - top)); return body; }
    private void AddButton(string label, float x, float y, Action click, UiActionRole role = UiActionRole.Edit)
    { float width = Mathf.Min(154, (_host.rect.width - 12) / 2); var button = OverviewUi.Button(_host, label, click, width, 32, role); OverviewUi.Place((RectTransform)button.transform, x > 0 ? width + 12 : 0, y, width, 32); }
    private bool Save(bool showNotice = false)
    {
        try { if (_input == null) return true; _drafts.SaveText(_relative, _input.text); _initial = _input.text; _undo?.MarkSaved(); _status(_publication ? Loc.T("Publication draft saved.", "发布草稿已保存。", "發布草稿已儲存。") : Loc.T("File changes are saved in the draft. Select Save as personal copy to commit them.", "文件修改已保存到草稿，点击“保存为个人副本”提交。", "檔案修改已儲存至草稿，請按「另存為個人副本」提交。")); if (showNotice && _publication) OverviewUi.ShowText(Loc.T("Publication changes stored", "本次发布修改已存储", "本次發布修改已儲存"), _drafts.Notice); return true; }
        catch (Exception error) { _status(Loc.T("Save failed: ", "保存失败：", "儲存失敗：") + Loc.LocalizeMessage(error.Message)); return false; }
    }
    private void Hint(string text) { var form = new ManagementForm(_host, action => action()); form.Text(text); }
    private void Reset()
    {
        _editor?.Dispose(); _editor = null; _media?.Dispose(); _media = null; _input = null; _undo = null;
        if (_host == null) return;
        foreach (Transform child in _host) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
    }
    public void Dispose() => Reset();
}

internal sealed class PublicationPreviewLifetime : MonoBehaviour
{
    internal Action? Release;
    private void OnDestroy() { Release?.Invoke(); Release = null; }
}
