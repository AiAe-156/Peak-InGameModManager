namespace UnityEngine {
    public class GameObject {
        private readonly Dictionary<Type,object> _components = new();
        public T? GetComponent<T>() where T:class => _components.TryGetValue(typeof(T),out var c) ? (T)c : null;
        public void Add<T>(T component) where T:class => _components[typeof(T)] = component;
    }
    public class MonoBehaviour {
        public GameObject gameObject = new(); public bool isActiveAndEnabled = true;
        public T? GetComponent<T>() where T:class => gameObject.GetComponent<T>();
    }
    public struct Rect { public float width,height; }
    public class RectTransform { public Rect rect; }
    public static class Time { public static int frameCount = 1; public static float unscaledDeltaTime = 1f/60; }
    public static class Mathf {
        public static float Clamp01(float n) => Math.Clamp(n,0,1);
        public static float Abs(float n) => Math.Abs(n);
    }
}
namespace UnityEngine.EventSystems {
    public enum MoveDirection { Left,Right,Up,Down,None }
    public class BaseEventData { public bool used; public void Use() => used=true; }
    public class AxisEventData:BaseEventData { public MoveDirection moveDir; }
    public interface ISubmitHandler { void OnSubmit(BaseEventData data); }
    public interface ICancelHandler { void OnCancel(BaseEventData data); }
    public class EventSystem { public static EventSystem? current; public UnityEngine.GameObject? currentSelectedGameObject; }
}
namespace UnityEngine.UI {
    public class Selectable:UnityEngine.MonoBehaviour {
        public bool Interactable = true; public int NavigationMoves;
        public bool IsActive() => isActiveAndEnabled;
        public bool IsInteractable() => Interactable;
        public virtual void OnEnable() { }
        public virtual void OnDisable() { }
        public virtual void OnDeselect(UnityEngine.EventSystems.BaseEventData data) { }
        public virtual void OnMove(UnityEngine.EventSystems.AxisEventData data) => NavigationMoves++;
    }
    public class ScrollRect {
        public UnityEngine.RectTransform? content,viewport;
        public bool vertical=true,horizontal;
        public float verticalNormalizedPosition=1,horizontalNormalizedPosition;
        public void StopMovement() { }
    }
}
namespace UnityEngine.InputSystem {
    public class Button { public bool isPressed,wasPressedThisFrame; }
    public class Dpad { public Button up=new(),down=new(),left=new(),right=new(); }
    public class Gamepad { public static Gamepad? current; public Dpad dpad=new(); public Button buttonEast=new(); }
    public class Keyboard { public static Keyboard? current; public Button escapeKey=new(); }
}
namespace ModConfigEnhance {
    internal static class GamepadSupport {
        internal static bool IsGamepad=true; internal static int SwallowBackUntil=-1;
        internal sealed class FocusHighlight { internal bool Engaged; internal void SetEngaged(bool engaged) => Engaged=engaged; }
    }
    internal static class BackGuard { internal static int Resets; internal static void Reset() => Resets++; }
    internal static class GamepadIntent {
        internal static float ScrollY,ScrollX; internal static bool RightStickHeld = false;
        internal static float ScrollSpeed(float speed) => speed;
        internal static void HoldNavigation(object owner,bool held) { }
        internal static void Release(object owner) { }
    }
}
