#nullable enable
namespace ModConfigEnhance.Overview;
/// <summary>Shared 200ms pointer dwell; same-target refreshes never restart the deadline.</summary>
internal sealed class HoverDwell
{
    internal const float Delay = .200f;
    private string? _pending;
    private float _at;
    internal void Enter(string value,float now)
    {
        if(_pending==value)return;
        _pending=value;_at=now+Delay;
    }
    internal void Cancel()=>_pending=null;
    internal string? Take(float now)
    { if(_pending==null || now<_at)return null;var result=_pending;_pending=null;return result; }
}
