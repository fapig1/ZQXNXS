using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZQXNXS.ARPet.App;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Presentation;
using ZQXNXS.ARPet.UI;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 02_Main 的四色多宠接线：四张角色卡各挂一只对应颜色的宠物。
    /// 场景由菜单 5 / 6（或批处理入口 <c>ProjectSetup.RebuildMainSceneForBatch</c>）生成。
    /// 这些检查只证明序列化接线正确，不代表真机识别已通过。
    /// </summary>
    public sealed class MainSceneWiringTests
    {
        private const string MainScenePath = "Assets/_Project/Scenes/02_Main.unity";
        private const string XmlPath = "Assets/StreamingAssets/Vuforia/ZQXS.xml";
        private const string DataSetPath = "Vuforia/ZQXS.xml";

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
        public void EveryCatalogTarget_IsRootImageTargetWithDatabaseSize()
        {
            var sizes = LoadXmlSizes();
            foreach (var identity in PetIdentityCatalog.Default)
            {
                Assert.IsTrue(sizes.ContainsKey(identity.TargetId), $"ZQXS.xml 缺少 {identity.TargetId}");
                var size = sizes[identity.TargetId];

                var go = GameObject.Find("Target_" + identity.TargetId);
                Assert.IsNotNull(go, $"02_Main 缺少 Target_{identity.TargetId}");
                Assert.IsNull(go.transform.parent, "四张卡必须平级放在场景根，嵌套会让 Vuforia 改回默认宽高");

                Assert.AreEqual(identity.TargetId, Prop(go, "mTrackableName").stringValue);
                Assert.AreEqual(DataSetPath, Prop(go, "mDataSetPath").stringValue,
                    $"{go.name} 必须用 StreamingAssets 下的相对路径，写裸数据库名会让 Android 加载失败");
                Assert.IsTrue(Prop(go, "mInitializedInEditor").boolValue,
                    $"{go.name} 必须将 mInitializedInEditor 持久化为 true，否则 Vuforia 不会创建运行时观察者");
                Assert.IsFalse(Prop(go, "mTrackingOptimizationNeedsUpgrade").boolValue,
                    $"{go.name} 的 mTrackingOptimizationNeedsUpgrade 必须为 false");
                Assert.AreEqual(size.x, Prop(go, "mWidth").floatValue, 1e-5f, identity.TargetId + " 宽度");
                Assert.AreEqual(size.y, Prop(go, "mHeight").floatValue, 1e-5f, identity.TargetId + " 高度");
            }
        }

        [Test]
        public void Scene_HasNoLegacyDenFoodTargetsOrObserverHandlers()
        {
            // 前置条件：场景确实已按菜单 5 接成四卡（否则"找不到旧目标"会变成假通过）。
            Assert.IsNotNull(GameObject.Find("Target_card_classic"), "02_Main 尚未接四色多宠，先运行菜单 5");

            Assert.IsNull(GameObject.Find("DenCardTarget"), "旧单宠的 DenCardTarget 必须从 02_Main 删除");
            Assert.IsNull(GameObject.Find("FoodCardTarget"), "食物玩法已删除，FoodCardTarget 必须从 02_Main 删除");

            // 显隐只由 PetPresenter 按逐宠跟踪状态控制；示例程序的 DefaultObserverEventHandler
            // 会按自己的规则开关内容，四宠同屏下会与表现层打架。它所在程序集本工程不引用，按类型名匹配。
            var handlers = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(component => component != null && component.GetType().Name == "DefaultObserverEventHandler")
                .Select(component => component.gameObject.name)
                .ToArray();
            Assert.AreEqual(0, handlers.Length, "02_Main 不应保留 DefaultObserverEventHandler：" + string.Join("、", handlers));
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
            Assert.IsFalse(so.FindProperty("autoCreateStubSource").boolValue,
                "AR 主场景绝不能自动回退到 StubTrackingSource");

            var identities = PetIdentityCatalog.Default;
            var sizes = LoadXmlSizes();

            // 跟踪源必须用 targets 数组按稳定 TargetId 登记四张卡。
            var source = appRoot.GetComponent<VuforiaTrackingSource>();
            Assert.IsNotNull(source, "02_Main 的 VuforiaTrackingSource 必须挂在 App 物体上");
            var targets = new SerializedObject(source).FindProperty("targets");
            Assert.IsNotNull(targets, "VuforiaTrackingSource 必须用 targets 数组登记目标");
            Assert.AreEqual(identities.Count, targets.arraySize, "四张角色卡都要登记");
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                var entry = targets.GetArrayElementAtIndex(i);
                Assert.AreEqual(identity.TargetId, entry.FindPropertyRelative("targetId").stringValue);
                Assert.AreSame(GameObject.Find("Target_" + identity.TargetId),
                    entry.FindPropertyRelative("targetObject").objectReferenceValue,
                    $"{identity.TargetId} 必须指向 Target_{identity.TargetId}");
                var size = entry.FindPropertyRelative("size").vector3Value;
                Assert.AreEqual(sizes[identity.TargetId].x, size.x, 1e-5f, identity.TargetId + " 跟踪尺寸宽度");
                Assert.AreEqual(0f, size.y, 1e-6f, "尺寸的 y 分量留 0");
                Assert.AreEqual(sizes[identity.TargetId].y, size.z, 1e-5f, identity.TargetId + " 跟踪尺寸高度");
            }

            // 四条宠物绑定：petId 与身份目录一一对应，presenter 挂在对应卡下。
            var pets = so.FindProperty("pets");
            Assert.IsNotNull(pets, "AppRoot 必须用 pets 数组装配宠物（单宠过渡字段已删除）");
            Assert.AreEqual(identities.Count, pets.arraySize, "02_Main 必须绑定四只宠物");
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                var binding = pets.GetArrayElementAtIndex(i);
                Assert.AreEqual(identity.PetId, binding.FindPropertyRelative("petId").stringValue);
                var presenter = binding.FindPropertyRelative("presenter").objectReferenceValue as PetPresenter;
                Assert.IsNotNull(presenter, identity.PetId + " 必须配置 PetPresenter");
                Assert.AreSame(GameObject.Find("Target_" + identity.TargetId).transform,
                    presenter.transform.parent, $"{identity.PetId} 的表现层必须挂在自己的卡下");
            }

            var bridge = appRoot.GetComponent<TouchInputBridge>();
            Assert.IsNotNull(bridge);
            var bridgeSo = new SerializedObject(bridge);
            Assert.IsNotNull(bridgeSo.FindProperty("raycastCamera").objectReferenceValue);
        }

        [Test]
        public void Pets_AreAnchoredToOwnCards_WithExportCoordinateOrientation()
        {
            foreach (var identity in PetIdentityCatalog.Default)
            {
                var card = GameObject.Find("Target_" + identity.TargetId);
                Assert.IsNotNull(card, $"02_Main 缺少 Target_{identity.TargetId}");

                var petRoot = card.transform.Find("PetRoot_" + identity.PetId);
                Assert.IsNotNull(petRoot, $"PetRoot_{identity.PetId} 必须是 Target_{identity.TargetId} 的直接子物体");
                Assert.Less(petRoot.localPosition.sqrMagnitude, 1e-10f);
                Assert.Less(Quaternion.Angle(petRoot.localRotation, Quaternion.identity), 0.001f,
                    "不应额外旋转：Blender 导出与 ImageTarget 局部坐标都使用 +Y 向上、+Z 向前");

                var presenter = petRoot.GetComponent<PetPresenter>();
                Assert.IsNotNull(presenter, identity.PetId + " 的宠物根必须挂 PetPresenter");
                var presenterSo = new SerializedObject(presenter);
                Assert.IsNotNull(presenterSo.FindProperty("animator").objectReferenceValue,
                    identity.PetId + " 的 PetPresenter.animator 必须指向 Pet01 实例");
                Assert.IsNotNull(presenterSo.FindProperty("visualRoot").objectReferenceValue,
                    identity.PetId + " 的 PetPresenter.visualRoot 必须指向 Pet01 实例（跟踪丢失时只隐藏渲染）");

                // 触屏命中按 petId 路由到这只宠物。
                var hitTarget = petRoot.GetComponent<PetHitTarget>();
                Assert.IsNotNull(hitTarget, "宠物根必须挂 PetHitTarget 才能被触屏点中");
                Assert.AreEqual(identity.PetId, hitTarget.PetId);
                Assert.AreEqual(0.08f, hitTarget.HitRadius, 1e-4f, "近似胶囊半径 0.08 m");
                Assert.AreEqual(0.2f, hitTarget.HitHeight, 1e-4f, "近似胶囊从脚底到头顶，高度等于奶蛙身高 0.2 m");

                var animator = petRoot.GetComponentInChildren<Animator>(true);
                var renderer = petRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.IsNotNull(animator, identity.PetId + " 缺少 Animator");
                Assert.IsNotNull(animator.runtimeAnimatorController);
                Assert.AreEqual("Pet01Controller", animator.runtimeAnimatorController.name);
                Assert.IsNotNull(renderer, identity.PetId + " 缺少 SkinnedMeshRenderer");

                var variant = identity.Variant.ToString().ToLowerInvariant();
                Assert.AreEqual(3, renderer.sharedMaterials.Length);
                Assert.That(renderer.sharedMaterials.Select(m => m != null ? m.name : null),
                    Is.EqualTo(new[] { "Pet01_Skin_" + variant, "Pet01_Oral_" + variant, "Pet01_Eyes_" + variant }),
                    identity.PetId + " 的材质必须是自己的颜色变体");

                // P5-B：二维嘴眼贴片挂在本宠 Head 骨骼下，默认隐藏，形象与卡一致。
                Assert.AreEqual((int)identity.Variant, presenterSo.FindProperty("variant").intValue,
                    identity.PetId + " 的 PetPresenter.variant 必须与卡的形象一致");
                foreach (var field in new[] { "eyesDecal", "mouthDecal" })
                {
                    var decal = presenterSo.FindProperty(field).objectReferenceValue as Renderer;
                    Assert.IsNotNull(decal, $"{identity.PetId} 的 PetPresenter.{field} 未接线");
                    Assert.AreEqual("Head", decal.transform.parent.name, field + " 必须挂在 Head 骨骼下随头部动作");
                    Assert.IsTrue(decal.transform.IsChildOf(petRoot), field + " 不能挂到别的宠物身上");
                    Assert.IsFalse(decal.enabled, field + " 默认隐藏，由 SetExpression 打开");
                    Assert.IsNotNull(decal.sharedMaterial);
                    Assert.AreEqual("ZQXNXS/FaceDecal", decal.sharedMaterial.shader.name);
                }

                // 对位：眼贴片包围盒中心落在两眼骨中点附近，嘴贴片中心在嘴骨附近（容差 4mm，模型高 0.2m）。
                var eyeL = FindDeep(petRoot, "Eye.L");
                var eyeR = FindDeep(petRoot, "Eye.R");
                var mouthBone = FindDeep(petRoot, "MouthUpper");
                Assert.IsNotNull(eyeL);
                Assert.IsNotNull(eyeR);
                Assert.IsNotNull(mouthBone);
                // 在宠物根局部坐标里比较。实测脸朝局部 -Z（Blender +Y → Unity -Z），所以深度只比绝对值：
                // 贴片要比两眼中点更靠外，但不超过 2cm。
                var eyesMid = LocalPoint(petRoot, (eyeL.position + eyeR.position) * 0.5f);
                var eyesCentre = LocalPoint(petRoot, WorldCentre(presenterSo.FindProperty("eyesDecal").objectReferenceValue as Renderer));
                var mouthLocal = LocalPoint(petRoot, mouthBone.position);
                var mouthCentre = LocalPoint(petRoot, WorldCentre(presenterSo.FindProperty("mouthDecal").objectReferenceValue as Renderer));
                Assert.Less(Mathf.Abs(eyesCentre.x - eyesMid.x), 0.003f, $"{identity.PetId} 眼贴片左右偏移 {eyesCentre} vs {eyesMid}");
                Assert.Less(Mathf.Abs(eyesCentre.y - eyesMid.y), 0.006f, $"{identity.PetId} 眼贴片高度偏移 {eyesCentre} vs {eyesMid}");
                Assert.AreEqual(Mathf.Sign(eyesMid.z), Mathf.Sign(eyesCentre.z), identity.PetId + " 眼贴片与眼睛同在脸这一侧");
                Assert.Greater(Mathf.Abs(eyesCentre.z), Mathf.Abs(eyesMid.z) - 0.002f, identity.PetId + " 眼贴片必须在眼睛外侧");
                Assert.Less(Mathf.Abs(eyesCentre.z) - Mathf.Abs(eyesMid.z), 0.02f, identity.PetId + " 眼贴片应贴在眼前而不是飘远");

                // 着色器的脸前方必须与眼睛方向一致，否则视空间前移会把贴片推进脸里。
                var eyesDecal = presenterSo.FindProperty("eyesDecal").objectReferenceValue as Renderer;
                var forward = eyesDecal.sharedMaterial.GetVector("_FaceForward");
                var forwardWorld = eyesDecal.transform.rotation * new Vector3(forward.x, forward.y, forward.z);
                var forwardLocal = Quaternion.Inverse(petRoot.rotation) * forwardWorld;
                Assert.Greater(forwardLocal.z * Mathf.Sign(eyesMid.z), 0.9f,
                    $"{identity.PetId} 的 _FaceForward 必须指向脸外，实际 {forwardLocal}");
                Assert.Less(Mathf.Abs(mouthCentre.x - mouthLocal.x), 0.003f, identity.PetId + " 嘴贴片左右居中");
                Assert.Less(Mathf.Abs(mouthCentre.y - mouthLocal.y), 0.006f, identity.PetId + " 嘴贴片高度对齐嘴骨");

                // 2026-10-29：每只宠物一个 2D AudioSource，PlaySfx 才会真的出声。
                var audio = presenterSo.FindProperty("sfxSource").objectReferenceValue as AudioSource;
                Assert.IsNotNull(audio, identity.PetId + " 的 PetPresenter.sfxSource 未接线");
                Assert.IsFalse(audio.playOnAwake, "音效只由业务触发，不能启动就播");
                Assert.AreEqual(0f, audio.spatialBlend, 1e-5f, "桌面 AR 距离很近，用 2D 声音不做衰减");

                Assert.IsNotNull(presenterSo.FindProperty("faceLayout").objectReferenceValue);
                Assert.IsNotNull(presenterSo.FindProperty("eyesCoverMaterial").objectReferenceValue);
                Assert.IsNotNull(presenterSo.FindProperty("mouthCoverMaterial").objectReferenceValue);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>贴片网格顶点的世界坐标平均值（不依赖 Renderer.enabled，贴片默认隐藏）。</summary>
        private static Vector3 WorldCentre(Renderer renderer)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh, renderer.name + " 缺少网格");
            var vertices = mesh.vertices;
            Assert.Greater(vertices.Length, 0, renderer.name + " 网格为空");
            var sum = Vector3.zero;
            for (var i = 0; i < vertices.Length; i++) sum += vertices[i];
            var local = sum * (1f / vertices.Length);
            // 网格局部 → 世界：按父链逐级套局部变换（Transform.TransformPoint 的展开写法）。
            var point = local;
            for (var t = renderer.transform; t != null; t = t.parent)
            {
                point = t.localRotation * new Vector3(point.x * t.localScale.x, point.y * t.localScale.y, point.z * t.localScale.z)
                        + t.localPosition;
            }

            return point;
        }

        /// <summary>世界点 → <paramref name="root"/> 局部坐标（只用位置与旋转，宠物根缩放为 1）。</summary>
        private static Vector3 LocalPoint(Transform root, Vector3 world) =>
            Quaternion.Inverse(root.rotation) * (world - root.position);

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
            Assert.IsNotNull(so.FindProperty("behaviorText").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("happinessText").objectReferenceValue);
            // 多宠：逐行列出四只宠物的变体与跟踪状态。
            Assert.IsNotNull(so.FindProperty("petListText").objectReferenceValue, "多宠面板必须接好 petListText");
            // 选中宠物的开心条：Filled 类型才会按 fillAmount 显示填充。
            var happinessBar = so.FindProperty("happinessBar").objectReferenceValue as Image;
            Assert.IsNotNull(happinessBar, "多宠面板必须接好 happinessBar（选中宠物的开心值进度条）");
            Assert.AreEqual(Image.Type.Filled, happinessBar.type,
                "开心条必须是 Filled 类型，否则 fillAmount 写了也看不出来");
            Assert.AreEqual(Image.FillMethod.Horizontal, happinessBar.fillMethod, "开心条按水平方向填充");
            Assert.IsNotNull(happinessBar.sprite, "Filled 类型必须带 sprite，否则填充不显示");
            // 食物玩法已删除：面板不得再保留饥饿 / 精力字段与显示。
            Assert.IsNull(so.FindProperty("hungerText"), "PetStatusPanel 不应再有 hungerText 字段");
            Assert.IsNull(so.FindProperty("energyText"), "PetStatusPanel 不应再有 energyText 字段");

            // P6：真机帧率记录挂在 AppRoot 上（全局一份），面板显示最近窗口的 FPS。
            var appRoot = Object.FindFirstObjectByType<AppRoot>(FindObjectsInactive.Include);
            var recorders = Object.FindObjectsByType<ZQXNXS.ARPet.Platform.PerformanceRecorder>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(1, recorders.Length, "PerformanceRecorder 全局只能有一份");
            Assert.AreSame(appRoot.gameObject, recorders[0].gameObject, "PerformanceRecorder 挂在 AppRoot 上");
            Assert.IsNotNull(so.FindProperty("fpsText").objectReferenceValue, "面板必须接好 fpsText");
            Assert.AreSame(recorders[0], so.FindProperty("fpsSourceBehaviour").objectReferenceValue);

            // 2026-10-29 语音口令：全局一份识别器挂 AppRoot，"按住说话"按钮接收射线（不穿透成点宠物）。
            var listeners = Object.FindObjectsByType<ZQXNXS.ARPet.Platform.VoiceKeywordListener>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(1, listeners.Length, "VoiceKeywordListener 全局只能有一份");
            Assert.AreSame(appRoot.gameObject, listeners[0].gameObject);
            Assert.AreSame(listeners[0], new SerializedObject(appRoot).FindProperty("voiceSourceBehaviour").objectReferenceValue,
                "AppRoot.voiceSourceBehaviour 必须接到识别器");
            var voiceButton = Object.FindFirstObjectByType<VoiceHoldButton>(FindObjectsInactive.Include);
            Assert.IsNotNull(voiceButton, "缺少按住说话按钮");
            var buttonImage = voiceButton.GetComponent<Image>();
            Assert.IsNotNull(buttonImage);
            Assert.IsTrue(buttonImage.raycastTarget, "按钮必须接收射线，否则按住它会被当成点宠物");
            Assert.AreSame(listeners[0], new SerializedObject(voiceButton).FindProperty("sourceBehaviour").objectReferenceValue);
        }

        [Test]
        public void PortraitAndLandscape_AreBothSupported()
        {
            Assert.AreEqual(UIOrientation.AutoRotation, PlayerSettings.defaultInterfaceOrientation);
            Assert.IsTrue(PlayerSettings.allowedAutorotateToPortrait);
            Assert.IsTrue(PlayerSettings.allowedAutorotateToLandscapeLeft, "横屏才放得下两卡拉开 0.23m");
            Assert.IsTrue(PlayerSettings.allowedAutorotateToLandscapeRight);

            var panel = Object.FindFirstObjectByType<PetStatusPanel>(FindObjectsInactive.Include);
            var anchors = panel.GetComponent<OrientationAnchors>();
            Assert.IsNotNull(anchors, "状态面板需要横竖屏两套锚点");
            Assert.AreEqual(1f, anchors.Portrait.AnchorMax.x, 1e-4f, "竖屏铺满宽度");
            Assert.Less(anchors.Landscape.AnchorMax.x, 0.6f, "横屏只占左侧，不压住相机画面");

            var button = Object.FindFirstObjectByType<VoiceHoldButton>(FindObjectsInactive.Include);
            var rect = (RectTransform)button.transform;
            Assert.AreEqual(rect.anchorMin.x, rect.anchorMax.x, 1e-4f, "按钮固定宽度，横屏不拉成长条");
        }

        [Test]
        public void MainScene_IsTheEnabledBuildScene()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.AreEqual(1, scenes.Length);
            Assert.IsTrue(scenes[0].enabled);
            Assert.AreEqual(MainScenePath, scenes[0].path);
        }

        /// <summary>目标名 → 打印尺寸（米），唯一来源是 StreamingAssets 里的 Vuforia 数据库 XML。</summary>
        private static IDictionary<string, Vector2> LoadXmlSizes()
        {
            var document = new XmlDocument();
            document.Load(XmlPath);
            var result = new Dictionary<string, Vector2>();
            foreach (XmlElement node in document.SelectNodes("//ImageTarget"))
            {
                var parts = node.GetAttribute("size").Split(' ');
                result[node.GetAttribute("name")] = new Vector2(
                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture));
            }

            return result;
        }

        private static SerializedProperty Prop(GameObject go, string propertyName)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var property = new SerializedObject(component).FindProperty(propertyName);
                if (property != null) return property;
            }

            Assert.Fail($"{go.name} 缺少序列化字段 {propertyName}");
            return null;
        }
    }
}
