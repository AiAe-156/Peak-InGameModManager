using System;
namespace ModConfigEnhance.Overview;

/// <summary>Top-left window coordinates; independent of Canvas scale and Unity runtime.</summary>
internal static class UiGeometry
{
    internal readonly struct Box
    {
        internal readonly float X, Y, Width, Height;
        internal Box(float x, float y, float width, float height) { X=x; Y=y; Width=width; Height=height; }
    }
    private static float Clamp(float value,float min,float max) => Math.Max(min,Math.Min(max,value));
    internal static Box Popover(float hostWidth,float hostHeight,Box anchor,float wantedWidth,float wantedHeight)
    {
        float pad=Math.Min(8,Math.Max(0,Math.Min(hostWidth,hostHeight)/4)), gap=4;
        float width=Clamp(wantedWidth,0,Math.Max(0,hostWidth-2*pad));
        float below=Math.Max(0,hostHeight-pad-anchor.Y-anchor.Height-gap), above=Math.Max(0,anchor.Y-pad-gap);
        bool flip=wantedHeight>below && above>below;
        float height=Math.Min(Math.Max(0,wantedHeight),Math.Min(Math.Max(0,hostHeight-2*pad),Math.Max(0,flip?above:below)));
        float x=Clamp(anchor.X,pad,Math.Max(pad,hostWidth-width-pad));
        float y=flip?anchor.Y-gap-height:anchor.Y+anchor.Height+gap;
        return new Box(x,Clamp(y,pad,Math.Max(pad,hostHeight-height-pad)),width,height);
    }
    // TMP's public caretPosition already includes the IME composition length.
    // Return both the mesh character and the edge used by the actual renderer.
    internal static (int Index, bool Advance) CaretCharacter(int caretPosition, int characterCount)
        => (Math.Max(0, Math.Min(caretPosition, Math.Max(0, characterCount - 1))), caretPosition >= characterCount);
    // Glyph metrics, not a guessed pixel offset: works for fallback fonts and point-size changes.
    internal static (float Top,float Height) EmptyCaret(float viewportCenter,float ascender,float descender)
    {
        float height=Math.Max(1,ascender-descender);
        return (viewportCenter+height/2,height);
    }
}
