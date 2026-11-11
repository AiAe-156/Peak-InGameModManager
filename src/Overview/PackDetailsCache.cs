#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ModConfigEnhance.Overview;

/// <summary>One background job per revision. Results are pulled by the UI, never pushed into a selected view.</summary>
internal sealed class PackDetailsCache<T>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<T>> _jobs = new(StringComparer.Ordinal);
    internal Task<T> Get(string key, Func<T> load)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(key, out var job)) return job;
            job = Task.Run(load); _jobs.Add(key, job); return job;
        }
    }
    internal void Invalidate(string key) { lock (_gate) _jobs.Remove(key); }
}
