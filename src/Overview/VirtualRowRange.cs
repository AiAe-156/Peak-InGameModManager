#nullable enable
using System;
namespace ModConfigEnhance.Overview;
/// <summary>Rendering range only: selection, ordering and business scope retain the complete model.</summary>
internal static class VirtualRowRange
{
    internal static int Next(int index,int step,int count)
    { int next=index+step;return next>=0 && next<count?next:-1; }
    internal static bool Contains(float rowTop,float rowHeight,float scrollTop,float viewportHeight,float buffer)
        => rowTop+rowHeight>=Math.Max(0,scrollTop)-buffer && rowTop<=Math.Max(0,scrollTop)+viewportHeight+buffer;
    internal static float ScrollTo(float rowTop,float rowHeight,float scrollTop,float viewportHeight)
        => rowTop<scrollTop ? rowTop : rowTop+rowHeight>scrollTop+viewportHeight ? Math.Max(0,rowTop+rowHeight-viewportHeight) : scrollTop;
}
/// <summary>A callback may use its identity only until its lease is released, even if Unity defers Destroy.</summary>
internal sealed class RowIdentityLease
{
    internal string? Identity {get;private set;}
    private int _generation;
    internal void Bind(string identity) { _generation++;Identity=identity; }
    internal void Release() { _generation++;Identity=null; }
    internal Action Guard(Action action)
    { int generation=_generation;return ()=> {if(Identity!=null && generation==_generation)action();}; }
}
