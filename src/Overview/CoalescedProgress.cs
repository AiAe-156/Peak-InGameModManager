#nullable enable
namespace ModConfigEnhance.Overview;
/// <summary>Latest display-only progress; business callbacks are never queued here.</summary>
internal sealed class CoalescedProgress
{
    private readonly object _gate=new();
    private string? _pending;
    private string _last="";
    private float _next;
    internal void Publish(string value) { lock(_gate) { if(value!=_last)_pending=value; } }
    internal string? Take(float now)
    {
        lock(_gate)
        {
            if(_pending==null || now<_next)return null;
            var value=_pending;_pending=null;_last=value;_next=now+.15f;return value;
        }
    }
    internal void Clear() { lock(_gate) { _pending=null;_last="";_next=0; } }
}
