using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 跟踪状态的四态归并与去抖测试。
    /// 这里最容易被忽略、也最容易在真机上暴露的问题：单帧抖动导致提示闪烁，
    /// 以及"恢复"被误当成"首次识别"。
    /// </summary>
    public sealed class TrackingStateMachineTests
    {
        private TrackingStateMachine _machine;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _machine = new TrackingStateMachine(recoverHoldSeconds: 0.5f, lostDebounceSeconds: 0.2f);
            _now = 10f;
        }

        private static PoseSample Sample(TrackingStatus status, float time) => new PoseSample
        {
            Marker = MarkerSlot.Den,
            Status = status,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            SampleTime = time,
        };

        private TrackingUiState Tick(TrackingStatus status, float dt = 0.1f)
        {
            _now += dt;
            return _machine.Tick(Sample(status, _now), _now);
        }

        [Test]
        public void InitialState_IsSearching()
        {
            Assert.AreEqual(TrackingUiState.Searching, _machine.State);
        }

        [Test]
        public void FirstTrack_GoesStraightToTracking_NotRecovered()
        {
            var state = Tick(TrackingStatus.Tracked);

            Assert.AreEqual(TrackingUiState.Tracking, state);
            Assert.IsTrue(_machine.EverTracked);
            Assert.AreEqual(0, _machine.RecoveredCount, "首次识别不应计入恢复次数");
        }

        [Test]
        public void LostThenTracked_ReportsRecovered_ThenSettlesToTracking()
        {
            Tick(TrackingStatus.Tracked);

            // 丢失（去抖窗口 0.2 s 之外）。
            Tick(TrackingStatus.Lost, 0.5f);
            Assert.AreEqual(TrackingUiState.Lost, _machine.State);

            // 恢复。
            Assert.AreEqual(TrackingUiState.Recovered, Tick(TrackingStatus.Tracked));
            Assert.AreEqual(1, _machine.RecoveredCount);

            // 提示保持结束后回到 Tracking。
            Assert.AreEqual(TrackingUiState.Tracking, Tick(TrackingStatus.Tracked, 0.6f));
        }

        [Test]
        public void SingleFrameDrop_DoesNotFlickerToLost()
        {
            Tick(TrackingStatus.Tracked);

            // 一帧丢失，随后立即恢复：不应报告丢失。
            Tick(TrackingStatus.Lost, 0.1f);
            var state = Tick(TrackingStatus.Tracked, 0.1f);

            Assert.AreNotEqual(TrackingUiState.Lost, state);
            Assert.AreEqual(0, _machine.LostCount);
        }

        [Test]
        public void PausedStatus_IsTreatedAsLost_NotAsTracking()
        {
            Tick(TrackingStatus.Tracked);
            var state = Tick(TrackingStatus.Paused, 0.5f);

            Assert.AreEqual(TrackingUiState.Lost, state);
        }

        [Test]
        public void StaleSample_IsNotSettleable()
        {
            var stale = Sample(TrackingStatus.Tracked, _now - 5f);
            Assert.IsFalse(PoseFreshness.IsSettleable(stale, _now));

            var fresh = Sample(TrackingStatus.Tracked, _now);
            Assert.IsTrue(PoseFreshness.IsSettleable(fresh, _now));
        }

        [Test]
        public void InvalidFrame_DisablesInteractionBeforeLostHint()
        {
            Tick(TrackingStatus.Tracked);
            Assert.IsTrue(_machine.CanInteract);
            Assert.AreEqual(TrackingUiState.Tracking, Tick(TrackingStatus.Lost, 0.01f));
            Assert.IsFalse(_machine.CanInteract);
            Assert.AreEqual(TrackingUiState.Tracking, Tick(TrackingStatus.Tracked, 0.01f));
            Assert.IsTrue(_machine.CanInteract);
            Assert.AreEqual(0, _machine.LostCount);
        }

        [Test]
        public void RepeatedLossDuringRecovered_EventuallyBecomesLost()
        {
            Tick(TrackingStatus.Tracked);
            Tick(TrackingStatus.Lost, 0.5f);
            Tick(TrackingStatus.Tracked, 0.01f);
            Assert.AreEqual(TrackingUiState.Recovered, _machine.State);
            for (var i = 0; i < 120; i++) Tick(TrackingStatus.Lost, 1f / 60f);
            Assert.AreEqual(TrackingUiState.Lost, _machine.State);
            Assert.IsFalse(_machine.CanInteract);
            Assert.AreEqual(2, _machine.LostCount);
        }

        [Test]
        public void BeforeFirstDetection_RemainsSearching()
        {
            for (var i = 0; i < 20; i++) Tick(TrackingStatus.Initializing);
            Assert.AreEqual(TrackingUiState.Searching, _machine.State);
            Assert.AreEqual(0, _machine.LostCount);
            Assert.IsFalse(_machine.CanInteract);
        }

        [TestCase(TrackingStatus.Limited)]
        [TestCase(TrackingStatus.ExtendedTracked)]
        public void EstimatedOrLimitedPose_IsNotSettleable(TrackingStatus status)
        {
            Assert.IsFalse(PoseFreshness.IsSettleable(Sample(status, _now), _now));
        }

        [Test]
        public void CustomFreshnessLimit_IsUsedForInteraction()
        {
            _machine.Tick(Sample(TrackingStatus.Tracked, _now - 0.2f), _now, 0.1f);
            Assert.IsFalse(_machine.CanInteract);
        }

        [Test]
        public void Reset_ClearsCountersAndInteractionAvailability()
        {
            Tick(TrackingStatus.Tracked);
            Tick(TrackingStatus.Lost, 0.5f);
            Tick(TrackingStatus.Tracked);
            _machine.Reset();
            Assert.IsFalse(_machine.CanInteract);
            Assert.AreEqual(0, _machine.LostCount);
            Assert.AreEqual(0, _machine.RecoveredCount);
        }
    }

    /// <summary>
    /// 触屏逗弄判定测试，重点是"什么不算一次逗弄"：
    /// 滑动、长按、点空、UI 穿透、角色不可见。
    /// </summary>
    public sealed class TouchPokeRuleTests
    {
        private TouchPokeRule _rule;

        [SetUp]
        public void SetUp() => _rule = new TouchPokeRule(new TouchPokeRule.TouchPokeSettings
        {
            MaxTravelPixels = 40f,
            MaxHoldSeconds = 0.5f,
            CooldownSeconds = 0.45f,
        });

        [Test]
        public void TapOnPet_ProducesEvent()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            var ok = _rule.PointerUp(new Vector2(102f, 101f), 0.05f, hitPet: true, petVisible: true, now: 0.05f,
                out var produced);

            Assert.IsTrue(ok);
            Assert.AreEqual(InteractionEventKind.Pet, produced.Kind);
        }

        [Test]
        public void Swipe_DoesNotProduceEvent()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            var ok = _rule.PointerUp(new Vector2(300f, 100f), 0.1f, hitPet: true, petVisible: true, now: 0.1f,
                out _);

            Assert.IsFalse(ok);
            StringAssert.Contains("滑动", _rule.LastReason);
        }

        [Test]
        public void LongPress_DoesNotProduceEvent()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            var ok = _rule.PointerUp(new Vector2(101f, 100f), 1.2f, hitPet: true, petVisible: true, now: 1.2f,
                out _);

            Assert.IsFalse(ok);
            StringAssert.Contains("长按", _rule.LastReason);
        }

        [Test]
        public void PointerDownOnUi_IsSwallowed()
        {
            Assert.IsFalse(_rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: true));

            // 被吞掉的按下不应在抬起时产生事件（界面输入不得穿透到角色）。
            var ok = _rule.PointerUp(new Vector2(100f, 100f), 0.05f, hitPet: true, petVisible: true, now: 0.05f,
                out _);

            Assert.IsFalse(ok);
        }

        [Test]
        public void TapWhenPetInvisible_IsIgnored()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            var ok = _rule.PointerUp(new Vector2(100f, 100f), 0.05f, hitPet: true, petVisible: false, now: 0.05f,
                out _);

            Assert.IsFalse(ok);
            StringAssert.Contains("不可见", _rule.LastReason);
        }

        [Test]
        public void SecondTapWithinCooldown_IsIgnored()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            Assert.IsTrue(_rule.PointerUp(new Vector2(100f, 100f), 0.02f, true, true, 0.02f, out _));

            _rule.PointerDown(new Vector2(100f, 100f), 0.10f, overUi: false);
            Assert.IsFalse(_rule.PointerUp(new Vector2(100f, 100f), 0.12f, true, true, 0.12f, out _),
                "冷却期内的第二次点击不应产生事件");
        }

        [Test]
        public void CancelledPointer_IsNotReplayedAfterTrackingRecovers()
        {
            _rule.PointerDown(Vector2.zero, 0f, false);
            _rule.CancelPointer();
            Assert.IsFalse(_rule.PointerUp(Vector2.zero, 0.1f, true, true, 0.1f, out _));
        }
    }

    /// <summary>
    /// 交互总闸门测试：跟踪不可用期间的事件必须被丢弃，而不是排队等恢复后补发。
    /// </summary>
    public sealed class InteractionPipeTests
    {
        [Test]
        public void EventsAreDroppedWhileArUnavailable()
        {
            var sink = new RecordingEventSink();
            var pipe = new InteractionPipe(sink) { ArAvailable = false };

            var accepted = pipe.Submit(InteractionEvent.Create(1, InteractionEventKind.Feed, MarkerSlot.Food, 0f));

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, sink.Events.Count);
            Assert.AreEqual(1, pipe.DroppedWhileUnavailable);
        }

        [Test]
        public void EventsPassThroughWhenArAvailable()
        {
            var sink = new RecordingEventSink();
            var pipe = new InteractionPipe(sink) { ArAvailable = true };

            var accepted = pipe.Submit(InteractionEvent.Create(1, InteractionEventKind.Feed, MarkerSlot.Food, 0f));

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, sink.Events.Count);
            Assert.AreEqual(0, pipe.DroppedWhileUnavailable);
        }
    }
}
