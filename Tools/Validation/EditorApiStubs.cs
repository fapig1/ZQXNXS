// API and recording substitutes for offline checks only. No Unity scene serialization is simulated.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string path; }
}
namespace UnityEditor
{
    public sealed class MenuItem : Attribute { public MenuItem(string name, bool isValidateFunction = false, int priority = 0) { } }
    public enum BuildTargetGroup { Android }
    public enum BuildTarget { Android }
    public enum ScriptingImplementation { IL2CPP }
    public enum AndroidSdkVersions { AndroidApiLevel29, AndroidApiLevel36 }
    [Flags] public enum AndroidArchitecture { ARM64 = 1 }
    public static class PlayerSettings
    {
        public static ScriptingImplementation GetScriptingBackend(BuildTargetGroup group) => ScriptingImplementation.IL2CPP;
        public static void SetScriptingBackend(BuildTargetGroup group, ScriptingImplementation implementation) { }
        public static class Android
        {
            public static AndroidArchitecture targetArchitectures = AndroidArchitecture.ARM64;
            public static AndroidSdkVersions minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            public static AndroidSdkVersions targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
        }
    }
    public static class EditorUserBuildSettings
    {
        public static BuildTarget activeBuildTarget = BuildTarget.Android;
        public static bool SwitchActiveBuildTarget(BuildTargetGroup group, BuildTarget target)
        { activeBuildTarget = target; return true; }
    }
    public static class Selection { public static Object activeObject; public static GameObject activeGameObject; }
    public static class EditorUtility
    {
        public static void SetDirty(Object value) { }
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static void ClearProgressBar() { }
    }
    public static class EditorApplication
    {
        public static void ExecuteMenuItem(string menuItemPath) { }
        public static void Exit(int returnValue) { }
    }
    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public BuildTarget target;
        public BuildOptions options;
    }
    [Flags] public enum BuildOptions { None = 0, Development = 1 }
    public static class BuildPipeline
    {
        // No build is attempted offline; the recorded report always succeeds.
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => new();
    }
    public sealed class EditorBuildSettingsScene
    {
        public string path;
        public bool enabled;
        public EditorBuildSettingsScene(string path, bool enabled) { this.path = path; this.enabled = enabled; }
    }
    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes = Array.Empty<EditorBuildSettingsScene>();
    }
    public class AssetImporter : Object { public static AssetImporter GetAtPath(string path) => null; }
    public class PluginImporter : AssetImporter
    {
        public bool GetCompatibleWithPlatform(BuildTarget platform) => false;
        public bool GetCompatibleWithEditor() => false;
    }
    public enum ModelImporterAnimationType { Generic }
    public enum ModelImporterAnimationCompression { Off }
    public enum ModelImporterMaterialImportMode { None }
    public enum ModelImporterSkinWeights { None, Standard, Custom }
    public sealed class ModelImporterClipAnimation
    {
        public string name = string.Empty;
        public bool loopTime, loopPose;
    }
    public class ModelImporter : AssetImporter
    {
        public ModelImporterAnimationType animationType;
        public ModelImporterAnimationCompression animationCompression;
        public ModelImporterMaterialImportMode materialImportMode;
        public ModelImporterSkinWeights skinWeights;
        public bool importAnimation, importBlendShapes, optimizeGameObjects;
        public int maxBonesPerVertex;
        public float minBoneWeight;
        public ModelImporterClipAnimation[] defaultClipAnimations = Array.Empty<ModelImporterClipAnimation>();
        public ModelImporterClipAnimation[] clipAnimations = Array.Empty<ModelImporterClipAnimation>();
        public void SaveAndReimport() { }
    }
    public class TextureImporter : AssetImporter
    {
        public bool alphaIsTransparency, mipmapEnabled = true;
        public TextureWrapMode wrapMode;
        public FilterMode filterMode;
        public void SaveAndReimport() { }
    }
    public static class AssetDatabase
    {
        private static readonly Dictionary<string, Object> Assets = new();
        public static T LoadAssetAtPath<T>(string path) where T : Object => Assets.TryGetValue(path, out var value) ? value as T : null;
        // No asset import database offline: FBX contents are not readable.
        public static Object[] LoadAllAssetsAtPath(string path) => Array.Empty<Object>();
        public static void ImportAsset(string path) { }
        public static T GetBuiltinExtraResource<T>(string path) where T : Object => null;
        public static void CreateAsset(Object value, string path) { Assets[path] = value; }
        public static bool IsValidFolder(string path) => Directory.Exists(path);
        public static string CreateFolder(string parent, string name) { Directory.CreateDirectory(Path.Combine(parent, name)); return name; }
        public static string[] FindAssets(string filter) => Array.Empty<string>();
        public static string GUIDToAssetPath(string guid) => guid;
        public static void SaveAssets() { }
        public static void Refresh() { }
    }
    public static class PrefabUtility
    {
        public static Object InstantiatePrefab(Object prefab) => null;
        public static Object InstantiatePrefab(Object prefab, Transform parent) => null;
    }
    public sealed class SerializedObject
    {
        private readonly object _value;
        public SerializedObject(object value) => _value = value;
        public SerializedProperty FindProperty(string name) => new(_value, name);

        /// <summary>
        /// 应用修改并触发 <c>OnValidate</c>（Unity 的 SerializedObject 也会回调它）。
        /// 替身若不回调，就会掩盖"数组字段改动后没有重建索引"的问题
        /// （见 MarkerRegistryBase.RebuildIndex 的使用场景）。
        /// </summary>
        public bool ApplyModifiedPropertiesWithoutUndo()
        {
            InvokeOnValidate(_value);
            return true;
        }

        private static void InvokeOnValidate(object value)
        {
            var type = value?.GetType();
            while (type != null && type != typeof(object))
            {
                var method = type.GetMethod("OnValidate",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    method.Invoke(value, null);
                    return;
                }

                type = type.BaseType;
            }
        }
    }

    /// <summary>
    /// 离线替身：按字段名读写，支持数组字段的 arraySize / GetArrayElementAtIndex /
    /// FindPropertyRelative。元素是结构体时，写入会先读回元素、改字段、再整体写回数组，
    /// 以模拟 Unity 的按路径写回；这仍然不是真实的序列化实现。
    /// </summary>
    public sealed class SerializedProperty
    {
        private readonly object _owner;
        private readonly FieldInfo _field;
        private readonly int _elementIndex;
        private readonly string _relativeName;

        public SerializedProperty(object value, string name)
        {
            _owner = value;
            _field = FieldOf(value, name);
            _elementIndex = -1;
        }

        private SerializedProperty(object owner, FieldInfo field, int elementIndex, string relativeName)
        {
            _owner = owner;
            _field = field;
            _elementIndex = elementIndex;
            _relativeName = relativeName;
        }

        private static FieldInfo FieldOf(object value, string name) =>
            value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(value.GetType().FullName, name);

        private object RawValue => _elementIndex < 0
            ? _field.GetValue(_owner)
            : ((Array)_field.GetValue(_owner)).GetValue(_elementIndex);

        private void RawSet(object value)
        {
            if (_elementIndex < 0)
            {
                _field.SetValue(_owner, value);
                return;
            }

            ((Array)_field.GetValue(_owner)).SetValue(value, _elementIndex);
        }

        private object SlotValue()
        {
            var raw = RawValue;
            return _relativeName == null ? raw : FieldOf(raw, _relativeName).GetValue(raw);
        }

        private void SetSlot(object value)
        {
            if (_relativeName == null)
            {
                RawSet(value);
                return;
            }

            // 结构体元素按值取回，改完必须写回数组，否则读取方看不到修改。
            var raw = RawValue;
            FieldOf(raw, _relativeName).SetValue(raw, value);
            RawSet(raw);
        }

        public Object objectReferenceValue { get => (Object)SlotValue(); set => SetSlot(value); }
        public bool boolValue { get => (bool)SlotValue(); set => SetSlot(value); }
        public string stringValue { get => (string)SlotValue(); set => SetSlot(value); }
        public int intValue
        {
            get => SlotValue() switch { int value => value, Enum e => Convert.ToInt32(e), _ => 0 };
            set
            {
                // Unity 允许用 intValue 写枚举字段；替身按字段类型转换。
                var type = _relativeName == null ? (_elementIndex < 0 ? _field.FieldType : _field.FieldType.GetElementType())
                    : FieldOf(RawValue, _relativeName).FieldType;
                SetSlot(type != null && type.IsEnum ? Enum.ToObject(type, value) : value);
            }
        }
        public float floatValue
        {
            get => SlotValue() is float value ? value : 0f;
            set => SetSlot(value);
        }
        public Vector3 vector3Value { get => (Vector3)SlotValue(); set => SetSlot(value); }
        public int arraySize
        {
            get => RawValue is Array array ? array.Length : 0;
            set => RawSet(Array.CreateInstance(_field.FieldType.GetElementType()!, value));
        }
        public SerializedProperty GetArrayElementAtIndex(int index) => new(_owner, _field, index, null);
        public SerializedProperty FindPropertyRelative(string name) => new(_owner, _field, _elementIndex, name);
    }
}
namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }
    public sealed class BuildSummary
    {
        public BuildResult result = BuildResult.Succeeded;
        public int totalErrors;
        public ulong totalSize;
    }
    public sealed class BuildReport
    {
        public BuildSummary summary = new();
    }
}
namespace UnityEditor.Animations
{
    public sealed class AnimatorState
    {
        public string name = string.Empty;
        public Motion motion;
    }
    public struct ChildAnimatorState
    {
        public AnimatorState state;
    }
    public sealed class AnimatorStateMachine
    {
        public List<ChildAnimatorState> states = new();
        public AnimatorState defaultState;
        public AnimatorState AddState(string name)
        {
            var state = new AnimatorState { name = name };
            states.Add(new ChildAnimatorState { state = state });
            return state;
        }
    }
    public sealed class AnimatorControllerLayer
    {
        public string name = string.Empty;
        public AnimatorStateMachine stateMachine = new();
    }
    public sealed class AnimatorController : RuntimeAnimatorController
    {
        public AnimatorControllerLayer[] layers = { new AnimatorControllerLayer() };
        public static AnimatorController CreateAnimatorControllerAtPath(string path)
        {
            var controller = new AnimatorController();
            AssetDatabase.CreateAsset(controller, path);
            return controller;
        }
    }
}
namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene }
    public enum NewSceneMode { Single }
    public enum OpenSceneMode { Single, Additive }
    public sealed class SceneSetup { }
    public static class EditorSceneManager
    {
        public static readonly Dictionary<string, GameObject[]> SavedObjects = new();
        public static bool AllowSceneSwitch = true;
        public static int NewSceneCount, SavePromptCount;
        private static string _activeScenePath = string.Empty;
        public static UnityEngine.SceneManagement.Scene GetActiveScene()
            => new() { path = _activeScenePath };
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() { SavePromptCount++; return AllowSceneSwitch; }
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode)
        {
            NewSceneCount++;
            ResetObjects();
            _activeScenePath = string.Empty;
            return new UnityEngine.SceneManagement.Scene();
        }
        public static UnityEngine.SceneManagement.Scene OpenScene(string path, OpenSceneMode mode = OpenSceneMode.Single)
        {
            _activeScenePath = path;
            ResetObjects();
            // Rebuild the stand-in object registry from the last recorded save of that path.
            if (SavedObjects.TryGetValue(path, out var objects))
            {
                foreach (var go in objects)
                {
                    GameObject.Created.Add(go);
                    if (!Object.All.Contains(go)) Object.All.Add(go);
                    foreach (var component in go.GetComponents<Component>())
                        if (!Object.All.Contains(component)) Object.All.Add(component);
                }
            }
            return new UnityEngine.SceneManagement.Scene { path = path };
        }
        public static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene) { }
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene)
            => SaveScene(scene, _activeScenePath);
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string path)
        {
            SavedObjects[path] = GameObject.Created.ToArray();
            File.WriteAllText(path, "OFFLINE TEST PLACEHOLDER. NOT A UNITY SCENE.");
            _activeScenePath = path;
            return true;
        }
        public static SceneSetup[] GetSceneManagerSetup() => Array.Empty<SceneSetup>();
        public static void RestoreSceneManagerSetup(SceneSetup[] setup) { }
        private static void ResetObjects()
        {
            GameObject.Created.Clear();
            Object.All.RemoveAll(value => value is GameObject || value is Component);
        }
    }
}

namespace UnityEditor.Android
{
    public interface IPostGenerateGradleAndroidProject
    {
        int callbackOrder { get; }
        void OnPostGenerateGradleAndroidProject(string path);
    }
}
