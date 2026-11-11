// 2.5.0 正则规则夹具：链接真实 Loc.Regex.cs，只补 Loc 的几个依赖桩。
// 验证解析与 XUnity 5.6.1 RegexTranslation 一致、后写优先、坏规则报错不抛、超时保护。
#nullable enable
using System;
using System.Collections.Generic;
using ModConfigEnhance;

int failed = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }

Loc.RegexRule? Make(string key, string value) => Loc.TryCompileRegex(key, value, "test.txt", 1, out _);
string Run(List<Loc.RegexRule> rules, string text) => Loc.TryRegex(rules, text, out string t) ? t : "<no match>";

// 1. 基本格式：r:"..."=译文，$1 引用
var players = Make("r:\"^(\\d+) players$\"", "$1 名玩家")!;
Check(players != null, "compile quoted pattern");
Check(Run(new() { players! }, "12 players") == "12 名玩家", "$1 substitution");
Check(Run(new() { players! }, "12 players online") == "<no match>", "anchored pattern does not match longer text");

// 2. 译文也可以带 r:"..."（XUnity 允许）
var quotedValue = Make("r:\"^Level (\\d+)$\"", "r:\"第 $1 关\"")!;
Check(Run(new() { quotedValue }, "Level 3") == "第 3 关", "quoted r: value is unwrapped");

// 3. 没有引号：整段就是正则
var bare = Make("r:^Hello (.+)$", "你好 $1")!;
Check(Run(new() { bare }, "Hello Scout") == "你好 Scout", "unquoted pattern");

// 4. 命名组
var named = Make("r:\"^(?<n>\\d+)s left$\"", "剩余 ${n} 秒")!;
Check(Run(new() { named }, "5s left") == "剩余 5 秒", "named group");

// 5. 后写的优先
var early = Make("r:\"^(\\d+) items$\"", "$1 件物品")!;
var late = Make("r:\"^(\\d+) items$\"", "$1 个物品")!;
Check(Run(new() { early, late }, "4 items") == "4 个物品", "later rule wins");

// 6. 坏规则：引号不成对 / 正则语法错 → null + 原因，不抛
Check(Loc.TryCompileRegex("r:\"^(abc", "x", "f.txt", 7, out string e1) == null && e1.Length > 0, "unclosed quote reported");
Check(Loc.TryCompileRegex("r:\"^(abc\"", "x", "f.txt", 8, out string e2) == null && e2.Length > 0, "bad regex reported");
Check(Loc.TryCompileRegex("r:\"\"", "x", "f.txt", 9, out string e3) == null && e3.Length > 0, "empty pattern reported");

// 7. 2.5.4：与 XUnity 一致——命中即用 Match.Result 作整句结果，即使与原文相同也算命中、不再试更早的规则
var identity = Make("r:\"^(.+)$\"", "$1")!;
var earlier = Make("r:\"^same$\"", "早的规则")!;
Check(Run(new() { earlier, identity }, "same") == "same", "a match is a hit even when the result equals the original (later rule wins)");

// 7b. 不带锚点：XUnity 用匹配结果代替整句，不保留匹配外的文字
var loose = Make("r:\"(\\d+) players\"", "$1 名玩家")!;
Check(Run(new() { loose }, "Now 3 players online") == "3 名玩家", "unanchored rule: whole sentence becomes Match.Result (XUnity semantics)");

// 7c. XUnity 词典转义（用 verbatim 字符串写文件里的原样字节）
Check(Loc.UnescapeXUnity(@"^(\\d+) x$") == @"^(\d+) x$", "double backslash -> one backslash");
Check(Loc.UnescapeXUnity(@"a\=b%3Dc") == "a=b=c", "backslash-equals and %3D -> =");
Check(Loc.UnescapeXUnity(@"x\u4E2Dy") == "x中y", "backslash-uXXXX");
Check(Loc.UnescapeXUnity(@"^(\d+)$") == @"^(\d+)$", "regex escapes such as backslash-d kept");
Check(Loc.UnescapeXUnity(@"line\nnext") == "line\nnext", "backslash-n -> newline");

// 7d. 普通行的转义往返：原文以反斜杠结尾 / 含路径 / 含 = 和换行
foreach (string original in new[] { @"C:\path\", @"config\ModConfigEnhance\language", "a=b", "two\nlines", @"x\=y", @"\\server" })
{
    string line = Loc.Escape(original) + "=" + Loc.Escape("译文");
    int eq = Loc.FindUnescapedEquals(line);
    Check(eq > 0 && Loc.Unescape(line.Substring(0, eq)) == original && Loc.Unescape(line.Substring(eq + 1)) == "译文", "escape round trip: " + original.Replace("\n", "\\n"));
}
Check(Loc.Escape(@"config\ModConfigEnhance\language") == @"config\ModConfigEnhance\language", "plain paths keep single backslashes");
Check(Loc.Unescape(@"old\path") == @"old\path", "old files: lone backslash stays");

// 8. 超时保护：灾难性回溯不卡死，返回未命中
var evil = Make("r:\"^(a+)+$\"", "x")!;
var start = DateTime.UtcNow;
string evilResult = Run(new() { evil }, new string('a', 40) + "!");
Check(evilResult == "<no match>" && (DateTime.UtcNow - start).TotalSeconds < 5, "catastrophic pattern times out safely");

// 9. IsRegexKey
Check(Loc.IsRegexKey("r:\"x\"") && !Loc.IsRegexKey("Regular text") && !Loc.IsRegexKey("sr:\"x\""), "IsRegexKey only r:");

Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
return failed == 0 ? 0 : 1;
