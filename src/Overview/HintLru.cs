#nullable enable
using System;
using System.Collections.Generic;
namespace ModConfigEnhance.Overview;

/// <summary>Bounded, generation-scoped display cache. Never owns game objects or file-policy data.</summary>
internal sealed class HintLru<TKey,TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey,LinkedListNode<(TKey Key,TValue Value)>> _map;
    private readonly LinkedList<(TKey Key,TValue Value)> _order = new();
    internal HintLru(int capacity, IEqualityComparer<TKey>? comparer = null)
    { _capacity=Math.Max(1,capacity);_map=new(comparer); }
    internal int Count => _map.Count;
    internal bool TryGet(TKey key,out TValue value)
    {
        if(!_map.TryGetValue(key,out var node)) { value=default!;return false; }
        _order.Remove(node);_order.AddFirst(node);value=node.Value.Value;return true;
    }
    internal void Set(TKey key,TValue value)
    {
        if(_map.TryGetValue(key,out var old)) { _order.Remove(old);_map.Remove(key); }
        var node=_order.AddFirst((key,value));_map.Add(key,node);
        if(_map.Count<=_capacity)return;
        var last=_order.Last!;_map.Remove(last.Value.Key);_order.RemoveLast();
    }
    internal void Clear() { _map.Clear();_order.Clear(); }
}
