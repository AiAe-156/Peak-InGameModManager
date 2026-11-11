using ModConfigEnhance.Overview.Editor;
using System.Text.RegularExpressions;
string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../Help"));
if(args.Length>0)root=Path.GetFullPath(args[0]);
int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
foreach(string lang in new[]{"zh-cn","zh-tw","en-us"}){
 var docs=Directory.GetFiles(Path.Combine(root,lang),"*.md").ToDictionary(p=>Path.GetFileNameWithoutExtension(p)!,p=>DocMarkdown.Import(File.ReadAllText(p)));
 foreach(var (id,doc) in docs){
  var anchors=doc.Blocks.OfType<ParagraphBlock>().Where(p=>p.AnchorId.Length>0).Select(p=>p.AnchorId).ToArray();
  Check(anchors.Contains("top")&&anchors.Distinct().Count()==anchors.Length,lang+"/"+id+" unique anchors");
  foreach(var link in doc.Blocks.OfType<ParagraphBlock>().SelectMany(p=>p.Runs()).Select(r=>r.Style.Link).Where(s=>s.Length>0).Distinct()){
   if(Uri.TryCreate(link,UriKind.Absolute,out var _))continue;
   var parts=link.Split('#',2);string page=parts[0].Length==0?id:parts[0].Replace(".md","");string anchor=parts.Length==2?parts[1]:"top";
   Check(docs.TryGetValue(page,out var target)&&target.Blocks.OfType<ParagraphBlock>().Any(p=>p.AnchorId==anchor),lang+"/"+id+" broken link: "+link);
  }
 }
 var overview=docs["overview"];var ps=overview.Blocks.OfType<ParagraphBlock>().ToList();
 Check(ps.Any(p=>p.Style==ParaStyle.Quote)&&ps.Any(p=>p.Style==ParaStyle.Numbered)&&ps.Any(p=>p.Style==ParaStyle.Bullet)&&overview.Blocks.Any(b=>b is DividerBlock),lang+" supported Markdown display styles");
 Check(ps.SelectMany(p=>p.Runs()).Any(r=>r.Style.Bold),lang+" bold emphasis preserved");
 var round=DocMarkdown.Import(DocMarkdown.Export(overview));
 Check(ps.Where(p=>p.AnchorId.Length>0).All(p=>round.Blocks.OfType<ParagraphBlock>().Any(q=>q.AnchorId==p.AnchorId)),lang+" anchors survive Markdown roundtrip");
}
Console.WriteLine($"PASS {n} production Markdown help checks across 30 articles");
namespace ModConfigEnhance {internal static class Loc {internal static string T(string a,string b,string c)=>a;internal static string F(string a,string b,string c,params object[] values)=>string.Format(a,values);}}
