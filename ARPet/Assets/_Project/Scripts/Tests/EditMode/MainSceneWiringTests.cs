using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using ZQXNXS.ARPet.App;
using ZQXNXS.ARPet.Presentation;
using ZQXNXS.ARPet.UI;

namespace ZQXNXS.ARPet.Tests
{
    public sealed class MainSceneWiringTests
    {
        private const string MainScenePath = "Assets/_Project/Scenes/02_Main.unity";

        private string _previousScenePath;

        [SetUp]
        public void OpenMainScene()
        {
            _previousScenePath = EditorSceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }

        [TearDown]
        public void RestorePreviousScene()
        {
            if (!string.IsNullOrEmpty(_previousScenePath) && _previousScenePath != MainScenePath)
            {
                EditorSceneManager.OpenScene(_previousScenePath, OpenSceneMode.Single);
            }
        }

        [Test]
        public void Pet_IsAnchoredToDen_WithExportCoordinateOrientation()
        {
            var den = GameObject.Find("DenCardTarget");
            Assert.IsNotNull(den, "02_Main 缺少 DenCardTarget");

            var petRoot = den.transform.Find("PetRoot (AnchoredToDen)");
            Assert.IsNotNull(petRoot, "PetRoot 必须是 DenCardTarget 的直接子物体");
            Assert.Less(petRoot.localPosition.sqrMagnitude, 1e-10f);
            Assert.Less(Quaternion.Angle(petRoot.localRotation, Quaternion.identity), 0.001f,
                "不应额外旋转：Blender 导出与 ImageTarget 局部坐标都使用 +Y 向上、+Z 向前");

            var presenter = petRoot.GetComponent<PetPresenter>();
            var animator = petRoot.GetComponentInChildren<Animator>(true);
            var renderer = petRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(presenter);
            Assert.IsNotNull(animator);
            Assert.IsNotNull(animator.runtimeAnimatorController);
            Assert.IsNotNull(renderer);
            Assert.AreEqual(3, renderer.sharedMaterials.Length);
            Assert.That(renderer.sharedMaterials, Has.All.Not.Null);
        }

        [Test]
        public void ImageTargets_UseStreamingAssetsRelativeVuforiaDataSetPath()
        {
            AssertDataSetPath("DenCardTarget");
            AssertDataSetPath("FoodCardTarget");
        }

        [Test]
        public void ImageTargets_AreInitializedForRuntimeObserverCreation()
        {
            AssertRuntimeInitializationState("DenCardTarget");
            AssertRuntimeInitializationState("FoodCardTarget");
        }

        private static void AssertDataSetPath(string targetName)
        {
            var target = GameObject.Find(targetName);
            Assert.IsNotNull(target, $"02_Main 缺少 {targetName}");

            SerializedProperty property = null;
            foreach (var component in target.GetComponents<Component>())
            {
                if (component == null) continue;
                var candidate = new SerializedObject(component).FindProperty("mDataSetPath");
                if (candidate == null) continue;
                property = candidate;
                break;
            }

            Assert.IsNotNull(property, $"{targetName} 缺少 mDataSetPath 序列化字段");
            Assert.That(property.stringValue, Does.StartWith("Vuforia/"));
            Assert.That(property.stringValue, Does.EndWith(".xml"));
            Assert.That(property.stringValue, Does.Contain("ZQXS"));
        }

        private static void AssertRuntimeInitializationState(string targetName)
        {
            var target = GameObject.Find(targetName);
            Assert.IsNotNull(target, $"02_Main 缺少 {targetName}");

            var initialized = FindSerializedProperty(target, "mInitializedInEditor");
            Assert.IsNotNull(initialized, $"{targetName} 缺少 mInitializedInEditor 序列化字段");
            Assert.IsTrue(initialized.boolValue,
                $"{targetName} 必须将 mInitializedInEditor 持久化为 true，否则 Vuforia 不会创建运行时观察者");

            var needsUpgrade = FindSerializedProperty(target, "mTrackingOptimizationNeedsUpgrade");
            Assert.IsNotNull(needsUpgrade, $"{targetName} 缺少 mTrackingOptimizationNeedsUpgrade 序列化字段");
            Assert.IsFalse(needsUpgrade.boolValue,
                $"{targetName} 的 mTrackingOptimizationNeedsUpgrade 必须为 false");
        }

        private static SerializedProperty FindSerializedProperty(GameObject target, string propertyName)
        {
            foreach (var component in target.GetComponents<Component>())
            {
                if (component == null) continue;
                var property = new SerializedObject(component).FindProperty(propertyName);
                if (property != null) return property;
            }

            return null;
        }

        [Test]
        public void AppRoot_UsesRealTrackingAndWiredPresentation()
        {
            var appRoot = Object.FindFirstObjectByType<AppRoot>(FindObjectsInactive.Include);
            Assert.IsNotNull(appRoot);

            var so = new SerializedObject(appRoot);
            Assert.IsNotNull(so.FindProperty("config").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("trackingSourceBehaviour").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("markerRegistryBehaviour").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("presenter").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("anchoredPetRoot").objectReferenceValue);
            Assert.IsFalse(so.FindProperty("autoCreateStubSource").boolValue,
                "AR 主场景绝不能自动回退到 StubTrackingSource");

            var bridge = appRoot.GetComponent<TouchInputBridge>();
            Assert.IsNotNull(bridge);
            var bridgeSo = new SerializedObject(bridge);
            Assert.IsNotNull(bridgeSo.FindProperty("raycastCamera").objectReferenceValue);
            Assert.IsNotNull(bridgeSo.FindProperty("hitRadiusOrigin").objectReferenceValue);
        }

        [Test]
        public void DiagnosticUi_HasSafeAreaEventSystemAndReadOnlyStateSource()
        {
            Assert.IsNotNull(Object.FindFirstObjectByType<SafeAreaFitter>(FindObjectsInactive.Include));
            Assert.IsNotNull(Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include));

            var panel = Object.FindFirstObjectByType<PetStatusPanel>(FindObjectsInactive.Include);
            Assert.IsNotNull(panel);
            var so = new SerializedObject(panel);
            Assert.IsNotNull(so.FindProperty("stateSourceBehaviour").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("trackingText").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("instructionText").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("hungerText").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("happinessText").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("energyText").objectReferenceValue);
        }

        [Test]
        public void MainScene_IsTheEnabledBuildScene()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.AreEqual(1, scenes.Length);
            Assert.IsTrue(scenes[0].enabled);
            Assert.AreEqual(MainScenePath, scenes[0].path);
        }
    }
}
