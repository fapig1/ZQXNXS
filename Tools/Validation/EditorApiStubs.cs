// API and recording substitutes for offline checks only. No Unity scene serialization is simulated.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEngine.SceneManagement
{
    public struct Scene { }
}
namespace UnityEditor
{
    public sealed class MenuItem : Attribute { public MenuItem(string name, bool isValidateFunction = false, int priority = 0) { } }
    public enum BuildTargetGroup { Android }
    public enum BuildTarget { Android }
    public enum ScriptingImplementation { IL2CPP }
    [Flags] public enum AndroidArchitecture { ARM64 = 1 }
    public static class PlayerSettings
    {
        public static ScriptingImplementation GetScriptingBackend(BuildTargetGroup group) => ScriptingImplementation.IL2CPP;
        public static class Android { public static AndroidArchitecture targetArchitectures = AndroidArchitecture.ARM64; }
    }
    public static class EditorUserBuildSettings { public static BuildTarget activeBuildTarget = BuildTarget.Android; }
    public static class Selection { public static Object activeObject; }
    public static class EditorUtility { public static void SetDirty(Object value) { } }
    public class AssetImporter : Object { public static AssetImporter GetAtPath(string path) => null; }
    public enum ModelImporterAnimationType { Generic }
    public enum ModelImporterAnimationCompression { Off }
    public enum ModelImporterMaterialImportMode { None }
    public class ModelImporter : AssetImporter
    {
        public ModelImporterAnimationType animationType;
        public ModelImporterAnimationCompression animationCompression;
        public ModelImporterMaterialImportMode materialImportMode;
        public bool importAnimation, importBlendShapes, optimizeGameObjects;
        public void SaveAndReimport() { }
    }
    public static class AssetDatabase
    {
        private static readonly Dictionary<string, Object> Assets = new();
        public static T LoadAssetAtPath<T>(string path) where T : Object => Assets.TryGetValue(path, out var value) ? value as T : null;
        public static void CreateAsset(Object value, string path) { Assets[path] = value; }
        public static bool IsValidFolder(string path) => Directory.Exists(path);
        public static string CreateFolder(string parent, string name) { Directory.CreateDirectory(Path.Combine(parent, name)); return name; }
        public static string[] FindAssets(string filter) => Array.Empty<string>();
        public static string GUIDToAssetPath(string guid) => guid;
        public static void SaveAssets() { }
        public static void Refresh() { }
    }
    public static class PrefabUtility { public static Object InstantiatePrefab(Object prefab, Transform parent) => null; }
    public sealed class SerializedObject
    {
        private readonly object _value;
        public SerializedObject(object value) => _value = value;
        public SerializedProperty FindProperty(string name) => new(_value, name);
        public bool ApplyModifiedPropertiesWithoutUndo() => true;
    }
    public sealed class SerializedProperty
    {
        private readonly object _value;
        private readonly FieldInfo _field;
        public SerializedProperty(object value, string name)
        {
            _value = value;
            _field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(value.GetType().FullName, name);
        }
        public Object objectReferenceValue { get => (Object)_field.GetValue(_value); set => _field.SetValue(_value, value); }
        public bool boolValue { get => (bool)_field.GetValue(_value); set => _field.SetValue(_value, value); }
    }
}
namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene }
    public enum NewSceneMode { Single }
    public static class EditorSceneManager
    {
        public static readonly Dictionary<string, GameObject[]> SavedObjects = new();
        public static bool AllowSceneSwitch = true;
        public static int NewSceneCount, SavePromptCount;
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() { SavePromptCount++; return AllowSceneSwitch; }
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode)
        { NewSceneCount++; GameObject.Created.Clear(); return new UnityEngine.SceneManagement.Scene(); }
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string path)
        {
            SavedObjects[path] = GameObject.Created.ToArray();
            File.WriteAllText(path, "OFFLINE TEST PLACEHOLDER. NOT A UNITY SCENE.");
            return true;
        }
    }
}
