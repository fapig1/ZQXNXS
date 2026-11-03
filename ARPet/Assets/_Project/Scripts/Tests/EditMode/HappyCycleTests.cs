using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// P3 开心值满值周期（单击 / 双击加分、满值专属演出、冷却、重新武装、读档确认）的规则测试。
    /// 只用纯 C# 的 <see cref="PetRuntime"/> + 假表现层，不需要场景、Animator 或 AR 跟踪。
    ///
    /// 这些用例守护的是"错了会双重结算 / 重复播演出 / 白丢一次资格"的部分：
    /// 加分只在接受事件的那一处结算一次、满值演出原子开始、冷却只随行为时钟走、读档不自动开始。
    /// </summary>
    public sealed class HappyCycleTests
    {
        private readonly List<PetRuntime> _runtimes = new();
        private readonly Dictionary<string, RecordingPresenter> _presenters = new();

        private PetConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PetConfig>();
            _runtimes.Clear();
            _presenters.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _runtimes.Clear();
            _presenters.Clear();
            UnityEngine.Object.DestroyImmediate(_config);
        }

        // ── 单击 / 双击加分 ────────────────────────────────────────────────────

        [TestCase(PetVariantId.Classic, "pet_classic", 0.06f, 0.10f)]
        [TestCase(PetVariantId.Calm, "pet_calm", 0.04f, 0.07f)]
        [TestCase(PetVariantId.Rage, "pet_rage", 0.08f, 0.12f)]
        [TestCase(PetVariantId.Divine, "pet_divine", 0.06f, 0.10f)]
        public void TapGains_MatchVariantTuning_AndDoubleTapNeverAddsSingle(
            PetVariantId variant, string petId, float singleGain, float doubleGain)
        {
            var pet = Create(petId);
            pet.RestoreFrom(new PetState { Happiness = 0.30f });

            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1, PetGesture.SingleTap)));
            Assert.AreEqual(0.30f + singleGain, pet.Snapshot.Happiness, 0.0001f,
                $"{variant} 的单击加分应取调参表的 {singleGain}");

            // 触屏反应是单次行为：先让它播完，再做第二次点按。
            pet.Tick(0.8f);

            Assert.IsTrue(pet.TrySubmit(Tap(pet, 2, PetGesture.DoubleTap)));
            Assert.AreEqual(0.30f + singleGain + doubleGain, pet.Snapshot.Happiness, 0.0001f,
                "双击只结算双击值一次，不得再叠加一次单击");
        }

        [Test]
        public void TappingOnePet_DoesNotChangeTheOther()
        {
            var classic = Create("pet_classic");
            var calm = Create("pet_calm");
            classic.RestoreFrom(new PetState { Happiness = 0.30f });
            calm.RestoreFrom(new PetState { Happiness = 0.30f });

            Assert.IsTrue(classic.TrySubmit(Tap(classic, 1)));

            Assert.AreEqual(0.36f, classic.Snapshot.Happiness, 0.0001f);
            Assert.AreEqual(0.30f, calm.Snapshot.Happiness, 0.0001f, "点 classic 不得改 calm 的开心值");
            Assert.AreEqual(PetBehaviorId.Idle, calm.CurrentBehavior);
            Assert.AreEqual(0, calm.Snapshot.PetCount);
        }

        // ── 满值：原子开始专属演出 ──────────────────────────────────────────────

        [TestCase(PetVariantId.Classic, "pet_classic", 0.70f, 8f, "classic_proud", 2.0f)]
        [TestCase(PetVariantId.Calm, "pet_calm", 0.75f, 8f, "calm_zen", 2.5f)]
        [TestCase(PetVariantId.Rage, "pet_rage", 0.65f, 6f, "rage_roar", 2.0f)]
        [TestCase(PetVariantId.Divine, "pet_divine", 0.65f, 12f, "divine_awaken", 2.5f)]
        public void ReachingFullValue_StartsExclusiveAtomically_ThenCompletes(
            PetVariantId variant, string petId, float fallback, float cooldown, string stateName, float duration)
        {
            var pet = Create(petId);
            var presenter = PresenterOf(petId);
            pet.RestoreFrom(new PetState { Happiness = 0.99f });

            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)), "跨越满值的点按必须被接受");

            Assert.AreEqual(PetBehaviorId.Exclusive, pet.CurrentBehavior, "满值直接播正式专属片段");
            Assert.AreEqual(stateName, presenter.LastAnimationName, "专属动画名必须来自本变体的调参表");
            Assert.IsFalse(presenter.LastLoop, "专属演出是单次行为");
            Assert.AreEqual(fallback, pet.Snapshot.Happiness, 0.0001f, "开心值必须回落到本变体的回落值");
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount, "开始次数恰好加一");
            Assert.AreEqual(0, pet.Snapshot.ExclusiveWatchedCount, "还没播完，不算看完");
            Assert.AreEqual(cooldown, pet.Snapshot.ExclusiveCooldownRemaining, 0.0001f, "冷却从专属开始的这一刻计时");
            Assert.AreEqual(HappyCycleState.Armed, pet.Snapshot.HappyCycle, "回落值满足重新武装条件，立即转回 Armed");

            Assert.AreEqual(PetExpression.Signature, presenter.LastExpression, "专属演出用招牌表情");

            // 专属演出期间的新点击被拒，且不会排队等播完补发。
            Assert.IsFalse(pet.TrySubmit(Tap(pet, 2)));
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount);
            Assert.AreEqual(fallback, pet.Snapshot.Happiness, 0.0001f, "被拒绝的点按不得加分");
            Assert.AreEqual(PetExpression.Signature, presenter.LastExpression,
                "演出中被拒的点按不得把招牌表情换成拒绝表情（BUG-05）");

            pet.Tick(duration + 0.1f);

            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior, "到时长后回到默认行为");
            Assert.AreEqual(1, pet.Snapshot.ExclusiveWatchedCount, "正常播完才算看完");
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount, "不得因为播完再开一次");
        }

        // ── 冷却 ────────────────────────────────────────────────────────────────

        [Test]
        public void FullDuringCooldown_PendsOneAutoStart_ThenStartsOnceWhenCooldownEnds()
        {
            var pet = Create("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.99f });
            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)));
            pet.Tick(2.1f);   // 播完 → Idle，冷却剩 5.9

            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior);
            Assert.Greater(pet.Snapshot.ExclusiveCooldownRemaining, 0f);

            // 冷却内再次攒到 99%，这一次点按跨越满值 → 只挂起一条自动开始请求。
            pet.Needs.Apply(PetNeed.Happiness, 0.99f - pet.Snapshot.Happiness, 0f);
            Assert.IsTrue(pet.TrySubmit(Tap(pet, 2)));

            Assert.AreEqual(HappyCycleState.PendingAuto, pet.Snapshot.HappyCycle, "冷却内攒满只挂起一条请求");
            Assert.AreEqual(PetBehaviorId.Petted, pet.CurrentBehavior, "本次先播触屏反应");
            Assert.AreEqual(1f, pet.Snapshot.Happiness, 0.0001f);
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount);

            // 触屏反应播完，但冷却还没结束 → 不开始。
            pet.Tick(0.8f);
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior);
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount, "冷却未结束不得提前开始");

            // 冷却到点且处于默认行为 → 自动开始一次。
            pet.Tick(10f);
            Assert.AreEqual(PetBehaviorId.Exclusive, pet.CurrentBehavior, "冷却结束且回到 Idle 时自动开始专属演出");
            Assert.AreEqual(2, pet.Snapshot.ExclusiveStartCount, "自动开始只发生一次");
            Assert.AreEqual(0.70f, pet.Snapshot.Happiness, 0.0001f);
        }

        [Test]
        public void CooldownFreezesWhenBehaviorClockIsNotTicked()
        {
            var pet = Create("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.99f });
            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)));
            var cooldownAfterStart = pet.Snapshot.ExclusiveCooldownRemaining;
            Assert.AreEqual(8f, cooldownAfterStart, 0.0001f);

            // 模拟跟踪丢失：调用方不再推进行为时钟，只有真实时间流逝（衰减路径照常走）。
            pet.ApplyDecay(30f, 30f);

            Assert.AreEqual(cooldownAfterStart, pet.Snapshot.ExclusiveCooldownRemaining, 0.0001f,
                "冷却只随行为时钟递减，跟踪丢失时冻结");
        }

        [Test]
        public void TapsAtFullValue_NeverScoreAgainOrStartASecondExclusive()
        {
            var pet = Create("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.99f });
            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)));
            pet.Tick(2.1f);   // 播完 → Idle

            // 直接拉满（不经过"跨越"判定），模拟"已满值时继续点按"的分支。
            pet.Needs.Apply(PetNeed.Happiness, 1f - pet.Snapshot.Happiness, 0f);
            Assert.AreEqual(1f, pet.Snapshot.Happiness, 0.0001f);

            Assert.IsTrue(pet.TrySubmit(Tap(pet, 2)), "已满值时点按仍然被接受");
            Assert.AreEqual(PetBehaviorId.Petted, pet.CurrentBehavior);
            Assert.AreEqual(1f, pet.Snapshot.Happiness, 0.0001f, "已满值不再加分");
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount, "不再产生第二条开始请求");

            // 连点：执行中被拒，播完后点按也只播触屏反应。
            Assert.IsFalse(pet.TrySubmit(Tap(pet, 3)));
            pet.Tick(0.8f);
            Assert.IsTrue(pet.TrySubmit(Tap(pet, 4)));
            pet.Tick(0.8f);

            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount);
            Assert.AreEqual(1f, pet.Snapshot.Happiness, 0.0001f);
            Assert.AreEqual(HappyCycleState.Armed, pet.Snapshot.HappyCycle);
        }

        // ── 读档 / 确认 ─────────────────────────────────────────────────────────

        [Test]
        public void LoadAtFullValue_DoesNotAutoStart_FirstTapConfirms()
        {
            var pet = Create("pet_classic");
            // 旧 v2 条目没有新字段：HappyCycle 默认 0 = Armed，开心值已满。
            pet.RestoreFrom(new PetState { Happiness = 1f });

            Assert.AreEqual(HappyCycleState.PendingConfirm, pet.Snapshot.HappyCycle, "已满值读档改为待确认");
            Assert.AreEqual(0, pet.Snapshot.ExclusiveStartCount);

            pet.Tick(10f);
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior, "读档不因 100% 自动开始演出");
            Assert.AreEqual(0, pet.Snapshot.ExclusiveStartCount);

            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)));
            Assert.AreEqual(PetBehaviorId.Exclusive, pet.CurrentBehavior);
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount);
            Assert.AreEqual(0.70f, pet.Snapshot.Happiness, 0.0001f, "确认加点按不加分，直接演出并回落到回落值");
        }

        [Test]
        public void ConfirmWhileCooldownRuns_OnlyPlaysTouchReact_AndWaits()
        {
            var pet = Create("pet_classic");
            pet.RestoreFrom(new PetState
            {
                Happiness = 1f,
                HappyCycle = HappyCycleState.PendingConfirm,
                ExclusiveCooldownRemaining = 5f,
            });

            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)));
            Assert.AreEqual(PetBehaviorId.Petted, pet.CurrentBehavior, "冷却未结束时确认点按只播触屏反应");
            Assert.AreEqual(1f, pet.Snapshot.Happiness, 0.0001f, "确认点按不加分");
            Assert.AreEqual(0, pet.Snapshot.ExclusiveStartCount);
            Assert.AreEqual(HappyCycleState.PendingConfirm, pet.Snapshot.HappyCycle, "周期仍等待确认");

            pet.Tick(0.8f);
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior);
            pet.Tick(4.3f);
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior, "待确认不会自动开始，必须玩家点一下");
            Assert.AreEqual(0, pet.Snapshot.ExclusiveStartCount);

            Assert.IsTrue(pet.TrySubmit(Tap(pet, 2)));
            Assert.AreEqual(PetBehaviorId.Exclusive, pet.CurrentBehavior);
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount);
        }

        [Test]
        public void InterruptedExclusive_ConsumesEligibility_ButDoesNotCountAsWatched()
        {
            var pet = Create("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.99f });
            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)));
            Assert.AreEqual(PetBehaviorId.Exclusive, pet.CurrentBehavior);

            pet.Interrupt("应用切到后台");

            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior);
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount, "资格已消费，不返还");
            Assert.AreEqual(0, pet.Snapshot.ExclusiveWatchedCount, "被打断不计看完");
            Assert.AreEqual(0.70f, pet.Snapshot.Happiness, 0.0001f, "开心值不会补回 100%");

            pet.Tick(20f);
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior, "恢复后不得重复开始专属演出");
            Assert.AreEqual(1, pet.Snapshot.ExclusiveStartCount);
        }

        [Test]
        public void RestoreClampsCooldownAndRejectsUnknownCycleState()
        {
            var pet = Create("pet_classic");
            pet.RestoreFrom(new PetState
            {
                Happiness = 0.5f,
                HappyCycle = (HappyCycleState)99,
                ExclusiveCooldownRemaining = 999f,
                ExclusiveStartCount = -3,
                ExclusiveWatchedCount = -2,
            });

            Assert.AreEqual(HappyCycleState.Armed, pet.Snapshot.HappyCycle, "未知枚举值读成 Armed");
            Assert.AreEqual(8f, pet.Snapshot.ExclusiveCooldownRemaining, 0.0001f, "冷却上限为该变体的冷却秒数");
            Assert.AreEqual(0, pet.Snapshot.ExclusiveStartCount);
            Assert.AreEqual(0, pet.Snapshot.ExclusiveWatchedCount);
        }

        // ── 能力清单 ────────────────────────────────────────────────────────────

        [Test]
        public void MissingExclusiveState_DisablesOnlyExclusive()
        {
            _config.AnimatorStates = WithoutState(_config.AnimatorStates, "classic_proud");
            var pet = Create("pet_classic");

            CollectionAssert.DoesNotContain(pet.Behavior.AllowedBehaviors, PetBehaviorId.Exclusive);
            // 别的行为不受影响：触屏反应仍然可用。
            CollectionAssert.Contains(pet.Behavior.AllowedBehaviors, PetBehaviorId.Petted);

            pet.RestoreFrom(new PetState { Happiness = 0.99f });
            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)));

            Assert.AreEqual(PetBehaviorId.Petted, pet.CurrentBehavior);
            Assert.AreEqual(1f, pet.Snapshot.Happiness, 0.0001f, "专属不可用时满值只停在 100%");
            Assert.AreEqual(HappyCycleState.Armed, pet.Snapshot.HappyCycle, "不消费周期");
            Assert.AreEqual(0, pet.Snapshot.ExclusiveStartCount);
        }

        [Test]
        public void MissingTouchReact_DisablesOnlyPetted()
        {
            _config.AnimatorStates = WithoutState(_config.AnimatorStates, "TouchReact");
            var pet = Create("pet_classic");

            CollectionAssert.DoesNotContain(pet.Behavior.AllowedBehaviors, PetBehaviorId.Petted);
            // 别的行为不受影响：专属演出仍然可用。
            CollectionAssert.Contains(pet.Behavior.AllowedBehaviors, PetBehaviorId.Exclusive);

            pet.RestoreFrom(new PetState { Happiness = 0.30f });
            Assert.IsFalse(pet.TrySubmit(Tap(pet, 1)), "TouchReact 缺片段时普通点按被拒绝");
            Assert.AreEqual(0.30f, pet.Snapshot.Happiness, 0.0001f, "被拒绝的点按不得加分");
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior);
        }

        [Test]
        public void UnknownVariant_FallsBackToClassicTuning()
        {
            // 删掉 calm 的调参条目，模拟"表里缺该变体"：回退 classic 数值。
            _config.VariantTunings = OnlyClassic(_config.VariantTunings);
            var pet = Create("pet_calm");
            pet.RestoreFrom(new PetState { Happiness = 0.30f });

            Assert.AreEqual(PetVariantId.Calm, pet.HappyCycle.Variant);
            Assert.IsTrue(pet.TrySubmit(Tap(pet, 1)));
            Assert.AreEqual(0.36f, pet.Snapshot.Happiness, 0.0001f, "缺条目时按 classic 的 +0.06 结算");
        }

        // ── 衰减关闭 ────────────────────────────────────────────────────────────

        [Test]
        public void HappinessDoesNotDecay_OnlineOrOffline()
        {
            var needs = new PetNeeds(_config);
            var before = needs.Get(PetNeed.Happiness);

            for (var i = 0; i < 600; i++) needs.ApplyDecay(1f, i);
            needs.ApplyOfflineDecay(365f * 24f * 3600f, 0f);

            Assert.AreEqual(before, needs.Get(PetNeed.Happiness), 0.000001f, "在线与离线都不衰减");
            Assert.IsFalse(needs.Dirty, "没有变化就不该打存档脏标记");
        }

        // ── 辅助 ────────────────────────────────────────────────────────────────

        private PetRuntime Create(string petId)
        {
            var presenter = new RecordingPresenter();
            var runtime = new PetRuntime(IdentityOf(petId), _config, presenter);
            runtime.Start();
            _runtimes.Add(runtime);
            _presenters[petId] = presenter;
            return runtime;
        }

        private RecordingPresenter PresenterOf(string petId) => _presenters[petId];

        private static InteractionEvent Tap(PetRuntime pet, int id,
            PetGesture gesture = PetGesture.SingleTap) =>
            InteractionEvent.Create(id, InteractionEventKind.Pet, pet.PetId, 0f,
                localPoint: default, distance: 0f, gesture: gesture);

        private static string[] WithoutState(string[] states, string removed)
        {
            var kept = new List<string>();
            foreach (var state in states)
            {
                if (!string.Equals(state, removed, StringComparison.Ordinal)) kept.Add(state);
            }
            return kept.ToArray();
        }

        private static VariantTuning[] OnlyClassic(VariantTuning[] tunings)
        {
            var kept = new List<VariantTuning>();
            foreach (var tuning in tunings)
            {
                if (tuning.Variant == PetVariantId.Classic) kept.Add(tuning);
            }
            return kept.ToArray();
        }

        private static PetIdentity IdentityOf(string petId)
        {
            foreach (var identity in PetIdentityCatalog.Default)
            {
                if (identity.PetId == petId) return identity;
            }

            throw new InvalidOperationException("身份目录里没有 " + petId);
        }

        /// <summary>记录型假表现层：只记最近一次播放指令，不做渲染。</summary>
        private sealed class RecordingPresenter : IPetPresenter
        {
            public PetBehaviorId LastBehavior { get; private set; } = PetBehaviorId.None;

            public string LastAnimationName { get; private set; }

            public bool LastLoop { get; private set; }

            public PetExpression LastExpression { get; private set; } = PetExpression.Neutral;

            public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds)
            {
                LastBehavior = behavior;
                LastAnimationName = animationName;
                LastLoop = loop;
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
