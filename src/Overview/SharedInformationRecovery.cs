#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Rebase recovered shared information onto the latest saved workspace.</summary>
internal static class SharedInformationRecovery
{
    internal static OverviewState Merge(OverviewState baseline, OverviewState draft, OverviewState current, out bool conflict)
    {
        bool collided = false;
        JToken? MergeValue(JToken? before, JToken? edited, JToken? saved)
        {
            if (JToken.DeepEquals(before, edited)) return saved?.DeepClone();
            if (JToken.DeepEquals(before, saved) || JToken.DeepEquals(edited, saved)) return edited?.DeepClone();
            if (before is JObject b && edited is JObject e && saved is JObject s)
            {
                var merged = new JObject();
                foreach (string key in b.Properties().Select(p => p.Name).Union(e.Properties().Select(p => p.Name)).Union(s.Properties().Select(p => p.Name)))
                {
                    var value = MergeValue(b[key], e[key], s[key]);
                    if (value != null) merged[key] = value;
                }
                return merged;
            }
            if (before is JArray ba && edited is JArray ea && saved is JArray sa && ba.Concat(ea).Concat(sa).All(v => v is JObject && v["id"]?.Type == JTokenType.String)
                && new[] { ba, ea, sa }.All(a => a.Select(v => v.Value<string>("id")).Distinct(StringComparer.OrdinalIgnoreCase).Count() == a.Count))
            {
                var merged = new JArray();
                var ids = sa.Concat(ea).Concat(ba).Select(v => v.Value<string>("id")!).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (string id in ids)
                {
                    JToken? Find(JArray a) => a.FirstOrDefault(v => string.Equals(v.Value<string>("id"), id, StringComparison.OrdinalIgnoreCase));
                    var value = MergeValue(Find(ba), Find(ea), Find(sa));
                    if (value != null) merged.Add(value);
                }
                return merged;
            }
            collided = true;
            return edited?.DeepClone(); // Keep the unsaved edit and mark it for review.
        }
        var result = JObject.FromObject(draft);
        var original = JObject.FromObject(baseline);
        var latest = JObject.FromObject(current);
        foreach (string key in new[] { "pack", "groups", "mods" })
            result[key] = MergeValue(original[key], result[key], latest[key]);
        conflict = collided;
        return result.ToObject<OverviewState>()!;
    }
}
