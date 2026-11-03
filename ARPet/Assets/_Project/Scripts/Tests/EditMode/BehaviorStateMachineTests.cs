using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 行为状态机与开心值的关键规则测试。
    /// 覆盖范围刻意收窄到"错了会直接导致结算或存档出问题"的部分：
    /// 默认行为回落、动画就绪白名单、事件去重、每帧结算不重复、存档恢复。
    ///
    /// 食物卡投喂已删除：Feed → Eating 的事件链、投喂停留/冷却/离开锁等判定随
    /// <c>FeedingRule</c> 一并移除，本文件只保留仍然成立的性质。
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

        private static InteractionEvent PetEvent(int id, float time = 0f, string petId = "pet_classic") =>
            InteractionEvent.Create(id, InteractionEventKind.Pet, petId, time);

        [Test]
        public void Start_EntersDefaultBehavior()
        {
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
            Assert.IsTrue(_presenter.IsPlaying(PetBehaviorId.Idle));
        }

        [Test]
        public void OnlyBehaviorsInAnimatorStates_AreSelectable()
        {
            // P3 起以 AnimatorStates 能力清单为唯一判据：Idle / TouchReact / 专属演出在清单里，
            // DozeLoop / WalkLoop / Eat 仍未接线，不得进入允许集合。
            CollectionAssert.Contains(_machine.AllowedBehaviors, PetBehaviorId.Idle);
            CollectionAssert.Contains(_machine.AllowedBehaviors, PetBehaviorId.Petted);
            CollectionAssert.Contains(_machine.AllowedBehaviors, PetBehaviorId.Exclusive);

            CollectionAssert.DoesNotContain(_machine.AllowedBehaviors, PetBehaviorId.Resting);
            CollectionAssert.DoesNotContain(_machine.AllowedBehaviors, PetBehaviorId.Chasing);
            CollectionAssert.DoesNotContain(_machine.AllowedBehaviors, PetBehaviorId.Eating);
        }

        [Test]
        public void PetEvent_IsAccepted_AndScoresOnceAccordingToGesture()
        {
            var happiness = _needs.Snapshot.Happiness;

            Assert.IsTrue(_machine.TrySubmit(PetEvent(9)));

            Assert.AreEqual(PetBehaviorId.Petted, _machine.Current);
            Assert.AreEqual(1, _machine.AcceptedCount);
            Assert.AreEqual(happiness + 0.06f, _needs.Snapshot.Happiness, 0.0001f,
                "单击加分来自变体调参表，且只在接受事件的那一处结算一次");
        }

        [Test]
        public void RepeatedOrStaleEventIds_AreNeverAcceptedTwice()
        {
            Assert.IsTrue(_machine.TrySubmit(PetEvent(1)));
            Assert.AreEqual(0, _machine.RefusedCount);
            Assert.AreEqual(1, _machine.AcceptedCount);

            Assert.IsFalse(_machine.TrySubmit(PetEvent(1)), "同一事件 Id 再次到达必须被拒绝");
            Assert.AreEqual(1, _machine.RefusedCount);

            Assert.IsFalse(_machine.TrySubmit(PetEvent(0)), "陈旧的事件 Id 必须被丢弃");
            Assert.AreEqual(2, _machine.RefusedCount);

            Assert.AreEqual(1, _machine.AcceptedCount);

            // 新 Id 仍然可以被接受（去重没有把整条流锁死）。
            _machine.Tick(0.8f);
            Assert.IsTrue(_machine.TrySubmit(PetEvent(2)));
            Assert.AreEqual(2, _machine.AcceptedCount);
        }

        [TestCase(PetBehaviorId.Eating)]
        [TestCase(PetBehaviorId.Resting)]
        public void ConfiguredDefault_CannotBypassInteractionOrAnimationReadiness(PetBehaviorId configured)
        {
            _config.DefaultBehavior = configured;
            _machine = new BehaviorStateMachine(_config, _needs, _presenter);
            var happiness = _needs.Snapshot.Happiness;
            _machine.Start();
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
            _machine.Tick(10f);
            Assert.AreEqual(happiness, _needs.Snapshot.Happiness, 0.0001f,
                "不能通过默认行为获得无交互的自动结算");
        }

        [Test]
        public void RepeatedStart_DoesNotDiscardRunningBehavior()
        {
            _machine.Tick(1f);
            _machine.Start();
            Assert.AreEqual(PetBehaviorId.Idle, _machine.Current);
            Assert.AreEqual(1f, _machine.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void SameEventId_FromDifferentPets_AreTrackedAsSeparateStreams()
        {
            // 去重键是 (PetId, Kind)：两只宠物的同 Id 事件各占一条流，不会互相吞并。
            Assert.IsTrue(_machine.TrySubmit(PetEvent(5, petId: "pet_classic")));

            // 单次行为执行中：新事件被拒（不排队），因此先让触屏反应播完。
            _machine.Tick(0.8f);
            Assert.IsTrue(_machine.TrySubmit(PetEvent(5, petId: "pet_calm")));

            var consumed = (System.Collections.IDictionary)Field(_machine, "_lastConsumedIds");
            Assert.AreEqual(2, consumed.Count, "两只宠物的同名事件必须各占一条事件流");
            Assert.IsTrue(consumed.Contains((InteractionEventKind.Pet, "pet_classic")));
            Assert.IsTrue(consumed.Contains((InteractionEventKind.Pet, "pet_calm")));

            // 同一只宠物的同/更小 Id 仍然必须去重（语义与旧版一致）。
            var refusedBefore = _machine.RefusedCount;
            Assert.IsFalse(_machine.TrySubmit(PetEvent(5, petId: "pet_classic")));
            Assert.AreEqual(refusedBefore + 1, _machine.RefusedCount);
            Assert.AreEqual(2, consumed.Count, "重复事件不得新增事件流");
        }

        private static object Field(object value, string name) => value.GetType()
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(value);

        [Test]
        public void PerSecondTable_DoesNotRepeatOneTimeAdjustments()
        {
            // Idle 是当前唯一允许的行为：用它的 PerSecond 表验证"每秒项只在 Tick 中累计，
            // 进入时的一次性项不会每帧重放"。
            for (var i = 0; i < _config.Behaviors.Length; i++)
            {
                if (_config.Behaviors[i].Behavior != PetBehaviorId.Idle) continue;
                _config.Behaviors[i].OnEnter = new[]
                {
                    new NeedAdjust { Need = PetNeed.Happiness, OnEnter = 0.10f },
                };
                _config.Behaviors[i].PerSecond = new[]
                {
                    new NeedAdjust { Need = PetNeed.Happiness, PerSecond = -0.01f, OnEnter = 0.5f },
                };
            }
            _machine = new BehaviorStateMachine(_config, _needs, _presenter);
            _machine.Start();

            var afterEnter = _needs.Snapshot.Happiness;
            Assert.AreEqual(0.70f, afterEnter, 0.0001f, "进入时只结算一次 OnEnter");

            for (var i = 0; i < 20; i++) _machine.Tick(0.1f);

            // 0.60 + 0.10 − 0.01 × 2.0 s = 0.68；若每帧重放 OnEnter 会远高于此。
            Assert.AreEqual(0.68f, _needs.Snapshot.Happiness, 0.0001f);
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

    /// <summary>
    /// 开心值（新版唯一运行数值）与存档计数的结构测试：
    /// 异常输入必须回落到可用状态，而不是抛异常或写出 NaN。
    /// 原属 FeedingRuleTests.cs 的 PetNeedsTests，投喂相关用例已删除，其余随新版单值模型改写。
    /// </summary>
    public sealed class PetNeedsTests
    {
        private PetConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<PetConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void Apply_ClampsHappinessToUnitRange()
        {
            var needs = new PetNeeds(_config);

            needs.Apply(PetNeed.Happiness, 5f, 0f);
            Assert.AreEqual(1f, needs.Get(PetNeed.Happiness), 0.0001f);

            needs.Apply(PetNeed.Happiness, -5f, 0f);
            Assert.AreEqual(0f, needs.Get(PetNeed.Happiness), 0.0001f);
        }

        [Test]
        public void Apply_IgnoresLegacyHungerAndEnergy()
        {
            var needs = new PetNeeds(_config);
            var before = needs.Snapshot;

            Assert.IsFalse(needs.Apply(PetNeed.Hunger, 0.3f, 1f));
            Assert.IsFalse(needs.Apply(PetNeed.Energy, -0.3f, 1f));

            Assert.IsFalse(needs.Dirty, "旧版需要不得触发存档写入");
            Assert.AreEqual(0.60f, needs.Get(PetNeed.Happiness), 0.0001f);
            Assert.AreEqual(before.Hunger, needs.Snapshot.Hunger, 0.0001f);
            Assert.AreEqual(before.Energy, needs.Snapshot.Energy, 0.0001f);
        }

        [Test]
        public void RestoreFrom_SanitisesNanAndOutOfRangeHappiness()
        {
            var needs = new PetNeeds(_config);

            needs.RestoreFrom(new PetState
            {
                Hunger = 0.12f,
                Happiness = 42f,
                Energy = 0.9f,
            });

            Assert.AreEqual(1f, needs.Get(PetNeed.Happiness), 0.0001f, "越界开心值必须被夹到 0..1");
            Assert.AreEqual(0.12f, needs.Snapshot.Hunger, 0.0001f, "旧版饥饿值原样保留，供 v1 迁移");
            Assert.AreEqual(0.9f, needs.Snapshot.Energy, 0.0001f, "旧版精力值原样保留，供 v1 迁移");

            var neutral = new PetNeeds(_config);
            neutral.RestoreFrom(new PetState { Happiness = float.NaN });
            Assert.IsFalse(float.IsNaN(neutral.Get(PetNeed.Happiness)), "NaN 必须被替换为可用值");
            Assert.AreEqual(0.60f, neutral.Get(PetNeed.Happiness), 0.0001f, "NaN 回落到配置初始值");
        }

        [Test]
        public void OfflineDecay_IsCappedByConfig()
        {
            _config.MaxOfflineSeconds = 60f;
            // 2026-10-27 起运行默认不衰减（DecayPerSecond = 0）；本用例验证的是"补算上限"，
            // 因此显式给一个非零衰减。
            _config.NeedSeeds[0].DecayPerSecond = -0.0015f;
            var needs = new PetNeeds(_config);

            var happinessBefore = needs.Get(PetNeed.Happiness);
            // 离线一年，但只能按 60 秒补算。
            needs.ApplyOfflineDecay(365f * 24f * 3600f, 0f);
            var happinessAfter = needs.Get(PetNeed.Happiness);

            var expectedDecrease = _config.NeedSeeds[0].DecayPerSecond * 60f;
            Assert.AreEqual(happinessBefore + expectedDecrease, happinessAfter, 0.001f);
        }

        [Test]
        public void DirtyFlag_IsClearedAfterFlush()
        {
            var needs = new PetNeeds(_config);
            Assert.IsFalse(needs.Dirty);

            needs.Apply(PetNeed.Happiness, 0.1f, 0f);
            Assert.IsTrue(needs.Dirty);

            needs.ClearDirty();
            Assert.IsFalse(needs.Dirty);
        }

        [TestCase(30)]
        [TestCase(60)]
        public void MinuteOfSmallFrameDeltas_AccumulatesHappiness(int fps)
        {
            // 衰减默认已关闭；本用例验证"小步长累计不丢增量"，因此显式给一个非零衰减。
            _config.NeedSeeds[0].DecayPerSecond = -0.0015f;
            var needs = new PetNeeds(_config);
            for (var frame = 0; frame < fps * 60; frame++)
                needs.ApplyDecay(1f / fps, (frame + 1f) / fps);
            Assert.AreEqual(0.51f, needs.Snapshot.Happiness, 0.001f);
            Assert.IsTrue(needs.Dirty);
        }

        [Test]
        public void TinyDelta_IsRetainedAndMarksDirty()
        {
            var needs = new PetNeeds(_config);
            var before = needs.Get(PetNeed.Happiness);
            Assert.IsTrue(needs.Apply(PetNeed.Happiness, 0.000058f, 1f));
            Assert.AreEqual(before + 0.000058f, needs.Snapshot.Happiness, 0.000001f);
            Assert.IsTrue(needs.Dirty);
        }

        [Test]
        public void OfflineDecay_KeepsDirectionAndRemainsDirty()
        {
            _config.NeedSeeds[0].DecayPerSecond = -0.0015f;   // 显式开启衰减，验证方向与脏标记
            var needs = new PetNeeds(_config);
            needs.ApplyOfflineDecay(60f, 0f);
            Assert.AreEqual(0.51f, needs.Snapshot.Happiness, 0.0001f);
            Assert.IsTrue(needs.Dirty);
        }

        [Test]
        public void RestoreAndUpdate_PreservesAllInteractionCounts()
        {
            var needs = new PetNeeds(_config);
            var saved = PetState.CreateDefault();
            saved.FeedCount = 8;   // 旧版投喂次数：只保留，不再自增
            saved.PetCount = 7;
            saved.PlayCount = 6;
            saved.PhotoCount = 5;
            needs.RestoreFrom(saved);
            needs.ApplyDecay(1f, 1f);
            needs.IncrementCount(InteractionEventKind.Pet);
            Assert.AreEqual(8, needs.Snapshot.FeedCount, "旧版投喂次数必须原样保留");
            Assert.AreEqual(8, needs.Snapshot.PetCount);
            Assert.AreEqual(6, needs.Snapshot.PlayCount);
            Assert.AreEqual(5, needs.Snapshot.PhotoCount);
        }
    }
}
