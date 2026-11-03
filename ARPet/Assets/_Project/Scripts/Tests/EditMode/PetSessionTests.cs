using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZQXNXS.ARPet.App;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 多宠运行容器（<see cref="PetSession"/>）的关键规则测试。覆盖的是"错了会让两只宠物互相干扰"
    /// 的部分：逐宠跟踪独立、丢失/恢复不重建运行时、事件按 PetId 路由、全局暂停。
    ///
    /// 用 <see cref="StubTrackingSource"/> + 记录型假表现层，不需要真实场景、Vuforia 或动画资产。
    /// </summary>
    public sealed class PetSessionTests
    {
        private readonly List<GameObject> _created = new();
        private readonly Dictionary<string, RecordingPresenter> _presenters = new();

        private PetConfig _config;
        private StubTrackingSource _source;

        /// <summary>
        /// 测试时钟。EditMode 下 <c>Time.time</c> 基本不前进，而 <c>Time.time</c> 是只读的、
        /// 不能赋值，因此这里用显式递增的字段，并通过 <see cref="MarkerRegistryBase.Clock"/>
        /// 注入给模拟跟踪源。运行期不注入，仍是 <c>Time.time</c>。
        /// </summary>
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _now = 0f;

            _config = ScriptableObject.CreateInstance<PetConfig>();
            // 大部分用例只关心"丢失/恢复不重置数值"，关掉衰减以免干扰断言。
            // 需要验证衰减的用例会自己改回来。
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

        // ── 组装 ────────────────────────────────────────────────────────────────

        [Test]
        public void TwoPets_AreAssembledWithIndependentTracking()
        {
            var session = CreateSession("pet_classic", "pet_calm");

            Assert.IsTrue(session.IsReady);
            Assert.AreEqual(2, session.PetIds.Count);
            Assert.AreEqual("pet_classic", session.PetIds[0]);
            Assert.AreEqual("pet_calm", session.PetIds[1]);
            Assert.AreEqual("pet_classic", session.SelectedPetId, "初始选中第一只可用宠物");

            Tick(session, 0.1f);

            Assert.AreEqual(TrackingUiState.Tracking, session.TrackingStateOf("pet_classic"));
            Assert.AreEqual(TrackingUiState.Tracking, session.TrackingStateOf("pet_calm"));
            Assert.IsTrue(session.CanInteract("pet_classic", _now));
            Assert.IsTrue(session.CanInteract("pet_calm", _now));
            Assert.IsTrue(_presenters["pet_classic"].Visible);
            Assert.IsTrue(_presenters["pet_calm"].Visible);
        }

        [Test]
        public void DuplicateIdentities_AreRejectedAtConstruction()
        {
            LogAssert.Expect(LogType.Error, new Regex("重复 PetId"));

            var identities = new List<PetIdentity>
            {
                FindIdentity("pet_classic"),
                FindIdentity("pet_classic"),
            };
            var session = new PetSession(_config, identities, _source, _source, PresenterFor);

            Assert.IsFalse(session.IsReady, "身份目录不合法时应整体拒绝，而不是装出两只同 ID 的宠物");
            Assert.AreEqual(0, session.PetIds.Count);
            Assert.AreEqual(string.Empty, session.SelectedPetId);
        }

        [Test]
        public void PetWithUnregisteredTarget_IsSkipped_OthersStillRun()
        {
            // card_divine 没有在本文件的模拟源里注册：该宠被跳过并报错，其余宠物继续。
            LogAssert.Expect(LogType.Error, new Regex("pet_divine"));

            var session = CreateSession("pet_classic", "pet_divine");

            Assert.IsTrue(session.IsReady);
            CollectionAssert.AreEqual(new[] { "pet_classic" }, session.PetIds);
            Tick(session, 0.1f);
            Assert.AreEqual(TrackingUiState.Tracking, session.TrackingStateOf("pet_classic"));
            Assert.AreEqual(TrackingUiState.Searching, session.TrackingStateOf("pet_divine"));
        }

        // ── 逐宠丢失 / 恢复 ─────────────────────────────────────────────────────

        [Test]
        public void OnePetLost_DoesNotAffectTheOther_AndKeepsRuntimeInstance()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);

            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var calmBefore));
            calmBefore.Needs.Apply(PetNeed.Happiness, 0.2f, _now);
            var boosted = calmBefore.Needs.Snapshot.Happiness;

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);
            for (var i = 0; i < 5; i++) Tick(session, 0.1f);

            Assert.AreEqual(TrackingUiState.Lost, session.TrackingStateOf("pet_calm"));
            Assert.IsFalse(_presenters["pet_calm"].Visible, "丢失的宠物必须隐藏渲染");
            Assert.IsFalse(session.CanInteract("pet_calm", _now));

            Assert.AreEqual(TrackingUiState.Tracking, session.TrackingStateOf("pet_classic"),
                "另一只宠物必须继续正常跟踪");
            Assert.IsTrue(_presenters["pet_classic"].Visible);
            Assert.IsTrue(session.CanInteract("pet_classic", _now), "另一只宠物必须仍可交互");

            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var calmAfter));
            Assert.AreSame(calmBefore, calmAfter, "丢失不得重建 PetRuntime");
            Assert.AreEqual(boosted, calmAfter.Needs.Snapshot.Happiness, 0.0001f, "丢失不得重置开心值");
        }

        [Test]
        public void LimitedTracking_KeepsPetVisible_ButBlocksInteraction()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(_presenters["pet_calm"].Visible);

            // 卡仍在画面里、只是跟踪质量下降：角色照常显示（不闪），但不能被点、不开放交互。
            _source.SetSimulatedStatus("card_calm", TrackingStatus.Limited);
            for (var i = 0; i < 5; i++) Tick(session, 0.1f);
            Assert.IsTrue(_presenters["pet_calm"].Visible, "LIMITED 时角色应继续显示，避免 TRACKED/LIMITED 抖动闪烁");
            Assert.IsFalse(_presenters["pet_calm"].Interactable, "LIMITED 时碰撞体关闭，不能被点中");
            Assert.IsFalse(session.CanInteract("pet_calm", _now), "LIMITED 不可结算");
            Assert.IsFalse(session.CanInteractThisFrame("pet_calm"));

            // 延伸跟踪 = 卡已不在画面里，必须隐藏。
            _source.SetSimulatedStatus("card_calm", TrackingStatus.ExtendedTracked);
            Tick(session, 0.1f);
            Assert.IsFalse(_presenters["pet_calm"].Visible, "延伸跟踪不代表卡可见，必须隐藏");

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Tracked);
            Tick(session, 0.1f);
            Assert.IsTrue(_presenters["pet_calm"].Visible);
            Assert.IsTrue(_presenters["pet_calm"].Interactable);
        }

        [Test]
        public void LostPet_FreezesBehaviorClock_ButKeepsDecayingHappiness()
        {
            _config.NeedSeeds[0].DecayPerSecond = -0.01f;

            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var calm));

            var idleElapsedBefore = calm.Behavior.ElapsedSeconds;
            var happinessBefore = calm.Needs.Snapshot.Happiness;

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);
            for (var i = 0; i < 5; i++) Tick(session, 0.1f);

            Assert.AreEqual(idleElapsedBefore, calm.Behavior.ElapsedSeconds, 0.0001f,
                "跟踪无效时必须冻结行为时钟，不能快进补帧");
            Assert.Less(calm.Needs.Snapshot.Happiness, happinessBefore, "开心值仍随时间衰减");
        }

        [Test]
        public void RecoveredPet_ReturnsToTracking_WithSameRuntimeAndVisibility()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var before));

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);
            for (var i = 0; i < 5; i++) Tick(session, 0.1f);
            Assert.AreEqual(TrackingUiState.Lost, session.TrackingStateOf("pet_calm"));

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Tracked);

            Tick(session, 0.05f);
            Assert.AreEqual(TrackingUiState.Recovered, session.TrackingStateOf("pet_calm"),
                "丢失后重新识别应先进入恢复提示态");
            Assert.IsTrue(_presenters["pet_calm"].Visible, "恢复后必须重新显示");
            Assert.IsTrue(session.CanInteract("pet_calm", _now));

            // 恢复提示保持结束后回到正常跟踪；期间不得重建运行时。
            for (var i = 0; i < 20; i++) Tick(session, 0.1f);
            Assert.AreEqual(TrackingUiState.Tracking, session.TrackingStateOf("pet_calm"));

            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var after));
            Assert.AreSame(before, after, "恢复不得重建 PetRuntime（数值与去重状态必须保留）");
        }

        // ── 事件路由 ────────────────────────────────────────────────────────────

        [Test]
        public void Events_AreRoutedToOwningPetOnly()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(session.TryGetRuntime("pet_classic", out var classic));
            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var calm));

            Assert.IsTrue(session.Select("pet_calm"), "先把选中项切到另一只，才能看出事件路由是否改变了选中");

            var accepted = session.TrySubmit(
                InteractionEvent.Create(1, InteractionEventKind.Pet, "pet_classic", _now));

            // TouchReact 已进入能力清单，触屏事件会被接受一次并只结算到归属宠物。
            Assert.IsTrue(accepted);
            Assert.AreEqual(1, classic.Needs.Snapshot.PetCount, "事件必须进入 classic 的行为状态机");
            Assert.AreEqual(0, calm.Needs.Snapshot.PetCount, "别的宠物不得收到这条事件");
            Assert.AreEqual(0, calm.Behavior.AcceptedCount);
            Assert.AreEqual(0.60f, calm.Needs.Snapshot.Happiness, 0.0001f, "别宠物的开心值不得变化");
            Assert.AreEqual("pet_classic", session.SelectedPetId, "被接受的事件把选中切到归属宠物");
        }

        [Test]
        public void UnknownPetId_IsRejectedWithoutTouchingAnyPet()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(session.TryGetRuntime("pet_classic", out var classic));
            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var calm));

            Assert.IsFalse(session.TrySubmit(
                InteractionEvent.Create(2, InteractionEventKind.Pet, "pet_unknown", _now)));

            Assert.AreEqual(0, classic.Behavior.RefusedCount);
            Assert.AreEqual(0, calm.Behavior.RefusedCount);
        }

        [Test]
        public void EventForLostPet_IsRejectedBeforeReachingBehavior()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var calm));

            _source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);
            for (var i = 0; i < 5; i++) Tick(session, 0.1f);
            Assert.AreEqual(TrackingUiState.Lost, session.TrackingStateOf("pet_calm"));

            Assert.IsFalse(session.TrySubmit(
                InteractionEvent.Create(3, InteractionEventKind.Pet, "pet_calm", _now)));
            Assert.AreEqual(0, calm.Behavior.RefusedCount, "不在场的宠物的事件应在路由层拒绝，不进行为层");
        }

        // ── 全局暂停 / 选中 ─────────────────────────────────────────────────────

        [Test]
        public void GlobalTrackingStop_MakesEveryPetNonInteractable()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(session.CanInteract("pet_classic", _now));

            Tick(session, 0.1f, trackingRunning: false);

            Assert.IsFalse(session.CanInteract("pet_classic", _now));
            Assert.IsFalse(session.CanInteract("pet_calm", _now));
            Assert.IsFalse(session.CanInteractThisFrame("pet_classic"));
            Assert.IsFalse(session.CanInteractThisFrame("pet_calm"));
            Assert.IsFalse(_presenters["pet_classic"].Visible);
            Assert.IsFalse(_presenters["pet_calm"].Interactable);
            Assert.IsFalse(session.TrySubmit(
                InteractionEvent.Create(4, InteractionEventKind.Pet, "pet_classic", _now)));
        }

        [Test]
        public void PauseAll_InterruptsAndHidesEveryPet_WithoutRebuildingRuntime()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(session.TryGetRuntime("pet_classic", out var before));

            session.PauseAll("应用切到后台", _now);

            Assert.AreEqual(TrackingUiState.Lost, session.TrackingStateOf("pet_classic"));
            Assert.IsFalse(session.CanInteractThisFrame("pet_classic"));
            Assert.IsFalse(_presenters["pet_classic"].Visible);
            Assert.IsFalse(_presenters["pet_classic"].Interactable);

            Assert.IsTrue(session.TryGetRuntime("pet_classic", out var after));
            Assert.AreSame(before, after, "暂停不得重建运行时");
        }

        [Test]
        public void Select_ChangesUiFocusWithoutScoring()
        {
            var session = CreateSession("pet_classic", "pet_calm");
            Tick(session, 0.1f);
            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var calm));
            var happiness = calm.Needs.Snapshot.Happiness;

            Assert.IsTrue(session.Select("pet_calm"));
            Assert.AreEqual("pet_calm", session.SelectedPetId);
            Assert.AreEqual(happiness, calm.Needs.Snapshot.Happiness, 0.0001f, "选择宠物不得加分");

            Assert.IsFalse(session.Select("pet_unknown"));
            Assert.AreEqual("pet_calm", session.SelectedPetId, "未知宠物不得改变选中项");
        }

        // ── 辅助 ────────────────────────────────────────────────────────────────

        private PetSession CreateSession(params string[] petIds)
        {
            var identities = new List<PetIdentity>();
            foreach (var petId in petIds) identities.Add(FindIdentity(petId));
            return new PetSession(_config, identities, _source, _source, PresenterFor);
        }

        private void Tick(PetSession session, float deltaTime, bool trackingRunning = true)
        {
            _now += deltaTime;
            session.Tick(_now, deltaTime, trackingRunning);
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

        /// <summary>记录型假表现层：只记状态变化次数与最近值，不做任何渲染。</summary>
        private sealed class RecordingPresenter : IPetPresenter
        {
            public bool Visible { get; private set; } = true;

            public bool Interactable { get; private set; } = true;

            public int VisibilityChanges { get; private set; }

            public PetBehaviorId LastBehavior { get; private set; } = PetBehaviorId.None;

            public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds)
                => LastBehavior = behavior;

            public void SetExpression(PetExpression expression, float intensity) { }

            public void PlaySfx(string sfxId) { }

            public void PlayFx(string fxId) { }

            public void SetPlaybackSpeed(float speed) { }

            public void SetInteractable(bool interactable)
            {
                Interactable = interactable;
            }

            public void SetTrackingVisibility(bool visible)
            {
                if (Visible == visible) return;
                Visible = visible;
                VisibilityChanges++;
            }
        }
    }
}
