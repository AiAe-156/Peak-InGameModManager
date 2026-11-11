#nullable enable
using System;
namespace ModConfigEnhance.Overview;
internal sealed class UiMenuEntry
{
    internal string Label = "", DisabledReason = "";
    internal bool Header, Enabled = true, Checked;
    internal UiActionRole Role = UiActionRole.Neutral;
    internal Action Invoke = () => { };
    internal Func<bool>? CanInvoke;
    internal bool Execute()
    {
        if (Header || !Enabled || CanInvoke?.Invoke() == false) return false;
        Invoke(); return true;
    }
}
