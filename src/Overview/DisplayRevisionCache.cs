#nullable enable
using System;
namespace ModConfigEnhance.Overview;

// Display only: safety-critical operations must read and validate live state.
internal sealed class DisplayRevisionCache<T> where T : class
{
    internal T? Value { get; private set; }
    private int _revision = -1;
    private double _next;
    internal void Refresh(int revision, double now, bool reliable, Func<T> read, bool force = false)
    {
        if (!force && Value != null && revision == _revision && (reliable || now < _next)) return;
        // Publish only after a successful read. Failed reads remain retryable.
        var value = read();
        Value = value; _revision = revision; _next = now + 2;
    }
}
