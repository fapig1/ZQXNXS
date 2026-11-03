using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZQXNXS.ARPet.App;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// <see cref="TouchInputBridge"/> 的命中判定测试。多宠切片后桥接脚本不再只认一个碰撞体，
    /// 而是遍历所有启用的 <see cref="PetHitTarget"/> 并取沿射线最近的那一只，因此这里覆盖：
    /// 球形近似、碰撞体优先、禁用目标被跳过、重叠时取最近、以及"没有相机/没有目标"不抛异常。
    /// 不模拟真实 Input 事件，只验证 <c>HitPetIdAt</c> 这段纯几何判定。
    /// </summary>
    public sealed class TouchInputBridgeTests
    {
        private Camera _camera;
        private GameObject _cameraGo;
        private GameObject _bridgeGo;
        private TouchInputBridge _bridge;
        private readonly List<GameObject> _created = new();

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("TestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _cameraGo.transform.position = new Vector3(0f, 0f, -1f);
            _cameraGo.transform.LookAt(Vector3.zero);

            _bridgeGo = new GameObject("Bridge");
            _bridge = _bridgeGo.AddComponent<TouchInputBridge>();

            var so = new SerializedObject(_bridge);
            so.FindProperty("raycastCamera").objectReferenceValue = _camera;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_bridgeGo);
            Object.DestroyImmediate(_cameraGo);
            foreach (var go in _created) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private Vector2 ScreenCenter() => new Vector2(_camera.pixelWidth / 2f, _camera.pixelHeight / 2f);

        private PetHitTarget CreateTarget(string petId, Vector3 position, float radius, Collider collider = null,
            float height = 0.2f)
        {
            var go = new GameObject(petId + "Hit");
            go.transform.position = position;
            _created.Add(go);

            var target = go.AddComponent<PetHitTarget>();
            var so = new SerializedObject(target);
            so.FindProperty("petId").stringValue = petId;
            so.FindProperty("hitRadiusMeters").floatValue = radius;
            so.FindProperty("hitHeightMeters").floatValue = height;
            if (collider != null) so.FindProperty("hitCollider").objectReferenceValue = collider;
            so.ApplyModifiedPropertiesWithoutUndo();
            return target;
        }

        [Test]
        public void NoHitTarget_NeverHits()
        {
            Assert.IsFalse(_bridge.HitsPet(ScreenCenter()));
            Assert.IsNull(_bridge.HitPetIdAt(ScreenCenter()));
        }

        [Test]
        public void RadiusApproximation_ReturnsMatchingPetId()
        {
            CreateTarget("pet_classic", Vector3.zero, 0.12f);

            Assert.IsTrue(_bridge.HitsPet(ScreenCenter()), "屏幕中心的射线应该正好穿过锚点");
            Assert.AreEqual("pet_classic", _bridge.HitPetIdAt(ScreenCenter()));
        }

        [Test]
        public void RadiusApproximation_MissesWhenPointerFarFromOrigin()
        {
            CreateTarget("pet_classic", Vector3.zero, 0.05f);

            // 屏幕角落的射线方向会明显偏离锚点，超出命中半径。
            Assert.IsFalse(_bridge.HitsPet(Vector2.zero), "屏幕角落的点击不应命中原点附近的角色");
        }

        [Test]
        public void RadiusApproximation_MissesWhenOriginBehindCamera()
        {
            // 把锚点放到摄像机背后：射线方向上的投影应为负值。
            CreateTarget("pet_classic", new Vector3(0f, 0f, -2f), 0.12f);

            Assert.IsFalse(_bridge.HitsPet(ScreenCenter()), "角色在摄像机背后时不应被命中");
        }

        [Test]
        public void ColliderPresent_TakesPriorityOverRadiusApproximation()
        {
            var colliderGo = new GameObject("PetCollider");
            colliderGo.transform.position = Vector3.zero;
            _created.Add(colliderGo);
            var collider = colliderGo.AddComponent<SphereCollider>();
            collider.radius = 0.1f;

            // 半径配成 0.01 且球心在很远的地方：命中只可能来自碰撞体判定。
            CreateTarget("pet_classic", new Vector3(5f, 5f, 5f), 0.01f, collider);

            Assert.IsTrue(_bridge.HitsPet(ScreenCenter()), "挂了碰撞体时应该以碰撞体判定为准");
            Assert.AreEqual("pet_classic", _bridge.HitPetIdAt(ScreenCenter()));
        }

        [Test]
        public void DisabledHitTarget_IsSkipped()
        {
            var target = CreateTarget("pet_classic", Vector3.zero, 0.12f);
            target.enabled = false;

            Assert.IsFalse(_bridge.HitsPet(ScreenCenter()), "禁用的命中目标不应还能被点中");
        }

        [Test]
        public void NearestHitTarget_Wins()
        {
            // 两个球形近似目标都在屏幕中心射线上，距离更近的那只应该被选中。
            CreateTarget("pet_far", new Vector3(0f, 0f, 0.5f), 0.12f);
            CreateTarget("pet_near", Vector3.zero, 0.12f);

            Assert.AreEqual("pet_near", _bridge.HitPetIdAt(ScreenCenter()));
        }

        // ── 2026-10-30 真机 BUG-02：上半身点不到 ───────────────────────────────

        [TestCase(0.02f, TestName = "CapsuleApproximation_HitsFeet")]
        [TestCase(0.10f, TestName = "CapsuleApproximation_HitsBelly")]
        [TestCase(0.18f, TestName = "CapsuleApproximation_HitsHead")]
        public void CapsuleApproximation_CoversWholeBody(float heightOnBody)
        {
            // 宠物根在脚底（卡面原点），与 02_Main 一致：半径 0.08、高 0.2。
            CreateTarget("pet_classic", Vector3.zero, 0.08f);

            var screen = _camera.WorldToScreenPoint(new Vector3(0f, heightOnBody, 0f));
            Assert.AreEqual("pet_classic", _bridge.HitPetIdAt(screen),
                $"点在身体 {heightOnBody:F2}m 高处必须命中（旧的脚底球只覆盖到 0.08m）");
        }

        [Test]
        public void CapsuleApproximation_MissesAboveHeadAndBeside()
        {
            CreateTarget("pet_classic", Vector3.zero, 0.08f);

            Assert.IsNull(_bridge.HitPetIdAt(_camera.WorldToScreenPoint(new Vector3(0f, 0.32f, 0f))), "头顶上方 12cm 不算");
            Assert.IsNull(_bridge.HitPetIdAt(_camera.WorldToScreenPoint(new Vector3(0.15f, 0.10f, 0f))), "身侧 15cm 不算");
        }

        [Test]
        public void CapsuleApproximation_FollowsTiltedCard()
        {
            // 卡面绕 X 轴转 90°：局部 +Y（身高方向）变成世界 +Z，沿视线背离摄像机。
            // 整个胶囊投影到屏幕中心附近，原本"身高方向"的屏幕位置（世界 y=0.18）应该点不到。
            var target = CreateTarget("pet_classic", Vector3.zero, 0.05f);
            target.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            Assert.AreEqual("pet_classic", _bridge.HitPetIdAt(ScreenCenter()));
            Assert.IsNull(_bridge.HitPetIdAt(_camera.WorldToScreenPoint(new Vector3(0f, 0.18f, 0f))),
                "胶囊必须跟着卡面旋转，不能固定朝世界 +Y");
        }

        [Test]
        public void TwoPetsSideBySide_EachClickRoutesToNearestBody()
        {
            CreateTarget("pet_left", new Vector3(-0.09f, 0f, 0f), 0.08f);
            CreateTarget("pet_right", new Vector3(0.09f, 0f, 0f), 0.08f);

            Assert.AreEqual("pet_left", _bridge.HitPetIdAt(_camera.WorldToScreenPoint(new Vector3(-0.09f, 0.17f, 0f))));
            Assert.AreEqual("pet_right", _bridge.HitPetIdAt(_camera.WorldToScreenPoint(new Vector3(0.09f, 0.17f, 0f))));
        }

        [Test]
        public void NoCameraAssigned_AndNoMainCamera_ReturnsFalseWithoutThrowing()
        {
            CreateTarget("pet_classic", Vector3.zero, 0.12f);

            var so = new SerializedObject(_bridge);
            so.FindProperty("raycastCamera").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 编辑模式下场景里没有打了 MainCamera 标签的相机，Camera.main 应为 null。
            Assert.DoesNotThrow(() => _bridge.HitsPet(Vector2.zero));
            Assert.IsFalse(_bridge.HitsPet(Vector2.zero));
        }
    }
}
