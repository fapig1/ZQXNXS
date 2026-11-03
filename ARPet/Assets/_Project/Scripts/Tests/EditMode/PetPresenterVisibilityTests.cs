using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 表现层多宠可见性开关的测试。跟踪丢失时只能切 <c>Renderer.enabled</c>，
    /// 不能 SetActive 本物体（那会让 Vuforia 的目标子树失效，也把 Animator 一起停掉）。
    /// </summary>
    public sealed class PetPresenterVisibilityTests
    {
        private GameObject _petGo;
        private GameObject _meshGo;
        private PetPresenter _presenter;
        private SkinnedMeshRenderer _renderer;
        private SphereCollider _collider;

        [SetUp]
        public void SetUp()
        {
            _petGo = new GameObject("Pet");
            _presenter = _petGo.AddComponent<PetPresenter>();

            _meshGo = new GameObject("PetMesh");
            _meshGo.transform.SetParent(_petGo.transform, worldPositionStays: false);
            _renderer = _meshGo.AddComponent<SkinnedMeshRenderer>();

            _collider = _petGo.AddComponent<SphereCollider>();
        }

        [TearDown]
        public void TearDown()
        {
            // 先销毁子物体再销毁父物体：Unity 下销毁父物体会连带子物体，反过来调用会重复销毁。
            Object.DestroyImmediate(_meshGo);
            Object.DestroyImmediate(_petGo);
        }

        [Test]
        public void SetTrackingVisibility_TogglesRenderers_WithoutDeactivatingTheObject()
        {
            Assert.IsTrue(_presenter.IsVisible, "默认应当可见");

            _presenter.SetTrackingVisibility(false);

            Assert.IsFalse(_presenter.IsVisible);
            Assert.IsFalse(_renderer.enabled, "隐藏必须落在 Renderer.enabled 上");
            Assert.IsTrue(_petGo.activeSelf, "不得通过 SetActive 隐藏：Vuforia 目标子树必须保留");

            _presenter.SetTrackingVisibility(true);

            Assert.IsTrue(_presenter.IsVisible);
            Assert.IsTrue(_renderer.enabled);
            Assert.IsTrue(_petGo.activeSelf);
        }

        [Test]
        public void SetInteractable_TogglesCollidersInSubtree()
        {
            Assert.IsTrue(_collider.enabled);

            _presenter.SetInteractable(false);
            Assert.IsFalse(_presenter.IsInteractable);
            Assert.IsFalse(_collider.enabled, "不可交互时碰撞体必须关闭，否则还能被点中");

            _presenter.SetInteractable(true);
            Assert.IsTrue(_presenter.IsInteractable);
            Assert.IsTrue(_collider.enabled);
        }
    }
}
