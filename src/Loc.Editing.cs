#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ModConfigEnhance;

internal static partial class Loc
{
    internal static int TranslationRevision { get; private set; }
    private static LanguageFile? _editingGroup;
    private static bool _editingGroupRead;
    private static string _editingGroupError = "";
    internal static void InvalidateEditingTranslations()
    {
        _editingGroupRead = false; _editingGroup = null; _editingGroupError = ""; TranslationRevision++;
    }
    internal static LanguageFile? SelectedLanguageGroup
    {
        get
        {
            if (!_editingGroupRead)
            {
                try { _editingGroup = CurrentGroup(); }
                catch (Exception ex) { _editingGroupError = ex.Message; Plugin.Log.LogWarning("[语言] 读取当前译文失败：" + ex.Message); }
                _editingGroupRead = true;
            }
            return _editingGroup;
        }
    }
    /// <summary>
    /// 2.5.0：「显示原文」开关。开着时本模组负责显示的模组文字（侧栏、概览、说明面板、设置页）一律显示原文，
    /// 本模组自己的界面词条不受影响。只在本次运行有效，切换时版本号加一让各处重画。
    /// </summary>
    internal static bool ShowingOriginal { get; private set; }
    internal static void ToggleOriginal()
    {
        ShowingOriginal = !ShowingOriginal; TranslationRevision++;
        ModConfigLocalizationPatch.RefreshStaticTexts();
    }

    /// <summary>模组文字的唯一翻译入口：整句精确（含本模组内置的 cfg 词条）→ 正则规则 → 原文。2.5.0 起不再问 XUnity。</summary>
    internal static string TranslateExternal(string original)
    {
        if (string.IsNullOrEmpty(original)) return "";
        if (ShowingOriginal) return original;
        string lookup = original.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        LanguageFile? group = SelectedLanguageGroup;
        string canonical = EnglishDisplay(lookup);
        if (group?.Entries.TryGetValue(canonical, out string? translated) == true && !string.IsNullOrWhiteSpace(translated)) return translated;
        if (TryConfigText(original, out string configText)) return configText;
        if (group?.Entries.TryGetValue(lookup, out translated) == true && !string.IsNullOrWhiteSpace(translated)) return translated;
        return TryRegex(lookup, out string regexTranslated) ? regexTranslated : original;
    }

    /// <summary>补漏翻用：这句原文在当前语言组里有没有译文（整句或正则）。</summary>
    internal static bool HasTranslation(string original)
    {
        string lookup = original.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        LanguageFile? group = SelectedLanguageGroup;
        if (group == null) return false;
        if (group.Entries.TryGetValue(lookup, out string? value) && IsEffectiveTranslation(lookup, value, group.Code)) return true;
        if (group.Entries.TryGetValue(EnglishDisplay(lookup), out value) && IsEffectiveTranslation(lookup, value, group.Code)) return true;
        return group.Regexes.Count > 0 && TryRegex(group.Regexes, lookup, out _);
    }
    private sealed class EditingFile
    {
        internal string Path = "";
        internal byte[] Original = Array.Empty<byte>();
        internal bool Bom;
        internal string Content = "";
        internal List<Match> Lines = new();
        internal readonly Dictionary<int, string> Replacements = new();
        internal readonly List<(string Key, string Value)> Append = new();
        internal string NewContent()
        {
            var text = new StringBuilder();
            for (int i = 0; i < Lines.Count; i++)
            {
                Match line = Lines[i];
                if (Replacements.TryGetValue(i, out string? rhs))
                {
                    string raw = line.Groups[1].Value; int eq = FindUnescapedEquals(raw);
                    text.Append(raw.Substring(0, eq + 1)).Append(Escape(rhs)).Append(line.Groups[2].Value);
                }
                else text.Append(line.Value);
            }
            string newline = Content.Contains("\r\n") ? "\r\n" : Content.Contains("\n") ? "\n" : "\r\n";
            foreach (var entry in Append)
            {
                if (text.Length > 0 && text[text.Length - 1] != '\n' && text[text.Length - 1] != '\r') text.Append(newline);
                text.Append(Escape(entry.Key)).Append('=').Append(Escape(entry.Value)).Append(newline);
            }
            return text.ToString();
        }
    }
    private static EditingFile ReadEditingFile(string path)
    {
        string root = System.IO.Path.GetFullPath(LanguageDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!System.IO.Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(Loc.T("Language files must stay within the language directory and cannot be links.", "语言文件路径不能越出语言目录或使用文件链接", "語言文件路徑不能越出語言目錄或使用文件鏈接"));
        byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
        bool bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191;
        string content = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        return new EditingFile { Path = path, Original = bytes, Bom = bom, Content = content,
            Lines = Regex.Matches(content, @"([^\r\n]*)(\r\n|\n|\r|$)").Cast<Match>().Where(m => m.Length > 0).ToList() };
    }
    internal static bool WriteExternalTranslations(IReadOnlyList<string> keys, string translation, out string error)
    {
        error = "";
        try
        {
            LanguageFile? group = SelectedLanguageGroup;
            if (_editingGroupError.Length > 0) throw new InvalidDataException(_editingGroupError);
            string defaultPath = System.IO.Path.Combine(LanguageDirectory, FollowStem() + ".txt");
            if (group == null && !string.IsNullOrWhiteSpace(Plugin.LanguageFile.Value))
            {
                string stem = NormalizeSelection(Plugin.LanguageFile.Value);
                if (!Regex.IsMatch(stem, @"^[A-Za-z0-9_-]+$")) throw new InvalidDataException(Loc.T("Invalid language filename", "语言文件名无效", "語言文件名無效"));
                defaultPath = System.IO.Path.Combine(LanguageDirectory, stem + ".txt");
            }
            string stemForEdit = group?.Key ?? Path.GetFileNameWithoutExtension(defaultPath);
            // 2.5.0：一种语言一个文件，概览里改的译名直接写进 language\<组名>.txt（作者语言文件在整合包目录，不受影响）
            defaultPath = Path.Combine(LanguageDirectory, stemForEdit + ".txt");
            string[] paths = group != null ? group.Paths.Where(IsPersonalLanguage).Concat(new[] { defaultPath }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : new[] { defaultPath };
            var files = paths.Select(ReadEditingFile).ToList();
            bool reset = string.IsNullOrWhiteSpace(translation);
            foreach (string key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal))
            {
                string lookup = key.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                EditingFile? winnerFile = null; int winnerLine = -1; string? winnerValue = null; bool foundAny = false;
                foreach (EditingFile file in files)
                {
                    int fileWinner = -1; string? fileValue = null;
                    for (int lineIndex = 0; lineIndex < file.Lines.Count; lineIndex++)
                    {
                        string raw = file.Lines[lineIndex].Groups[1].Value;
                        if (raw.StartsWith("//", StringComparison.Ordinal) || raw.StartsWith("#", StringComparison.Ordinal)) continue;
                        int eq = FindUnescapedEquals(raw); if (eq <= 0 || Unescape(raw.Substring(0, eq)).Trim() != lookup) continue;
                        foundAny = true;
                        string value = Unescape(raw.Substring(eq + 1));
                        if (reset) file.Replacements[lineIndex] = "";
                        if (string.IsNullOrWhiteSpace(value)) continue;
                        bool incoming = IsEffectiveTranslation(lookup, value, group?.Code);
                        if (fileValue != null && !incoming && IsEffectiveTranslation(lookup, fileValue, group?.Code) && value != fileValue) continue;
                        fileValue = value; fileWinner = lineIndex;
                    }
                    if (fileWinner < 0) continue;
                    if (winnerValue != null && !IsEffectiveTranslation(lookup, fileValue!, group?.Code) && IsEffectiveTranslation(lookup, winnerValue, group?.Code) && winnerValue != fileValue) continue;
                    winnerFile = file; winnerLine = fileWinner; winnerValue = fileValue;
                }
                if (!reset && winnerFile != null) winnerFile.Replacements[winnerLine] = translation;
                else if (reset && !foundAny) { }
                else if (!reset && winnerFile == null) files[files.Count - 1].Append.Add((key, translation));
            }
            var changed = files.Where(f => f.Replacements.Count > 0 || f.Append.Count > 0).ToList();
            // Verify every original before touching any file; never overwrite an external edit.
            foreach (var file in changed)
                if (File.Exists(file.Path) && !File.ReadAllBytes(file.Path).SequenceEqual(file.Original)) throw new IOException(Loc.T("The language file has changed. Refresh and try again.", "语言文件已变化，请刷新后重试", "語言文件已變化，請刷新后重試"));
            var written = new List<EditingFile>();
            try
            {
                foreach (var file in changed)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.Path)!);
                    string backup = file.Path + ".mce-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "-" + Guid.NewGuid().ToString("N") + ".bak";
                    if (File.Exists(file.Path)) File.Copy(file.Path, backup, false);
                    string staged = file.Path + ".mce-" + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(staged, file.NewContent(), new UTF8Encoding(file.Bom));
                    if (File.Exists(file.Path)) File.Replace(staged, file.Path, null); else File.Move(staged, file.Path);
                    written.Add(file);
                }
            }
            catch
            {
                foreach (var file in written)
                {
                    string staged = file.Path + ".rollback.tmp"; File.WriteAllBytes(staged, file.Original); File.Replace(staged, file.Path, null);
                }
                throw;
            }
            InvalidateEditingTranslations();
            ApplyResult applied = Apply(Plugin.LanguageFile.Value ?? "", notify: false, refresh: true);
            if (!applied.Success) { error = applied.Message; return false; }
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}


