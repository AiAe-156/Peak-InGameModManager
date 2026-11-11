#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance.Updating;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>
/// Preview of a modpack import: verdict first, then four tabs of collapsible groups.
/// Icons are drawn as vectors because the game font has no ✔ ▸ ▫ glyphs (they showed up as empty boxes).
/// </summary>
internal sealed class PackPreviewView
{
    internal static readonly Color Good = new(.55f, .82f, .50f), Warn = new(.95f, .78f, .38f), Bad = new(.96f, .50f, .46f), Info = new(.62f, .78f, .88f);
    private enum Tab { Result, Introduction, Dependencies, Mods, Files }
    private static Tab _tab = Tab.Result;
    private static readonly HashSet<string> Collapsed = new(StringComparer.OrdinalIgnoreCase);
    private readonly RectTransform _host;
    private readonly PackReport _report;
    private readonly PackTask _task;
    private readonly float _width;
    private readonly Action _rebuild;
    private float _y;
    private readonly Action<string>? _openFile;

    internal PackPreviewView(RectTransform host, PackTask task, float width, Action rebuild, Action<string>? openFile = null) { _host = host; _task = task; _report = task.Report; _width = width; _rebuild = rebuild; _openFile = openFile; }
    private bool Importing => _task.Request.Operation == "preview-import";
    private static string L(string en, string zh) => Loc.T(en, zh, zh);

    // ---------- vector icons ----------
    private static void Icon(Transform parent, string kind, Color color, float x, float y, float size)
    {
        var rect = OverviewUi.Rect("Icon_" + kind, parent); OverviewUi.Place(rect, x, y, size, size);
        var graphic = rect.gameObject.AddComponent<OverviewIconGraphic>(); graphic.Kind = kind; graphic.color = color; graphic.raycastTarget = false;
    }
    private static string StateIcon(string state) => state switch { "ok" => "check", "warn" => "warn", "error" => "cross", _ => "dot" };
    private static Color StateColor(string state) => state switch { "ok" => Good, "warn" => Warn, "error" => Bad, _ => Info };
    private static string FileIcon(string name)
    {
        return PluginFileNames.IsAssembly(name) ? "mod" : "file";
    }

    // ---------- banner ----------
    /// <summary>The verdict banner. Returns the new bottom edge.</summary>
    internal float Banner(float top)
    {
        _y = top; var r = _report;
        if (r.Verdict.Length == 0) { Line(L("No preview is available for this task. Preview again.", "此任务没有可用的预览，请重新预览。"), Warn, 17); return _y; }
        bool ready = r.Verdict == "ready";
        var banner = OverviewUi.Panel("Verdict", _host, ready ? ModConfigLook.Surface(new Color(.17f, .27f, .17f, 1)) : ModConfigLook.Surface(new Color(.31f, .21f, .14f, 1)));
        string head = (r.Name.Length > 0 ? r.Name : L("Modpack", "整合包")) + (r.Version.Length > 0 ? "  " + r.Version : "");
        float inner = _width - 28;
        Icon(banner, ready ? "check" : "warn", ready ? Good : Warn, 14, 12, 26);
        var title = OverviewUi.Text(banner, head, 24); title.color = ready ? Good : Warn; OverviewUi.PlaceClipped(title, 48, 10, inner - 34, 34);
        var sub = OverviewUi.Text(banner, r.Headline, 17); sub.textWrappingMode = TextWrappingModes.Normal;
        float subHeight = sub.GetPreferredValues(r.Headline, inner, 0).y + 4; OverviewUi.Place(sub.rectTransform, 14, 46, inner, subHeight);
        float y = 46 + subHeight + 2;
        if (r.KindText.Length > 0)
        {
            var kind = OverviewUi.Text(banner, r.KindText, 16); kind.color = Info; kind.textWrappingMode = TextWrappingModes.Normal;
            float kh = kind.GetPreferredValues(r.KindText, inner, 0).y + 4; OverviewUi.Place(kind.rectTransform, 14, y, inner, kh); y += kh;
        }
        string counts = L("Mods: ", "模组：") + r.ModCount + L(" (", "（") + r.EnabledCount + L(" enabled)", " 已启用）")
            + "    " + L("Included ", "随包依赖 ") + r.BundledCount + "    " + L("To download ", "需下载 ") + r.DownloadCount + "    " + L("Files ", "文件 ") + r.FileCount;
        if (r.Kind == "snapshot" || r.Kind == "switch") counts = L("Files to save/apply ", "保存／应用文件 ") + r.FileCount
            + "    " + L("Configuration files ", "配置文件 ") + r.Files.Count(f => f.Group == "配置文件");
        var summary = OverviewUi.Text(banner, counts, 16); summary.color = OverviewUi.Muted; summary.textWrappingMode = TextWrappingModes.Normal;
        float summaryHeight = Mathf.Max(26, summary.GetPreferredValues(counts, inner, 0).y + 4); OverviewUi.Place(summary.rectTransform, 14, y + 2, inner, summaryHeight);
        float bannerHeight = y + summaryHeight + 10; OverviewUi.Place(banner, 0, _y, _width, bannerHeight); _y += bannerHeight;
        return _y;
    }

    /// <summary>The language choice, shown only when the pack ships language files. The game language is picked automatically.</summary>
    internal float LanguageRow(float top)
    {
        _y = top; var languages = _report.Languages; if (languages.Count == 0) return _y;
        if (PackLanguageChoice.Length > 0 && languages.Any(l => l.Key == PackLanguageChoice)) _task.Proposed.Language = PackLanguageChoice;
        var chosen = languages.FirstOrDefault(l => l.Key == _task.Proposed.Language) ?? languages[0];
        string label = L("Display language", "显示语言");
        var caption = OverviewUi.Text(_host, label, 17); caption.color = OverviewUi.Muted; OverviewUi.Place(caption.rectTransform, 0, _y + 6, 130, 28);
        string shown = (chosen.Name.Length > 0 ? chosen.Name + "  " : "") + chosen.Key;
        RectTransform? anchor = null;
        var button = OverviewUi.Button(_host, shown + "  " + OverviewUi.Caret, () =>
        {
            var options = languages.Select(l => ((l.Name.Length > 0 ? l.Name + "  " : "") + l.Key, l.Key)).ToList();
            OverviewUi.Dropdown(anchor, label, options, _task.Proposed.Language, value => { _task.Proposed.Language = value; PackLanguageChoice = value; _rebuild(); });
        }, Mathf.Min(360, _width - 140), 34, UiActionRole.Edit);
        anchor = (RectTransform)button.transform; OverviewUi.Place(anchor, 136, _y, Mathf.Min(360, _width - 140), 34);
        _y += 40;
        string hint = L("Used for the pack introduction and mod names. Chosen automatically from the game language; change it here if you prefer another file.",
            "预览仅使用本包所选词典；未翻译文本保留原文。介绍无对应译文时显示作者原文，文件名及资源路径保持不变。");
        Line(hint, OverviewUi.Muted, 14);
        return _y;
    }
    /// <summary>Remembered across rebuilds so the confirm button sends the player's choice.</summary>
    internal static string PackLanguageChoice = "";

    // ---------- tabs ----------
    /// <summary>The four tabs and the selected tab's content. Returns the new bottom edge.</summary>
    internal float Body(float top)
    {
        _y = top; var r = _report; if (r.Verdict.Length == 0) return _y;
        int problems = r.Lines.Count(l => l.Section == "problem" && !OverviewPage._ignoredPackLines.Contains(l.Title));
        int depends = r.Lines.Count(l => l.Section is "prereq" or "download");
        var tabs = new (Tab Tab, string Label)[] {
            (Tab.Result, problems > 0 ? L("Issues ", "待处理 ") + problems : L("Result", "检查结果")),
            (Tab.Introduction, L("Introduction", "介绍")),
            (Tab.Dependencies, L("Dependencies: ", "依赖项：") + depends),
            (Tab.Mods, L("Mods: ", "模组：") + r.ModCount),
            (Tab.Files, L("Files ", "全部文件 ") + r.FileCount) };
        float x = 0, tabWidth = Mathf.Min(190, (_width - 4 * (tabs.Length - 1)) / tabs.Length);
        foreach (var item in tabs)
        {
            var button = OverviewUi.Button(_host, item.Label, () => {
                _tab = item.Tab; _rebuild();
                if (item.Tab == Tab.Introduction && _openFile != null)
                {
                    string? introFile = _task.Proposed.Files.Select(f => f.Path).FirstOrDefault(p => p.EndsWith("/introduction.md", StringComparison.OrdinalIgnoreCase));
                    if (introFile != null) _openFile(introFile);
                }
            }, tabWidth, 34, _tab == item.Tab ? UiActionRole.Edit : UiActionRole.Neutral);
            var tabLabel = button.GetComponentInChildren<TMP_Text>(true); tabLabel.enableAutoSizing = true; tabLabel.fontSizeMin = 11; tabLabel.fontSizeMax = 17;
            OverviewUi.Place((RectTransform)button.transform, x, _y, tabWidth, 34); x += tabWidth + 4;
        }
        _y += 44;
        switch (_tab)
        {
            case Tab.Result: DrawResult(); break;
            case Tab.Introduction:
                var intro = _task.Proposed.Files.Select(f => f.Path).FirstOrDefault(p => p.EndsWith("/introduction.md", StringComparison.OrdinalIgnoreCase));
                if (intro != null && _openFile != null)
                {
                    var open = OverviewUi.Button(_host, L("Read introduction", "阅读介绍"), () => _openFile(intro), 180, 34, UiActionRole.Edit); OverviewUi.Place((RectTransform)open.transform, 0, _y, 180, 34); _y += 42;
                }
                else Line(L("No introduction file.", "没有介绍文件。"), OverviewUi.Muted, 17);
                break;
            case Tab.Dependencies: DrawDependencies(); break;
            case Tab.Mods: DrawMods(); break;
            default: DrawFiles(); break;
        }
        return _y;
    }

    private void DrawResult()
    {
        var errors = _report.Lines.Where(l => l.Section == "problem" && !OverviewPage._ignoredPackLines.Contains(l.Title)).ToList();
        if (errors.Count == 0)
        {
            Line(L("No problems found.", "未发现问题。"), Good, 18);
            Line(Importing ? L("Saving only adds the pack to the library; current game files are not changed until it is enabled.", "存入只会把整合包加入整合包库，启用之前不会改动当前的游戏文件。")
                : _report.Kind == "snapshot"
                    ? L("Copies the current snapshot immediately. Game files are not replaced.", "立即复制当前文件快照，不会替换游戏文件。")
                    : _task.Request.Operation == "view-stored" ? L("Viewing stored contents only. Use Preview switch to review installation changes.", "当前仅查看库内内容。需要切换时，使用“预览切换影响”。")
                    : L("Nothing changes until PEAK exits. The manager then finishes the switch; wait for it before launching again.", "退出 PEAK 之前不会有任何改动。退出后由管理器完成切换，请等待完成后再启动游戏。"), OverviewUi.Muted, 16);
        }
        else foreach (var line in errors) Card(line);
        foreach (var note in _report.Lines.Where(l => l.Section == "note" && !OverviewPage._ignoredPackLines.Contains(l.Title))) Card(note);
        int hidden = _report.Lines.Count(l => OverviewPage._ignoredPackLines.Contains(l.Title));
        if (hidden > 0)
        {
            Line(L("Hidden notes: ", "已隐藏的提示：") + hidden, OverviewUi.Muted, 15);
            var show = OverviewUi.Button(_host, L("Show hidden", "显示已隐藏项"), () => { OverviewPage._ignoredPackLines.Clear(); _rebuild(); }, 150, 30, UiActionRole.Neutral);
            OverviewUi.Place((RectTransform)show.transform, 0, _y, 150, 30); _y += 38;
        }
    }

    private void DrawDependencies()
    {
        Line(L("What this pack depends on and its current status.", "此整合包依赖的内容及当前状态。"), OverviewUi.Muted, 16);
        var lines = _report.Lines.Where(l => l.Section is "prereq" or "download").ToList();
        if (lines.Count == 0) Line(L("No dependencies.", "没有依赖项。"), OverviewUi.Muted, 17);
        foreach (var group in lines.GroupBy(l => l.Section == "prereq" ? L("Installed components (checked only, not reinstalled)", "已安装组件（仅检查，不会重复安装）") : L("Included or downloaded dependencies", "随包提供或需下载的依赖")))
        {
            if (!Header(group.Key, group.Count())) continue;
            foreach (var line in group.OrderBy(l => l.Group).ThenBy(l => l.Title, StringComparer.OrdinalIgnoreCase)) Row(line, 1);
        }
    }

    private void DrawMods()
    {
        var mods = _report.Lines.Where(l => l.Section == "mod").ToList();
        if (mods.Count == 0) { Line(L("No mods found in this pack.", "此整合包内没有模组。"), OverviewUi.Muted, 17); return; }
        Line(L("Mods in this pack, grouped by the author's folders.", "此整合包内的模组，按作者的文件夹分组。"), OverviewUi.Muted, 16);
        foreach (var group in mods.GroupBy(m => m.Group.Length > 0 ? m.Group : L("Ungrouped", "未分组")).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!Header(group.Key, group.Count())) continue;
            foreach (var mod in group.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase)) Row(mod, 1);
        }
    }

    private sealed class Node { internal readonly SortedDictionary<string, Node> Dirs = new(StringComparer.OrdinalIgnoreCase); internal readonly List<string> Files = new(); internal int Count; }

    private void DrawFiles()
    {
        Line(L("All files that will be stored, by folder.", "整合包文件清单，按文件夹层级显示。"), OverviewUi.Muted, 16);
        foreach (var group in _report.Files.GroupBy(f => f.Group))
        {
            if (!Header(group.Key, group.Count())) continue;
            var root = new Node();
            foreach (var file in group)
            {
                var parts = file.Path.Split('/'); var node = root; node.Count++;
                for (int i = 0; i < parts.Length - 1; i++) { if (!node.Dirs.TryGetValue(parts[i], out var child)) node.Dirs[parts[i]] = child = new Node(); node = child; node.Count++; }
                node.Files.Add(parts[parts.Length - 1]);
            }
            Tree(root, group.Key, 1, 0, "");
        }
    }

    private void Tree(Node node, string path, int depth, int level, string folder)
    {
        foreach (var pair in node.Dirs)
        {
            string key = path + "/" + pair.Key, here = folder.Length == 0 ? pair.Key : folder + "/" + pair.Key; bool open = !Collapsed.Contains(key) && level < 2;
            _report.FolderVersions.TryGetValue(here, out string? folderVersion);
            if (Collapsed.Contains("+" + key)) open = true;
            Toggle(open, pair.Key + "  (" + pair.Value.Count + ")", depth, () => { if (open) { Collapsed.Add(key); Collapsed.Remove("+" + key); } else { Collapsed.Remove(key); Collapsed.Add("+" + key); } _rebuild(); }, false, folderVersion ?? "");
            if (open) Tree(pair.Value, key, depth + 1, level + 1, here);
        }
        foreach (string file in node.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) FileRow(file, depth, folder.Length == 0 ? file : folder + "/" + file);
    }

    // ---------- rows ----------
    private bool Header(string text, int count)
    {
        string key = "#" + text; bool open = !Collapsed.Contains(key);
        Toggle(open, text + "  (" + count + ")", 0, () => { if (open) Collapsed.Add(key); else Collapsed.Remove(key); _rebuild(); }, true);
        return open;
    }

    private void Toggle(bool open, string label, int depth, Action click, bool strong = false, string version = "")
    {
        float indent = depth * 22, maxWidth = _width - indent, h = strong ? 36 : 30;
        var button = OverviewUi.Button(_host, label, click, maxWidth, h, strong ? UiActionRole.Overview : UiActionRole.Neutral);
        var caption = button.GetComponentInChildren<TextMeshProUGUI>(true); caption.alignment = TextAlignmentOptions.MidlineLeft; caption.margin = new Vector4(36, 0, 8, 0);
        caption.enableAutoSizing = false; caption.fontSize = strong ? 19 : 16; caption.overflowMode = TextOverflowModes.Ellipsis;
        float vw = version.Length > 0 ? Mathf.Min(130, 18 + version.Length * 9) : 0;
        float width = Mathf.Min(maxWidth, caption.GetPreferredValues(label).x + 56 + vw);
        if (vw > 0) caption.margin = new Vector4(36, 0, vw + 12, 0);
        OverviewUi.Place((RectTransform)button.transform, indent, _y, width, h);
        Icon(button.transform, open ? "triangle-down" : "triangle-right", OverviewUi.Ivory, 10, (h - 18) / 2, 18);
        if (version.Length > 0)
        {
            var v = OverviewUi.Text(button.transform, version, 14); v.color = OverviewUi.Muted; v.alignment = TextAlignmentOptions.TopRight; v.textWrappingMode = TextWrappingModes.NoWrap;
            OverviewUi.Place(v.rectTransform, width - vw - 8, (h - 22) / 2, vw, 22);
        }
        _y += h + 3;
    }

    private string PreviewPath(string relative)
    {
        string root = _task.Request.Operation == "view-stored" ? _task.StagingDirectory : System.IO.Path.Combine(_task.StagingDirectory, "content");
        return OverviewProfileAssets.CheckedPath(root, relative);
    }
    private void FileRow(string name, int depth, string relative)
    {
        float indent = depth * 22 + 12; Icon(_host, FileIcon(name), OverviewUi.Muted, indent, _y + 2, 18);
        var t = OverviewUi.Text(_host, name, 15); t.color = OverviewUi.Ivory; OverviewUi.PlaceClipped(t, indent + 26, _y, Mathf.Max(30, _width - indent - 104), 24);
        var folder = OverviewUi.IconButton(_host, "folder", () => PackFileShortcuts.Reveal(PreviewPath(relative)), 26, 24);
        OverviewUi.Place((RectTransform)folder.transform, _width - 66, _y, 26, 24);
        var external = OverviewUi.Button(_host, L("Open", "打开"), () => PackFileShortcuts.Open(PreviewPath(relative), false), 36, 24, UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)external.transform, _width - 38, _y, 36, 24);
        if (_openFile != null) { t.raycastTarget = true; t.gameObject.AddComponent<OverviewHeaderClick>().Clicked = _ => _openFile(relative); }
        _y += 25;
    }

    private void Line(string text, Color color, float size)
    {
        var t = OverviewUi.Text(_host, text, size); t.color = color; t.textWrappingMode = TextWrappingModes.Normal;
        float h = t.GetPreferredValues(text, _width, 0).y + 6; OverviewUi.Place(t.rectTransform, 0, _y, _width, h); _y += h + 6;
    }

    /// <summary>"PEAKLib Core 1.7.2" → name and version; a trailing word that starts with a digit goes to the right column.</summary>
    private static void SplitVersion(string title, out string name, out string version)
    {
        name = title; version = ""; int cut = title.LastIndexOf(' ');
        if (cut > 0 && cut < title.Length - 1 && char.IsDigit(title[cut + 1]) && title.Substring(cut + 1).All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-'))
        { name = title.Substring(0, cut); version = title.Substring(cut + 1); }
    }

    private static string Safe(string text) => text.Replace("<", "＜").Replace(">", "＞");
    private void Row(PackReportLine line, int depth)
    {
        float indent = depth * 22 + 12, width = _width - indent;
        SplitVersion(line.Title, out string name, out string version);
        string original = name;
        if (line.Section == "mod")
        {
            string translated = PackPreviewLanguage.Translate(_task, name);
            if (!string.Equals(translated, original, StringComparison.Ordinal)) name = Safe(translated) + "<color=#8C918E><size=85%>（" + Safe(original) + (version.Length > 0 ? " " + Safe(version) : "") + "）</size></color>";
            else name = Safe(name) + (version.Length > 0 ? "<color=#8C918E><size=85%>（" + Safe(version) + "）</size></color>" : "");
            version = "";
        }
        float versionWidth = version.Length > 0 ? Mathf.Min(150, 18 + version.Length * 9) : 0;
        bool isMod = line.Section == "mod";
        Color iconColor = isMod ? (line.State == "ok" ? Good : OverviewUi.Muted) : StateColor(line.State);
        Icon(_host, isMod ? "mod" : StateIcon(line.State), iconColor, indent, _y + 3, 18);
        var t = OverviewUi.Text(_host, name, 16); t.richText = isMod; t.color = isMod ? (line.State == "ok" ? OverviewUi.Ivory : OverviewUi.Muted) : StateColor(line.State); t.textWrappingMode = TextWrappingModes.Normal;
        float textWidth = width - 28 - versionWidth;
        float h = t.GetPreferredValues(name, textWidth, 0).y + 2; OverviewUi.Place(t.rectTransform, indent + 26, _y, textWidth, h);
        if (version.Length > 0)
        {
            var v = OverviewUi.Text(_host, version, 14); v.color = OverviewUi.Muted; v.alignment = TextAlignmentOptions.TopRight; v.textWrappingMode = TextWrappingModes.NoWrap;
            OverviewUi.Place(v.rectTransform, _width - versionWidth, _y + 2, versionWidth, 22);
        }
        _y += h + 1;
        if (isMod && line.State == "ok") { _y += 1; return; }
        string detail = (line.Section == "download" && line.Group.Length > 0 ? line.Group + "。" : "") + line.Detail;
        if (detail.Length > 0)
        {
            var d = OverviewUi.Text(_host, detail, 14); d.color = OverviewUi.Muted; d.textWrappingMode = TextWrappingModes.Normal;
            float dh = d.GetPreferredValues(detail, width - 26, 0).y + 2; OverviewUi.Place(d.rectTransform, indent + 26, _y, width - 26, dh); _y += dh + 3;
        }
        _y += 3;
    }

    private void Card(PackReportLine line)
    {
        Color color = StateColor(line.State);
        var card = OverviewUi.Panel("Card", _host, ModConfigLook.Surface(new Color(.12f, .10f, .08f, .95f)));
        float inner = _width - 32, y = 10;
        Icon(card, StateIcon(line.State), color, 14, y + 2, 22);
        var title = OverviewUi.Text(card, line.Title, 18); title.color = color; title.textWrappingMode = TextWrappingModes.Normal;
        float th = title.GetPreferredValues(line.Title, inner - 32, 0).y + 4; OverviewUi.Place(title.rectTransform, 46, y, inner - 32, th); y += th + 4;
        void Para(string text, Color paint, float size)
        {
            var t = OverviewUi.Text(card, text, size); t.color = paint; t.textWrappingMode = TextWrappingModes.Normal;
            float h = t.GetPreferredValues(text, inner, 0).y + 4; OverviewUi.Place(t.rectTransform, 14, y, inner, h); y += h + 2;
        }
        if (line.Detail.Length > 0) Para(line.Detail, OverviewUi.Muted, 16);
        if (line.Hint.Length > 0) Para(L("Suggestion: ", "建议：") + line.Hint, OverviewUi.Ivory, 16);
        if (line.Items.Count > 0) Para(string.Join("、", line.Items), OverviewUi.Muted, 15);
        if (line.Ignorable)
        {
            var hide = OverviewUi.Button(card, L("Hide", "隐藏"), () => { OverviewPage._ignoredPackLines.Add(line.Title); _rebuild(); }, 110, 30, UiActionRole.Neutral);
            OverviewUi.Place((RectTransform)hide.transform, 14, y + 4, 110, 30); y += 38;
        }
        y += 10; OverviewUi.Place(card, 0, _y, _width, y); _y += y + 8;
    }
}
