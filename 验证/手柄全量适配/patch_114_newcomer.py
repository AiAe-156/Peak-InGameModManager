# -*- coding: utf-8 -*-
"""1.1.4 export (user 2026-11-13 "带上三件套"): a complete local modpack also produces a newcomer ZIP = the same content
+ the game-root BepInEx loader files (winhttp.dll, doorstop_config.ini, .doorstop_version) + a bilingual install note,
so someone without BepInEx / r2modman unzips it into the PEAK folder and plays. The IGMM-import ZIP stays unchanged.
Every export also clears values that look private (API keys, secrets, tokens, passwords, proxy credentials) in the
packed config copies — never the live files — and lists what was cleared in the README."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src'))
def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('Overview/PackPublisher.cs', [
('''    internal bool Sign = true, IncludeBootstrap = true;''',
 '''    internal bool Sign = true, IncludeBootstrap = true;
    /// <summary>1.1.4: complete local packs also write a "newcomer" ZIP with the BepInEx loader files and an install note.</summary>
    internal bool NewcomerZip = true;'''),
('''OutputDirectory = OutputDirectory, SigningKeyPath = SigningKeyPath, Sign = Sign, IncludeBootstrap = IncludeBootstrap,''',
 '''OutputDirectory = OutputDirectory, SigningKeyPath = SigningKeyPath, Sign = Sign, IncludeBootstrap = IncludeBootstrap, NewcomerZip = NewcomerZip,'''),
# scrub before hashing / readme
('''        OverviewRepository.AtomicWrite(Path.Combine(output, "CHANGELOG.md"), Changelog(request));''',
 '''        var scrubbed = PublicationScrub.Run(Path.Combine(output, "BepInEx", "config"));
        OverviewRepository.AtomicWrite(Path.Combine(output, "CHANGELOG.md"), Changelog(request));'''),
('''        if(missing.Count>0) File.AppendAllText(Path.Combine(output,"README.md"),"\\n\\n"+string.Join("\\n",missing.Select(m=>"- "+m))+"\\n",new UTF8Encoding(false));''',
 '''        if(missing.Count>0) File.AppendAllText(Path.Combine(output,"README.md"),"\\n\\n"+string.Join("\\n",missing.Select(m=>"- "+m))+"\\n",new UTF8Encoding(false));
        if(scrubbed.Count>0) File.AppendAllText(Path.Combine(output,"README.md"),"\\n\\n"+Loc.T("Values that looked private were cleared from the packed configuration (set them again yourself):","打包时清空了疑似私人信息的配置值（需要的话请自己重新填写）：","打包時清空了疑似私人資訊的設定值（需要的話請自行重新填寫）：")+"\\n"+string.Join("\\n",scrubbed.Select(m=>"- "+m))+"\\n",new UTF8Encoding(false));
        LastScrubbed = scrubbed;'''),
('''        string partial = zip + ".partial"; ZipFile.CreateFromDirectory(output, partial, CompressionLevel.Optimal, false); File.Move(partial, zip);
        return zip;
    }''', '''        string partial = zip + ".partial"; ZipFile.CreateFromDirectory(output, partial, CompressionLevel.Optimal, false); File.Move(partial, zip);
        LastNewcomerNote = "";
        if (request.Mode == PublishMode.Local && request.NewcomerZip) WriteNewcomerZip(output, zip, request);
        return zip;
    }
    internal static List<string> LastScrubbed = new();
    internal static string LastNewcomerNote = "";
    internal static string NewcomerPath(string zip) => Path.Combine(Path.GetDirectoryName(zip)!, Path.GetFileNameWithoutExtension(zip) + Loc.T("_newcomer", "_新手版", "_新手版") + ".zip");
    /// <summary>1.1.4: the same files plus the game-root loader (winhttp.dll, doorstop_config.ini, .doorstop_version) and an
    /// install note, so a player without BepInEx unzips it into the PEAK folder. Built after the import ZIP, from the same
    /// folder, so the import ZIP never carries game-root files (its importer rejects them).</summary>
    private static void WriteNewcomerZip(string output, string zip, PublishRequest request)
    {
        try
        {
            string winhttp = Path.Combine(GameRoot, "winhttp.dll");
            if (!File.Exists(winhttp)) { LastNewcomerNote = Loc.T("No newcomer ZIP: winhttp.dll is not in the game folder.", "未生成新手版：游戏目录里没有 winhttp.dll。", "未產生新手版：遊戲目錄裡沒有 winhttp.dll。"); return; }
            CopyFile(winhttp, Path.Combine(output, "winhttp.dll"), true);
            string version = Path.Combine(GameRoot, ".doorstop_version");
            if (File.Exists(version)) CopyFile(version, Path.Combine(output, ".doorstop_version"), true);
            // The author's ini only when it is the standard BepInEx one; an r2modman ini points into a profile folder.
            string ini = Path.Combine(GameRoot, "doorstop_config.ini");
            string text = File.Exists(ini) ? File.ReadAllText(ini) : "";
            bool standard = System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\\s*target_assembly\\s*=\\s*BepInEx[\\\\/]core[\\\\/]BepInEx\\.Preloader\\.dll\\s*$")
                && System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\\s*enabled\\s*=\\s*true\\s*$");
            File.WriteAllText(Path.Combine(output, "doorstop_config.ini"), standard ? text : StandardDoorstopIni, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, Loc.T("INSTALL.txt", "安装说明.txt", "安裝說明.txt")), NewcomerNote(request), new UTF8Encoding(true));
            string target = NewcomerPath(zip), partial = target + ".partial";
            if (File.Exists(partial)) File.Delete(partial);
            ZipFile.CreateFromDirectory(output, partial, CompressionLevel.Optimal, false); File.Move(partial, target);
        }
        catch (Exception error) { LastNewcomerNote = Loc.T("Newcomer ZIP failed: ", "新手版生成失败：", "新手版產生失敗：") + error.Message; Plugin.Log.LogWarning("[发布包] " + LastNewcomerNote); }
    }
    private const string StandardDoorstopIni = "[General]\\r\\nenabled = true\\r\\ntarget_assembly = BepInEx\\\\core\\\\BepInEx.Preloader.dll\\r\\nredirect_output_log = false\\r\\nboot_config_override =\\r\\nignore_disable_switch = false\\r\\n\\r\\n[UnityMono]\\r\\ndll_search_path_override =\\r\\ndebug_enabled = false\\r\\ndebug_address = 127.0.0.1:10000\\r\\ndebug_suspend = false\\r\\n";
    private static string NewcomerNote(PublishRequest request)
    {
        string bepinex = "";
        try { var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(Paths.BepInExRootPath, "core", "BepInEx.dll")); bepinex = info.ProductVersion ?? info.FileVersion ?? ""; } catch { }
        string game = UnityEngine.Application.version;
        string zh = "【" + request.Name + " " + request.Version + " · 新手版】\\r\\n\\r\\n"
            + "这个压缩包已经包含模组加载器 BepInEx" + (bepinex.Length > 0 ? "（" + bepinex + "）" : "") + "、全部模组和作者的配置，不需要另外安装 r2modman 或雷霆商店。\\r\\n"
            + "制作时的游戏版本：" + game + "\\r\\n\\r\\n"
            + "安装：\\r\\n"
            + "1. 在 Steam 里右键 PEAK → 管理 → 浏览本地文件，打开游戏文件夹（里面有 PEAK.exe）。\\r\\n"
            + "2. 如果文件夹里已经有 BepInEx，先把它整个复制一份备份。\\r\\n"
            + "3. 把这个压缩包里的全部内容解压到这个文件夹，遇到同名文件选「替换」。\\r\\n"
            + "4. 启动游戏。第一次进入主菜单时会应用作者的默认方案，按提示重启一次游戏。\\r\\n\\r\\n"
            + "怎么确认装好了：主菜单左上角出现「模组概览」按钮；或者游戏文件夹里出现 BepInEx\\\\LogOutput.log。\\r\\n\\r\\n"
            + "常见问题：\\r\\n"
            + "· 杀毒软件可能把 winhttp.dll 报成可疑文件，这是 BepInEx 的加载器，请把游戏文件夹加入信任。\\r\\n"
            + "· 游戏更新后模组失效：等作者发布新版本，或在「模组概览」里检查更新。\\r\\n"
            + "· 想恢复原版：删掉游戏文件夹里的 winhttp.dll（或整个 BepInEx 文件夹）即可。\\r\\n"
            + "· 这个压缩包只用于直接解压安装；已经装了 IGMM 的人请用不带「新手版」的那个压缩包导入。\\r\\n";
        string en = "\\r\\n----------------------------------------\\r\\n\\r\\n"
            + "[" + request.Name + " " + request.Version + " · Newcomer edition]\\r\\n\\r\\n"
            + "This archive already contains the BepInEx mod loader" + (bepinex.Length > 0 ? " (" + bepinex + ")" : "") + ", every mod and the author's configuration. No r2modman or Thunderstore needed.\\r\\n"
            + "Game version when built: " + game + "\\r\\n\\r\\n"
            + "Install:\\r\\n"
            + "1. In Steam, right-click PEAK → Manage → Browse local files (the folder with PEAK.exe).\\r\\n"
            + "2. If that folder already has a BepInEx folder, back it up first.\\r\\n"
            + "3. Extract everything in this archive into that folder and replace existing files.\\r\\n"
            + "4. Start the game. The author's default profile is applied on the first main-menu visit; restart once when asked.\\r\\n\\r\\n"
            + "It worked if the main menu shows a \\"Mod overview\\" button, or BepInEx\\\\LogOutput.log appears in the game folder.\\r\\n"
            + "Antivirus may flag winhttp.dll (the BepInEx loader); allow the game folder. To go back to vanilla, delete winhttp.dll.\\r\\n"
            + "This archive is for extracting only; IGMM users should import the archive without \\"newcomer\\" in its name.\\r\\n";
        return zh + en;
    }'''),
])

edit('Overview/PackPublisher.cs', [
('''                File.Move(zip, destination); return destination;''',
 '''                File.Move(zip, destination);
                string newcomer = NewcomerPath(zip);
                if (File.Exists(newcomer)) File.Move(newcomer, NewcomerPath(destination));
                return destination;'''),
])

# wizard: option + result text
edit('Overview/PublishWizard.cs', [
('''            Note(T("Complete packages always include independent BepInEx core. Game-root Doorstop remains a platform prerequisite.", "完整包始终包含独立 BepInEx core；游戏根 Doorstop 仍是平台前置条件。"));''',
 '''            Note(T("Complete packages always include independent BepInEx core. Game-root Doorstop remains a platform prerequisite.", "完整包始终包含独立 BepInEx core；游戏根 Doorstop 仍是平台前置条件。"));
            if (_request.Mode == PublishMode.Local)
            {
                Check(Loc.T("Also make a newcomer ZIP (adds the BepInEx loader files and an install note; unzip into the PEAK folder)", "同时生成新手版压缩包（附带 BepInEx 加载文件和安装说明，解压到游戏目录即可）", "同時產生新手版壓縮檔（附帶 BepInEx 載入檔案和安裝說明，解壓到遊戲目錄即可）"), _request.NewcomerZip, value => _request.NewcomerZip = value);
            }
            Note(Loc.T("Values that look private (API keys, secrets, tokens, passwords, proxy credentials) are cleared in the packed configuration and listed in the README. Your own files are not changed.", "打包时会清空看起来像私人信息的配置值（API 密钥、Secret、Token、密码、代理账号），并写进 README；你自己的文件不变。", "打包時會清空看起來像私人資訊的設定值（API 金鑰、Secret、Token、密碼、代理帳號），並寫進 README；你自己的檔案不變。"));'''),
('''            _status(T("Publication generated: ", "发布包已生成：") + output);
            _baseline = builtFingerprint;
            if (panel != null) ShowResult(T("Publication ready", "发布包已生成"), output, Directory.Exists(output) ? output : Path.GetDirectoryName(output));''',
 '''            string report = output;
            string newcomer = File.Exists(output) ? PackPublisher.NewcomerPath(output) : "";
            if (newcomer.Length > 0 && File.Exists(newcomer)) report += "\\n" + Loc.T("Newcomer ZIP: ", "新手版：", "新手版：") + newcomer;
            if (PackPublisher.LastNewcomerNote.Length > 0) report += "\\n" + PackPublisher.LastNewcomerNote;
            if (PackPublisher.LastScrubbed.Count > 0) report += "\\n" + Loc.F("Cleared {0} private-looking value(s) in the packed configuration (listed in the README).", "已清空打包配置里 {0} 处疑似私人信息（列在 README 里）。", "已清空打包設定裡 {0} 處疑似私人資訊（列在 README 裡）。", PackPublisher.LastScrubbed.Count);
            _status(T("Publication generated: ", "发布包已生成：") + report);
            _baseline = builtFingerprint;
            if (panel != null) ShowResult(T("Publication ready", "发布包已生成"), report, Directory.Exists(output) ? output : Path.GetDirectoryName(output));'''),
('''        _request.Sign, _request.IncludeBootstrap, _request.LoaderDependency,''', '''        _request.Sign, _request.IncludeBootstrap, _request.NewcomerZip, _request.LoaderDependency,'''),
])

# scrubber
p = os.path.join(SRC, 'Overview', 'PublicationScrub.cs')
io.open(p, 'w', encoding='utf-8', newline='\n').write('''#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.4 发布前脱敏（按值不按名，两者都看）：只改输出目录里的配置副本，绝不碰正在用的文件。
/// 清空的条件：键名像私人信息（api key / secret / token / password / appid / access key / cookie / session），或者
/// 值的形状像凭据（sk- / sk_tr_ / ghp_ / github_pat_ / eyJ…JWT / 带账号密码的代理地址 / PEM 私钥）。被清空的项写进 README。
/// </summary>
internal static class PublicationScrub
{
    private static readonly Regex SensitiveKey = new(@"(?i)(api[\\s_.-]*key|secret|token|pass(word|wd)?\\b|pwd\\b|app[\\s_.-]*id|access[\\s_.-]*key|private[\\s_.-]*key|client[\\s_.-]*secret|cookie|session[\\s_.-]*(id|key)|webhook)", RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveValue = new(@"(sk-[A-Za-z0-9_-]{16,}|sk_tr_[A-Za-z0-9]{8,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|eyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}|[a-z][a-z0-9+.-]*://[^\\s/:@]+:[^\\s/@]+@|-----BEGIN [A-Z ]*PRIVATE KEY-----)", RegexOptions.CultureInvariant);
    private static readonly Regex Line = new(@"^(\\s*)([^=#;\\[\\]\\r\\n][^=\\r\\n]*?)(\\s*=\\s*)(.*?)(\\s*)$", RegexOptions.CultureInvariant);
    private static readonly Regex JsonPair = new(@"""([^""\\\\]{1,80})""(\\s*:\\s*)""((?:[^""\\\\]|\\\\.)*)""", RegexOptions.CultureInvariant);
    private static readonly string[] Text = { ".cfg", ".ini", ".json", ".txt", ".yml", ".yaml", ".toml", ".properties" };

    internal static List<string> Run(string configRoot)
    {
        var cleared = new List<string>();
        if (!Directory.Exists(configRoot)) return cleared;
        foreach (string file in Directory.EnumerateFiles(configRoot, "*", SearchOption.AllDirectories))
        {
            if (!Text.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            try
            {
                if (new FileInfo(file).Length > 2 * 1024 * 1024) continue;
                string source = File.ReadAllText(file);
                string relative = file.Substring(configRoot.Length).TrimStart('\\\\', '/').Replace('\\\\', '/');
                string result = Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase) ? ScrubJson(source, relative, cleared) : ScrubLines(source, relative, cleared);
                if (!ReferenceEquals(result, source) && result != source) File.WriteAllText(file, result, new UTF8Encoding(false));
            }
            catch (Exception error) { Plugin.Log.LogWarning("[发布包] 脱敏检查跳过 " + file + "：" + error.Message); }
        }
        return cleared;
    }
    private static bool Private(string key, string value)
    {
        string v = value.Trim().Trim('"');
        if (v.Length == 0) return false;
        if (SensitiveValue.IsMatch(v)) return true;
        if (!SensitiveKey.IsMatch(key)) return false;
        // Switches and enums are settings, not secrets ("Use token = true", "Password mode = Hidden").
        if (bool.TryParse(v, out _) || Regex.IsMatch(v, @"^(none|null|default|off|on|disabled|enabled|hidden|-1|0)$", RegexOptions.IgnoreCase)) return false;
        return true;
    }
    private static string ScrubLines(string text, string file, List<string> cleared)
    {
        var lines = text.Split('\\n'); bool changed = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i].TrimEnd('\\r'); string trimmed = raw.TrimStart();
            if (trimmed.StartsWith("#") || trimmed.StartsWith(";") || trimmed.StartsWith("//")) continue;
            var m = Line.Match(raw); if (!m.Success) continue;
            string key = m.Groups[2].Value.Trim(), value = m.Groups[4].Value;
            if (!Private(key, value)) continue;
            lines[i] = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + (lines[i].EndsWith("\\r") ? "\\r" : "");
            cleared.Add(file + " → " + key); changed = true;
        }
        return changed ? string.Join("\\n", lines) : text;
    }
    private static string ScrubJson(string text, string file, List<string> cleared)
    {
        bool changed = false;
        string result = JsonPair.Replace(text, m =>
        {
            if (!Private(m.Groups[1].Value, m.Groups[3].Value)) return m.Value;
            cleared.Add(file + " → " + m.Groups[1].Value); changed = true;
            return "\\"" + m.Groups[1].Value + "\\"" + m.Groups[2].Value + "\\"\\"";
        });
        return changed ? result : text;
    }
}
''')
print('ok')
