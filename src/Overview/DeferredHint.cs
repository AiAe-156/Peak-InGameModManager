#nullable enable
using System;
namespace ModConfigEnhance.Overview;
// The identity, not generated text, owns the dwell deadline.
internal sealed class DeferredHint
{
    private object? _target;
    private Func<string>? _factory;
    private float _at;
    private bool _shown;
    internal void Enter(object target, Func<string> factory, float now)
    {
        if(!ReferenceEquals(_target,target)) { _target=target;_at=now+HoverDwell.Delay;_shown=false; }
        _factory=factory;
    }
    internal string? Take(float now, Func<object,bool>? valid=null)
    {
        if(_target!=null && valid!=null && !valid(_target)) { Cancel();return null; }
        if(_factory==null || _shown || now<_at)return null;
        _shown=true;return _factory();
    }
    internal string Focus(object target,Func<string> factory,float now)
    { Enter(target,factory,now);_shown=true;return factory(); }
    internal string? Refresh(object target,Func<string> factory)
    { if(!ReferenceEquals(_target,target))return null;_factory=factory;return _shown?factory():null; }
    internal void Cancel() { _target=null;_factory=null;_shown=false; }
}
