using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 驱动一键生成工具产出的真实 Pet01Controller，走一遍 Idle → Eat → Idle → TouchReact → Idle，
    /// 而不只是断言字段不为空。覆盖的是"编辑器工具产出的 Animator Controller 资产
    /// 是否真的能配合 PetPresenter 播放"，这是此前 <c>PetPresenter.animator</c>
    /// 一直为空、动画从未真正播放过这一缺口的验收测试。
    /// <para>同时核对 <c>PetConfig</c> 的能力清单 / 变体调参表与 Controller 实际状态一致，
    /// 以及 TouchReact 片段是 0.7 s 单次动作（与行为表 Petted.DurationSeconds 对齐）。</para>
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

            // 能力清单与调参表必须和 Controller 实际状态一致：清单里有、Controller 里没有时，
            // 行为层会以为"可播"而实际什么都不播。
            // 这三组断言放在本用例内而不是新开用例，是因为整个 fixture 的 SetUp 需要真实的
            // Pet01Controller；离线替身没有资产数据库，单开会变成"只可能在离线失败的孤儿用例"。
            var config = AssetDatabase.LoadAssetAtPath<PetConfig>("Assets/_Project/Configs/PetBehaviorConfig.asset");
            Assert.IsNotNull(config, "缺少 PetBehaviorConfig.asset，请先运行“1. 生成默认配置与场景”");
            var stateNames = states.Select(value => value.state.name).ToList();

            foreach (var stateName in config.AnimatorStates)
            {
                Assert.That(stateNames.Contains(stateName), Is.True,
                    $"PetConfig.AnimatorStates 里的 {stateName} 在 Pet01Controller 第 0 层不存在");
            }

            foreach (var tuning in config.VariantTunings)
            {
                Assert.That(stateNames.Contains(tuning.ExclusiveStateName), Is.True,
                    $"{tuning.Variant} 的专属演出状态 {tuning.ExclusiveStateName} 在 Pet01Controller 里不存在");
            }

            // TouchReact 是 0.7 s 单次片段，时长要与行为表里 Petted.DurationSeconds 对齐。
            var touchReact = states.FirstOrDefault(value => value.state.name == "TouchReact").state;
            Assert.IsNotNull(touchReact, "Controller 缺少 TouchReact 状态，请运行“动画/接入四色专属动作”");
            var touchReactClip = touchReact.motion as AnimationClip;
            Assert.IsNotNull(touchReactClip, "TouchReact 状态没有挂 AnimationClip");
            Assert.IsFalse(touchReactClip.isLooping, "触屏反应是单次动作，不能循环");
            Assert.AreEqual(0.7f, touchReactClip.length, 0.06f, "TouchReact 时长应为 0.7 s（行为层按该时长结算）");
            Assert.That(touchReactClip.events, Is.Empty, "动画事件不结算开心值，片段里不应有结算用事件");
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
            // 旧 Eat 片段仍留在 Animator Controller 中（进食已不属于新版玩法），
            // 这里验证 PetPresenter 按状态名驱动真实状态切换：Idle → Eat → Idle → TouchReact → Idle。
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

            // 动触摸屏反应：PetConfig 的 Petted 行为指向 TouchReact，状态缺失会让每次点按都被拒绝。
            _presenter.PlayBehavior(PetBehaviorId.Petted, "TouchReact", loop: false, crossFadeSeconds: 0f);
            _animator.Update(0f);
            Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("TouchReact"),
                "单击 / 双击必须真的能播 TouchReact");

            _presenter.PlayBehavior(PetBehaviorId.Idle, "Idle", loop: true, crossFadeSeconds: 0f);
            _animator.Update(0f);
            Assert.IsTrue(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
                "触屏反应结束后必须能切回 Idle");
        }

        [Test]
        public void PlayBehavior_UnknownStateName_LogsWarning_AndDoesNotThrow()
        {
            // 用确实不存在的名字：TouchReact 自 P3 B 片起已接入 Controller，再拿它当"缺失状态"会变成假通过。
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("没有状态"));
            Assert.DoesNotThrow(() =>
                _presenter.PlayBehavior(PetBehaviorId.Petted, "__missing_state__", loop: false, crossFadeSeconds: 0f));
        }
    }
}
