using ModConfigEnhance;
int n=0;void Check(bool x,string m){if(!x)throw new Exception(m);n++;}
string raw="Saved resource batch needs attention: Combined resource environment requires dependency impact confirmation before continuing:\nConsumer requires Team-Provider 2.0.0\nCombined dependency confirmation is required.";
Loc.Chinese=true;string zh=Loc.LocalizeMessage(raw);Check(zh.Contains("已保存的资源批次需要处理")&&zh.Contains("整批资源需确认"),"nested prefix translation");Check(zh.Contains("Consumer requires Team-Provider 2.0.0"),"unknown package detail retained");Check(!zh.Contains("Combined dependency confirmation"),"multiline term translated");Check(Loc.LocalizeMessage("Submitted; waiting for worker acknowledgement.").Contains("已提交"),"pending acknowledgement translated");
Loc.Chinese=false;Check(Loc.LocalizeMessage(raw)==raw,"English compound original unchanged");Check(Loc.UpdaterTerms.All(t=>t.Key==t.English),"new updater keys retain original English");Console.WriteLine($"PASS {n} production message localization checks");
namespace ModConfigEnhance{
internal static partial class Loc{
 internal readonly struct Term{internal readonly string Key,English,SimplifiedChinese,TraditionalChinese;internal Term(string k,string e,string s,string t){Key=k;English=e;SimplifiedChinese=s;TraditionalChinese=t;}}
 internal static bool Chinese;
 internal static string T(string e,string s,string t)=>Chinese?s:e;
 internal static readonly Term[] InlineTerms=Array.Empty<Term>(),AdditionalTerms=Array.Empty<Term>(),Release163Terms=Array.Empty<Term>(),Release165Terms=Array.Empty<Term>(),Release166Terms=Array.Empty<Term>(),Release167Terms=Array.Empty<Term>(),Release168Terms=Array.Empty<Term>(),Release169Terms=Array.Empty<Term>(),Release1610Terms=Array.Empty<Term>(),Release20UiTerms=Array.Empty<Term>(),ConflictPresetEditorTerms=Array.Empty<Term>(),ConflictPresetTerms=Array.Empty<Term>(),ConfigTerms=Array.Empty<Term>(),AdditionalConfigTerms=Array.Empty<Term>(),Terms=Array.Empty<Term>();
 internal static Dictionary<string,string> CompatibilityAliases=new(),_inlineSnapshot=new();
 internal enum UiLang{ZhCn,ZhTw,En};internal static UiLang UiLanguage=>Chinese?UiLang.ZhCn:UiLang.En;
 internal class LanguageFile{internal Dictionary<string,string> Entries=new();}
}}
