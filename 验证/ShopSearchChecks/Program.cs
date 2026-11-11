using ModConfigEnhance.Overview;
int n=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);n++;}
Check(ShopSearch.Matches("Author-PeakStatsEx","","peak stats ex"),"spaced name matches camelcase");
Check(ShopSearch.Matches("Author-PeakStatsEx","","peak stats"),"partial spaced name");
Check(ShopSearch.Matches("Author-Peak_Stats_Ex","","peak-stats-ex"),"underscore hyphen normalization");
Check(ShopSearch.Matches("Author-Mod","voice communication","mod voice"),"tokens match name and description");
Check(ShopSearch.Matches("Author-Mod","",""),"empty query displays initial catalog");
Check(!ShopSearch.Matches("Author-Mod","","missing"),"unrelated query excluded");
Check(ShopDocumentRules.ImageUrl("https://github.com/MiiMii1205/PiggyBank/blob/master/imgs/piggybank.png?raw=true")=="https://raw.githubusercontent.com/MiiMii1205/PiggyBank/master/imgs/piggybank.png","github blob image normalized");
var doc=ShopDocumentRules.Prepare("<p><img src=\"https://github.com/a/b/blob/main/a.png?raw=true\" /></p>\n<dl><dt>Weight</dt><dd>1</dd></dl>");
Check(doc.Contains("![image](https://raw.githubusercontent.com/a/b/main/a.png)")&&!doc.Contains("<dt>"),"HTML image and definition list converted");
Check(!ShopDocumentRules.Prepare("![badge](https://img.shields.io/thunderstore/v/A/B)").StartsWith("!"),"SVG badge becomes link");
Console.WriteLine($"PASS {n} search/document checks");
