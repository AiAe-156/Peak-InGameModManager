using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;

var root = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var dir = Path.Combine(root, "plugins", "Test", "MCEPack", "language"); Directory.CreateDirectory(dir);
File.WriteAllText(Path.Combine(dir, "en-us.txt"), "MCE_SEARCH=Search\n");
File.WriteAllText(Path.Combine(dir, "zh-cn.txt"), "Mod One=模组一\n# Heading=标题\n");
File.WriteAllText(Path.Combine(dir, "zh-cn_补翻.txt"), "Mod Two=模组二\n");
var task = new PackTask { Id = Guid.NewGuid().ToString("N"), StagingDirectory = root, Request = new() { Operation = "view-stored" }, Proposed = new() { Id = new string('a',32), AuthorRoot = "plugins/Test/MCEPack", Language = "zh-cn" } };
PackLibrary.Home = root;
int count=0;void Check(bool ok,string title){if(!ok)throw new Exception(title);count++;Console.WriteLine("PASS "+title);}
Check(PackPreviewLanguage.Translate(task,"Mod One")=="模组一","Chinese uses the selected pack dictionary");
Check(PackPreviewLanguage.Translate(task,"Mod Two")=="模组二","supplement files merge into the same group");
task.Proposed.Language="en-us";
Check(PackPreviewLanguage.Translate(task,"Mod One")=="Mod One","English missing entries use original names rather than current-game Chinese");
Check(PackPreviewLanguage.Translate(task,"山顶伙伴")=="山顶伙伴","missing English introduction is retained as author original");
Check(PackPreviewLanguage.TranslateMarkdown(task,"# 山顶伙伴\n![image](media/test.png)")=="# 山顶伙伴\n![image](media/test.png)","missing Markdown translations and media paths stay intact");
task.Proposed.Language="zh-cn";
Check(PackPreviewLanguage.Translate(task,"Mod One")=="模组一","switching back restores only that language cache");
Check(PackPreviewLanguage.TranslateMarkdown(task,"# Heading\n![image](media/test.png)")=="标题\n![image](media/test.png)","exact translated lines change without rewriting media paths");
var other = new PackTask { Id=Guid.NewGuid().ToString("N"), StagingDirectory=root, Request=new(){Operation="view-stored"}, Proposed=new(){Id=new string('b',32),AuthorRoot="plugins/Other/MCEPack",Language="zh-cn"}};
Check(PackPreviewLanguage.Translate(other,"Mod One")=="Mod One","another pack never inherits this pack's dictionary");
Console.WriteLine("PASS "+count+" pack-local preview-language checks");

namespace ModConfigEnhance.Updating
{
    public sealed class PackTask { public string Id="",StagingDirectory="";public PackRequest Request=new(); public PackRecord Proposed=new(); }
    public sealed class PackRequest { public string Operation=""; }
    public sealed class PackRecord { public string Id="",AuthorRoot="",Language=""; public List<PackFile> Files=new(); }
    public sealed class PackFile { public string Path=""; }
}
namespace ModConfigEnhance.Overview
{
    internal static class PackLibrary { internal static string Home=""; }
    internal static class OverviewProfileAssets
    {
        internal static string CheckedPath(string root,string relative){string prefix=Path.GetFullPath(root)+Path.DirectorySeparatorChar;string p=Path.GetFullPath(Path.Combine(root,relative));if(!p.StartsWith(prefix))throw new IOException("outside");return p;}
    }
}
namespace ModConfigEnhance
{
    internal static class Loc
    {
        internal sealed class LanguageFile {internal Dictionary<string,string> Entries=new();}
        internal static LanguageFile Parse(string path){var f=new LanguageFile();foreach(var l in File.ReadAllLines(path)){int at=l.IndexOf('=');if(at>0)f.Entries[l[..at]]=l[(at+1)..];}return f;}
    }
}
