#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using ModConfigEnhance.Updating;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Edits are staged outside the immutable revision. The worker creates a personal copy on explicit save.</summary>
internal sealed class PackContentFiles : IContentFiles, IContentReadTransform
{
    private readonly PackTask _task;
    private readonly string _content;
    internal bool HasEdits { get; private set; }
    public string Root { get; }
    public string Notice => Loc.T("Edits are saved as a personal copy; the author's pack and the current game files stay unchanged. Click \"Save as personal copy\" after editing.", "修改保存为个人副本；作者原包和当前游戏文件保持不变。编辑后点击“保存为个人副本”提交。", "修改儲存為個人副本；作者原包和目前遊戲檔案保持不變。編輯後點擊「儲存為個人副本」提交。");
    public string SaveLabel => Loc.T("Save file changes", "保存文件修改", "儲存檔案修改");
    internal PackContentFiles(PackTask task)
    {
        _task = task;
        _content = task.Request.Operation == "view-stored" ? task.StagingDirectory : Path.Combine(task.StagingDirectory, "content");
        Root = OverviewProfileAssets.CheckedPath(PackLibrary.Home, ".editor-drafts/" + Guid.NewGuid().ToString("N"));
    }
    public string DisplayText(string relative, string source) => relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? PackPreviewLanguage.TranslateMarkdown(_task, source) : source;
    public bool IsIntroduction(string relative) => false;
    public bool CanEdit(string relative)
    {
        return PackEditPolicy.CanEdit(relative);
    }
    public string? Resolve(string relative)
    {
        string draft = EditablePath(relative);
        if (File.Exists(draft)) return draft;
        string source = OverviewProfileAssets.CheckedPath(_content, relative);
        return File.Exists(source) ? source : null;
    }
    public string EditablePath(string relative) => OverviewProfileAssets.CheckedPath(Root, relative);
    public void SaveText(string relative, string text)
    {
        if (!CanEdit(relative)) throw new InvalidOperationException("该文件为包结构或二进制内容，仅支持查看。");
        if (Encoding.UTF8.GetByteCount(text) > PackEditPolicy.TextLimit) throw new InvalidOperationException("文本修改超过 2 MB。");
        if (relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) JToken.Parse(text);
        OverviewRepository.AtomicWrite(EditablePath(relative), text); HasEdits = true;
    }
    public void RegisterAssets(string directory, string relativeDirectory)
    {
        // The editor imports directly into this draft directory. The worker validates ownership and references.
        HasEdits = true;
    }
    internal void SavePersonalCopy(string name)
    {
        if (!HasEdits) throw new InvalidOperationException("没有文件修改需要保存。");
        OverviewUpdateQueue.SendPack(new PackRequest { Operation = "personal-copy", PackId = _task.Proposed.Id, PreviewId = _task.Id, Source = Root, SnapshotName = name });
    }
}
