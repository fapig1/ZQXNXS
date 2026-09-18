using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.App;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// <see cref="TouchInputBridge"/> 的命中判定测试。覆盖两条路径：
    /// 有碰撞体时走 <c>Collider.Raycast</c>，没有碰撞体时走"到锚点的球形近似"。
    /// 不模拟真实 Input 事件，只验证 <c>HitsPet</c> 这段纯几何判定。
    /// </summary>
    public sealed class TouchInputBridgeTests
    {
        private Camera _camera;
        private GameObject _cameraGo;
        private GameObject _bridgeGo;
        private TouchInputBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("TestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _cameraGo.transform.position = new Vector3(0f, 0f, -1f);
            _cameraGo.transform.LookAt(Vector3.zero);

            _bridgeGo = new GameObject("Bridge");
            _bridge = _bridgeGo.AddComponent<TouchInputBridge>();

            var so = new UnityEditor.SerializedObject(_bridge);
            so.FindProperty("raycastCamera").objectReferenceValue = _camera;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_bridgeGo);
            Object.DestroyImmediate(_cameraGo);
        }

        private Vector2 ScreenCenter() => new Vector2(_camera.pixelWidth / 2f, _camera.pixelHeight / 2f);

        [Test]
        public void NoColliderNoOrigin_NeverHits()
        {
            Assert.IsFalse(_bridge.HitsPet(ScreenCenter()));
        }

        [Test]
        public void RadiusApproximation_HitsWhenRayPassesThroughOrigin()
        {
            var originGo = new GameObject("PetOrigin");
            originGo.transform.position = Vector3.zero;

            var so = new UnityEditor.SerializedObject(_bridge);
            so.FindProperty("hitRadiusOrigin").objectReferenceValue = originGo.transform;
            so.FindProperty("hitRadiusMeters").floatValue = 0.12f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsTrue(_bridge.HitsPet(ScreenCenter()), "屏幕中心的射线应该正好穿过锚点");

            Object.DestroyImmediate(originGo);
        }

        [Test]
        public void RadiusApproximation_MissesWhenPointerFarFromOrigin()
        {
            var originGo = new GameObject("PetOrigin");
            originGo.transform.position = Vector3.zero;

            var so = new UnityEditor.SerializedObject(_bridge);
            so.FindProperty("hitRadiusOrigin").objectReferenceValue = originGo.transform;
            so.FindProperty("hitRadiusMeters").floatValue = 0.05f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 屏幕角落的射线方向会明显偏离锚点，超出命中半径。
            Assert.IsFalse(_bridge.HitsPet(Vector2.zero), "屏幕角落的点击不应命中原点附近的角色");

            Object.DestroyImmediate(originGo);
        }

        [Test]
        public void RadiusApproximation_MissesWhenOriginBehindCamera()
        {
            var originGo = new GameObject("PetOrigin");
            // 把锚点放到摄像机背后：射线方向上的投影应为负值。
            originGo.transform.position = new Vector3(0f, 0f, -2f);

            var so = new UnityEditor.SerializedObject(_bridge);
            so.FindProperty("hitRadiusOrigin").objectReferenceValue = originGo.transform;
            so.FindProperty("hitRadiusMeters").floatValue = 0.12f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsFalse(_bridge.HitsPet(ScreenCenter()), "角色在摄像机背后时不应被命中");

            Object.DestroyImmediate(originGo);
        }

        [Test]
        public void ColliderPresent_TakesPriorityOverRadiusApproximation()
        {
            var colliderGo = new GameObject("PetCollider");
            colliderGo.transform.position = Vector3.zero;
            var collider = colliderGo.AddComponent<SphereCollider>();
            collider.radius = 0.1f;

            var missOriginGo = new GameObject("FarOrigin");
            missOriginGo.transform.position = new Vector3(5f, 5f, 5f);

            var so = new UnityEditor.SerializedObject(_bridge);
            so.FindProperty("petHitCollider").objectReferenceValue = collider;
            // 即使锚点近似会判定为不命中（离得很远），有碰撞体时也应该只看碰撞体。
            so.FindProperty("hitRadiusOrigin").objectReferenceValue = missOriginGo.transform;
            so.FindProperty("hitRadiusMeters").floatValue = 0.01f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsTrue(_bridge.HitsPet(ScreenCenter()), "挂了碰撞体时应该以碰撞体判定为准");

            Object.DestroyImmediate(colliderGo);
            Object.DestroyImmediate(missOriginGo);
        }

        [Test]
        public void NoCameraAssigned_AndNoMainCamera_ReturnsFalseWithoutThrowing()
        {
            var so = new UnityEditor.SerializedObject(_bridge);
            so.FindProperty("raycastCamera").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 编辑模式下场景里没有打了 MainCamera 标签的相机，Camera.main 应为 null。
            Assert.DoesNotThrow(() => _bridge.HitsPet(Vector2.zero));
        }
    }
}
