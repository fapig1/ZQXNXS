using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 投喂判定测试。这是项目里规则最密、最容易出重复结算 bug 的地方，
    /// 因此边界条件（停留时长、冷却、离开后再次进入、位姿过期）逐个覆盖。
    /// </summary>
    public sealed class FeedingRuleTests
    {
        private const float Stale = PoseFreshness.DefaultStaleSeconds;

        private PetConfigEatSettings _settings;
        private FeedingRule _rule;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _settings = new PetConfigEatSettings
            {
                MaxHorizontalDistance = 0.10f,
                MaxHeightDelta = 0.06f,
                RequiredDwellSeconds = 0.5f,
                CooldownSeconds = 4f,
                ReArmDistance = 0.16f,
            };

            _rule = new FeedingRule(_settings);
            _now = 100f;
        }

        private PoseSample Sample(MarkerSlot marker, float time) => new PoseSample
        {
            Marker = marker,
            Status = TrackingStatus.Tracked,
            Position = marker == MarkerSlot.Den ? Vector3.zero : new Vector3(0.05f, 0f, 0f),
            Rotation = Quaternion.identity,
            SampleTime = time,
        };

        private bool Step(Vector3 foodLocal, float dt = 0.1f, TrackingStatus denStatus = TrackingStatus.Tracked,
            TrackingStatus foodStatus = TrackingStatus.Tracked)
        {
            _now += dt;

            var den = Sample(MarkerSlot.Den, _now);
            den.Status = denStatus;

            var food = Sample(MarkerSlot.Food, _now);
            food.Status = foodStatus;

            return _rule.Evaluate(den, food, _now, dt, foodLocal, Stale, out _);
        }

        [Test]
        public void InRange_ForRequiredDwell_ProducesExactlyOneEvent()
        {
            var produced = 0;
            // 停留 1.0 s（10 步 × 0.1 s），超过 0.5 s 的要求。
            for (var i = 0; i < 10; i++)
            {
                if (Step(new Vector3(0.04f, 0f, 0.02f))) produced++;
            }

            Assert.AreEqual(1, produced, "一次进入只应产生一个投喂事件");
            Assert.AreEqual(1, _rule.IssuedEventCount);
        }

        [Test]
        public void StayingInZone_DoesNotProduceSecondEvent_EvenAfterCooldown()
        {
            for (var i = 0; i < 10; i++) Step(new Vector3(0.04f, 0f, 0.02f));

            // 继续放 10 秒，冷却早已结束，但没有离开判定区。
            var producedAfter = 0;
            for (var i = 0; i < 100; i++)
            {
                if (Step(new Vector3(0.04f, 0f, 0.02f))) producedAfter++;
            }

            Assert.AreEqual(0, producedAfter, "未离开判定区不得再次结算");
        }

        [Test]
        public void LeavingAndReturning_ProducesSecondEvent()
        {
            for (var i = 0; i < 10; i++) Step(new Vector3(0.04f, 0f, 0.02f));

            // 移开（超过 ReArmDistance 0.16 m）。
            for (var i = 0; i < 5; i++) Step(new Vector3(0.30f, 0f, 0f));

            // 冷却 4 s。
            for (var i = 0; i < 45; i++) Step(new Vector3(0.30f, 0f, 0f));

            // 重新放入。
            var produced = 0;
            for (var i = 0; i < 10; i++)
            {
                if (Step(new Vector3(0.04f, 0f, 0.02f))) produced++;
            }

            Assert.AreEqual(1, produced, "离开后再次进入应产生新事件");
            Assert.AreEqual(2, _rule.IssuedEventCount);
        }

        [Test]
        public void TooFarHorizontally_ProducesNothing()
        {
            for (var i = 0; i < 20; i++) Assert.IsFalse(Step(new Vector3(0.25f, 0f, 0f)));
            Assert.AreEqual(0, _rule.IssuedEventCount);
        }

        [Test]
        public void TooHigh_ProducesNothing()
        {
            // 水平距离为 0，但高度差 0.20 m 超过 0.06 m 上限。
            for (var i = 0; i < 20; i++) Assert.IsFalse(Step(new Vector3(0f, 0.20f, 0f)));
            Assert.AreEqual(0, _rule.IssuedEventCount);
        }

        [Test]
        public void FoodCardLost_ResetsDwellTimer()
        {
            // 先累计 0.4 s，差一点到 0.5 s。
            for (var i = 0; i < 4; i++) Step(new Vector3(0.04f, 0f, 0.02f));

            // 丢失一帧，停留计时必须清零。
            Step(new Vector3(0.04f, 0f, 0.02f), foodStatus: TrackingStatus.Lost);

            // 再停留 0.4 s 仍然不到 0.5 s，不应产生事件。
            var produced = 0;
            for (var i = 0; i < 4; i++)
            {
                if (Step(new Vector3(0.04f, 0f, 0.02f))) produced++;
            }

            Assert.AreEqual(0, produced, "位姿中断后停留计时必须重新开始");
        }

        [Test]
        public void StalePose_BlocksSettlement()
        {
            // 参考点位置正常，但采样时间非常旧。
            var oldDen = Sample(MarkerSlot.Den, _now - 5f);
            var oldFood = Sample(MarkerSlot.Food, _now - 5f);

            var ok = _rule.Evaluate(oldDen, oldFood, _now, 0.1f, new Vector3(0.04f, 0f, 0.02f), Stale, out _);

            Assert.IsFalse(ok, "过期位姿不得用于结算");
            StringAssert.Contains("未同时有效", _rule.LastReason);
        }

        [TestCase(MarkerSlot.Den)]
        [TestCase(MarkerSlot.Food)]
        public void LostCard_DoesNotRearmSettledPlacement(MarkerSlot lost)
        {
            var point = new Vector3(0.04f, 0f, 0.02f);
            for (var i = 0; i < 10; i++) Step(point);
            for (var i = 0; i < 60; i++)
                Step(point, denStatus: lost == MarkerSlot.Den ? TrackingStatus.Lost : TrackingStatus.Tracked,
                    foodStatus: lost == MarkerSlot.Food ? TrackingStatus.Lost : TrackingStatus.Tracked);
            for (var i = 0; i < 20; i++) Assert.IsFalse(Step(point));
            Assert.AreEqual(1, _rule.IssuedEventCount, "遮挡恢复不能证明卡片离开过");

            Step(new Vector3(0.3f, 0f, 0f));
            for (var i = 0; i < 10; i++) Step(point);
            Assert.AreEqual(2, _rule.IssuedEventCount, "有效地移开再进入才可以重新投喂");
        }

        [Test]
        public void DenCardLost_RequiresFullDwellAgain()
        {
            var point = new Vector3(0.04f, 0f, 0.02f);
            for (var i = 0; i < 4; i++) Assert.IsFalse(Step(point));
            Assert.IsFalse(Step(point, denStatus: TrackingStatus.Lost));
            for (var i = 0; i < 4; i++) Assert.IsFalse(Step(point));
            Assert.IsTrue(Step(point));
        }

        [TestCase(TrackingStatus.ExtendedTracked)]
        [TestCase(TrackingStatus.Limited)]
        [TestCase(TrackingStatus.Paused)]
        public void NonDirectTracking_NeitherCompletesDwellNorRearms(TrackingStatus status)
        {
            var point = new Vector3(0.04f, 0f, 0.02f);
            for (var i = 0; i < 4; i++) Step(point);
            for (var i = 0; i < 50; i++) Assert.IsFalse(Step(point, foodStatus: status));
            for (var i = 0; i < 4; i++) Assert.IsFalse(Step(point));
            Assert.IsTrue(Step(point));
            for (var i = 0; i < 50; i++) Step(point, foodStatus: status);
            for (var i = 0; i < 10; i++) Assert.IsFalse(Step(point));
            Assert.AreEqual(1, _rule.IssuedEventCount);
        }

        [Test]
        public void SuspendDwell_PreservesExitLock()
        {
            var point = new Vector3(0.04f, 0f, 0.02f);
            for (var i = 0; i < 4; i++) Step(point);
            _rule.SuspendDwell();
            for (var i = 0; i < 4; i++) Assert.IsFalse(Step(point));
            Assert.IsTrue(Step(point));
            _rule.SuspendDwell();
            for (var i = 0; i < 60; i++) Assert.IsFalse(Step(point));
        }

        [Test]
        public void StaleFarPose_DoesNotProveExit()
        {
            var point = new Vector3(0.04f, 0f, 0.02f);
            for (var i = 0; i < 10; i++) Step(point);
            var oldDen = Sample(MarkerSlot.Den, _now - 5f);
            var oldFood = Sample(MarkerSlot.Food, _now - 5f);
            Assert.IsFalse(_rule.Evaluate(oldDen, oldFood, _now, 5f, new Vector3(0.3f, 0f, 0f), Stale, out _));
            for (var i = 0; i < 60; i++) Assert.IsFalse(Step(point));
        }

        [Test]
        public void ExactDistanceAndHeightLimits_AreAccepted()
        {
            for (var i = 0; i < 10; i++) Step(new Vector3(0.10f, 0.06f, 0f));
            Assert.AreEqual(1, _rule.IssuedEventCount);
        }

        [Test]
        public void ExactRearmDistance_DoesNotUnlockUntilExceeded()
        {
            for (var i = 0; i < 10; i++) Step(Vector3.zero);
            for (var i = 0; i < 50; i++) Step(new Vector3(_settings.ReArmDistance, 0f, 0f));
            for (var i = 0; i < 10; i++) Assert.IsFalse(Step(Vector3.zero));
            Step(new Vector3(_settings.ReArmDistance + 0.001f, 0f, 0f));
            for (var i = 0; i < 10; i++) Step(Vector3.zero);
            Assert.AreEqual(2, _rule.IssuedEventCount);
        }
    }

    /// <summary>
    /// 状态数值与存档的结构测试：异常输入必须回落到可用状态，而不是抛异常或写出 NaN。
    /// </summary>
    public sealed class PetNeedsTests
    {
        private PetConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<PetConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void Apply_ClampsToUnitRange()
        {
            var needs = new PetNeeds(_config);

            needs.Apply(PetNeed.Hunger, 5f, 0f);
            Assert.AreEqual(1f, needs.Get(PetNeed.Hunger), 0.0001f);

            needs.Apply(PetNeed.Hunger, -5f, 0f);
            Assert.AreEqual(0f, needs.Get(PetNeed.Hunger), 0.0001f);
        }

        [Test]
        public void RestoreFrom_SanitisesNanAndOutOfRangeValues()
        {
            var needs = new PetNeeds(_config);

            needs.RestoreFrom(new PetState
            {
                Hunger = float.NaN,
                Happiness = 42f,
                Energy = -7f,
            });

            Assert.IsFalse(float.IsNaN(needs.Get(PetNeed.Hunger)), "NaN 必须被替换为可用值");
            Assert.AreEqual(1f, needs.Get(PetNeed.Happiness), 0.0001f);
            Assert.AreEqual(0f, needs.Get(PetNeed.Energy), 0.0001f);
        }

        [Test]
        public void OfflineDecay_IsCappedByConfig()
        {
            _config.MaxOfflineSeconds = 60f;
            var needs = new PetNeeds(_config);

            var hungerBefore = needs.Get(PetNeed.Hunger);
            // 离线一年，但只能按 60 秒补算。
            needs.ApplyOfflineDecay(365f * 24f * 3600f, 0f);
            var hungerAfter = needs.Get(PetNeed.Hunger);

            var expectedIncrease = _config.NeedSeeds[0].DecayPerSecond * 60f;
            Assert.AreEqual(hungerBefore + expectedIncrease, hungerAfter, 0.001f);
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
        public void MinuteOfSmallFrameDeltas_AccumulatesInAllNeeds(int fps)
        {
            var needs = new PetNeeds(_config);
            for (var frame = 0; frame < fps * 60; frame++)
                needs.ApplyDecay(1f / fps, (frame + 1f) / fps);
            Assert.AreEqual(0.51f, needs.Snapshot.Hunger, 0.001f);
            Assert.AreEqual(0.51f, needs.Snapshot.Happiness, 0.001f);
            Assert.AreEqual(0.79f, needs.Snapshot.Energy, 0.001f);
            Assert.IsTrue(needs.Dirty);
        }

        [Test]
        public void TinyDelta_IsRetainedAndMarksDirty()
        {
            var needs = new PetNeeds(_config);
            var before = needs.Get(PetNeed.Hunger);
            Assert.IsTrue(needs.Apply(PetNeed.Hunger, 0.000058f, 1f));
            Assert.AreEqual(before + 0.000058f, needs.Snapshot.Hunger, 0.000001f);
            Assert.IsTrue(needs.Dirty);
        }

        [Test]
        public void OfflineDecay_HasCorrectDirectionsAndRemainsDirty()
        {
            var needs = new PetNeeds(_config);
            needs.ApplyOfflineDecay(60f, 0f);
            Assert.AreEqual(0.51f, needs.Snapshot.Hunger, 0.0001f);
            Assert.AreEqual(0.51f, needs.Snapshot.Happiness, 0.0001f);
            Assert.AreEqual(0.79f, needs.Snapshot.Energy, 0.0001f);
            Assert.IsTrue(needs.Dirty);
        }

        [Test]
        public void RestoreAndUpdate_PreservesAllInteractionCounts()
        {
            var needs = new PetNeeds(_config);
            var saved = PetState.CreateDefault();
            saved.FeedCount = 8;
            saved.PetCount = 7;
            saved.PlayCount = 6;
            saved.PhotoCount = 5;
            needs.RestoreFrom(saved);
            needs.ApplyDecay(1f, 1f);
            needs.IncrementCount(InteractionEventKind.Feed);
            Assert.AreEqual(9, needs.Snapshot.FeedCount);
            Assert.AreEqual(7, needs.Snapshot.PetCount);
            Assert.AreEqual(6, needs.Snapshot.PlayCount);
            Assert.AreEqual(5, needs.Snapshot.PhotoCount);
        }

        [Test]
        public void DefaultPetConfig_CanFeedWithDwellAndCooldown()
        {
            var settings = PetConfigEatSettings.From(_config);
            Assert.Greater(settings.RequiredDwellSeconds, 0f);
            Assert.Greater(settings.CooldownSeconds, 0f);
            Assert.Greater(settings.ReArmDistance, settings.MaxHorizontalDistance);
            var rule = new FeedingRule(settings);
            for (var i = 1; i <= 10; i++)
            {
                var now = i * 0.1f;
                var den = new PoseSample { Marker = MarkerSlot.Den, Status = TrackingStatus.Tracked, SampleTime = now };
                var food = new PoseSample { Marker = MarkerSlot.Food, Status = TrackingStatus.Tracked, SampleTime = now };
                rule.Evaluate(den, food, now, 0.1f, new Vector3(0.05f, 0f, 0f), 0.35f, out _);
                if (i < 6) Assert.AreEqual(0, rule.IssuedEventCount);
            }
            Assert.AreEqual(1, rule.IssuedEventCount);
        }
    }
}
