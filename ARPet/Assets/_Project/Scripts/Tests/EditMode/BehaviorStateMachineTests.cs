using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 行为状态机与投喂规则的关键规则测试。
    /// 覆盖范围刻意收窄到"错了会直接导致游戏经济或结算出问题"的部分：
    /// 重复结算、优先级、阈值边界、存档恢复。不为文案或低影响改动补测试。
    /// </summary>
    public sealed class BehaviorStateMachineTests
    {
        private PetConfig _config;
        private PetNeeds _needs;
        private FakePresenter _presenter;
        private BehaviorStateMachine _machine;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PetConfig>();
            _needs = new PetNeeds(_config);
            _presenter = new FakePresenter();
            _machine = new BehaviorStateMachine(_config, _needs, _presenter);
            _machine.Start();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        private static InteractionEvent FeedEvent(int id, float time = 0f) =>
            InteractionEvent.Create(id, InteractionEventKind.Feed, MarkerSlot.Food, time);

        [Test]
        public void Start_EntersDefaultBehavior()
        {
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
            Assert.IsTrue(_presenter.IsPlaying(PetBehaviorId.Idle));
        }

        [Test]
        public void Feed_IsAccepted_AndEntersEating()
        {
            var accepted = _machine.TrySubmit(FeedEvent(1));

            Assert.IsTrue(accepted);
            Assert.AreEqual(PetBehaviorId.Eating, _machine.Current);
        }

        [Test]
        public void SameEventId_IsRejected_SoOneApproachSettlesOnce()
        {
            Assert.IsTrue(_machine.TrySubmit(FeedEvent(1)));
            // 模拟"跟踪恢复后又提交了一次同一个事件"。
            Assert.IsFalse(_machine.TrySubmit(FeedEvent(1)));

            Assert.AreEqual(1, _machine.AcceptedCount);
        }

        [Test]
        public void StaleEventId_IsRejected()
        {
            _machine.TrySubmit(FeedEvent(5));
            Assert.IsFalse(_machine.TrySubmit(FeedEvent(3)));
        }

        [Test]
        public void Eating_IsNotInterruptedByAnotherFeed()
        {
            _machine.TrySubmit(FeedEvent(1));
            _machine.Tick(1f);
            var happiness = _needs.Get(PetNeed.Happiness);
            Assert.AreEqual(PetBehaviorId.Eating, _machine.Current);

            // 进食是单次行为，未播完之前不接受新的投喂。
            Assert.IsFalse(_machine.TrySubmit(FeedEvent(2)));
            Assert.AreEqual(PetBehaviorId.Eating, _machine.Current);
            Assert.AreEqual(1f, _machine.ElapsedSeconds, 0.0001f);
            Assert.AreEqual(happiness, _needs.Get(PetNeed.Happiness), 0.0001f);
            _machine.Tick(2.2f);
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
            Assert.IsFalse(_machine.TrySubmit(FeedEvent(2)), "忙碌时拒绝的事件也不能恢复后重放");
            Assert.IsTrue(_machine.TrySubmit(FeedEvent(3)));
        }

        [Test]
        public void Eating_SettlesHungerOnce_ThenReturnsToIdle()
        {
            var hungerBefore = _needs.Get(PetNeed.Hunger);

            _machine.TrySubmit(FeedEvent(1));
            var hungerAfterEnter = _needs.Get(PetNeed.Hunger);

            // 推进超过 Eat 的 3.2 s。
            for (var i = 0; i < 40; i++) _machine.Tick(0.1f);

            var hungerAfterComplete = _needs.Get(PetNeed.Hunger);

            Assert.AreEqual(hungerBefore, hungerAfterEnter, 0.0001f, "进入进食时不结算饥饿度");
            Assert.Less(hungerAfterComplete, hungerAfterEnter, "完成时饥饿度应下降一次");

            // 再推进 3.2 s 不应该继续下降（不能重复结算）。
            for (var i = 0; i < 40; i++) _machine.Tick(0.1f);
            Assert.AreEqual(hungerAfterComplete, _needs.Get(PetNeed.Hunger), 0.0001f);

            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
        }

        [Test]
        public void Interrupt_DoesNotApplySettledDelta()
        {
            _machine.TrySubmit(FeedEvent(1));
            var hungerMid = _needs.Get(PetNeed.Hunger);

            _machine.Interrupt("测试打断");

            // OnInterrupted 为空表，因此饥饿度不应因为打断而下降。
            Assert.AreEqual(hungerMid, _needs.Get(PetNeed.Hunger), 0.0001f);
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
        }

        [Test]
        public void UnanimatedBehaviors_AreNotSelectable()
        {
            // TouchReact / DozeLoop / WalkLoop 尚未制作动画，必须不在允许集合里。
            CollectionAssert.DoesNotContain(_machine.AllowedBehaviors, PetBehaviorId.Petted);
            CollectionAssert.DoesNotContain(_machine.AllowedBehaviors, PetBehaviorId.Resting);
            CollectionAssert.DoesNotContain(_machine.AllowedBehaviors, PetBehaviorId.Chasing);

            CollectionAssert.Contains(_machine.AllowedBehaviors, PetBehaviorId.Idle);
            CollectionAssert.Contains(_machine.AllowedBehaviors, PetBehaviorId.Eating);
        }

        [Test]
        public void PetEvent_IsRefused_WhenAnimationIsMissing()
        {
            var petEvent = InteractionEvent.Create(9, InteractionEventKind.Pet, MarkerSlot.Den, 0f);
            Assert.IsFalse(_machine.TrySubmit(petEvent));
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
        }

        [Test]
        public void RejectedTouch_DoesNotBlockSubsequentFeedSequence()
        {
            var touch = new TouchPokeRule(new TouchPokeRule.TouchPokeSettings());
            touch.PointerDown(Vector2.zero, 0f, false);
            Assert.IsTrue(touch.PointerUp(Vector2.zero, 0.1f, true, true, 0.1f, out var tap));
            Assert.IsFalse(_machine.TrySubmit(tap));
            Assert.IsTrue(_machine.TrySubmit(FeedEvent(1)));
            _machine.Tick(3.2f);
            Assert.IsTrue(_machine.TrySubmit(FeedEvent(2)));
        }

        [Test]
        public void EventIds_AreScopedToKindAndSource()
        {
            Assert.IsFalse(_machine.TrySubmit(InteractionEvent.Create(1, InteractionEventKind.Pet, MarkerSlot.Den, 0f)));
            Assert.IsTrue(_machine.TrySubmit(FeedEvent(1)));
            _machine.Tick(3.2f);
            Assert.IsFalse(_machine.TrySubmit(FeedEvent(1)), "已完成的事件也不得再次结算");
        }

        [Test]
        public void Presenter_ReceivesEatAnimationName()
        {
            _machine.TrySubmit(FeedEvent(1));
            Assert.AreEqual("Eat", _presenter.LastAnimationName);
        }

        [TestCase("CustomEat", "CustomEat")]
        [TestCase("", "Eating")]
        public void Presenter_ReceivesResolvedConfigAnimationName(string configured, string expected)
        {
            for (var i = 0; i < _config.Behaviors.Length; i++)
            {
                if (_config.Behaviors[i].Behavior == PetBehaviorId.Eating)
                    _config.Behaviors[i].AnimationName = configured;
            }
            _machine = new BehaviorStateMachine(_config, _needs, _presenter);
            _machine.Start();
            _machine.TrySubmit(FeedEvent(1));
            Assert.AreEqual(expected, _presenter.LastAnimationName);
        }

        [TestCase(PetBehaviorId.Eating)]
        [TestCase(PetBehaviorId.Resting)]
        public void ConfiguredDefault_CannotBypassInteractionOrAnimationReadiness(PetBehaviorId configured)
        {
            _config.DefaultBehavior = configured;
            _machine = new BehaviorStateMachine(_config, _needs, _presenter);
            var hunger = _needs.Snapshot.Hunger;
            _machine.Start();
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
            _machine.Tick(10f);
            Assert.AreEqual(hunger, _needs.Snapshot.Hunger, 0.0001f, "不能通过默认行为无投喂自动进食");
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidOneShotDuration_CannotStartAnUnfinishableEat(float duration)
        {
            _config.Behaviors[1].DurationSeconds = duration;
            _machine = new BehaviorStateMachine(_config, _needs, _presenter);
            _machine.Start();
            Assert.IsFalse(_machine.TrySubmit(FeedEvent(1)));
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
        }

        [Test]
        public void RepeatedStart_DoesNotDiscardRunningEat()
        {
            _machine.TrySubmit(FeedEvent(1));
            _machine.Tick(1f);
            _machine.Start();
            Assert.AreEqual(PetBehaviorId.Eating, _machine.Current);
            Assert.AreEqual(1f, _machine.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void PerSecondTable_DoesNotRepeatOneTimeAdjustments()
        {
            _config.Behaviors[1].PerSecond = new[]
            {
                new NeedAdjust { Need = PetNeed.Energy, PerSecond = -0.01f, OnEnter = 0.5f },
            };
            _machine = new BehaviorStateMachine(_config, _needs, _presenter);
            _machine.Start();
            _machine.TrySubmit(FeedEvent(1));
            for (var i = 0; i < 20; i++) _machine.Tick(0.1f);
            Assert.AreEqual(0.83f, _needs.Snapshot.Energy, 0.0001f);
        }

        [Test]
        public void OneShotPerSecondEffects_StopAtCompletionEvenWithLargeDelta()
        {
            _config.Behaviors[1].PerSecond = new[]
            {
                new NeedAdjust { Need = PetNeed.Energy, PerSecond = -0.1f },
            };
            _machine = new BehaviorStateMachine(_config, _needs, _presenter);
            _machine.Start();
            _machine.TrySubmit(FeedEvent(1));
            _machine.Tick(10f);
            Assert.AreEqual(0.53f, _needs.Snapshot.Energy, 0.0001f);
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
        }

        private sealed class FakePresenter : IPetPresenter
        {
            private readonly List<PetBehaviorId> _played = new();

            public bool IsPlaying(PetBehaviorId behavior) => _played.Contains(behavior);

            public IReadOnlyList<PetBehaviorId> Played => _played;

            public PetExpression LastExpression { get; private set; } = PetExpression.Neutral;

            public string LastAnimationName { get; private set; }

            public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds)
            {
                LastAnimationName = animationName;
                _played.Add(behavior);
            }

            public void SetExpression(PetExpression expression, float intensity) => LastExpression = expression;

            public void PlaySfx(string sfxId) { }

            public void PlayFx(string fxId) { }

            public void SetPlaybackSpeed(float speed) { }

            public void SetInteractable(bool interactable) { }

            public void SetTrackingVisibility(bool visible) { }
        }
    }
}
