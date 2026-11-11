// Converts a hand-written introduction Markdown into the editor's own JSON document with MCE's real importer,
// then checks the round trip is stable. Usage: convert <input.md> <out.mcedoc.json> <out.md>
using ModConfigEnhance.Overview.Editor;
string input = args[0], json = args[1], md = args[2];
var doc = DocMarkdown.Import(File.ReadAllText(input));
string written = DocJson.Write(doc);
var reread = DocJson.Read(written);
string exported = DocMarkdown.Export(reread);
var again = DocMarkdown.Import(exported);
bool stable = DocJson.Write(again) == written;
var paras = doc.Blocks.OfType<ParagraphBlock>().ToList();
Console.WriteLine($"blocks {doc.Blocks.Count}, paragraphs {paras.Count}, headings {paras.Count(p => p.Style is ParaStyle.Heading1 or ParaStyle.Heading2 or ParaStyle.Heading3)}, bullets {paras.Count(p => p.Style == ParaStyle.Bullet)}, numbered {paras.Count(p => p.Style == ParaStyle.Numbered)}, dividers {doc.Blocks.Count(b => b is DividerBlock)}, media {doc.AllMedia().Count()}, backgrounds {paras.Count(p => p.Background.Length > 0)}, colored runs {paras.SelectMany(p => p.Runs()).Count(r => r.Style.Color.Length > 0)}");
Console.WriteLine("round trip stable: " + stable);
var raw = paras.Where(p => p.Text.Contains("<span") || p.Text.Contains("data-mce") || p.Text.Contains("**")).Select(p => p.Text.Substring(0, Math.Min(60, p.Text.Length))).ToList();
Console.WriteLine("paragraphs with leftover markup: " + raw.Count); foreach (var r in raw.Take(5)) Console.WriteLine("  " + r);
if (!stable || raw.Count > 0) return 1;
File.WriteAllText(json, written, new System.Text.UTF8Encoding(false));
File.WriteAllText(md, exported, new System.Text.UTF8Encoding(false));
return 0;
