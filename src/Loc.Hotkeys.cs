#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace ModConfigEnhance;

internal static partial class Loc
{
    internal static string HotkeyStableId(string guid, string section, string key)
    {
        // Length-prefixed fields cannot collide when config names contain separators.
        string input = guid.Length + ":" + guid + section.Length + ":" + section + key.Length + ":" + key;
        using var digest = SHA256.Create();
        var result = new StringBuilder(); foreach (byte b in digest.ComputeHash(Encoding.UTF8.GetBytes(input))) result.Append(b.ToString("x2"));
        return result.ToString();
    }
    /// <summary>写入与导出用新前缀 <c>IGMM.HotkeyDescription.</c>；读取时旧前缀 <c>ModConfigEnhance.HotkeyDescription.</c>（旧语言文件、旧作者词典）同样认。</summary>
    internal static string HotkeyDescriptionKey(string guid, string section, string key) => Updating.ManagerIdentity.HotkeyPrefix + HotkeyStableId(guid, section, key);
    internal static string GetHotkeyDescription(string guid, string section, string key)
    {
        var entries = SelectedLanguageGroup?.Entries;
        if (entries == null) return "";
        string stable = HotkeyStableId(guid, section, key);
        foreach (string id in new[] { Updating.ManagerIdentity.HotkeyPrefix + stable, Updating.ManagerIdentity.LegacyHotkeyPrefix + stable })
            if (entries.TryGetValue(id, out string? value) && !string.IsNullOrWhiteSpace(value) && value != id) return value;
        return "";
    }
    internal static bool HasHotkeyDescription(string guid, string section, string key) => GetHotkeyDescription(guid, section, key).Length > 0;
}
