namespace ModConfigEnhance.Overview
{
    internal static class OverviewUpdateQueue
    {
        internal static SnapshotData Snapshot = new();
        internal sealed class SnapshotData { internal NetworkData Network = new(); }
        internal sealed class NetworkData { internal string Mode = "direct", ProxyAddress = ""; }
    }
}
namespace UnityEngine
{
    public class Object { public static void Destroy(Object o) {} public static void DontDestroyOnLoad(Object o) {} }
    public class MonoBehaviour : Object
    {
        public T GetComponent<T>() where T : new() => new();
        public void StartCoroutine(System.Collections.IEnumerator c) {} public void StopAllCoroutines() {}
    }
    public class Texture2D(int w,int h,TextureFormat f,bool m) : Object
    { public int width=>w; public int height=>h; public bool LoadImage(byte[] bytes)=>true; }
    public enum TextureFormat { RGBA32 }
    public struct Color(float r,float g,float b,float a) { public static Color white=>new(1,1,1,1); }
}
namespace UnityEngine.UI
{ public class RawImage { public UnityEngine.Color color; public UnityEngine.Texture2D? texture; } }
