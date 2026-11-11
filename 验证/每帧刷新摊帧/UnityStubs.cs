// Minimal Unity surface for the maintenance-sweep checks. Only what OverviewMaintenance.cs touches.
namespace UnityEngine
{
    public class Object { public static void DontDestroyOnLoad(Object target) { } }
    public class Component : Object { }
    public class MonoBehaviour : Component { public bool isActiveAndEnabled = true; }
    public class GameObject : Object
    {
        public GameObject(string name) { }
        public T AddComponent<T>() where T : Component, new() => new T();
    }
    public static class Time { public static int frameCount; public static float unscaledTime; }
}

namespace ModConfigEnhance
{
    internal static class TimeThemeBridge { internal static bool IsDark => false; }
    internal static class Loc { internal static int TranslationRevision => 0; }
}
