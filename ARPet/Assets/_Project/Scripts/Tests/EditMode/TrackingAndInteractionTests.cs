using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
            TargetId = "card_classic",
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
        public void CanShow_IsWiderThanCanInteract_OnlyForFreshLimited()
        {
            Tick(TrackingStatus.Tracked);
            Assert.IsTrue(_machine.CanShow);
            Assert.IsTrue(_machine.CanInteract);

            Tick(TrackingStatus.Limited, 0.01f);
            Assert.IsTrue(_machine.CanShow, "新鲜的 LIMITED 采样可以显示");
            Assert.IsFalse(_machine.CanInteract, "但不可交互");

            foreach (var status in new[] { TrackingStatus.Lost, TrackingStatus.ExtendedTracked, TrackingStatus.Detected,
                         TrackingStatus.Paused, TrackingStatus.Initializing })
            {
                Tick(status, 0.01f);
                Assert.IsFalse(_machine.CanShow, status + " 不可显示");
            }

            _now += 0.01f;
            _machine.Tick(Sample(TrackingStatus.Limited, _now - 5f), _now);
            Assert.IsFalse(_machine.CanShow, "过期的 LIMITED 位姿不可显示");
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
    /// 触屏点按判定测试，重点是"什么不算一次合格点按"：
    /// 滑动、长按、点空、UI 穿透、角色不可见。
    ///
    /// P3 B 片之后职责收窄：<see cref="TouchPokeRule"/> 只回答"这次按下/抬起算不算合格点按"，
    /// 不再产生交互事件（事件 Id 与单击/双击由 <see cref="TapGestureRecognizer"/> 负责），
    /// 也不再自带 0.45 s 冷却（交互冷却由行为层 0.7 s TouchReact 忙碌实现）。
    /// </summary>
    public sealed class TouchPokeRuleTests
    {
        private TouchPokeRule _rule;

        [SetUp]
        public void SetUp() => _rule = new TouchPokeRule(new TouchPokeRule.TouchPokeSettings
        {
            MaxTravelPixels = 40f,
            MaxTravelInches = 0.25f,
            MaxHoldSeconds = 0.5f,
        });

        [Test]
        public void TapOnPet_IsQualified()
        {
            Assert.IsTrue(_rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false));

            var qualified = _rule.PointerUp(new Vector2(102f, 101f), 0.05f, hitPet: true, petVisible: true);

            Assert.IsTrue(qualified, "小范围位移、短按、命中角色且在场，必须判为合格点按");
            Assert.AreEqual(1, _rule.QualifiedTapCount);
            StringAssert.Contains("合格", _rule.LastReason);
        }

        [Test]
        public void Swipe_DoesNotQualify()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            var qualified = _rule.PointerUp(new Vector2(300f, 100f), 0.1f, hitPet: true, petVisible: true);

            Assert.IsFalse(qualified);
            Assert.AreEqual(0, _rule.QualifiedTapCount);
            StringAssert.Contains("滑动", _rule.LastReason);
        }

        [Test]
        public void LongPress_DoesNotQualify()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            var qualified = _rule.PointerUp(new Vector2(101f, 100f), 1.2f, hitPet: true, petVisible: true);

            Assert.IsFalse(qualified);
            StringAssert.Contains("长按", _rule.LastReason);
        }

        [Test]
        public void PointerDownOnUi_IsSwallowed()
        {
            Assert.IsFalse(_rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: true));

            // 被吞掉的按下不应在抬起时算成合格点按（界面输入不得穿透到角色）。
            var qualified = _rule.PointerUp(new Vector2(100f, 100f), 0.05f, hitPet: true, petVisible: true);

            Assert.IsFalse(qualified);
            Assert.AreEqual(0, _rule.QualifiedTapCount);
        }

        [Test]
        public void TapWhenPetInvisible_IsNotQualified()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            var qualified = _rule.PointerUp(new Vector2(100f, 100f), 0.05f, hitPet: true, petVisible: false);

            Assert.IsFalse(qualified);
            StringAssert.Contains("不可见", _rule.LastReason);
        }

        /// <summary>
        /// 旧用例 <c>SecondTapWithinCooldown_IsIgnored</c> 的替代：0.45 s 冷却已删除，
        /// 因为它会挡掉双击的第二下。两次快速点按都必须判为合格，节流交给行为层。
        /// </summary>
        [Test]
        public void TwoRapidTaps_AreBothQualified()
        {
            _rule.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            Assert.IsTrue(_rule.PointerUp(new Vector2(100f, 100f), 0.02f, true, true));

            _rule.PointerDown(new Vector2(100f, 100f), 0.10f, overUi: false);
            Assert.IsTrue(_rule.PointerUp(new Vector2(100f, 100f), 0.12f, true, true),
                "输入层不得节流：双击的第二下必须能通过判定");

            Assert.AreEqual(2, _rule.QualifiedTapCount);
        }

        [Test]
        public void TravelThreshold_UsesDpiWhenItIsHigher()
        {
            // dpi=400、0.25 in ⇒ 100 px，高于 40 px 的像素下限。
            var hdpi = new TouchPokeRule(new TouchPokeRule.TouchPokeSettings
            {
                MaxTravelPixels = 40f,
                MaxTravelInches = 0.25f,
                MaxHoldSeconds = 0.5f,
            }, dpi: 400f);

            Assert.AreEqual(100f, hdpi.MaxTravelPixels, 0.001f);

            hdpi.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            Assert.IsTrue(hdpi.PointerUp(new Vector2(160f, 100f), 0.05f, true, true),
                "60 px 位移在高 DPI 下应仍在容差内");

            // DPI 不可用时退回固定像素阈值，同样的 60 px 位移算滑动。
            var fallback = new TouchPokeRule(new TouchPokeRule.TouchPokeSettings
            {
                MaxTravelPixels = 40f,
                MaxTravelInches = 0.25f,
                MaxHoldSeconds = 0.5f,
            }, dpi: 0f);

            Assert.AreEqual(40f, fallback.MaxTravelPixels, 0.001f);
            fallback.PointerDown(new Vector2(100f, 100f), 0f, overUi: false);
            Assert.IsFalse(fallback.PointerUp(new Vector2(160f, 100f), 0.05f, true, true));
        }

        [Test]
        public void LowDpi_KeepsPixelFloor()
        {
            // dpi=96、0.25 in ⇒ 24 px，低于 40 px 下限，阈值保持 40 px。
            var rule = new TouchPokeRule(new TouchPokeRule.TouchPokeSettings(), dpi: 96f);
            Assert.AreEqual(40f, rule.MaxTravelPixels, 0.001f);
        }

        [Test]
        public void CancelledPointer_IsNotReplayedAfterTrackingRecovers()
        {
            _rule.PointerDown(Vector2.zero, 0f, false);
            _rule.CancelPointer();
            Assert.IsFalse(_rule.PointerUp(Vector2.zero, 0.1f, true, true));
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

            var accepted = pipe.Submit(InteractionEvent.Create(1, InteractionEventKind.Pet, "pet_classic", 0f));

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, sink.Events.Count);
            Assert.AreEqual(1, pipe.DroppedWhileUnavailable);
        }

        [Test]
        public void EventsPassThroughWhenArAvailable()
        {
            var sink = new RecordingEventSink();
            var pipe = new InteractionPipe(sink) { ArAvailable = true };

            var accepted = pipe.Submit(InteractionEvent.Create(1, InteractionEventKind.Pet, "pet_classic", 0f));

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, sink.Events.Count);
            Assert.AreEqual(0, pipe.DroppedWhileUnavailable);
        }
    }

    /// <summary>
    /// 模拟跟踪源按目标 ID 索引的契约测试：逐目标状态独立、未注册 ID 不抛异常、
    /// 重复 ID 只保留第一条。它是把槽位枚举泛化成 string TargetId 之后最容易回归的部分。
    /// </summary>
    public sealed class StubTrackingSourceTests
    {
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private StubTrackingSource.StubTarget Target(string targetId, Vector3 position)
        {
            var proxy = new GameObject(targetId + "Proxy").transform;
            _created.Add(proxy.gameObject);
            proxy.position = position;
            return new StubTrackingSource.StubTarget
            {
                targetId = targetId,
                proxy = proxy,
                size = new Vector3(0.066f, 0f, 0.094f),
                simulatedStatus = TrackingStatus.Tracked,
            };
        }

        private StubTrackingSource CreateSource(params StubTrackingSource.StubTarget[] targets)
        {
            var go = new GameObject("StubTracking");
            _created.Add(go);
            var source = go.AddComponent<StubTrackingSource>();
            // 必须走 SetTargets：真实 Unity 在 AddComponent 时已经跑过 Awake 并在空表上建了索引，
            // 直接用反射改字段不会重建索引，会让所有 targetId 变成"未注册"。
            source.SetTargets(targets);
            return source;
        }

        [Test]
        public void PerTargetSimulatedStatus_IsIndependentAndPoseComesFromOwnProxy()
        {
            var classic = Target("card_classic", Vector3.zero);
            var calm = Target("card_calm", new Vector3(0.12f, 0f, 0.05f));
            var source = CreateSource(classic, calm);

            source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);

            var classicSample = source.GetSample("card_classic");
            var calmSample = source.GetSample("card_calm");

            Assert.AreEqual("card_classic", classicSample.TargetId);
            Assert.AreEqual(TrackingStatus.Tracked, classicSample.Status, "另一只丢失不应影响这一只");
            Assert.AreEqual(TrackingStatus.Lost, calmSample.Status);
            Assert.AreEqual(classic.proxy.position.x, classicSample.Position.x, 0.0001f, "位姿必须来自各自代理");
            Assert.AreEqual(classic.proxy.position.z, classicSample.Position.z, 0.0001f);
            Assert.AreEqual(calm.proxy.position.x, calmSample.Position.x, 0.0001f,
                "丢失目标仍保留其代理位置，供恢复后重新采样");

            Assert.AreEqual(2, source.TargetIds.Count);
            Assert.AreEqual("card_classic", source.TargetIds[0]);
            Assert.AreEqual("card_calm", source.TargetIds[1]);

            var samples = source.AllSamples;
            Assert.AreEqual(2, samples.Length);
            Assert.AreEqual(TrackingStatus.Tracked, samples[0].Status);
            Assert.AreEqual(TrackingStatus.Lost, samples[1].Status);

            Assert.IsTrue(source.IsPoseValid("card_classic", Time.time));
            Assert.IsFalse(source.IsPoseValid("card_calm", Time.time));
            Assert.IsTrue(source.TryGetTargetPose("card_classic", out var worldPosition, out _));
            Assert.AreEqual(classic.proxy.position.x, worldPosition.x, 0.0001f);
        }

        [Test]
        public void GlobalSimulatedStatus_OverridesEveryTarget()
        {
            var source = CreateSource(
                Target("card_classic", Vector3.zero),
                Target("card_calm", new Vector3(0.12f, 0f, 0f)));

            source.SetGlobalSimulatedStatus(TrackingStatus.Limited);

            Assert.AreEqual(TrackingStatus.Limited, source.GetSample("card_classic").Status);
            Assert.AreEqual(TrackingStatus.Limited, source.GetSample("card_calm").Status);
        }

        [Test]
        public void UnregisteredTargetId_IsAnsweredAsLostWithoutThrowing()
        {
            var source = CreateSource(Target("card_classic", Vector3.zero));

            var sample = source.GetSample("card_divine");

            Assert.AreEqual("card_divine", sample.TargetId, "未注册 ID 必须原样带回，便于日志定位");
            Assert.AreEqual(TrackingStatus.Lost, sample.Status);
            Assert.IsFalse(source.IsRegistered("card_divine"));
            Assert.IsFalse(source.IsRegistered(null));
            Assert.IsFalse(source.IsPoseValid("card_divine", Time.time));
            Assert.IsFalse(source.TryGetTargetPose("card_divine", out _, out _));
            Assert.AreEqual(0f, source.GetTargetSize("card_divine").x, 0.0001f);
        }

        [Test]
        public void DuplicateTargetId_KeepsFirstEntryAndReportsError()
        {
            LogAssert.Expect(LogType.Error, new Regex("card_classic"));

            var first = Target("card_classic", Vector3.zero);
            var second = Target("card_classic", new Vector3(0.5f, 0f, 0f));
            var source = CreateSource(first, second);

            Assert.IsTrue(source.IsRegistered("card_classic"));
            Assert.AreEqual(1, source.TargetIds.Count, "重复 ID 只应保留第一条");
            Assert.AreEqual(0f, source.GetSample("card_classic").Position.x, 0.0001f, "保留的应是第一条条目");
        }
    }
}
