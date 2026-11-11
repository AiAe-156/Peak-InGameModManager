#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;
internal sealed class ChangeRecord
{
    public string before = "", after = "", kind = "";
}
internal sealed class ChangeJournal
{
    public string generation = "";
    public Dictionary<string, Dictionary<string, string>> baselines = new(StringComparer.Ordinal);
    public Dictionary<string, ChangeRecord> unread = new(StringComparer.Ordinal);
    internal void Observe(string scope, Dictionary<string, string> current)
    {
        if (baselines.TryGetValue(scope, out var previous))
        foreach (string key in previous.Keys.Union(current.Keys, StringComparer.Ordinal))
        {
            previous.TryGetValue(key, out var before); current.TryGetValue(key, out var after);
            if (before == after) continue;
            string id = scope + "|" + key;
            string original = unread.TryGetValue(id, out var old) ? old.before : before ?? "";
            if (original == (after ?? "")) unread.Remove(id);
            else unread[id] = new ChangeRecord { before = original, after = after ?? "", kind = before == null ? "added" : after == null ? "removed" : "changed" };
        }
        baselines[scope] = new Dictionary<string, string>(current, StringComparer.Ordinal);
    }
    internal void Read(string key) { foreach (string item in unread.Keys.Where(k => k.EndsWith("|" + key, StringComparison.Ordinal)).ToArray()) unread.Remove(item); }
}
