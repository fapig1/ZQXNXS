using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 接近规则（<see cref="ProximityRule"/>）的纯几何与状态测试。不需要场景、跟踪源或任何资产：
    /// 输入就是一帧的位姿列表，输出是"哪些关系对够近 / 已分开 / 已锁"。
    ///
    /// 这些用例守护的是"错了会让共同大笑凭空开演或永远不再开演"的部分：
    /// 对称几何（法向夹角 + 双向高度差 + 卡面内水平距离）、进入 / 退出迟滞、
    /// 短暂丢失不等于离开、拿出画面满 2s 才算分开、第一次同时可见就贴近也照常开演、整组锁住全部关系对。
    /// </summary>
    public sealed class ProximityRuleTests
    {
        private const string PetCalm = "pet_calm";
        private const string PetClassic = "pet_classic";
        private const string PetDivine = "pet_divine";
        private const string PetRage = "pet_rage";

        private ProximityRule _rule;

        [SetUp]
        public void SetUp()
        {
            _rule = new ProximityRule(SocialTuning.Default);
        }

        // ── 几何（Measure）────────────────────────────────────────────────────────

        [Test]
        public void Measure_FlatCardsFifteenCentimetersApart_AreInEnterZone()
        {
            var measure = ProximityRule.Measure(Vector3.zero, Quaternion.identity,
                new Vector3(0.15f, 0f, 0f), Quaternion.identity, SocialTuning.Default);

            Assert.AreEqual(0.15f, measure.HorizontalDistance, 0.0001f, "水平距离取卡面内距离");
            Assert.AreEqual(0f, measure.HeightDelta, 0.0001f, "同一平面没有高度差");
            Assert.AreEqual(0f, measure.NormalAngleDegrees, 0.0001f, "平放的两卡法向相同");
            Assert.IsTrue(measure.InEnterZone);
            Assert.IsFalse(measure.InExitZone);
        }

        [Test]
        public void Measure_RaisedCard_ExceedsHeightTolerance()
        {
            var measure = ProximityRule.Measure(Vector3.zero, Quaternion.identity,
                new Vector3(0.15f, 0.05f, 0f), Quaternion.identity, SocialTuning.Default);

            Assert.AreEqual(0.05f, measure.HeightDelta, 0.0001f);
            Assert.IsFalse(measure.InEnterZone, "抬高 0.05m 超过 0.035m 上限，不得进入");
            Assert.IsTrue(measure.InHysteresis, "高度超限但水平没拉开 → 迟滞带，不清锁");
        }

        [Test]
        public void Measure_UprightCard_ExceedsNormalAngle()
        {
            var measure = ProximityRule.Measure(Vector3.zero, Quaternion.identity,
                new Vector3(0.15f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), SocialTuning.Default);

            Assert.AreEqual(90f, measure.NormalAngleDegrees, 0.01f, "竖起 90° 的卡法向是水平的");
            Assert.IsFalse(measure.InEnterZone, "一张竖卡与一张平卡不得触发");
        }

        [Test]
        public void Measure_SameTiltedPlane_StillEnters()
        {
            // 两张卡同时倾斜 15°（同一倾斜平面），沿卡面内方向分开 0.15m。
            var tilt = Quaternion.Euler(0f, 0f, 15f);
            var inPlane = tilt * Vector3.right;
            var positionB = inPlane * 0.15f;

            var measure = ProximityRule.Measure(Vector3.zero, tilt, positionB, tilt, SocialTuning.Default);

            Assert.AreEqual(0f, measure.NormalAngleDegrees, 0.01f, "同一倾斜平面法向相同");
            Assert.AreEqual(0f, measure.HeightDelta, 0.0001f, "沿卡面分开没有高度差");
            Assert.AreEqual(0.15f, measure.HorizontalDistance, 0.0001f);
            Assert.IsTrue(measure.InEnterZone, "对称判定：一起倾斜不能把这一对判出去");

            // 交换 A/B 不改变任何对称量，只有相对位移取反。
            var swapped = ProximityRule.Measure(positionB, tilt, Vector3.zero, tilt, SocialTuning.Default);
            Assert.AreEqual(measure.HorizontalDistance, swapped.HorizontalDistance, 0.0001f);
            Assert.AreEqual(measure.HeightDelta, swapped.HeightDelta, 0.0001f);
            Assert.AreEqual(measure.NormalAngleDegrees, swapped.NormalAngleDegrees, 0.0001f);
            Assert.AreEqual(-measure.CenterDelta.x, swapped.CenterDelta.x, 0.0001f);
        }

        [Test]
        public void Measure_DetectsExitZoneOnlyBeyondExitDistance()
        {
            var band = ProximityRule.Measure(Vector3.zero, Quaternion.identity,
                new Vector3(0.20f, 0f, 0f), Quaternion.identity, SocialTuning.Default);
            Assert.IsTrue(band.InHysteresis, "0.20m 落在 0.18～0.23m 之间");

            var exit = ProximityRule.Measure(Vector3.zero, Quaternion.identity,
                new Vector3(0.23f, 0f, 0f), Quaternion.identity, SocialTuning.Default);
            Assert.IsTrue(exit.InExitZone, "0.23m 起算离开区");
            Assert.IsFalse(exit.InEnterZone);
        }

        // ── 进入停留 ──────────────────────────────────────────────────────────────

        [Test]
        public void EnterDwell_BelowThreshold_IsNotQualified()
        {
            _rule.Tick(0.59f, Close(0.15f));

            CollectionAssert.IsEmpty(_rule.QualifiedPairs, "0.59s 还不够 0.6s，不得交出资格");
        }

        [Test]
        public void EnterDwell_ReachesThreshold_IsQualified()
        {
            _rule.Tick(0.1f, Close(0.30f));
            _rule.Tick(0.3f, Close(0.15f));
            CollectionAssert.IsEmpty(_rule.QualifiedPairs);

            _rule.Tick(0.3f, Close(0.15f));

            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);
            Assert.IsFalse(_rule.IsLocked(TwoPetPair), "正常靠近不该被锁");
            Assert.IsFalse(_rule.IsSeparated(TwoPetPair));
        }

        [Test]
        public void EnterDwell_LeavingEnterZone_RestartsAccumulation()
        {
            _rule.Tick(0.5f, Close(0.15f));

            // 离开进入区（这里是退出区）：进入计时清零。
            _rule.Tick(0.1f, Close(0.30f));

            _rule.Tick(0.5f, Close(0.15f));
            CollectionAssert.IsEmpty(_rule.QualifiedPairs, "中途离开过就必须重新累计满 0.6s");

            _rule.Tick(0.11f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);
        }

        [Test]
        public void HysteresisBand_ClearsBothTimers_AndIsNotSeparated()
        {
            _rule.LockGroup(new[] { PetCalm, PetClassic });
            _rule.Tick(0.6f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);

            // 迟滞带（0.20m）：既不合格，也不解开锁，两个计时都清零。
            _rule.Tick(0.1f, Close(0.20f));
            CollectionAssert.IsEmpty(_rule.QualifiedPairs, "迟滞带里不得继续保留资格");
            Assert.IsFalse(_rule.IsSeparated(TwoPetPair), "迟滞带不是离开");
            Assert.IsTrue(_rule.IsLocked(TwoPetPair));

            // 再进退出区只累计了 0.29s：离开计时确实被迟滞带清零过。
            _rule.Tick(0.29f, Close(0.30f));
            Assert.IsFalse(_rule.IsSeparated(TwoPetPair), "离开计时被清零，需重新累计 0.3s");
            Assert.IsTrue(_rule.IsLocked(TwoPetPair));

            _rule.Tick(0.02f, Close(0.30f));
            Assert.IsTrue(_rule.IsSeparated(TwoPetPair));
            Assert.IsFalse(_rule.IsLocked(TwoPetPair), "确认分开后该对重新武装");
        }

        [Test]
        public void EnterTimer_IsClearedByHysteresisBandToo()
        {
            _rule.Tick(0.59f, Close(0.15f));
            _rule.Tick(0.1f, Close(0.20f));      // 迟滞带：进入计时清零
            _rule.Tick(0.3f, Close(0.15f));      // 只累计了 0.3s
            CollectionAssert.IsEmpty(_rule.QualifiedPairs);

            _rule.Tick(0.31f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);
        }

        // ── 离开（解锁）───────────────────────────────────────────────────────────

        [Test]
        public void ExitDwell_ShortOfThreshold_DoesNotUnlock()
        {
            _rule.LockGroup(new[] { PetCalm, PetClassic });

            _rule.Tick(0.29f, Close(0.30f));

            Assert.IsFalse(_rule.IsSeparated(TwoPetPair), "0.29s 还不算确认真实分开");
            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "同一次接近仍然算已消费");
        }

        [Test]
        public void ExitDwell_HeldLongEnough_SeparatesAndUnlocks()
        {
            _rule.LockGroup(new[] { PetCalm, PetClassic });

            _rule.Tick(0.3f, Close(0.30f));

            Assert.IsTrue(_rule.IsSeparated(TwoPetPair));
            Assert.IsFalse(_rule.IsLocked(TwoPetPair));
        }

        // ── 丢失 / 暂停 ───────────────────────────────────────────────────────────

        [Test]
        public void UnusablePet_ClearsFlagsButKeepsLock()
        {
            _rule.LockGroup(new[] { PetCalm, PetClassic });
            _rule.Tick(0.6f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);

            // 一只丢失：资格作废，"分开"也作废，但锁保持。
            _rule.Tick(0.1f, new[] { Flat(PetCalm, 0.15f), Unusable(PetClassic) });

            CollectionAssert.IsEmpty(_rule.QualifiedPairs, "丢失的一对不得继续交出资格");
            Assert.IsFalse(_rule.IsSeparated(TwoPetPair), "丢失不能当成离开");
            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "丢失不解锁");

            // 恢复后仍贴着：重新累计停留后也只是"锁着的合格对"，不会重开。
            _rule.Tick(0.6f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);
            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "恢复后仍按已消费处理");
        }

        [Test]
        public void ResetTransient_ClearsTimersButKeepsLock()
        {
            _rule.LockGroup(new[] { PetCalm, PetClassic });
            _rule.Tick(0.6f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);

            _rule.ResetTransient();

            CollectionAssert.IsEmpty(_rule.QualifiedPairs, "暂停后资格作废，要重新累计停留");
            Assert.IsFalse(_rule.IsSeparated(TwoPetPair), "暂停不是离开");
            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "暂停后仍要靠真实分开才能重演");

            _rule.Tick(0.6f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);
            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "锁没解，重新合格也不会重开");
        }

        // ── 第一次同时可见就已贴近（2026-10-30 真机 BUG-04）──────────────────────

        [Test]
        public void FirstSeenAlreadyTouching_IsNotLocked_AndQualifiesAfterDwell()
        {
            // 真实摆法：先放一张，再把第二张放到 15cm 旁边。第二张进 TRACKED 的那一帧两卡就已贴近，
            // 这不是"打开应用凭空开演"，必须照常累计停留后合格。
            _rule.Tick(0.1f, Close(0.15f));
            Assert.IsFalse(_rule.IsLocked(TwoPetPair), "第一次同时可见就贴近也不得上锁");

            _rule.Tick(0.5f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair, "停留满 0.6s 即合格");
            Assert.IsFalse(_rule.IsLocked(TwoPetPair));
        }

        [Test]
        public void SecondCardPlacedNextToFirst_Qualifies()
        {
            // 第一张已在画面里，第二张还没识别到。
            _rule.Tick(1f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            _rule.Tick(1f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });

            // 第二张放下时已经在 15cm 处。
            _rule.Tick(0.3f, Close(0.15f));
            _rule.Tick(0.3f, Close(0.15f));

            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);
            Assert.IsFalse(_rule.IsLocked(TwoPetPair));
        }

        // ── 拿出画面够久才算分开 ────────────────────────────────────────────────

        [Test]
        public void LostLongEnough_ThenPlacedElsewhere_UnlocksPair()
        {
            LockWhileTouching(0.15f);

            _rule.Tick(1.0f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            _rule.Tick(0.99f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            _rule.Tick(0.1f, Close(0.15f));
            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "1.99s 还不够 2s，按跟踪抖动处理");

            _rule.Tick(2.1f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "拿走期间仍锁着，放回才判断");

            // 放回旁边 10cm（原来 15cm，变化 5cm ≥ 3cm）。
            _rule.Tick(0.1f, Close(0.10f));
            Assert.IsFalse(_rule.IsLocked(TwoPetPair), "拿出画面满 2s 且换了位置 → 视为新一次接近");
            Assert.IsFalse(_rule.IsSeparated(TwoPetPair), "IsSeparated 仍只表示同时可见且已拉开，不因丢失置真");

            _rule.Tick(0.6f, Close(0.10f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);
            Assert.IsFalse(_rule.IsLocked(TwoPetPair));
        }

        [Test]
        public void LostLongEnough_ButBackInSamePlace_StaysLocked()
        {
            // 2026-10-30 真机 O-10：四卡同屏时边缘卡原地掉跟踪 2s 又回来，不能每 5 秒重演一轮。
            LockWhileTouching(0.15f);

            _rule.Tick(2.5f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            _rule.Tick(0.1f, Close(0.16f));   // 回来时只有 1cm 的识别误差

            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "放回原处（变化 < 3cm）不算新一次接近");

            // 继续原地抖动若干轮，仍然不解锁。
            for (var i = 0; i < 5; i++)
            {
                _rule.Tick(2.5f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
                _rule.Tick(1.0f, Close(0.15f));
            }

            Assert.IsTrue(_rule.IsLocked(TwoPetPair));
        }

        [Test]
        public void AwaitingRelocation_MovingLaterStillUnlocks()
        {
            LockWhileTouching(0.15f);
            _rule.Tick(2.5f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            _rule.Tick(0.1f, Close(0.15f));
            Assert.IsTrue(_rule.IsLocked(TwoPetPair));

            // 放回后才挪：仍在等待换位，相对位置变化够了就解锁。
            _rule.Tick(0.1f, Close(0.11f));
            Assert.IsFalse(_rule.IsLocked(TwoPetPair), "放回后再挪 4cm 也算换了摆法");
        }

        [Test]
        public void RelocationIsMeasuredRelativeToCard_NotWorld()
        {
            // 相机移动会让两张卡的世界坐标整体平移；相对位置没变就不算换位。
            LockWhileTouching(0.15f);
            _rule.Tick(2.5f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });

            var shift = new Vector3(0.20f, 0f, 0.10f);
            _rule.Tick(0.1f, new[] { Flat(PetCalm, shift), Flat(PetClassic, shift + new Vector3(0.15f, 0f, 0f)) });

            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "两卡一起平移（相机挪动）不是换位");
        }

        [Test]
        public void LimitedCard_DoesNotCountAsTakenAway()
        {
            LockWhileTouching(0.15f);

            // 边缘卡掉到 LIMITED 很久：不可用于判距离，但仍在画面里，不累计丢失计时。
            for (var i = 0; i < 10; i++) _rule.Tick(1f, new[] { Flat(PetCalm, 0f), Limited(PetClassic) });
            _rule.Tick(0.1f, Close(0.10f));

            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "LIMITED 不是拿走，换位也不解锁");
        }

        [Test]
        public void ShortLosses_DoNotAccumulateAcrossRecovery()
        {
            LockWhileTouching(0.15f);

            // 两次各 1.5s 的丢失，中间恢复过一帧：丢失计时清零，不能合计成 3s。
            _rule.Tick(1.5f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            _rule.Tick(0.1f, Close(0.15f));
            _rule.Tick(1.5f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            _rule.Tick(0.1f, Close(0.10f));

            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "丢失计时必须是连续的");
        }

        [Test]
        public void ResetTransient_ClearsLostTimer()
        {
            LockWhileTouching(0.15f);
            _rule.Tick(1.5f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });

            _rule.ResetTransient();
            _rule.Tick(1.0f, new[] { Flat(PetCalm, 0f), Unusable(PetClassic) });
            _rule.Tick(0.1f, Close(0.10f));

            Assert.IsTrue(_rule.IsLocked(TwoPetPair), "暂停会清零丢失计时，暂停前后不能拼成 2s");
        }

        /// <summary>两卡贴着（记下相对位置）再整组上锁，模拟一次刚开演的共同大笑。</summary>
        private void LockWhileTouching(float distance)
        {
            _rule.Tick(0.1f, Close(distance));
            _rule.LockGroup(new[] { PetCalm, PetClassic });
        }

        // ── 锁组与重置 ───────────────────────────────────────────────────────────

        [Test]
        public void LockGroup_LocksEveryPairInsideGroup()
        {
            _rule.LockGroup(new[] { PetClassic, PetCalm, PetRage });

            Assert.IsTrue(_rule.IsLocked(Pair(PetClassic, PetCalm)));
            Assert.IsTrue(_rule.IsLocked(Pair(PetClassic, PetRage)));
            Assert.IsTrue(_rule.IsLocked(Pair(PetCalm, PetRage)), "组内所有关系对都要锁，不只是连通的边");
            Assert.IsFalse(_rule.IsLocked(Pair(PetClassic, PetDivine)), "组外的关系对不受影响");
        }

        [Test]
        public void LockGroup_IgnoresDegenerateInput()
        {
            _rule.LockGroup(null);
            _rule.LockGroup(new[] { PetClassic });
            _rule.LockGroup(new[] { PetClassic, PetClassic });

            Assert.IsFalse(_rule.IsLocked(TwoPetPair), "不足两只（或同 ID）不产生任何锁");
        }

        [Test]
        public void ResetDwell_RequiresAnotherFullDwell()
        {
            _rule.Tick(0.6f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);

            _rule.ResetDwell(TwoPetPair);
            CollectionAssert.IsEmpty(_rule.QualifiedPairs, "丢弃候选后要重新累计停留");

            _rule.Tick(0.3f, Close(0.15f));
            CollectionAssert.IsEmpty(_rule.QualifiedPairs);

            _rule.Tick(0.3f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);
        }

        // ── 枚举顺序与退化输入 ────────────────────────────────────────────────────

        [Test]
        public void FourPets_ProduceSixPairsInStableOrder()
        {
            _rule.Tick(0.6f, new[]
            {
                Flat(PetClassic, new Vector3(0f, 0f, 0f)),
                Flat(PetCalm, new Vector3(0.10f, 0f, 0f)),
                Flat(PetRage, new Vector3(0f, 0f, 0.10f)),
                Flat(PetDivine, new Vector3(0.10f, 0f, 0.10f)),
            });

            CollectionAssert.AreEqual(new[]
            {
                Pair(PetCalm, PetClassic),
                Pair(PetCalm, PetDivine),
                Pair(PetCalm, PetRage),
                Pair(PetClassic, PetDivine),
                Pair(PetClassic, PetRage),
                Pair(PetDivine, PetRage),
            }, new List<PetPair>(_rule.QualifiedPairs));
        }

        [Test]
        public void SinglePet_ProducesNoPairs()
        {
            _rule.Tick(1f, new[] { Flat(PetClassic, Vector3.zero) });

            CollectionAssert.IsEmpty(_rule.QualifiedPairs);
            Assert.IsFalse(_rule.IsLocked(TwoPetPair));
            Assert.IsFalse(_rule.TryGetMeasure(TwoPetPair, out _));
        }

        [Test]
        public void NoInput_ClearsQualificationWithoutThrowing()
        {
            _rule.Tick(0.6f, Close(0.15f));
            CollectionAssert.Contains(_rule.QualifiedPairs, TwoPetPair);

            _rule.Tick(0.1f, null);

            CollectionAssert.IsEmpty(_rule.QualifiedPairs, "没有输入就没有合格对");
            Assert.IsFalse(_rule.IsSeparated(TwoPetPair));
        }

        [Test]
        public void InvalidDeltaTime_DoesNotAdvanceDwell()
        {
            _rule.Tick(float.NaN, Close(0.15f));
            _rule.Tick(float.PositiveInfinity, Close(0.15f));
            _rule.Tick(-1f, Close(0.15f));

            CollectionAssert.IsEmpty(_rule.QualifiedPairs, "非法帧长不得推进停留计时");
        }

        [Test]
        public void TryGetMeasure_ReportsLastGeometryForDebugging()
        {
            _rule.Tick(0.1f, Close(0.15f));

            Assert.IsTrue(_rule.TryGetMeasure(TwoPetPair, out var measure));
            Assert.AreEqual(0.15f, measure.HorizontalDistance, 0.0001f);
            Assert.IsTrue(measure.InEnterZone);
        }

        // ── 辅助 ──────────────────────────────────────────────────────────────────

        private static PetPair TwoPetPair => Pair(PetCalm, PetClassic);

        private static PetPair Pair(string first, string second) => new PetPair(first, second);

        /// <summary>两只平放的宠物，沿世界 X 轴分开给定距离。</summary>
        private static ProximityInput[] Close(float distance) => new[]
        {
            Flat(PetCalm, Vector3.zero),
            Flat(PetClassic, new Vector3(distance, 0f, 0f)),
        };

        private static ProximityInput Flat(string petId, float x) => Flat(petId, new Vector3(x, 0f, 0f));

        private static ProximityInput Flat(string petId, Vector3 position) => new ProximityInput
        {
            PetId = petId,
            Usable = true,
            Position = position,
            Rotation = Quaternion.identity,
        };

        /// <summary>LIMITED：卡仍在画面里，但位姿不可用于判距离。</summary>
        private static ProximityInput Limited(string petId) => new ProximityInput
        {
            PetId = petId,
            Usable = false,
            Present = true,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
        };

        private static ProximityInput Unusable(string petId) => new ProximityInput
        {
            PetId = petId,
            Usable = false,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
        };
    }
}
