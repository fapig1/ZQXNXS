using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>开发顺序第2步：四卡实测场景的序列化接线。不代表真机识别已通过。</summary>
    public sealed class FourCardTrackingSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/04_FourCardTracking.unity";
        private const string XmlPath = "Assets/StreamingAssets/Vuforia/ZQXS.xml";

        private string _previousScenePath;

        [SetUp]
        public void OpenScene()
        {
            _previousScenePath = EditorSceneManager.GetActiveScene().path;
            Assert.IsTrue(File.Exists(ScenePath), "缺少 04_FourCardTracking，运行菜单 8 生成");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [TearDown]
        public void RestorePreviousScene()
        {
            if (!string.IsNullOrEmpty(_previousScenePath) && _previousScenePath != ScenePath)
                EditorSceneManager.OpenScene(_previousScenePath, OpenSceneMode.Single);
        }

        [Test]
        public void EveryCatalogTarget_IsARootImageTargetWithDatabaseSize()
        {
            var xml = new XmlDocument();
            xml.Load(XmlPath);
            foreach (var identity in PetIdentityCatalog.Default)
            {
                var node = (XmlElement)xml.SelectSingleNode($"//ImageTarget[@name='{identity.TargetId}']");
                Assert.IsNotNull(node, $"ZQXS.xml 缺少 {identity.TargetId}");
                var size = node.GetAttribute("size").Split(' ').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();

                var go = GameObject.Find("Target_" + identity.TargetId);
                Assert.IsNotNull(go, $"场景缺少 Target_{identity.TargetId}");
                Assert.IsNull(go.transform.parent, "目标必须平级放在场景根，嵌套会让 Vuforia 改回默认宽高");

                Assert.AreEqual(identity.TargetId, Prop(go, "mTrackableName").stringValue);
                Assert.AreEqual("Vuforia/ZQXS.xml", Prop(go, "mDataSetPath").stringValue);
                Assert.IsTrue(Prop(go, "mInitializedInEditor").boolValue);
                Assert.IsFalse(Prop(go, "mTrackingOptimizationNeedsUpgrade").boolValue);
                Assert.AreEqual(size[0], Prop(go, "mWidth").floatValue, 1e-5f, identity.TargetId + " 宽度");
                Assert.AreEqual(size[1], Prop(go, "mHeight").floatValue, 1e-5f, identity.TargetId + " 高度");
            }
        }

        [Test]
        public void EachTarget_CarriesItsOwnVariantPet_HiddenUntilTracked()
        {
            var probe = Object.FindFirstObjectByType<TargetTrackingProbe>(FindObjectsInactive.Include);
            Assert.IsNotNull(probe);
            Assert.AreEqual(PetIdentityCatalog.Default.Count, probe.Entries.Count);
            for (var i = 0; i < probe.Entries.Count; i++)
            {
                var entry = probe.Entries[i];
                var identity = PetIdentityCatalog.Default[i];
                Assert.AreEqual("Target_" + identity.TargetId, entry.target.name);
                Assert.AreSame(entry.target.transform, entry.content.transform.parent, "角色子树必须挂在自己的卡下");

                var renderer = entry.content.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.IsNotNull(renderer);
                var variant = identity.Variant.ToString().ToLowerInvariant();
                Assert.That(renderer.sharedMaterials.Select(m => m.name),
                    Is.EqualTo(new[] { "Pet01_Skin_" + variant, "Pet01_Oral_" + variant, "Pet01_Eyes_" + variant }));
                var animator = entry.content.GetComponentInChildren<Animator>(true);
                Assert.IsNotNull(animator.runtimeAnimatorController);
            }
            Assert.AreEqual(PetIdentityCatalog.Default.Count,
                probe.Entries.Select(e => e.target).Distinct().Count(), "每张卡只能出现一次");
        }

        [Test]
        public void VuforiaConfig_AllowsFourSimultaneousImageTargets()
        {
            var text = File.ReadAllText("Assets/Resources/VuforiaConfiguration.asset");
            var line = text.Split('\n').Single(l => l.Trim().StartsWith("maxSimultaneousImageTargets:"));
            Assert.GreaterOrEqual(int.Parse(line.Split(':')[1].Trim(), CultureInfo.InvariantCulture), 4);
        }

        [Test]
        public void MainScene_IsStillTheDefaultBuildScene()
        {
            // 四卡 APK 通过显式场景列表构建，不应改写默认构建设置。
            Assert.AreEqual(1, EditorBuildSettings.scenes.Length);
            Assert.AreEqual("Assets/_Project/Scenes/02_Main.unity", EditorBuildSettings.scenes[0].path);
        }

        private static SerializedProperty Prop(GameObject go, string name)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var property = new SerializedObject(component).FindProperty(name);
                if (property != null) return property;
            }
            Assert.Fail($"{go.name} 缺少序列化字段 {name}");
            return null;
        }
    }
}
