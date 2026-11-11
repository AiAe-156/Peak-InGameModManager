using System;
namespace ModConfigEnhance.Overview;
internal readonly struct TagFilterColumns
{
    internal readonly float NameWidth, CountX, IncludeX, ExcludeX;
    internal TagFilterColumns(float width)
    { CountX = Math.Max(0, width - 160); IncludeX = Math.Max(0, width - 112); ExcludeX = Math.Max(0, width - 56); NameWidth = Math.Max(0, CountX - 20); }
}
