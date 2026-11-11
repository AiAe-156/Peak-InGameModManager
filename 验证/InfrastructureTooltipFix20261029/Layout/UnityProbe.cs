using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
class UnityProbe
{
    static int Main()
    {
        try { Construct(); Console.WriteLine("Unity component construction available."); return 0; }
        catch (Exception ex) { Console.WriteLine("UNAVAILABLE: standalone process cannot construct actual Unity/TMP components: " + ex); return 2; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Construct()
    {
        var root = new GameObject("OfflineDependencyCardProbe", typeof(RectTransform), typeof(Canvas));
        root.AddComponent<RectMask2D>(); root.AddComponent<ScrollRect>(); root.AddComponent<TextMeshProUGUI>();
        Console.WriteLine(((RectTransform)root.transform).rect);
    }
}
