using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.App;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 语音口令（2026-10-29）：识别结果 → 每只在场宠物一个语音事件 → 行为层唯一结算。
    /// 识别模型本身不在这里测（它的离线验证记录见 Docs/项目使用与调试说明.md），
    /// 这里守护的是"接起来才会错"的业务规则：加分等同单击、口令冷却、忙碌拒绝、只发给在场的宠物、
    /// 跨越满值时走专属演出、共同大笑整组只出一次声。
    /// </summary>
    public sealed class VoiceCommandTests
    {
        private PetConfig _config;
        private readonly Dictionary<string, RecordingPresenter> _presenters = new();
        private readonly List<GameObject> _created = new();
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PetConfig>();
            _config.NeedSeeds[0].DecayPerSecond = 0f;
            _presenters.Clear();
            _now = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created) UnityEngine.Object.DestroyImmediate(go);
            _created.Clear();
            UnityEngine.Object.DestroyImmediate(_config);
        }

        // ── 配置 ────────────────────────────────────────────────────────────────

        [Test]
        public void DefaultCommands_CoverTheFourRecordings_AndAreReady()
        {
            foreach (var (phrase, id) in new[] { ("安迪", "andi"), ("嘎嘎爹拉筛", "gaga"), ("雷霆大笑", "leiting"), ("欧拉", "oula") })
            {
                Assert.IsTrue(_config.TryGetVoiceCommand(phrase, out var byPhrase), phrase + " 必须能按原文查到");
                Assert.AreEqual(id, byPhrase.CommandId);
                Assert.IsTrue(_config.TryGetVoiceCommand(id, out _), id + " 必须能按 ID 查到");
                Assert.IsTrue(_config.IsVoiceCommandReady(byPhrase), phrase + " 的回应动作必须在能力清单里");
                Assert.AreEqual(id, byPhrase.SfxId, "回应音效就是同一句录音");
            }

            Assert.IsTrue(_config.TryGetVoiceCommand("雷霆 大笑", out _), "识别结果带空格也要能匹配");
            Assert.IsFalse(_config.TryGetVoiceCommand("你好", out _), "未登记的词不是口令");
            Assert.AreEqual(PetExpression.Roar, Command("oula").Expression, "欧拉配怒眼怒吼");
            Assert.AreEqual(PetExpression.VeryHappy, Command("leiting").Expression, "雷霆大笑配眯眼大笑");
        }

        // ── 单只宠物的结算 ──────────────────────────────────────────────────────

        [TestCase(PetVariantId.Classic, "pet_classic", 0.06f)]
        [TestCase(PetVariantId.Calm, "pet_calm", 0.04f)]
        [TestCase(PetVariantId.Rage, "pet_rage", 0.08f)]
        [TestCase(PetVariantId.Divine, "pet_divine", 0.06f)]
        public void Voice_AddsOneSingleTapGain_AndPlaysTheCommandResponse(PetVariantId variant, string petId, float gain)
        {
            var pet = Runtime(petId);
            pet.RestoreFrom(new PetState { Happiness = 0.30f });

            Assert.IsTrue(pet.TrySubmit(Voice(pet, 1, "oula", 10f)));

            Assert.AreEqual(0.30f + gain, pet.Snapshot.Happiness, 0.0001f, variant + " 语音加分等同一次单击");
            Assert.AreEqual(PetBehaviorId.VoiceReply, pet.CurrentBehavior);
            var presenter = _presenters[petId];
            Assert.AreEqual("rage_roar", presenter.LastAnimationName, "四色都用口令配置的同一个动作回应");
            Assert.AreEqual(PetExpression.Roar, presenter.LastExpression);
            Assert.AreEqual("oula", presenter.LastSfx);

            pet.Tick(2.1f);
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior, "回应按配置时长播完后回到 Idle");
        }

        [Test]
        public void Voice_DuringBusyOrCooldown_IsRefused_AndScoresNothing()
        {
            var pet = Runtime("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.30f });

            Assert.IsTrue(pet.TrySubmit(Voice(pet, 1, "gaga", 10f)));
            var afterFirst = pet.Snapshot.Happiness;

            var replyExpression = _presenters["pet_classic"].LastExpression;
            Assert.IsFalse(pet.TrySubmit(Voice(pet, 2, "andi", 10.2f)), "回应播放中再喊被拒绝、不排队");
            Assert.AreEqual(replyExpression, _presenters["pet_classic"].LastExpression, "回应中被拒不得改写回应表情");
            pet.Tick(1f);
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior);
            Assert.IsFalse(pet.TrySubmit(Voice(pet, 3, "andi", 11.5f)), "距上次语音不足 3 秒被拒绝");
            Assert.AreEqual(afterFirst, pet.Snapshot.Happiness, 0.0001f, "被拒绝的语音不得加分");

            Assert.IsTrue(pet.TrySubmit(Voice(pet, 4, "andi", 13.1f)), "冷却过后可以再次回应");
            Assert.AreEqual(afterFirst + 0.06f, pet.Snapshot.Happiness, 0.0001f);
        }

        [Test]
        public void Voice_DuplicateEventId_IsSettledOnce()
        {
            var pet = Runtime("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.30f });

            Assert.IsTrue(pet.TrySubmit(Voice(pet, 7, "andi", 10f)));
            pet.Tick(3f);
            Assert.IsFalse(pet.TrySubmit(Voice(pet, 7, "andi", 20f)), "同一事件 Id 只结算一次");
            Assert.AreEqual(0.36f, pet.Snapshot.Happiness, 0.0001f);
        }

        [Test]
        public void Voice_DoesNotShareIdStreamWithTaps()
        {
            var pet = Runtime("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.30f });

            Assert.IsTrue(pet.TrySubmit(InteractionEvent.Create(5, InteractionEventKind.Pet, pet.PetId, 0f)));
            pet.Tick(1f);
            Assert.IsTrue(pet.TrySubmit(Voice(pet, 1, "andi", 10f)), "语音 Id 从 1 开始，不被触屏的 Id 5 吞掉");
        }

        [Test]
        public void UnknownCommand_IsRefused_WithoutScoring()
        {
            var pet = Runtime("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.30f });

            Assert.IsFalse(pet.TrySubmit(Voice(pet, 1, "nihao", 10f)));
            Assert.AreEqual(0.30f, pet.Snapshot.Happiness, 0.0001f);
            Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior);
        }

        [Test]
        public void CommandWithMissingAnimation_IsRefused_OthersStillWork()
        {
            var states = new List<string>(_config.AnimatorStates);
            states.Remove("rage_roar");
            _config.AnimatorStates = states.ToArray();

            var pet = Runtime("pet_classic");
            pet.RestoreFrom(new PetState { Happiness = 0.30f });
            Assert.IsFalse(pet.TrySubmit(Voice(pet, 1, "oula", 10f)), "回应动作不在能力清单里就拒绝");
            Assert.IsTrue(pet.TrySubmit(Voice(pet, 2, "andi", 10f)), "其他口令不受影响");
        }

        [Test]
        public void Voice_CrossingFullValue_StartsExclusiveInsteadOfReply()
        {
            var pet = Runtime("pet_rage");
            pet.RestoreFrom(new PetState { Happiness = 0.95f });

            Assert.IsTrue(pet.TrySubmit(Voice(pet, 1, "andi", 10f)));

            Assert.AreEqual(PetBehaviorId.Exclusive, pet.CurrentBehavior, "语音和单击一样走满值周期");
            Assert.AreEqual("rage_roar", _presenters["pet_rage"].LastAnimationName);
            Assert.AreEqual("oula", _presenters["pet_rage"].LastSfx, "朱红专属演出配\"欧拉\"音效，口令音效不叠加");
            Assert.AreEqual(0.65f, pet.Snapshot.Happiness, 0.0001f);
        }

        [Test]
        public void ExclusiveSfx_FollowsConfig_PerVariant()
        {
            Assert.AreEqual("andi", _config.ExclusiveSfxFor(PetVariantId.Classic));
            Assert.AreEqual(string.Empty, _config.ExclusiveSfxFor(PetVariantId.Calm));
            Assert.AreEqual("oula", _config.ExclusiveSfxFor(PetVariantId.Rage));
            Assert.AreEqual(string.Empty, _config.ExclusiveSfxFor(PetVariantId.Divine));
        }

        // ── 交互判定：只发给在场的宠物 ────────────────────────────────────────

        [Test]
        public void Rule_BuildsOneEventPerPresentPet_WithIndependentIds()
        {
            var rule = new VoiceCommandRule();
            var events = new List<InteractionEvent>();
            var ids = new[] { "pet_classic", "pet_calm", "pet_rage" };

            Assert.AreEqual(2, rule.Build("andi", 1f, ids, id => id != "pet_calm", events));
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual("pet_classic", events[0].PetId);
            Assert.AreEqual("pet_rage", events[1].PetId);
            Assert.AreEqual(InteractionEventKind.Voice, events[0].Kind);
            Assert.AreEqual("andi", events[0].CommandId);
            Assert.AreEqual(1, events[0].Id);

            events.Clear();
            rule.Build("oula", 2f, ids, _ => true, events);
            Assert.AreEqual(2, events[0].Id, "classic 第二次语音 Id 递增");
            Assert.AreEqual(1, events[1].Id, "calm 第一次被听到，Id 从 1 开始");

            events.Clear();
            Assert.AreEqual(0, rule.Build(" ", 3f, ids, _ => true, events), "空口令不产生事件");
        }

        [Test]
        public void Session_OnlyPetsOnCameraRespond()
        {
            var source = StubSource();
            var session = new PetSession(_config,
                new List<PetIdentity> { Identity("pet_classic"), Identity("pet_calm") }, source, source, Presenter);
            source.SetSimulatedStatus("card_calm", TrackingStatus.Lost);
            Step(session, 0.1f);

            var rule = new VoiceCommandRule();
            var events = new List<InteractionEvent>();
            rule.Build("andi", _now, session.PetIds, session.CanInteractThisFrame, events);
            Assert.AreEqual(1, events.Count, "卡不在画面里的宠物听不到");

            foreach (var e in events) Assert.IsTrue(session.TrySubmit(e));
            Assert.IsTrue(session.TryGetRuntime("pet_classic", out var classic));
            Assert.IsTrue(session.TryGetRuntime("pet_calm", out var calm));
            Assert.AreEqual(PetBehaviorId.VoiceReply, classic.CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Idle, calm.CurrentBehavior);
        }

        [Test]
        public void SocialLaugh_PlaysSfxOnceForTheWholeGroup()
        {
            var source = StubSource();
            var session = new PetSession(_config,
                new List<PetIdentity> { Identity("pet_classic"), Identity("pet_calm") }, source, source, Presenter);
            Proxy("card_classic").position = Vector3.zero;
            Proxy("card_calm").position = new Vector3(0.30f, 0f, 0f);
            Step(session, 0.1f);
            Proxy("card_calm").position = new Vector3(0.15f, 0f, 0f);
            for (var i = 0; i < 6; i++) Step(session, 0.1f);

            Assert.AreEqual(1, session.Social.ActiveSessions.Count, "前置：两只已开始共同大笑");
            var plays = _presenters["pet_classic"].SfxCount + _presenters["pet_calm"].SfxCount;
            Assert.AreEqual(1, plays, "整组只播一次\"雷霆大笑\"，不叠音");
            Assert.AreEqual("leiting", _presenters["pet_calm"].LastSfx ?? _presenters["pet_classic"].LastSfx);
        }

        // ── 辅助 ────────────────────────────────────────────────────────────────

        private VoiceCommand Command(string id)
        {
            Assert.IsTrue(_config.TryGetVoiceCommand(id, out var command));
            return command;
        }

        private PetRuntime Runtime(string petId)
        {
            var runtime = new PetRuntime(Identity(petId), _config, Presenter(petId));
            runtime.Start();
            return runtime;
        }

        private IPetPresenter Presenter(string petId)
        {
            if (!_presenters.TryGetValue(petId, out var presenter))
            {
                presenter = new RecordingPresenter();
                _presenters[petId] = presenter;
            }

            return presenter;
        }

        private static InteractionEvent Voice(PetRuntime pet, int id, string commandId, float time) =>
            InteractionEvent.CreateVoice(id, pet.PetId, time, commandId);

        private readonly Dictionary<string, Transform> _proxies = new();

        private StubTrackingSource StubSource()
        {
            var go = new GameObject("Tracking");
            _created.Add(go);
            var source = go.AddComponent<StubTrackingSource>();
            var targets = new List<StubTrackingSource.StubTarget>();
            foreach (var id in new[] { "card_classic", "card_calm" })
            {
                var proxy = new GameObject(id + "Proxy").transform;
                _created.Add(proxy.gameObject);
                _proxies[id] = proxy;
                targets.Add(new StubTrackingSource.StubTarget
                {
                    targetId = id, proxy = proxy, size = new Vector3(0.066f, 0f, 0.094f),
                    simulatedStatus = TrackingStatus.Tracked,
                });
            }

            source.SetTargets(targets.ToArray());
            source.Clock = () => _now;
            return source;
        }

        private Transform Proxy(string targetId) => _proxies[targetId];

        private void Step(PetSession session, float dt)
        {
            _now += dt;
            session.Tick(_now, dt, trackingRunning: true);
            session.TickSocial(_now, dt, trackingRunning: true);
        }

        private static PetIdentity Identity(string petId)
        {
            foreach (var identity in PetIdentityCatalog.Default)
            {
                if (identity.PetId == petId) return identity;
            }

            throw new InvalidOperationException("身份目录里没有 " + petId);
        }

        private sealed class RecordingPresenter : IPetPresenter
        {
            public string LastAnimationName { get; private set; }
            public PetExpression LastExpression { get; private set; } = PetExpression.Neutral;
            public string LastSfx { get; private set; }
            public int SfxCount { get; private set; }

            public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds)
                => LastAnimationName = animationName;

            public void SetExpression(PetExpression expression, float intensity) => LastExpression = expression;

            public void PlaySfx(string sfxId)
            {
                LastSfx = sfxId;
                SfxCount++;
            }

            public void PlayFx(string fxId) { }
            public void SetPlaybackSpeed(float speed) { }
            public void SetInteractable(bool interactable) { }
            public void SetTrackingVisibility(bool visible) { }
        }
    }
}
