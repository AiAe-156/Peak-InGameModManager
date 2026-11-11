#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ModConfigEnhance.Updating;

/// <summary>
/// The language-file naming standard, shared by the game and the worker: "code[-variant][_note].txt".
/// Files with the same part before "_" form one group (for example zh-cn.txt + zh-cn_补翻.txt). The first lines may carry "// display: name".
/// </summary>
public static class PackLanguages
{
    public static string GroupStem(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName); int cut = stem.IndexOf('_');
        return cut < 0 ? stem : stem[..cut];
    }

    /// <summary>"zh-cn-old" has code zh-cn; "zh" and "en" are completed to zh-cn and en-us.</summary>
    public static string Code(string groupStem)
    {
        string stem = groupStem.Trim().Replace('_', '-').ToLowerInvariant(); var parts = stem.Split('-');
        string code = parts.Length >= 2 && parts[1].Length == 2 ? parts[0] + "-" + parts[1] : parts[0];
        return code == "zh" ? "zh-cn" : code == "en" ? "en-us" : code;
    }

    /// <summary>"zh-cn-old" → "zh-cn-old"; "zh" → "zh-cn". The variant suffix after the code is kept.</summary>
    static string Key(string stem, string code)
    {
        string lower = stem.Trim().Replace('_', '-').ToLowerInvariant(); var parts = lower.Split('-');
        int used = parts.Length >= 2 && parts[1].Length == 2 ? 2 : 1;
        string rest = string.Join("-", parts.Skip(used));
        return rest.Length == 0 ? code : code + "-" + rest;
    }

    public static string? DisplayName(string file)
    {
        try
        {
            using var reader = new StreamReader(file, Encoding.UTF8);
            for (int i = 0; i < 5; i++)
            {
                string? line = reader.ReadLine(); if (line == null) break;
                if (line.StartsWith("// display:", StringComparison.OrdinalIgnoreCase)) { string value = line["// display:".Length..].Trim(); return value.Length > 0 ? value : null; }
            }
        }
        catch (IOException) { }
        return null;
    }

    /// <summary>Groups the "*.txt" files of a language folder. Key is the group name in lower case.</summary>
    public static List<PackLanguage> Scan(string directory)
    {
        var groups = new Dictionary<string, PackLanguage>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory)) return new();
        foreach (string file in Directory.GetFiles(directory, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file); if (name.StartsWith("_", StringComparison.Ordinal)) continue;
            string stem = GroupStem(name), code = Code(stem), key = Key(stem, code);
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = new PackLanguage { Key = key, Code = code };
            group.Files.Add(name); if (group.Name.Length == 0) group.Name = DisplayName(file) ?? "";
        }
        return groups.Values.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Picks the group for the game language: the pure code group first, then any variant of the same code, then en-us, then the first one.</summary>
    public static PackLanguage? Choose(IReadOnlyList<PackLanguage> groups, string gameCode)
        => groups.FirstOrDefault(g => g.Key == gameCode) ?? groups.FirstOrDefault(g => g.Code == gameCode)
        ?? groups.FirstOrDefault(g => g.Key == "en-us") ?? groups.FirstOrDefault();
}

public sealed class PackLanguage
{
    public string Key { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Files { get; set; } = new();
}
