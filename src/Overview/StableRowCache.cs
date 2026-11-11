#nullable enable
using System;
using System.Collections.Generic;
namespace ModConfigEnhance.Overview;
/// <summary>Idle full views only. Active/pinned views are outside the budget; identities are never retargeted.</summary>
internal sealed class StableRowCache<T> where T : class
{
    private readonly int _capacity;
    private readonly LinkedList<T> _idle = new();
    internal StableRowCache(int capacity) { if(capacity<0)throw new ArgumentOutOfRangeException(nameof(capacity));_capacity=capacity; }
    internal int Count => _idle.Count;
    internal void Remove(T value) { _idle.Remove(value); }
    // Called only on transition to idle. Wake removes the entry, so this is least-recently-used order.
    internal T? Park(T value)
    {
        _idle.Remove(value);_idle.AddLast(value);
        if(_idle.Count<=_capacity)return null;
        var evicted=_idle.First!.Value;_idle.RemoveFirst();return evicted;
    }
}
