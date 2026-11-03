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
        // Offline registry so FindFirstObjectByType/FindObjectsByType can see stand-in objects.
        public static readonly List<Object> All = new();
        public string name;
        public static void DestroyImmediate(Object value)
        {
            if (value == null) return;
            All.Remove(value);
            if (value is GameObject go)
            {
                // Real Unity destroys the whole object graph; the stand-in must also drop the
                // GameObject's components so later FindObjectsByType calls cannot see them.
                foreach (var component in go.GetComponents<Component>()) All.Remove(component);
                GameObject.Created.Remove(go);
            }
        }
        public static void DontDestroyOnLoad(Object value) { }
        // Not a real clone: the stand-in returns the original reference.
        public static T Instantiate<T>(T original) where T : Object => original;
        public static T Instantiate<T>(T original, Transform parent, bool instantiateInWorldSpace) where T : Object => original;
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode = FindObjectsSortMode.None) where T : Object
            => All.OfType<T>().ToArray();
        public static T FindFirstObjectByType<T>(FindObjectsInactive includeInactive = FindObjectsInactive.Exclude) where T : Object
            => All.OfType<T>().FirstOrDefault();
        public static T[] FindObjectsByType<T>(FindObjectsInactive includeInactive, FindObjectsSortMode sortMode) where T : Object
            => All.OfType<T>().ToArray();
    }
    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None, InstanceID }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject
        {
            var instance = (T)Activator.CreateInstance(typeof(T));
            All.Add(instance);
            return instance;
        }
    }
    public class Component : Object
    {
        internal GameObject Owner;
        public GameObject gameObject => Owner;
        public Transform transform => Owner.transform;
        public T GetComponent<T>() where T : Component => Owner.GetComponent<T>();
        public T GetComponentInChildren<T>() where T : Component => Owner.GetComponentInChildren<T>();
        public T GetComponentInChildren<T>(bool includeInactive) where T : Component => Owner.GetComponentInChildren<T>(includeInactive);
        public T[] GetComponents<T>() where T : Component => Owner.GetComponents<T>();
        public T[] GetComponentsInChildren<T>() where T : Component => Owner.GetComponentsInChildren<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => Owner.GetComponentsInChildren<T>(includeInactive);
    }
    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public class MonoBehaviour : Behaviour
    {
        // Stand-in: coroutines are not run offline.
        public Coroutine StartCoroutine(System.Collections.IEnumerator routine) => new Coroutine();
    }
    public sealed class Coroutine { }
    public class GameObject : Object
    {
        public static readonly List<GameObject> Created = new();
        private readonly List<Component> _components = new();
        public Transform transform { get; }
        public string tag;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string objectName = "GameObject", params Type[] componentTypes)
        {
            name = objectName;
            var types = componentTypes ?? Array.Empty<Type>();
            var transformType = types.Contains(typeof(RectTransform)) ? typeof(RectTransform) : typeof(Transform);
            var rootTransform = (Transform)Activator.CreateInstance(transformType);
            rootTransform.Owner = this;
            transform = rootTransform;
            _components.Add(rootTransform);
            Created.Add(this);
            All.Add(this);
            All.Add(rootTransform);
            foreach (var type in types)
            {
                if (type == typeof(Transform) || type == typeof(RectTransform)) continue;
                var component = (Component)Activator.CreateInstance(type);
                component.Owner = this;
                _components.Add(component);
                All.Add(component);
            }
        }
        public T AddComponent<T>() where T : Component
        {
            var component = (T)Activator.CreateInstance(typeof(T));
            component.Owner = this;
            _components.Add(component);
            All.Add(component);

            // Unity 在把组件加到<b>激活</b>物体上时会立即调用 Awake，随后（若 enabled）调用 OnEnable。
            // 替身必须照做：否则"AddComponent 时 Awake 先建好空索引、测试再写字段"这类真实 Unity
            // 下必然失败的写法会被掩盖（2026-10-27 真机 EditMode 实测踩到过）。
            if (activeSelf)
            {
                InvokeMessage(component, "Awake");
                if (component is Behaviour behaviour && behaviour.enabled) InvokeMessage(component, "OnEnable");
            }

            return component;
        }

        /// <summary>
        /// 沿类型层次找第一个无参的 Unity 消息方法并调用（private / protected / override 都能命中）。
        /// 只用于离线替身，不代表 Unity 的完整生命周期时序。
        /// </summary>
        private static void InvokeMessage(object value, string name)
        {
            var type = value.GetType();
            while (type != null && type != typeof(object))
            {
                var method = type.GetMethod(name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (method != null)
                {
                    method.Invoke(value, null);
                    return;
                }

                type = type.BaseType;
            }
        }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponents<T>() where T : Component => _components.OfType<T>().ToArray();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component => GetComponent<T>() ??
            transform.Children.Select(child => child.gameObject.GetComponentInChildren<T>(includeInactive)).FirstOrDefault(value => value != null);
        public T[] GetComponentsInChildren<T>() where T : Component
        {
            var result = new List<T>(GetComponents<T>());
            foreach (var child in transform.Children) result.AddRange(child.gameObject.GetComponentsInChildren<T>());
            return result.ToArray();
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>(GetComponents<T>());
            foreach (var child in transform.Children)
            {
                if (!includeInactive && !child.gameObject.activeSelf) continue;
                result.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            }
            return result.ToArray();
        }
        public void SetActive(bool active) => activeSelf = active;
        public static GameObject CreatePrimitive(PrimitiveType type) => new(type.ToString());
        public static GameObject Find(string value) => Created.FirstOrDefault(go => go.name == value && go.activeInHierarchy);
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
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public Transform Find(string childPath)
        {
            // Stand-in: matches direct child names only, no hierarchy paths.
            var direct = Children.FirstOrDefault(value => value.name == childPath);
            return direct ?? Children.Select(child => child.Find(childPath)).FirstOrDefault(value => value != null);
        }
        public void LookAt(Vector3 target) => LookAt(target, Vector3.up);
        public void LookAt(Vector3 target, Vector3 worldUp) => rotation = Quaternion.LookRotation(target - position, worldUp);
        public bool IsChildOf(Transform value)
        {
            for (var t = this; t != null; t = t.parent) if (t == value) return true;
            return false;
        }
        public Vector3 forward => rotation * Vector3.forward;
        public Vector3 up => rotation * Vector3.up;
        public Vector3 right => rotation * Vector3.right;
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public static Vector2 one => new(1, 1);
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized => magnitude <= 0 ? zero : new Vector2(x / magnitude, y / magnitude);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new(a.x / b, a.y / b);
        public static implicit operator Vector2(Vector3 value) => new(value.x, value.y);
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
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 right => new(1, 0, 0);
        public static Vector3 forward => new(0, 0, 1);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { var m = magnitude; return m <= 0 ? zero : new Vector3(x / m, y / m, z / m); } }
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Project(Vector3 value, Vector3 normal)
        {
            var square = Dot(normal, normal);
            return square <= 0 ? zero : normal * (Dot(value, normal) / square);
        }
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Angle(Vector3 from, Vector3 to)
        {
            // 与 Unity 一致：返回 [0,180] 的夹角（度），任一向量为零时返回 0。
            var denominator = (float)System.Math.Sqrt((double)from.sqrMagnitude * to.sqrMagnitude);
            if (denominator < 1e-15f) return 0f;
            var dot = Dot(from, to) / denominator;
            return (float)(System.Math.Acos(System.Math.Clamp(dot, -1f, 1f)) * 180 / System.Math.PI);
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator *(float b, Vector3 a) => a * b;
        public static Vector3 operator /(Vector3 a, float b) => new(a.x / b, a.y / b, a.z / b);
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
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            var f = forward.normalized;
            if (f.sqrMagnitude <= 0) return identity;
            var r = Vector3.Cross(up, f).normalized;
            var u = Vector3.Cross(f, r);
            var matrix = new System.Numerics.Matrix4x4(
                r.x, r.y, r.z, 0,
                u.x, u.y, u.z, 0,
                f.x, f.y, f.z, 0,
                0, 0, 0, 1);
            return From(System.Numerics.Quaternion.CreateFromRotationMatrix(matrix));
        }
        public static float Angle(Quaternion a, Quaternion b)
        {
            var dot = Math.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w);
            return (float)(2 * Math.Acos(Math.Min(1, dot)) * 180 / Math.PI);
        }
        public Vector3 eulerAngles
        {
            get
            {
                return EulerFrom(Native);
            }
        }
        private static Vector3 EulerFrom(System.Numerics.Quaternion q)
        {
            // Approximation is sufficient for variance-tolerant offline checks.
            var sinp = 2 * (q.W * q.Y - q.Z * q.X);
            var pitch = Math.Abs(sinp) >= 1 ? Math.Sign(sinp) * Math.PI / 2 : Math.Asin(sinp);
            var siny = 2 * (q.W * q.Z + q.X * q.Y);
            var yaw = Math.Abs(siny) >= 1 ? Math.Sign(siny) * Math.PI / 2 : Math.Asin(siny);
            var sinr = 2 * (q.W * q.X + q.Y * q.Z);
            var roll = Math.Abs(sinr) >= 1 ? Math.Sign(sinr) * Math.PI / 2 : Math.Asin(sinr);
            return new Vector3((float)(pitch * 180 / Math.PI), (float)(yaw * 180 / Math.PI), (float)(roll * 180 / Math.PI));
        }
        public static Quaternion operator *(Quaternion a, Quaternion b) => From(a.Native * b.Native);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var result = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x, v.y, v.z), q.Native);
            return new Vector3(result.X, result.Y, result.Z);
        }
    }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public const float Deg2Rad = MathF.PI / 180;
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Abs(float value) => Math.Abs(value);
        public static int RoundToInt(float value) => (int)Math.Round(value);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
        public static float Floor(float value) => (float)Math.Floor(value);
        public static float Tan(float value) => MathF.Tan(value);
        public static float Sign(float value) => Math.Sign(value);
        public static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
        public static float Clamp01(float value) => Clamp(value, 0, 1);
        public static bool Approximately(float a, float b) => Abs(a - b) < Max(0.000001f * Max(Abs(a), Abs(b)), float.Epsilon * 8);
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1, 1, 1);
        public static Color black => new(0, 0, 0);
        public static Color clear => new(0, 0, 0, 0);
    }
    public struct Rect
    {
        public Vector2 position, size;
        public Rect(float x, float y, float width, float height)
        { position = new Vector2(x, y); size = new Vector2(width, height); }
        public float x { get => position.x; set => position.x = value; }
        public float y { get => position.y; set => position.y = value; }
        public float width { get => size.x; set => size.x = value; }
        public float height { get => size.y; set => size.y = value; }
        public float xMin { get => x; set { float max = xMax; x = value; width = max - value; } }
        public float yMin { get => y; set { float max = yMax; y = value; height = max - value; } }
        public float xMax { get => x + width; set => width = value - x; }
        public float yMax { get => y + height; set => height = value - y; }
        public static bool operator ==(Rect a, Rect b) => a.Equals(b);
        public static bool operator !=(Rect a, Rect b) => !a.Equals(b);
        public override bool Equals(object other) => other is Rect r && position.Equals(r.position) && size.Equals(r.size);
        public override int GetHashCode() => HashCode.Combine(position, size);
    }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, offsetMin, offsetMax, pivot; }
    public class Light : Behaviour
    {
        public LightType type;
        public float intensity;
        public Color color = Color.white;
        public LightShadows shadows;
    }
    public enum LightShadows { None, Hard, Soft }
    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction; }
    }
    public struct RaycastHit { public Collider collider; public float distance; public Vector3 point; }
    public class Collider : Component
    {
        public bool enabled = true;
        public bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance)
            => Raycast(ray, out hitInfo) && hitInfo.distance <= maxDistance;
        public virtual bool Raycast(Ray ray, out RaycastHit hitInfo) { hitInfo = default; return false; }
    }
    public class SphereCollider : Collider
    {
        public float radius = 0.5f;
        // Stand-in does plain sphere/ray intersection; no Unity physics engine involved.
        public override bool Raycast(Ray ray, out RaycastHit hitInfo)
        {
            hitInfo = default;
            var direction = ray.direction.normalized;
            var projection = Vector3.Dot(transform.position - ray.origin, direction);
            if (projection < 0) return false;
            var closest = ray.origin + direction * projection;
            if (Vector3.Distance(closest, transform.position) > radius) return false;
            hitInfo = new RaycastHit { collider = this, distance = projection, point = closest };
            return true;
        }
    }
    public class Camera : Behaviour
    {
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public bool orthographic;
        public float orthographicSize = 5, fieldOfView = 60, nearClipPlane = 0.3f, farClipPlane = 1000, aspect = 1;
        public bool allowHDR = true;
        public RenderTexture targetTexture;
        public static Camera main => All.OfType<Camera>().FirstOrDefault(value => value.gameObject.tag == "MainCamera");
        public int pixelWidth => Screen.width;
        public int pixelHeight => Screen.height;
        public Ray ScreenPointToRay(Vector3 position)
        {
            var width = Math.Max(1, Screen.width);
            var height = Math.Max(1, Screen.height);
            var halfHeight = MathF.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            var halfWidth = halfHeight * (aspect <= 0 ? (float)width / height : aspect);
            var direction = (transform.forward
                + transform.right * ((position.x / width * 2 - 1) * halfWidth)
                + transform.up * ((position.y / height * 2 - 1) * halfHeight)).normalized;
            return new Ray(transform.position, direction);
        }
        public Ray ScreenPointToRay(Vector2 position) => ScreenPointToRay(new Vector3(position.x, position.y, 0));
        public void Render() { }
    }
    public class Motion : Object { }
    public class AnimationEvent
    {
        public string functionName, stringParameter;
        public float time, floatParameter;
        public int intParameter;
    }
    public class AnimationClip : Motion
    {
        public float length;
        public bool isLooping;
        public AnimationEvent[] events = Array.Empty<AnimationEvent>();
        public void SampleAnimation(GameObject target, float time) { }
    }
    public class RuntimeAnimatorController : Object { }
    public enum AnimatorCullingMode { AlwaysAnimate, CullUpdateTransforms, CullCompletely }
    public struct AnimatorStateInfo
    {
        public int shortNameHash;
        public float normalizedTime;
        public bool IsName(string name) => shortNameHash == Animator.StringToHash(name);
        public bool IsTag(string tag) => false;
    }
    public class Animator : Behaviour
    {
        public float speed = 1;
        public int LastStateHash, LastParameterHash, LastParameterValue;
        public float LastFade, LastOffset;
        public string LastStateName;
        public RuntimeAnimatorController runtimeAnimatorController;
        public bool applyRootMotion;
        public AnimatorCullingMode cullingMode;
        private AnimatorStateInfo _state;
        public static int StringToHash(string value) => value.GetHashCode();
        public bool HasState(int layer, int hash) => hash == StringToHash("Idle") || hash == StringToHash("Eat");
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) => _state;
        public void Rebind() { }
        public void Update(float deltaTime) { }
        public void Play(string stateName, int layer = -1, float normalizedTime = float.NegativeInfinity)
        { LastStateName = stateName; LastStateHash = StringToHash(stateName); _state = new AnimatorStateInfo { shortNameHash = LastStateHash }; }
        public void SetInteger(int hash, int value) { LastParameterHash = hash; LastParameterValue = value; }
        public void CrossFadeInFixedTime(int hash, float duration, int layer = -1, float fixedTimeOffset = 0, float normalizedTransitionTime = 0)
        {
            LastStateHash = hash; LastFade = duration; LastOffset = fixedTimeOffset;
            _state = new AnimatorStateInfo { shortNameHash = hash };
        }
        public void CrossFadeInFixedTime(string stateName, float duration, int layer = -1, float fixedTimeOffset = 0, float normalizedTransitionTime = 0)
        {
            LastStateName = stateName; LastStateHash = StringToHash(stateName); LastFade = duration; LastOffset = fixedTimeOffset;
            _state = new AnimatorStateInfo { shortNameHash = LastStateHash };
        }
    }
    public class Texture : Object { }
    public class Sprite : Object { }
    public class Texture2D : Texture
    {
        public int width, height;
        public Texture2D(int width, int height, TextureFormat format = TextureFormat.RGBA32, bool mipChain = true)
        { this.width = width; this.height = height; }
        public void ReadPixels(Rect source, int destX, int destY) { }
        public void Apply() { }
        public byte[] EncodeToPNG() => Array.Empty<byte>();
    }
    public enum TextureFormat { RGBA32, RGB24 }
    public class RenderTexture : Texture
    {
        public static RenderTexture active;
        public int antiAliasing, width, height, depth;
        public RenderTexture(int width, int height, int depth) { this.width = width; this.height = height; this.depth = depth; }
        public void Release() { }
    }
    public class Shader : Object
    {
        public static Shader Find(string name) => new Shader { name = name };
    }
    public class Material : Object
    {
        public Texture mainTexture;
        public Shader shader;
        public Material() { }
        public Material(Shader shader) { this.shader = shader; }
        public Material(Material source) { shader = source?.shader; mainTexture = source?.mainTexture; }
        public void SetTexture(string name, Texture value) { mainTexture = value; }
        public void SetFloat(string name, float value) { }
        private readonly Dictionary<string, Vector4> _vectors = new();
        public void SetVector(string name, Vector4 value) => _vectors[name] = value;
        public Vector4 GetVector(string name) => _vectors.TryGetValue(name, out var v) ? v : Vector4.zero;
        public void SetTextureScale(string name, Vector2 value) { }
        public void SetTextureOffset(string name, Vector2 value) { }
        public void CopyPropertiesFromMaterial(Material source) { shader = source?.shader; mainTexture = source?.mainTexture; }
    }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public class Mesh : Object
    {
        public Vector3[] vertices = Array.Empty<Vector3>();
        public Vector3[] normals = Array.Empty<Vector3>();
        public Unity.Collections.NativeArray<byte> GetBonesPerVertex() => new(Array.Empty<byte>());
    }
    public enum SkinQuality { Auto, Bone1, Bone2, Bone4 }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new(0, 0, 0, 0);
        public static bool operator ==(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
        public override bool Equals(object obj) => obj is Vector4 other && this == other;
        public override int GetHashCode() => HashCode.Combine(x, y, z, w);
        public override string ToString() => $"({x}, {y}, {z}, {w})";
    }
    public class TextAsset : Object
    {
        public string text = string.Empty;
        public TextAsset() { }
        public TextAsset(string text) { this.text = text; }
    }
    // Stand-in keeps the values so tests can read back what a renderer was given.
    public class MaterialPropertyBlock
    {
        internal readonly Dictionary<string, object> Values = new();
        public bool isEmpty => Values.Count == 0;
        public void Clear() => Values.Clear();
        public void SetColor(string name, Color value) => Values[name] = value;
        public void SetVector(string name, Vector4 value) => Values[name] = value;
        public void SetFloat(string name, float value) => Values[name] = value;
        public Vector4 GetVector(string name) => Values.TryGetValue(name, out var v) && v is Vector4 vec ? vec : Vector4.zero;
        public float GetFloat(string name) => Values.TryGetValue(name, out var v) && v is float f ? f : 0f;
    }
    public class Renderer : Component
    {
        public bool enabled = true;
        public Material[] sharedMaterials = Array.Empty<Material>();
        public Material sharedMaterial;
        private readonly Dictionary<string, object> _block = new();
        public void SetPropertyBlock(MaterialPropertyBlock value)
        {
            _block.Clear();
            if (value == null) return;
            foreach (var pair in value.Values) _block[pair.Key] = pair.Value;
        }
        public void GetPropertyBlock(MaterialPropertyBlock dest)
        {
            dest.Values.Clear();
            foreach (var pair in _block) dest.Values[pair.Key] = pair.Value;
        }
    }
    public class MeshRenderer : Renderer
    {
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows = true;
    }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class SkinnedMeshRenderer : Renderer
    {
        public SkinQuality quality;
        public bool updateWhenOffscreen;
        public Mesh sharedMesh = new();
        public void BakeMesh(Mesh mesh) { }
    }
    public enum SkinWeights { OneBone, TwoBones, FourBones, Unlimited }
    public static class QualitySettings
    {
        public static string[] names = { "Very Low", "Low", "Medium", "High", "Very High", "Ultra" };
        public static int GetQualityLevel() => 2;
        public static void SetQualityLevel(int index, bool applyExpensiveChanges = true) { }
        public static SkinWeights skinWeights;
    }
    public static class RenderSettings
    {
        public static Rendering.AmbientMode ambientMode;
        public static Color ambientLight, ambientSkyColor, ambientEquatorColor, ambientGroundColor;
        public static Material skybox;
    }
    public static class DynamicGI { public static void UpdateEnvironment() { } }
    public class AudioClip : Object
    {
        public int samples, channels = 1, frequency = 44100;
        public float length => frequency > 0 ? (float)samples / frequency : 0f;
        public bool GetData(float[] data, int offsetSamples) => true;
    }
    public class AudioSource : Behaviour
    {
        public AudioClip clip;
        public bool playOnAwake = true, loop;
        public float volume = 1f, spatialBlend;
        public int PlayCount;
        public void PlayOneShot(AudioClip clip) { }
        public void Play() => PlayCount++;
        public void Stop() { }
    }
    public static class Microphone
    {
        public static string[] devices = Array.Empty<string>();
        public static AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency) => null;
        public static void End(string deviceName) { }
        public static int GetPosition(string deviceName) => 0;
    }
    public sealed class Touch
    {
        public int fingerId;
        public Vector2 position;
        public TouchPhase phase;
    }
    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
    public static class Input
    {
        public static Vector3 mousePosition;
        public static int touchCount;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetMouseButtonUp(int button) => false;
        public static Touch GetTouch(int index) => new();
    }
    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
    }
    /// <summary>
    /// 离线替身时钟。Assets 下的代码只能<b>读</b> <c>Time.*</c>：Unity 里这些是只读属性，
    /// 可写的替身字段曾经掩盖了"测试里给 Time.time 赋值"的编译错误。
    /// 需要推进时间的离线校验改为写 <see cref="ARPet.Validation.ValidationClock"/>。
    /// </summary>
    public static class Time
    {
        public static float time => ARPet.Validation.ValidationClock.time;
        public static float deltaTime => ARPet.Validation.ValidationClock.deltaTime;
        public static float unscaledTime => ARPet.Validation.ValidationClock.unscaledTime;
        public static float unscaledDeltaTime => ARPet.Validation.ValidationClock.unscaledDeltaTime;
        public static float realtimeSinceStartup => ARPet.Validation.ValidationClock.realtimeSinceStartup;
    }
    public static class Application
    {
        public static int targetFrameRate;
        public static bool isBatchMode, isPlaying;
        public static string unityVersion = "6000.0.83f1";
        public static string dataPath => Path.Combine(Directory.GetCurrentDirectory(), "Assets");
        public static string streamingAssetsPath => Path.Combine(dataPath, "StreamingAssets");
        private static string Root => Environment.GetEnvironmentVariable("ARPET_VALIDATION_ROOT") ?? Path.GetTempPath();
        public static string persistentDataPath => Path.Combine(Root, "persistent");
        public static string temporaryCachePath => Path.Combine(Root, "cache");
    }
    public static class Screen
    {
        public static int width = 1080, height = 1920, sleepTimeout;
        public static float dpi;
        public static Rect safeArea;
    }
    public static class SystemInfo
    {
        public static string deviceModel = "ValidationDevice", operatingSystem = "ValidationOS";
        public static float batteryLevel = -1f;
    }
    public sealed class GUIStyleState { public Color textColor = Color.white; }
    public sealed class GUIStyle
    {
        // Copy constructor stand-in: real GUIStyle copies the full native style.
        public GUIStyle() { }
        public GUIStyle(GUIStyle other)
        {
            if (other == null) return;
            fontSize = other.fontSize;
            normal = other.normal;
            textColor = other.textColor;
            richText = other.richText;
            wordWrap = other.wordWrap;
        }
        public int fontSize;
        public GUIStyleState normal = new();
        public Color textColor = Color.white;
        public bool richText, wordWrap;
    }
    public sealed class GUISkin
    {
        public GUIStyle label = new(), box = new(), button = new();
    }
    public static class GUI
    {
        public static GUISkin skin { get; set; } = new();
        public static void Label(Rect position, string text) { }
        public static void Label(Rect position, string text, GUIStyle style) { }
        public static bool Button(Rect position, string text) => false;
    }
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
        public static void LogException(Exception exception) => Logged?.Invoke(LogType.Exception, exception?.ToString());
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
        public const string Microphone = "android.permission.RECORD_AUDIO";
        public static bool Authorized;
        public static int Requests;
        public static PermissionCallbacks LastCallbacks;
        public static bool HasUserAuthorizedPermission(string permission) => Authorized;
        public static void RequestUserPermission(string permission, PermissionCallbacks callbacks)
        { Requests++; LastCallbacks = callbacks; }
    }
}

namespace UnityEngine.Networking
{
    // Stand-in: never used offline (Android-only branch), declared so the code compiles.
    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
        public Result result;
        public string error = string.Empty;
        public DownloadHandler downloadHandler = new();
        public static UnityWebRequest Get(string uri) => new();
        public object SendWebRequest() => null;
        public void Dispose() { }
    }
    public sealed class DownloadHandler { public byte[] data = Array.Empty<byte>(); }
}

namespace UnityEngine.Rendering
{
    public enum AmbientMode { Skybox, Trilight, Flat, Custom }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
}

namespace UnityEngine.EventSystems
{
    public class UIBehaviour : MonoBehaviour { }
    public sealed class EventSystem : UIBehaviour
    {
        public static EventSystem current;
        // No UI event processing offline: nothing is ever reported as covered.
        public bool IsPointerOverGameObject() => false;
        public bool IsPointerOverGameObject(int pointerId) => false;
    }
    public sealed class StandaloneInputModule : UIBehaviour { }
    public class PointerEventData { public GameObject pointerPress; }
    public interface IPointerDownHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler { void OnPointerUp(PointerEventData eventData); }
    public interface IPointerExitHandler { void OnPointerExit(PointerEventData eventData); }
}

namespace UnityEngine.UI
{
    public class Graphic : Behaviour
    {
        public Color color = Color.white;
        public bool raycastTarget = true;
        public RectTransform rectTransform => transform as RectTransform;
    }
    public sealed class Text : Graphic
    {
        public string text = string.Empty;
        public Font font;
        public int fontSize = 14;
        public FontStyle fontStyle;
        public TextAnchor alignment;
        public HorizontalWrapMode horizontalOverflow;
        public VerticalWrapMode verticalOverflow;
        public bool supportRichText = true;
    }
    public sealed class Image : Graphic
    {
        // 只补齐 Assets 代码与检查用到的成员，不是 Unity 的完整 Image 实现。
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public Sprite sprite;
        public Type type;
        public FillMethod fillMethod;
        public float fillAmount = 1f;
        public bool preserveAspect;
    }
    public sealed class Canvas : Behaviour
    {
        public RenderMode renderMode;
        public int sortingOrder;
    }
    public sealed class CanvasScaler : Behaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public ScaleMode uiScaleMode;
        public Vector2 referenceResolution;
        public ScreenMatchMode screenMatchMode;
        public float matchWidthOrHeight;
    }
    public sealed class GraphicRaycaster : Behaviour { }
}

namespace UnityEngine
{
    public sealed class Font : Object { }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
}

namespace Unity.Collections
{
    // Minimal stand-in for the Unity.Collections container used by mesh skin-weight queries.
    public struct NativeArray<T> : IDisposable
    {
        private T[] _values;
        public NativeArray(T[] values) => _values = values ?? Array.Empty<T>();
        public T[] ToArray() => _values ?? Array.Empty<T>();
        public int Length => _values?.Length ?? 0;
        public void Dispose() { }
    }
}

namespace ARPet.Validation
{
    /// <summary>
    /// 离线替身时钟的<b>可写</b>入口。只有校验代码（IntegrationChecks 等）可以推进它；
    /// <c>UnityEngine.Time</c> 的替身只暴露只读属性指向这里，因此 Assets 下任何
    /// "给 Time.time 赋值"的写法会像真实 Unity 一样编译失败。
    /// </summary>
    public static class ValidationClock
    {
        public static float time, deltaTime, unscaledTime, unscaledDeltaTime, realtimeSinceStartup;
    }
}
