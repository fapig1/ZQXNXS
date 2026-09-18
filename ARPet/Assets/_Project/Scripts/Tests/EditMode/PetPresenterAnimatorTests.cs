using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 驱动一键生成工具产出的真实 Pet01Controller，走一遍 Idle → Eat → Idle，
    /// 而不只是断言字段不为空。覆盖的是"编辑器工具产出的 Animator Controller 资产
    /// 是否真的能配合 PetPresenter 播放"，这是此前 <c>PetPresenter.animator</c>
    /// 一直为空、动画从未真正播放过这一缺口的验收测试。
    /// </summary>
    public sealed class PetPresenterAnimatorTests
    {
        private const string ControllerPath = "Assets/_Project/Animations/Pet01Controller.controller";

        private GameObject _go;
        private Animator _animator;
        private PetPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assume.That(controller, Is.Not.Null,
                $"未找到 {ControllerPath}，请先运行菜单“桌上有龙 / 3. 接入角色材质与 Animator”。");

            _go = new GameObject("PetPresenterAnimatorTest");
            _animator = _go.AddComponent<Animator>();
            _animator.runtimeAnimatorController = controller;
            _presenter = _go.AddComponent<PetPresenter>();

            var so = new SerializedObject(_presenter);
            so.FindProperty("animator").objectReferenceValue = _animator;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void Controller_HasIdleAndEatStates_WithCorrectLoopFlagsAndDurations()
        {
            var controller = (AnimatorController)_animator.runtimeAnimatorController;
            var states = controller.layers[0].stateMachine.states;

            var idleState = states.FirstOrDefault(s => s.state.name == "Idle").state;
            var eatState = states.FirstOrDefault(s => s.state.name == "Eat").state;

            Assert.IsNotNull(idleState, "Controller 缺少 Idle 状态");
            Assert.IsNotNull(eatState, "Controller 缺少 Eat 状态");

            var idleClip = idleState.motion as AnimationClip;
            var eatClip = eatState.motion as AnimationClip;

            Assert.IsNotNull(idleClip, "Idle 状态没有挂 AnimationClip");
            Assert.IsNotNull(eatClip, "Eat 状态没有挂 AnimationClip");

            Assert.IsTrue(idleClip.isLooping, "Idle 片段必须循环（否则待机会播完就定住）");
            Assert.IsFalse(eatClip.isLooping, "Eat 片段是单次动作，不应循环");

            Assert.AreEqual(3.0f, idleClip.length, 0.05f, "Idle 时长应为 3.0s");
            Assert.AreEqual(3.2f, eatClip.length, 0.05f, "Eat 时长应为 3.2s");
        }

        [Test]
        public void PlayBehavior_Idle_ActuallyEntersIdleState()
        {
            _presenter.PlayBehavior(PetBehaviorId.Idle, "Idle", loop: true, crossFadeSeconds: 0f);
            _animator.Update(0f);

            Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
                "PlayBehavior(Idle) 之后 Animator 应当真的处于 Idle 状态");
        }

        [Test]
        public void PlayBehavior_EatThenIdle_DrivesRealStateTransitions()
        {
            // 模拟行为层的真实调用顺序：启动进入 Idle → 投喂触发 Eating → 结算完毕回落 Idle。
            _presenter.PlayBehavior(PetBehaviorId.Idle, "Idle", loop: true, crossFadeSeconds: 0f);
            _animator.Update(0f);
            Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));

            _presenter.PlayBehavior(PetBehaviorId.Eating, "Eat", loop: false, crossFadeSeconds: 0f);
            _animator.Update(0f);
            Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("Eat"),
                "行为层切到 Eating 后，Animator 必须真的进入 Eat 状态" +
                "（PetPresenter 用 CrossFadeInFixedTime 按状态名直接驱动，不依赖 Controller 里的过渡边）");

            _presenter.PlayBehavior(PetBehaviorId.Idle, "Idle", loop: true, crossFadeSeconds: 0f);
            _animator.Update(0f);
            Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
                "进食结算完成后行为层会切回 Idle，Animator 必须真的回到 Idle 状态，不能停留在 Eat");
        }

        [Test]
        public void PlayBehavior_UnknownStateName_LogsWarning_AndDoesNotThrow()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("没有状态"));
            Assert.DoesNotThrow(() =>
                _presenter.PlayBehavior(PetBehaviorId.Petted, "TouchReact", loop: false, crossFadeSeconds: 0f));
        }
    }
}
