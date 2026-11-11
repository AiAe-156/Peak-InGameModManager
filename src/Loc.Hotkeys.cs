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
    internal static string HotkeyDescriptionKey(string guid, string section, string key) => "ModConfigEnhance.HotkeyDescription." + HotkeyStableId(guid, section, key);
    internal static string GetHotkeyDescription(string guid, string section, string key)
    {
        string id = HotkeyDescriptionKey(guid, section, key);
        return SelectedLanguageGroup?.Entries.TryGetValue(id, out string? value) == true && !string.IsNullOrWhiteSpace(value) && value != id ? value : "";
    }
    internal static bool HasHotkeyDescription(string guid, string section, string key) => GetHotkeyDescription(guid, section, key).Length > 0;
}
