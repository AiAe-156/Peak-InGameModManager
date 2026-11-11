#nullable enable
using System;
using System.Collections.Generic;
namespace ModConfigEnhance.Overview;

// Used only on the Unity thread. Leased objects cannot be evicted or destroyed.
internal sealed class ShopLeaseCache<T> where T : class
{
    internal sealed class Entry
    {
        internal readonly T Value;
        internal readonly long Bytes;
        internal int References;
        internal long Used;
        internal Entry(T value, long bytes) { Value = value; Bytes = bytes; }
    }
    private readonly Dictionary<string, Entry> _items = new(StringComparer.Ordinal);
    private readonly int _count;
    private readonly long _capacity;
    private readonly Action<T> _destroy;
    private long _bytes, _clock;
    internal ShopLeaseCache(int count, long capacity, Action<T> destroy) { _count = count; _capacity = capacity; _destroy = destroy; }
    internal Entry? Acquire(string key)
    {
        if (!_items.TryGetValue(key, out var entry)) return null;
        entry.References++; entry.Used = ++_clock; return entry;
    }
    internal Entry? Add(string key, T value, long bytes)
    {
        var existing = Acquire(key);
        if (existing != null) { _destroy(value); return existing; }
        if (bytes > _capacity) { _destroy(value); return null; }
        while (_items.Count >= _count || _bytes + bytes > _capacity)
        {
            string? oldest = null; long used = long.MaxValue;
            foreach (var pair in _items)
                if (pair.Value.References == 0 && pair.Value.Used < used) { oldest = pair.Key; used = pair.Value.Used; }
            if (oldest == null) { _destroy(value); return null; }
            var removed = _items[oldest]; _items.Remove(oldest); _bytes -= removed.Bytes; _destroy(removed.Value);
        }
        var entry = new Entry(value, bytes) { References = 1, Used = ++_clock };
        _items.Add(key, entry); _bytes += bytes; return entry;
    }
    internal void Release(Entry? entry)
    {
        if (entry == null) return;
        if (entry.References <= 0) throw new InvalidOperationException("Unbalanced texture lease.");
        entry.References--; entry.Used = ++_clock;
    }
}
