// Offline validation only. These stand-ins are outside Unity Assets and are not engine implementations.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static void DestroyImmediate(Object value) { if (value is GameObject go) GameObject.Created.Remove(go); }
        public static void DontDestroyOnLoad(Object value) { }
    }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => (T)Activator.CreateInstance(typeof(T));
    }
    public class Component : Object
    {
        internal GameObject Owner;
        public GameObject gameObject => Owner;
        public Transform transform => Owner.transform;
        public T GetComponent<T>() where T : Component => Owner.GetComponent<T>();
        public T GetComponentInChildren<T>() where T : Component => Owner.GetComponentInChildren<T>();
    }
    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        public static readonly List<GameObject> Created = new();
        private readonly List<Component> _components = new();
        public Transform transform { get; }
        public string tag;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string objectName = "GameObject")
        {
            name = objectName;
            transform = new Transform { Owner = this };
            _components.Add(transform);
            Created.Add(this);
        }
        public T AddComponent<T>() where T : Component
        {
            var component = (T)Activator.CreateInstance(typeof(T));
            component.Owner = this;
            _components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T GetComponentInChildren<T>() where T : Component => GetComponent<T>() ??
            transform.Children.Select(child => child.gameObject.GetComponentInChildren<T>()).FirstOrDefault(value => value != null);
        public void SetActive(bool active) => activeSelf = active;
        public static GameObject CreatePrimitive(PrimitiveType type) => new(type.ToString());
    }
    public class Transform : Component
    {
        public Transform parent { get; private set; }
        public readonly List<Transform> Children = new();
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 position
        {
            get => parent == null ? localPosition : parent.position + parent.rotation * localPosition;
            set => localPosition = parent == null ? value : Quaternion.Inverse(parent.rotation) * (value - parent.position);
        }
        public Quaternion rotation
        {
            get => parent == null ? localRotation : parent.rotation * localRotation;
            set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value;
        }
        public void SetParent(Transform value, bool worldPositionStays = true)
        {
            var worldPosition = position;
            var worldRotation = rotation;
            parent?.Children.Remove(this);
            parent = value;
            parent?.Children.Add(this);
            if (worldPositionStays) { position = worldPosition; rotation = worldRotation; }
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    }
    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 one => new(1, 1, 1);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        private System.Numerics.Quaternion Native => new(x, y, z, w);
        private static Quaternion From(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
        public static Quaternion identity => new(0, 0, 0, 1);
        public static Quaternion Euler(float x, float y, float z) => From(System.Numerics.Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180));
        public static Quaternion Inverse(Quaternion value) => From(System.Numerics.Quaternion.Inverse(value.Native));
        public static Quaternion operator *(Quaternion a, Quaternion b) => From(a.Native * b.Native);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var result = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x, v.y, v.z), q.Native);
            return new Vector3(result.X, result.Y, result.Z);
        }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Abs(float value) => Math.Abs(value);
        public static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
        public static float Clamp01(float value) => Clamp(value, 0, 1);
        public static bool Approximately(float a, float b) => Abs(a - b) < Max(0.000001f * Max(Abs(a), Abs(b)), float.Epsilon * 8);
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1, 1, 1);
    }
    public struct Rect
    {
        public Vector2 position, size;
        public static bool operator ==(Rect a, Rect b) => a.Equals(b);
        public static bool operator !=(Rect a, Rect b) => !a.Equals(b);
        public override bool Equals(object other) => other is Rect r && position.Equals(r.position) && size.Equals(r.size);
        public override int GetHashCode() => HashCode.Combine(position, size);
    }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, offsetMin, offsetMax; }
    public class Light : Behaviour { public LightType type; public float intensity; }
    public class Camera : Behaviour { public CameraClearFlags clearFlags; public Color backgroundColor; }
    public class AnimationClip : Object { }
    public class Animator : Behaviour
    {
        public float speed = 1;
        public int LastStateHash, LastParameterHash, LastParameterValue;
        public float LastFade, LastOffset;
        public static int StringToHash(string value) => value.GetHashCode();
        public bool HasState(int layer, int hash) => hash == StringToHash("Idle") || hash == StringToHash("Eat");
        public void SetInteger(int hash, int value) { LastParameterHash = hash; LastParameterValue = value; }
        public void CrossFadeInFixedTime(int hash, float duration, int layer = -1, float fixedTimeOffset = 0, float normalizedTransitionTime = 0)
        { LastStateHash = hash; LastFade = duration; LastOffset = fixedTimeOffset; }
    }
    public class MaterialPropertyBlock { public void SetColor(string name, Color value) { } }
    public class Renderer : Component { public void SetPropertyBlock(MaterialPropertyBlock value) { } }
    public class AudioClip : Object { }
    public class AudioSource : Behaviour { public void PlayOneShot(AudioClip clip) { } }
    public static class Resources { public static T Load<T>(string path) where T : Object => null; }
    public static class Time { public static float time, deltaTime; }
    public static class Application
    {
        public static int targetFrameRate;
        private static string Root => Environment.GetEnvironmentVariable("ARPET_VALIDATION_ROOT") ?? Path.GetTempPath();
        public static string persistentDataPath => Path.Combine(Root, "persistent");
        public static string temporaryCachePath => Path.Combine(Root, "cache");
    }
    public static class Screen { public static int width = 1080, height = 1920, sleepTimeout; public static Rect safeArea; }
    public static class SleepTimeout { public const int NeverSleep = -1; }
    public enum PrimitiveType { Capsule }
    public enum LightType { Directional }
    public enum CameraClearFlags { SolidColor }
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public static class Debug
    {
        public static Action<LogType, string> Logged;
        public static void Log(object message) => Logged?.Invoke(LogType.Log, message?.ToString());
        public static void LogWarning(object message) => Logged?.Invoke(LogType.Warning, message?.ToString());
        public static void LogError(object message) => Logged?.Invoke(LogType.Error, message?.ToString());
    }
    public static class JsonUtility
    {
        // Uses a different JSON engine: real Unity JsonUtility still requires Unity verification.
        public static string ToJson(object value, bool prettyPrint = false) => JsonSerializer.Serialize(value, value.GetType(), new JsonSerializerOptions { IncludeFields = true, WriteIndented = prettyPrint });
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true });
    }
    public sealed class SerializeField : Attribute { }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string value) { } }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string value) { } }
    public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    public sealed class ContextMenu : Attribute { public ContextMenu(string value) { } }
    public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class RequireComponent : Attribute { public RequireComponent(Type type) { } }
    public sealed class CreateAssetMenuAttribute : Attribute { public string menuName, fileName; }
}

namespace UnityEngine.Android
{
    public sealed class PermissionCallbacks
    {
        public event Action<string> PermissionGranted, PermissionDenied, PermissionDeniedAndDontAskAgain;
        public void Grant() => PermissionGranted?.Invoke(Permission.Camera);
        public void Deny() => PermissionDenied?.Invoke(Permission.Camera);
        public void DenyPermanently() => PermissionDeniedAndDontAskAgain?.Invoke(Permission.Camera);
    }
    public static class Permission
    {
        public const string Camera = "android.permission.CAMERA";
        public static bool Authorized;
        public static int Requests;
        public static PermissionCallbacks LastCallbacks;
        public static bool HasUserAuthorizedPermission(string permission) => Authorized;
        public static void RequestUserPermission(string permission, PermissionCallbacks callbacks)
        { Requests++; LastCallbacks = callbacks; }
    }
}
