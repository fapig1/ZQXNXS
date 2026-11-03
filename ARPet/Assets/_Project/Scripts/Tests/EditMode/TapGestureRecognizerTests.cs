using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;
using ZQXNXS.ARPet.Pet;
using ZQXNXS.ARPet.App;
using ZQXNXS.ARPet.UI;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 单击 / 双击识别窗口的纯逻辑测试。<see cref="TapGestureRecognizer"/> 不结算任何数值，
    /// 只决定"什么时候发一条什么手势的事件"，因此这里全部用假时钟直接驱动。
    ///
    /// 门限附近的两条用例（0.279 s / 0.281 s）就是行为契约：
    /// 间隔<b>严格小于</b>窗口才算双击；超窗时先补发上一次单击，再为本次开新窗口。
    /// </summary>
    public sealed class TapGestureRecognizerTests
    {
        private const string Classic = "pet_classic";
        private const string Calm = "pet_calm";

        private TapGestureRecognizer _recognizer;
        private List<InteractionEvent> _events;

        [SetUp]
        public void SetUp()
        {
            _recognizer = new TapGestureRecognizer(new TapGestureRecognizer.TapGestureSettings
            {
                DoubleTapWindowSeconds = 0.28f,
            });
            _events = new List<InteractionEvent>();
        }

        [Test]
        public void SingleTap_IsEmittedOnlyAfterWindowExpires()
        {
            Assert.IsTrue(_recognizer.Tap(Classic, Vector3.zero, 1.00f, _events));
            Assert.AreEqual(0, _events.Count, "第一次合格抬起只开窗口，不能提前加分或播放触屏反应");
            Assert.IsTrue(_recognizer.HasPendingTap(Classic));

            Assert.AreEqual(0, _recognizer.Tick(1.20f, _events), "窗口未到，不发单击");
            Assert.AreEqual(0, _events.Count);

            Assert.AreEqual(1, _recognizer.Tick(1.35f, _events), "窗口到期发一条单击");
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(InteractionEventKind.Pet, _events[0].Kind);
            Assert.AreEqual(PetGesture.SingleTap, _events[0].Gesture);
            Assert.AreEqual(Classic, _events[0].PetId, "事件必须带上归属宠物的稳定 ID");
            Assert.AreEqual(1.00f, _events[0].Time, 1e-3f, "事件时刻记第一次抬起的时刻");
            Assert.AreEqual(0, _recognizer.PendingCount);
        }

        [Test]
        public void SecondTapInsideWindow_EmitsOneDoubleTap_AndNoSingleTap()
        {
            _recognizer.Tap(Classic, Vector3.zero, 1.00f, _events);
            _events.Clear();

            Assert.IsTrue(_recognizer.Tap(Classic, Vector3.zero, 1.10f, _events));
            Assert.AreEqual(1, _events.Count, "双击只产生一条事件");
            Assert.AreEqual(PetGesture.DoubleTap, _events[0].Gesture);
            Assert.IsFalse(_recognizer.HasPendingTap(Classic), "双击要取消待发单击");

            Assert.AreEqual(0, _recognizer.Tick(2.00f, _events), "双击之后窗口不再补发单击");
            Assert.AreEqual(1, _events.Count);
        }

        [Test]
        public void GapJustUnderWindow_IsDouble_GapJustOver_IsTwoSingles()
        {
            var doubleEvents = new List<InteractionEvent>();
            _recognizer.Tap(Classic, Vector3.zero, 1.000f, doubleEvents);
            _recognizer.Tap(Classic, Vector3.zero, 1.279f, doubleEvents);
            Assert.AreEqual(1, doubleEvents.Count);
            Assert.AreEqual(PetGesture.DoubleTap, doubleEvents[0].Gesture, "间隔 0.279 s 在窗口内，应合成双击");

            var late = new TapGestureRecognizer(new TapGestureRecognizer.TapGestureSettings
            {
                DoubleTapWindowSeconds = 0.28f,
            });
            var singleEvents = new List<InteractionEvent>();
            late.Tap(Classic, Vector3.zero, 1.000f, singleEvents);
            late.Tap(Classic, Vector3.zero, 1.281f, singleEvents);

            Assert.AreEqual(1, singleEvents.Count, "间隔 0.281 s 已超窗：第二下抬起时先补发上一次单击");
            Assert.AreEqual(PetGesture.SingleTap, singleEvents[0].Gesture);
            Assert.IsTrue(late.HasPendingTap(Classic), "第二下自己开一个新窗口");

            Assert.AreEqual(1, late.Tick(1.600f, singleEvents));
            Assert.AreEqual(2, singleEvents.Count);
            Assert.AreEqual(PetGesture.SingleTap, singleEvents[1].Gesture);
        }

        [Test]
        public void TapsOnDifferentPets_NeverBecomeDoubleTap()
        {
            _recognizer.Tap(Classic, Vector3.zero, 1.00f, _events);
            _recognizer.Tap(Calm, Vector3.zero, 1.10f, _events);

            Assert.AreEqual(0, _events.Count, "两次点在不同宠物上绝不能合成双击");
            Assert.AreEqual(2, _recognizer.PendingCount, "每只宠物各自一个待判窗口");

            Assert.AreEqual(2, _recognizer.Tick(1.40f, _events));
            Assert.AreEqual(2, _events.Count);
            Assert.IsTrue(_events.All(value => value.Gesture == PetGesture.SingleTap));
            CollectionAssert.Contains(_events.Select(value => value.PetId).ToList(), Classic);
            CollectionAssert.Contains(_events.Select(value => value.PetId).ToList(), Calm);
        }

        [Test]
        public void ThirdTap_OpensNewWindow_WithoutSecondDoubleTap()
        {
            _recognizer.Tap(Classic, Vector3.zero, 1.00f, _events);
            _recognizer.Tap(Classic, Vector3.zero, 1.10f, _events);   // 双击（已产生 1 条）
            _recognizer.Tap(Classic, Vector3.zero, 1.20f, _events);   // 第三下：开新窗口

            Assert.AreEqual(1, _events.Count, "三连击不应产生第二个双击");
            Assert.IsTrue(_recognizer.HasPendingTap(Classic));

            Assert.AreEqual(1, _recognizer.Tick(1.60f, _events));
            Assert.AreEqual(2, _events.Count);
            Assert.AreEqual(PetGesture.DoubleTap, _events[0].Gesture);
            Assert.AreEqual(PetGesture.SingleTap, _events[1].Gesture);
        }

        [Test]
        public void Cancel_DropsPendingSingle_WithoutReplay()
        {
            _recognizer.Tap(Classic, Vector3.zero, 1.00f, _events);

            Assert.IsTrue(_recognizer.Cancel(Classic));
            Assert.AreEqual(0, _recognizer.Tick(2.00f, _events));
            Assert.AreEqual(0, _events.Count, "被取消的待判单击不补发");
            Assert.IsFalse(_recognizer.Cancel(Classic), "已经没有待判窗口时返回 false");
        }

        [Test]
        public void CancelAll_DropsEveryPendingSingle()
        {
            _recognizer.Tap(Classic, Vector3.zero, 1.00f, _events);
            _recognizer.Tap(Calm, Vector3.zero, 1.05f, _events);

            _recognizer.CancelAll();

            Assert.AreEqual(0, _recognizer.PendingCount);
            Assert.AreEqual(0, _recognizer.Tick(2.00f, _events));
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void Tick_WhenPetBecameUnavailable_DropsPendingSingle()
        {
            var available = true;
            _recognizer.CanInteract = _ => available;

            _recognizer.Tap(Classic, Vector3.zero, 1.00f, _events);
            available = false;

            Assert.AreEqual(0, _recognizer.Tick(1.50f, _events), "窗口到期时该宠已不可交互，单击必须丢弃");
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _recognizer.PendingCount);
        }

        [Test]
        public void CancelUnavailable_DropsOnlyThePetsThatCannotBeTouched()
        {
            _recognizer.CanInteract = petId => petId == Classic;

            _recognizer.Tap(Classic, Vector3.zero, 1.00f, _events);
            _recognizer.Tap(Calm, Vector3.zero, 1.05f, _events);

            Assert.AreEqual(1, _recognizer.CancelUnavailable());
            Assert.IsTrue(_recognizer.HasPendingTap(Classic), "另一只宠物的待判窗口不受影响");
            Assert.IsFalse(_recognizer.HasPendingTap(Calm));
        }

        [Test]
        public void EventIds_IncreaseStrictlyPerPet_AcrossSinglesAndDoubles()
        {
            // classic：单击 → 双击 → 单击。三种都走同一条每宠递增序列。
            _recognizer.Tap(Classic, Vector3.zero, 1.00f, _events);
            _recognizer.Tick(1.50f, _events);
            _recognizer.Tap(Classic, Vector3.zero, 2.00f, _events);
            _recognizer.Tap(Classic, Vector3.zero, 2.10f, _events);
            _recognizer.Tap(Classic, Vector3.zero, 3.00f, _events);
            _recognizer.Tick(3.50f, _events);

            var classicEvents = _events.Where(value => value.PetId == Classic).ToList();
            Assert.AreEqual(3, classicEvents.Count);
            for (var i = 1; i < classicEvents.Count; i++)
            {
                Assert.Greater(classicEvents[i].Id, classicEvents[i - 1].Id,
                    "同一只宠物的事件 Id 必须严格递增，否则行为层会把合法事件当成重复事件丢掉");
            }

            // 另一只宠物有自己的序列，互不吞并。
            var calmEvents = new List<InteractionEvent>();
            _recognizer.Tap(Calm, Vector3.zero, 4.00f, calmEvents);
            _recognizer.Tick(4.50f, calmEvents);
            Assert.AreEqual(1, calmEvents.Count);
            Assert.AreEqual(1, calmEvents[0].Id, "每宠独立编号，从 1 开始");
        }

        [Test]
        public void NonFiniteClock_DoesNotOpenAWindow()
        {
            Assert.IsFalse(_recognizer.Tap(Classic, Vector3.zero, float.NaN, _events));
            Assert.AreEqual(0, _recognizer.PendingCount, "非法时钟不能开窗口，否则单击会被永久挂起");
        }
    }

    /// <summary>
    /// 手势识别 + 多宠会话的组合测试（<see cref="TapGestureRecognizer"/> → <see cref="InteractionPipe"/> →
    /// <see cref="PetSession"/> → <see cref="PetRuntime"/>）。走的是与 AppRoot 相同的调用顺序，
    /// 但不依赖 MonoBehaviour 生命周期与存档，因此能在 EditMode 里稳定复现。
    ///
    /// 覆盖 P3 的验收点：点黄蛙不改蓝蛙、双击只结算一次、窗口内丢失不加分、切后台不补发、
    /// 以及"忙碌由行为层 0.7 s TouchReact 拒绝，而不是输入层冷却"。
    /// </summary>
    public sealed class TapGestureSessionTests
    {
        private readonly List<GameObject> _created = new();
        private readonly Dictionary<string, RecordingPresenter> _presenters = new();

        private PetConfig _config;
        private StubTrackingSource _source;
        private PetSession _session;
        private TapGestureRecognizer _recognizer;
        private InteractionPipe _pipe;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _now = 0f;

            // 默认配置：初始开心值 0.60，classic 单击 +0.06 / 双击 +0.10，TouchReact 0.7 s 单次。
            _config = ScriptableObject.CreateInstance<PetConfig>();
            _config.NeedSeeds[0].DecayPerSecond = 0f;

            var sourceGo = new GameObject("Tracking");
            _created.Add(sourceGo);
            _source = sourceGo.AddComponent<StubTrackingSource>();
            _source.SetTargets(new[]
            {
                Target("card_classic", Vector3.zero),
                Target("card_calm", new Vector3(0.12f, 0f, 0.05f)),
            });
            _source.Clock = () => _now;

            var identities = new List<PetIdentity> { FindIdentity("pet_classic"), FindIdentity("pet_calm") };
            _session = new PetSession(_config, identities, _source, _source, PresenterFor);

            // 与 AppRoot 相同：单击在窗口到期时才结算，用"本帧能否交互"。
            _recognizer = new TapGestureRecognizer(new TapGestureRecognizer.TapGestureSettings(),
                petId => _session.CanInteractThisFrame(petId));
            _pipe = new InteractionPipe(new SessionSink(_session));

            Advance(0.1f); // 让跟踪状态机进入 Tracking，本帧可交互
        }

        [TearDown]
        public void TearDown()
        {
            _presenters.Clear();
            foreach (var go in _created) Object.DestroyImmediate(go);
            _created.Clear();
            Object.DestroyImmediate(_config);
            _now = 0f;
        }

        [Test]
        public void TappingClassic_DoesNotChangeCalm()
        {
            Assert.IsTrue(Tap("pet_classic"));
            Assert.AreEqual(1, FlushWindow());

            Assert.IsTrue(_session.TryGetRuntime("pet_classic", out var classic));
            Assert.IsTrue(_session.TryGetRuntime("pet_calm", out var calm));

            Assert.AreEqual(1, classic.Needs.Snapshot.PetCount);
            Assert.AreEqual(0.66f, classic.Needs.Snapshot.Happiness, 1e-4f, "classic 单击 +0.06");

            Assert.AreEqual(0, calm.Needs.Snapshot.PetCount, "点黄蛙不得改蓝蛙");
            Assert.AreEqual(0.60f, calm.Needs.Snapshot.Happiness, 1e-4f);
            Assert.AreEqual(0, calm.Behavior.AcceptedCount);
        }

        [Test]
        public void DoubleTap_AddsOnlyTheDoubleGainOnce()
        {
            Assert.IsTrue(Tap("pet_classic"));
            Advance(0.08f); // 仍在 0.28 s 窗口内

            var emitted = new List<InteractionEvent>();
            Assert.IsTrue(_recognizer.Tap("pet_classic", Vector3.zero, _now, emitted));
            Assert.AreEqual(1, emitted.Count);
            Assert.AreEqual(PetGesture.DoubleTap, emitted[0].Gesture);
            foreach (var gestureEvent in emitted) _pipe.Submit(gestureEvent);

            Assert.IsTrue(_session.TryGetRuntime("pet_classic", out var classic));
            Assert.AreEqual(1, classic.Needs.Snapshot.PetCount, "双击只算一次交互");
            Assert.AreEqual(0.70f, classic.Needs.Snapshot.Happiness, 1e-4f, "双击只加 +0.10，不叠加单击");
            Assert.AreEqual(1, classic.Behavior.AcceptedCount);
        }

        [Test]
        public void PetLostDuringWindow_ScoresNothing()
        {
            Assert.IsTrue(Tap("pet_classic"));

            _source.SetSimulatedStatus("card_classic", TrackingStatus.Lost);
            for (var i = 0; i < 5; i++) Advance(0.1f); // 去抖确认丢失，窗口也早已过期

            var events = new List<InteractionEvent>();
            Assert.AreEqual(0, _recognizer.Tick(_now, events));
            foreach (var gestureEvent in events) _pipe.Submit(gestureEvent);

            Assert.AreEqual(0, events.Count, "窗口内丢失的待判单击必须在识别器里被丢弃");
            Assert.IsTrue(_session.TryGetRuntime("pet_classic", out var classic));
            Assert.AreEqual(0.60f, classic.Needs.Snapshot.Happiness, 1e-4f);
            Assert.AreEqual(0, classic.Needs.Snapshot.PetCount);
        }

        [Test]
        public void Backgrounding_DropsPendingTapInsteadOfReplayingIt()
        {
            Assert.IsTrue(Tap("pet_classic"));

            // 切后台：闸门关闭 + 全部暂停 + 丢弃待判单击（AppRoot.UpdateForegroundState 的顺序）。
            _pipe.ArAvailable = false;
            _session.PauseAll("应用切到后台", _now);
            _recognizer.CancelAll();

            var events = new List<InteractionEvent>();
            Assert.AreEqual(0, _recognizer.Tick(_now + 1f, events));
            foreach (var gestureEvent in events) _pipe.Submit(gestureEvent);

            Assert.AreEqual(0, events.Count, "恢复后不得补发");
            Assert.AreEqual(0, _pipe.DroppedWhileUnavailable, "不是排队等恢复再提交，而是根本不产生");
            Assert.IsTrue(_session.TryGetRuntime("pet_classic", out var classic));
            Assert.AreEqual(0.60f, classic.Needs.Snapshot.Happiness, 1e-4f);
        }

        [Test]
        public void TapDuringTouchReact_IsRefusedByBehaviorLayer_WithoutScoring()
        {
            Assert.IsTrue(Tap("pet_classic"));
            Assert.AreEqual(1, FlushWindow());

            Assert.IsTrue(_session.TryGetRuntime("pet_classic", out var classic));
            Assert.AreEqual(PetBehaviorId.Petted, classic.CurrentBehavior, "单击后进入 0.7 s TouchReact");
            var happinessAfterFirstTap = classic.Needs.Snapshot.Happiness;

            // 行为时钟没有推进（仍是那 0.7 s 内）：再点一下，事件照常发出但被行为层拒绝。
            Assert.IsTrue(Tap("pet_classic"));
            Assert.AreEqual(1, FlushWindow(), "输入层不节流，事件仍然发出");

            Assert.AreEqual(1, classic.Behavior.AcceptedCount);
            Assert.AreEqual(1, classic.Behavior.RefusedCount, "忙碌期间的点按由行为层拒绝");
            Assert.AreEqual(happinessAfterFirstTap, classic.Needs.Snapshot.Happiness, 1e-4f,
                "被拒绝的点按不得加分，也不排队补发");
        }

        // ── 辅助 ────────────────────────────────────────────────────────────────

        /// <summary>一次合格抬起：时间取当前假时钟。</summary>
        private bool Tap(string petId) => _recognizer.Tap(petId, Vector3.zero, _now, null);

        /// <summary>
        /// 把窗口推过期并提交到期的单击。行为时钟取 0 增量，
        /// 这样 TouchReact 的忙碌状态只由测试自己推进，不会被辅助函数偷偷结束。
        /// </summary>
        private int FlushWindow()
        {
            _now += 0.5f;
            _session.Tick(_now, 0f, trackingRunning: true);

            var events = new List<InteractionEvent>();
            var emitted = _recognizer.Tick(_now, events);
            foreach (var gestureEvent in events) _pipe.Submit(gestureEvent);
            return emitted;
        }

        private void Advance(float deltaTime)
        {
            _now += deltaTime;
            _session.Tick(_now, deltaTime, trackingRunning: true);
            _pipe.ArAvailable = true;
        }

        private IPetPresenter PresenterFor(string petId)
        {
            if (_presenters.TryGetValue(petId, out var existing)) return existing;

            var presenter = new RecordingPresenter();
            _presenters.Add(petId, presenter);
            return presenter;
        }

        private static PetIdentity FindIdentity(string petId)
        {
            foreach (var identity in PetIdentityCatalog.Default)
            {
                if (identity.PetId == petId) return identity;
            }

            throw new System.InvalidOperationException("身份目录里没有 " + petId);
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

        /// <summary>把事件交给会话的接收器（与 AppRoot.TrySubmit 同样的路由）。</summary>
        private sealed class SessionSink : IInteractionEventSink
        {
            private readonly PetSession _session;

            public SessionSink(PetSession session) => _session = session;

            public bool TrySubmit(in InteractionEvent interactionEvent) => _session.TrySubmit(interactionEvent);
        }

        private sealed class RecordingPresenter : IPetPresenter
        {
            public PetBehaviorId LastBehavior { get; private set; } = PetBehaviorId.None;

            public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds)
                => LastBehavior = behavior;

            public void SetExpression(PetExpression expression, float intensity) { }

            public void PlaySfx(string sfxId) { }

            public void PlayFx(string fxId) { }

            public void SetPlaybackSpeed(float speed) { }

            public void SetInteractable(bool interactable) { }

            public void SetTrackingVisibility(bool visible) { }
        }
    }

    /// <summary>
    /// 状态面板的百分比与文案格式化。百分比必须向下取整：
    /// 99.9% 显示 99%，只有内部值真的 ≥ 1 才显示 100%——界面不能提前宣布"已满"。
    /// </summary>
    public sealed class PetStatusPanelFormattingTests
    {
        [TestCase(0.999f, 99)]
        [TestCase(1f, 100)]
        [TestCase(0.06f, 6)]
        [TestCase(0f, 0)]
        [TestCase(-0.5f, 0)]
        [TestCase(float.NaN, 0)]
        public void PercentValue_FloorsToWholePercent(float happiness, int expected)
        {
            Assert.AreEqual(expected, PetStatusPanel.PercentValue(happiness));
        }

        [Test]
        public void Percent_FormatsWithPercentSign()
        {
            Assert.AreEqual("99%", PetStatusPanel.Percent(0.999f));
            Assert.AreEqual("100%", PetStatusPanel.Percent(1f));
            Assert.AreEqual("6%", PetStatusPanel.Percent(0.06f));
        }

        [Test]
        public void FormatBehavior_DistinguishesBusyHappyAndPendingConfirm()
        {
            Assert.AreEqual("Busy", PetStatusPanel.FormatBehavior(PetBehaviorId.Petted, HappyCycleState.Armed));
            Assert.AreEqual("Happy!", PetStatusPanel.FormatBehavior(PetBehaviorId.Exclusive, HappyCycleState.Armed));
            Assert.AreEqual("Full - tap to celebrate",
                PetStatusPanel.FormatBehavior(PetBehaviorId.Idle, HappyCycleState.PendingConfirm));
        }
    }
}
