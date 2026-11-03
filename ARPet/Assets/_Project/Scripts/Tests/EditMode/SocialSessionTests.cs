using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.App;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 共同大笑的<b>装配级</b>测试：真实的 <see cref="PetSession"/> + <see cref="StubTrackingSource"/> +
    /// 代理 Transform，把"两卡靠近 → 同帧开演 → 看完记账 → 分开再靠近再演一次"整条链路走完。
    ///
    /// 与 <c>ProximityRuleTests</c>（纯几何）和 <c>SocialCoordinatorTests</c>（纯编排）互补：
    /// 这里守护的是"接起来才出错"的部分——输入装配口径、开始后锁组、丢失/暂停整组取消、
    /// 三宠链式合组、第二张卡直接放到旁边也能开演、拿出画面满 2s 后重新武装。
    ///
    /// 时钟一律显式注入（<see cref="MarkerRegistryBase.Clock"/>），不依赖 EditMode 下几乎不前进的
    /// <c>Time.time</c>；也不调用 <c>StartTracking</c>，避免模拟源在等第一帧采样时给出 Initializing。
    /// </summary>
    public sealed class SocialSessionTests
    {
        private const string Classic = "pet_classic";
        private const string Calm = "pet_calm";
        private const string Rage = "pet_rage";
        private const string Divine = "pet_divine";

        private readonly List<GameObject> _created = new();
        private readonly Dictionary<string, RecordingPresenter> _presenters = new();
        private readonly Dictionary<string, Transform> _proxies = new();

        private PetConfig _config;
        private StubTrackingSource _source;
        private PetSession _session;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _now = 0f;
            _config = ScriptableObject.CreateInstance<PetConfig>();
            _config.NeedSeeds[0].DecayPerSecond = 0f;

            var sourceGo = new GameObject("Tracking");
            _created.Add(sourceGo);
            _source = sourceGo.AddComponent<StubTrackingSource>();
            _source.SetTargets(new[]
            {
                Target("card_classic"),
                Target("card_calm"),
                Target("card_rage"),
                Target("card_divine"),
            });
            _source.Clock = () => _now;
        }

        [TearDown]
        public void TearDown()
        {
            _session = null;
            _presenters.Clear();
            _proxies.Clear();
            foreach (var go in _created) Object.DestroyImmediate(go);
            _created.Clear();
            Object.DestroyImmediate(_config);
            _now = 0f;
        }

        // ── 一次完整会话 ─────────────────────────────────────────────────────────

        [Test]
        public void ApproachAndHold_StartsOneSession_CountsOnce_AndDoesNotReplayWhileLocked()
        {
            _session = CreateSession(Classic, Calm);
            Place(Classic, 0f);
            Place(Calm, 0.30f);
            Step(0.1f);
            Assert.AreEqual(0, _session.Social.ActiveSessions.Count, "还没靠近时不得开演");
            Assert.IsFalse(_session.Proximity.IsLocked(Pair(Classic, Calm)));

            Place(Calm, 0.15f);
            Steps(6, 0.1f);   // 连续停留满 0.6s

            Assert.AreEqual(1, _session.Social.ActiveSessions.Count, "两只同时进入共同大笑");
            var sessionId = _session.Social.ActiveSessions[0].SessionId;
            Assert.AreEqual(PetBehaviorId.Social, Runtime(Classic).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Social, Runtime(Calm).CurrentBehavior);
            Assert.AreEqual(sessionId, Runtime(Classic).ActiveSocialSessionId, "同一会话 ID");
            Assert.AreEqual(sessionId, Runtime(Calm).ActiveSocialSessionId);
            Assert.IsTrue(_session.IsInSocial(Classic));
            Assert.IsTrue(_session.IsInSocial(Calm));
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Classic, Calm)), "开始时锁住该对");
            Assert.AreEqual(0.6f, Runtime(Classic).Snapshot.Happiness, 0.0001f, "共同大笑不加开心值");

            Step(2.4f);   // 2.4s 正常播完

            Assert.AreEqual(PetBehaviorId.Idle, Runtime(Classic).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Idle, Runtime(Calm).CurrentBehavior);
            Assert.AreEqual(0, _session.Social.ActiveSessions.Count);
            Assert.IsFalse(_session.IsInSocial(Classic));
            Assert.AreEqual(1, Runtime(Classic).Snapshot.SocialWatchedCount, "每只各记一次看完");
            Assert.AreEqual(1, Runtime(Calm).Snapshot.SocialWatchedCount);

            Step(10f);   // 继续贴着不动
            Assert.AreEqual(0, _session.Social.ActiveSessions.Count, "同一次接近只触发一次");
            Assert.AreEqual(1, Runtime(Classic).Snapshot.SocialWatchedCount, "没有第二次会话就没有第二次记账");

            // 真实分开 0.4s（> 0.3s）解锁，再靠近 0.6s → 第二次会话（个人冷却 5s 早已过去）。
            Place(Calm, 0.30f);
            Steps(4, 0.1f);
            Assert.IsFalse(_session.Proximity.IsLocked(Pair(Classic, Calm)), "确认分开后重新武装");

            Place(Calm, 0.15f);
            Steps(6, 0.1f);
            Assert.AreEqual(1, _session.Social.ActiveSessions.Count, "分开再靠近可以再演一次");
            Assert.AreNotEqual(sessionId, _session.Social.ActiveSessions[0].SessionId, "新一轮接近是新会话");

            Step(2.4f);
            Assert.AreEqual(2, Runtime(Classic).Snapshot.SocialWatchedCount);
            Assert.AreEqual(2, Runtime(Calm).Snapshot.SocialWatchedCount);
        }

        // ── 取消：丢失 / 暂停 ────────────────────────────────────────────────────

        [Test]
        public void MemberLost_CancelsWholeSession_WithoutCounting_AndRecoveryDoesNotReplay()
        {
            _session = CreateSession(Classic, Calm);
            StartTwoPetSession();

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);
            Step(0.1f);

            Assert.AreEqual(0, _session.Social.ActiveSessions.Count, "任一成员丢失即整组取消");
            Assert.AreEqual(PetBehaviorId.Idle, Runtime(Classic).CurrentBehavior, "仍可见的一只回 Idle");
            Assert.AreEqual(PetBehaviorId.Idle, Runtime(Calm).CurrentBehavior);
            Assert.AreEqual(0, Runtime(Classic).Snapshot.SocialWatchedCount, "取消不计看完");
            Assert.AreEqual(0, Runtime(Calm).Snapshot.SocialWatchedCount);

            // 恢复并保持贴着：锁还没解，不能重开。
            _source.SetSimulatedStatus("card_calm", TrackingStatus.Tracked);
            Steps(10, 0.1f);

            Assert.AreEqual(0, _session.Social.ActiveSessions.Count, "恢复不伪造重新进入");
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Classic, Calm)), "丢失不解锁");
            Assert.AreEqual(0, Runtime(Classic).Snapshot.SocialWatchedCount);
        }

        [Test]
        public void PauseAll_CancelsSocial_AndDoesNotRestartAfterResume()
        {
            _session = CreateSession(Classic, Calm);
            StartTwoPetSession();

            _session.PauseAll("应用切到后台", _now);

            Assert.AreEqual(0, _session.Social.ActiveSessions.Count, "切后台整组取消");
            Assert.AreEqual(PetBehaviorId.Idle, Runtime(Classic).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Idle, Runtime(Calm).CurrentBehavior);
            Assert.AreEqual(0, Runtime(Classic).Snapshot.SocialWatchedCount);

            Steps(10, 0.1f);   // 回来后两卡仍在原处
            Assert.AreEqual(0, _session.Social.ActiveSessions.Count, "暂停不是离开，回来不自动重开");
            Assert.AreEqual(0, Runtime(Classic).Snapshot.SocialWatchedCount);
        }

        // ── 多宠分组 ─────────────────────────────────────────────────────────────

        [Test]
        public void ChainOfThreePets_FormsOneThreePetSession()
        {
            _session = CreateSession(Classic, Calm, Rage);
            Place(Classic, 0f);
            Place(Calm, 0.30f);
            Place(Rage, 0.60f);
            Step(0.1f);

            // A-B 0.15、B-C 0.15、A-C 0.30：链式相连，合成一个三宠会话。
            Place(Calm, 0.15f);
            Place(Rage, 0.30f);
            Steps(6, 0.1f);

            Assert.AreEqual(1, _session.Social.ActiveSessions.Count, "ABC 链只产生一个会话");
            var session = _session.Social.ActiveSessions[0];
            CollectionAssert.AreEqual(new[] { Calm, Classic, Rage }, session.Members);

            var sessionId = session.SessionId;
            Assert.AreEqual(PetBehaviorId.Social, Runtime(Classic).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Social, Runtime(Calm).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Social, Runtime(Rage).CurrentBehavior);
            Assert.AreEqual(sessionId, Runtime(Classic).ActiveSocialSessionId);
            Assert.AreEqual(sessionId, Runtime(Calm).ActiveSocialSessionId);
            Assert.AreEqual(sessionId, Runtime(Rage).ActiveSocialSessionId);

            // 组内所有关系对都要锁住，否则 AB / BC 会在下一帧各自重开。
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Classic, Calm)));
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Calm, Rage)));
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Classic, Rage)));
        }

        [Test]
        public void SecondCardPlacedBesideFirst_StartsSession()
        {
            // 2026-10-30 真机 BUG-04：先放暖黄，再把湖蓝直接放到 15cm 旁边，按演示脚本两只要一起笑。
            _session = CreateSession(Classic, Calm);
            _source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);
            Place(Classic, 0f);
            Place(Calm, 0.15f);
            Steps(10, 0.1f);
            Assert.AreEqual(0, _session.Social.ActiveSessions.Count, "只有一张卡在画面里");

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Tracked);
            Steps(6, 0.1f);

            Assert.AreEqual(1, _session.Social.ActiveSessions.Count, "第二张放下时已贴近，停留 0.6s 后开演");
            Assert.AreEqual(PetBehaviorId.Social, Runtime(Classic).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Social, Runtime(Calm).CurrentBehavior);
        }

        [Test]
        public void AlreadyTouchingAtStartup_ThreePets_StartOneSession()
        {
            _session = CreateSession(Classic, Calm, Rage);
            Place(Classic, 0f);
            Place(Calm, 0.15f);
            Place(Rage, 0.30f);

            Steps(6, 0.1f);

            Assert.AreEqual(1, _session.Social.ActiveSessions.Count);
            Assert.AreEqual(3, _session.Social.ActiveSessions[0].Members.Count, "三只一起笑");
        }

        [Test]
        public void AfterLaugh_TakingOneCardOutOfFrame_RearmsThePair()
        {
            // 竖屏下两卡同时入镜拉开 0.23m 很难做到；拿走一张卡 2s 以上再放回也能再演一次。
            _session = CreateSession(Classic, Calm);
            StartTwoPetSession();
            Step(2.4f);
            Assert.AreEqual(1, Runtime(Classic).Snapshot.SocialWatchedCount);
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Classic, Calm)));

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);
            Steps(21, 0.1f);
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Classic, Calm)), "拿走期间仍锁着，放回时才判断");

            // 放回旁边但换了位置（15cm → 10cm）。
            Place(Calm, 0.10f);
            _source.SetSimulatedStatus("card_calm", TrackingStatus.Tracked);
            Steps(7, 0.1f);
            Assert.AreEqual(1, _session.Social.ActiveSessions.Count, "拿走 2s 以上再换位放回，再演一次");
        }

        [Test]
        public void FourCardsStill_EdgeCardFlickering_DoesNotLoopLaughter()
        {
            // 2026-10-30 真机 O-10：四卡同屏不动时，边缘卡在 LIMITED / 丢失之间反复，
            // 旧规则下每 5 秒笑一轮。现在：原地回来不解锁，整段时间只演一次。
            _session = CreateSession(Classic, Calm);
            StartTwoPetSession();
            Step(2.4f);

            for (var round = 0; round < 6; round++)
            {
                _source.SetSimulatedStatus("card_calm", round % 2 == 0 ? TrackingStatus.Lost : TrackingStatus.Limited);
                Steps(25, 0.1f);
                _source.SetSimulatedStatus("card_calm", TrackingStatus.Tracked);
                Steps(30, 0.1f);
            }

            Assert.AreEqual(1, Runtime(Classic).Snapshot.SocialWatchedCount, "卡没动就只笑一次");
            Assert.AreEqual(0, _session.Social.ActiveSessions.Count);
        }

        [Test]
        public void PauseLongerThanLostThreshold_DoesNotRearm()
        {
            _session = CreateSession(Classic, Calm);
            StartTwoPetSession();
            Step(2.4f);
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Classic, Calm)));

            // 切后台 5s：全局暂停不是"拿出画面"，回来两卡仍贴着也不能重演。
            _session.PauseAll("应用切到后台", _now);
            for (var i = 0; i < 50; i++)
            {
                _now += 0.1f;
                _session.Tick(_now, 0.1f, trackingRunning: false);
                _session.TickSocial(_now, 0.1f, trackingRunning: false);
            }

            Steps(10, 0.1f);
            Assert.IsTrue(_session.Proximity.IsLocked(Pair(Classic, Calm)), "暂停时长不计入丢失计时");
            Assert.AreEqual(0, _session.Social.ActiveSessions.Count);
        }

        [Test]
        public void SinglePet_HasSocialComponentsButNoPairs()
        {
            _session = CreateSession(Classic);
            Step(1f);

            Assert.IsNotNull(_session.Social);
            Assert.IsNotNull(_session.Proximity);
            Assert.AreEqual(0, _session.Social.ActiveSessions.Count);
            CollectionAssert.IsEmpty(_session.Proximity.QualifiedPairs, "单宠永远不会产生关系对");
            Assert.AreEqual(PetBehaviorId.Idle, Runtime(Classic).CurrentBehavior);
        }

        // ── 会话期间点按 ─────────────────────────────────────────────────────────

        [Test]
        public void TapDuringSocial_IsRefusedWithoutScoring()
        {
            _session = CreateSession(Classic, Calm);
            StartTwoPetSession();

            var classic = Runtime(Classic);
            var before = classic.Snapshot.Happiness;
            Assert.IsFalse(_session.TrySubmit(InteractionEvent.Create(1, InteractionEventKind.Pet, Classic, _now)),
                "共同大笑期间点按被拒绝");
            Assert.AreEqual(before, classic.Snapshot.Happiness, 0.0001f, "被拒绝的点按不得加分");
            Assert.AreEqual(PetBehaviorId.Social, classic.CurrentBehavior, "仍在演共同大笑");
        }

        // ── 辅助 ──────────────────────────────────────────────────────────────────

        /// <summary>把两只宠物按"先分开、再靠近、停留 0.6s"的标准流程送进会话。</summary>
        private void StartTwoPetSession()
        {
            Place(Classic, 0f);
            Place(Calm, 0.30f);
            Step(0.1f);
            Place(Calm, 0.15f);
            Steps(6, 0.1f);

            Assert.AreEqual(1, _session.Social.ActiveSessions.Count, "前置条件：两只已经进入共同大笑");
            Assert.AreEqual(PetBehaviorId.Social, Runtime(Classic).CurrentBehavior);
        }

        /// <summary>一帧 = 一次显式时钟推进 + 行为推进 + 社交推进（与 AppRoot.Update 的顺序一致）。</summary>
        private void Step(float deltaTime)
        {
            _now += deltaTime;
            _session.Tick(_now, deltaTime, trackingRunning: true);
            _session.TickSocial(_now, deltaTime, trackingRunning: true);
        }

        private void Steps(int count, float deltaTime)
        {
            for (var i = 0; i < count; i++) Step(deltaTime);
        }

        /// <summary>移动某只宠物的模拟卡（代理 Transform）。位姿直接来自它，不需要重建目标表。</summary>
        private void Place(string petId, float x) => _proxies[petId].position = new Vector3(x, 0f, 0f);

        private PetRuntime Runtime(string petId)
        {
            Assert.IsTrue(_session.TryGetRuntime(petId, out var runtime), "缺少运行时：" + petId);
            return runtime;
        }

        private static PetPair Pair(string first, string second) => new PetPair(first, second);

        private PetSession CreateSession(params string[] petIds)
        {
            var identities = new List<PetIdentity>();
            foreach (var petId in petIds) identities.Add(FindIdentity(petId));
            return new PetSession(_config, identities, _source, _source, PresenterFor);
        }

        private IPetPresenter PresenterFor(string petId)
        {
            if (_presenters.TryGetValue(petId, out var existing)) return existing;

            var presenter = new RecordingPresenter();
            _presenters.Add(petId, presenter);
            return presenter;
        }

        private StubTrackingSource.StubTarget Target(string targetId)
        {
            var proxy = new GameObject(targetId + "Proxy").transform;
            _created.Add(proxy.gameObject);
            _proxies[targetId.Replace("card_", "pet_")] = proxy;

            return new StubTrackingSource.StubTarget
            {
                targetId = targetId,
                proxy = proxy,
                size = new Vector3(0.066f, 0f, 0.094f),
                simulatedStatus = TrackingStatus.Tracked,
            };
        }

        private static PetIdentity FindIdentity(string petId)
        {
            foreach (var identity in PetIdentityCatalog.Default)
            {
                if (string.Equals(identity.PetId, petId, System.StringComparison.Ordinal)) return identity;
            }

            throw new System.InvalidOperationException("身份目录里没有 " + petId);
        }

        /// <summary>记录型假表现层：只记最近一次播放指令，不做渲染。</summary>
        private sealed class RecordingPresenter : IPetPresenter
        {
            public PetBehaviorId LastBehavior { get; private set; } = PetBehaviorId.None;

            public string LastAnimationName { get; private set; }

            public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds)
            {
                LastBehavior = behavior;
                LastAnimationName = animationName;
            }

            public void SetExpression(PetExpression expression, float intensity) { }

            public void PlaySfx(string sfxId) { }

            public void PlayFx(string fxId) { }

            public void SetPlaybackSpeed(float speed) { }

            public void SetInteractable(bool interactable) { }

            public void SetTrackingVisibility(bool visible) { }
        }
    }
}
