#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

namespace ModConfigEnhance;

internal static partial class Loc
{
    private static Dictionary<string, string> _inlineSnapshot = new(StringComparer.Ordinal);
    // Update checks also translate messages on worker threads while UI labels bind here.
    internal static readonly ConcurrentDictionary<string, string> KnownInline = new(StringComparer.Ordinal);
    private static readonly Lazy<HashSet<string>> InlineKeyCache = new(() => new HashSet<string>(AllInlineTerms.Select(t => t.English), StringComparer.Ordinal));
    private static HashSet<string> InlineKeys => InlineKeyCache.Value;
    internal static bool IsPersonalLanguage(string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(LanguageDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static int CompareLayeredFiles(string left, string right)
    {
        int layers = IsPersonalLanguage(left).CompareTo(IsPersonalLanguage(right));
        if (layers != 0) return layers;
        int personal = left.EndsWith("_personal.txt", StringComparison.OrdinalIgnoreCase).CompareTo(right.EndsWith("_personal.txt", StringComparison.OrdinalIgnoreCase));
        return personal != 0 ? personal : CompareGroupFiles(left, right);
    }
    private static string PickTerm(Term term, string code) => code == "zh-cn" ? term.SimplifiedChinese : code == "zh-tw" ? term.TraditionalChinese : term.English;
    internal static void ExportLanguages(string destination, IEnumerable<string>? selected = null)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in SnapshotLanguages(selected)) Overview.OverviewRepository.AtomicWrite(Path.Combine(destination, file.Key), file.Value);
    }
    internal static Dictionary<string, string> SnapshotLanguages(IEnumerable<string>? selected = null)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var builtin = new HashSet<string>(Terms.Select(t => t.Key), StringComparer.Ordinal);
        foreach (LanguageFile group in Scan().Where(g => selected == null || selected.Contains(g.Key, StringComparer.OrdinalIgnoreCase)))
        {
            LanguageFile merged = ParseGroup(group.Key);
            var text = new StringBuilder("// display: " + group.DisplayOrStem + "\n// mce-snapshot: effective-v1\n");
            foreach (var entry in merged.Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                text.Append(builtin.Contains(entry.Key) ? KeyPrefix : "").Append(Escape(entry.Key)).Append('=').Append(Escape(entry.Value)).Append('\n');
            files[group.Key + ".txt"] = text.ToString();
        }
        return files;
    }
    internal static void BindInline(TextMeshProUGUI text)
    {
        if (KnownInline.TryGetValue(text.text, out string? key))
        {
            var binding = text.gameObject.GetComponent<InlineLanguageLabel>() ?? text.gameObject.AddComponent<InlineLanguageLabel>();
            binding.Target = text; binding.Key = key; binding.Last = text.text;
        }
    }
    internal static string Inline(string english)
    {
        if (_inlineSnapshot.TryGetValue(english.Trim(), out string? value)) return value;
        foreach (Term term in AllInlineTerms) if (term.English == english) return PickTerm(term, ActiveLanguageCode);
        return english;
    }
}

internal sealed class InlineLanguageLabel : MonoBehaviour
{
    internal TextMeshProUGUI? Target;
    internal string Key = "", Last = "";
    private int _revision = -1;
    private void Update()
    {
        if (Target == null || _revision == Loc.TranslationRevision) return;
        _revision = Loc.TranslationRevision;
        if (Target.text != Last) { Key = ""; return; }
        if (Key.Length > 0) Target.text = Last = Loc.Inline(Key);
    }
}
