#nullable enable
using System;
using System.IO;
using System.Linq;
using ModConfigEnhance.Overview.Editor;
namespace ModConfigEnhance.Overview;
internal sealed class ShopMarkdownSource : IDocumentEditorSource
{
    private readonly string _markdown;
    private readonly System.Collections.Generic.Dictionary<string,System.Threading.Tasks.Task<byte[]>> _media=new();
    private int _finishedMedia;
    internal bool MediaChanged(){int finished=_media.Values.Count(t=>t.IsCompleted);if(finished==_finishedMedia)return false;_finishedMedia=finished;return true;}
    internal ShopMarkdownSource(string markdown)=>_markdown=ShopUrlPolicy.PrepareMarkdown(markdown);
    public string RootDirectory=>Loc.RootDirectory;
    public string ExportDirectory=>Loc.ExportDirectory;
    public string? ReadNote=>null;
    public Document Load()=>ImportMarkdown(_markdown);
    public Document ImportMarkdown(string source)=>DocMarkdown.Import(source);
    public void Save(Document document)=>throw new InvalidOperationException("Store documents are read-only.");
    public string Export()=>throw new InvalidOperationException("Use store export.");
    public string ResolveMedia(string relative)
    {
        if (!ShopUrlPolicy.TryNormalize(relative, out relative)) return "";
        if (!_media.TryGetValue(relative, out var request) || request.IsFaulted || request.IsCanceled)
        {
            if (request?.IsFaulted == true) _ = request.Exception;
            if (!_media.ContainsKey(relative) && _media.Count >= 32) return "";
            request = ShopResourceCache.Get(relative,16*1024*1024); _media[relative] = request;
        }
        if (!request.IsCompletedSuccessfully) return "";
        var bytes=request.Result;
        string ext=Path.GetExtension(new Uri(relative).AbsolutePath);if(!new[]{".png",".jpg",".jpeg",".gif",".webp"}.Contains(ext.ToLowerInvariant()))ext=".png";
        using var sha=System.Security.Cryptography.SHA256.Create();string name=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(relative))).Replace("-","")+ext;
        string path=Path.Combine(Loc.RuntimeDirectory,"shop-media",name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);if(!File.Exists(path))File.WriteAllBytes(path,bytes);return path;
    }
}
