using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Persistence;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// P4 共同大笑（靠近组协调）的规则测试。用真实的 <see cref="PetRuntime"/> 与真实的
    /// <see cref="SocialCoordinator"/>，自己维护"合格关系对 / 已锁关系对 / 已确认真实分开"
    /// 三个集合来充当接近规则，因此不需要场景、位姿或 AR 跟踪。
    ///
    /// 这些用例守护的是"错了会出现半套演出 / 重复记账 / 无期限占位"的部分：
    /// 组划分确定、整组原子开始、整组取消、同一次接近只触发一次、看完只记一次、
    /// 个人冷却与候选等待上限、以及共同大笑「不打断、也不被打断」的仲裁顺序。
    /// </summary>
    public sealed class SocialCoordinatorTests
    {
        private const string PetClassic = "pet_classic";
        private const string PetCalm = "pet_calm";
        private const string PetRage = "pet_rage";
        private const string PetDivine = "pet_divine";

        private PetConfig _config;
        private readonly List<PetConfig> _extraConfigs = new();
        private readonly List<PetRuntime> _pets = new();
        private readonly Dictionary<string, PetRuntime> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RecordingPresenter> _presenters = new(StringComparer.Ordinal);
        private readonly HashSet<string> _canInteract = new(StringComparer.Ordinal);
        private readonly HashSet<PetPair> _locked = new();
        private readonly HashSet<PetPair> _separated = new();
        private readonly List<PetPair> _qualified = new();

        private SocialCoordinator _coordinator;
        private float _now;
        private string _saveDirectory;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PetConfig>();
            _now = 0f;
            Rebuild(_ => _config);

            _saveDirectory = Path.Combine(Path.GetTempPath(), "ARPetSocialTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            _pets.Clear();
            _byId.Clear();
            _presenters.Clear();
            _canInteract.Clear();
            _locked.Clear();
            _separated.Clear();
            _qualified.Clear();

            foreach (var config in _extraConfigs) UnityEngine.Object.DestroyImmediate(config);
            _extraConfigs.Clear();
            UnityEngine.Object.DestroyImmediate(_config);

            if (!string.IsNullOrEmpty(_saveDirectory) && Directory.Exists(_saveDirectory))
                Directory.Delete(_saveDirectory, recursive: true);
        }

        // ── 组划分：六个配对 / ABC 链 / 两组 / 四宠 ─────────────────────────────

        [TestCase(PetCalm, PetClassic)]
        [TestCase(PetCalm, PetDivine)]
        [TestCase(PetCalm, PetRage)]
        [TestCase(PetClassic, PetDivine)]
        [TestCase(PetClassic, PetRage)]
        [TestCase(PetDivine, PetRage)]
        public void EachPair_StartsOneTwoPetSessionInTheSameFrame(string first, string second)
        {
            SetPairs(Pair(first, second));

            Step(0.1f);

            Assert.AreEqual(1, _coordinator.ActiveSessions.Count, "一对合格关系只产生一个会话");
            var session = _coordinator.ActiveSessions[0];
            CollectionAssert.AreEqual(Sorted(first, second), session.Members);   // 成员按 Ordinal 排序

            var a = PetOf(first);
            var b = PetOf(second);
            Assert.AreEqual(PetBehaviorId.Social, a.CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Social, b.CurrentBehavior);
            Assert.AreEqual(session.SessionId, a.ActiveSocialSessionId);
            Assert.AreEqual(session.SessionId, b.ActiveSocialSessionId);
            Assert.AreEqual("social_laugh", _presenters[first].LastAnimationName);
            Assert.AreEqual("social_laugh", _presenters[second].LastAnimationName);

            Assert.AreEqual(1, _coordinator.StartedThisTick.Count, "本帧开始时逐组上报成员，供上一层锁关系对");
            CollectionAssert.AreEqual(Sorted(first, second),
                new List<string>(_coordinator.StartedThisTick[0]));

            Assert.AreEqual(0.6f, a.Snapshot.Happiness, 0.0001f, "共同大笑不加开心值");
            Assert.AreEqual(0.6f, b.Snapshot.Happiness, 0.0001f);

            // 没参与的两只保持在 Idle，不受牵连。
            for (var i = 0; i < _pets.Count; i++)
            {
                var pet = _pets[i];
                if (pet.PetId == first || pet.PetId == second) continue;
                Assert.AreEqual(PetBehaviorId.Idle, pet.CurrentBehavior, pet.PetId + " 不该被卷入别人的会话");
            }
        }

        [Test]
        public void ChainOfPairs_FormsOneThreePetSession()
        {
            // A-B 合格、B-C 合格，A-C 不合格：链式传播成一个三宠组（相邻伙伴相互带动）。
            SetPairs(Pair(PetClassic, PetCalm), Pair(PetClassic, PetRage));

            Step(0.1f);

            Assert.AreEqual(1, _coordinator.ActiveSessions.Count, "ABC 链只产生一个会话");
            var members = _coordinator.ActiveSessions[0].Members;
            CollectionAssert.AreEqual(new[] { PetCalm, PetClassic, PetRage }, members);
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetDivine).CurrentBehavior);
        }

        [Test]
        public void TwoDisjointGroups_StartTwoSeparateSessions()
        {
            SetPairs(Pair(PetClassic, PetCalm), Pair(PetRage, PetDivine));

            Step(0.1f);

            Assert.AreEqual(2, _coordinator.ActiveSessions.Count);
            Assert.AreEqual(2, _coordinator.StartedThisTick.Count);
            CollectionAssert.AreEqual(new[] { PetCalm, PetClassic },
                _coordinator.ActiveSessions[0].Members);
            CollectionAssert.AreEqual(new[] { PetDivine, PetRage },
                _coordinator.ActiveSessions[1].Members);
            Assert.AreNotEqual(_coordinator.ActiveSessions[0].SessionId, _coordinator.ActiveSessions[1].SessionId);
        }

        [Test]
        public void FullyConnectedPets_FormOneFourPetSession()
        {
            SetPairs(
                Pair(PetClassic, PetCalm), Pair(PetClassic, PetRage), Pair(PetClassic, PetDivine),
                Pair(PetCalm, PetRage), Pair(PetCalm, PetDivine), Pair(PetRage, PetDivine));

            Step(0.1f);

            Assert.AreEqual(1, _coordinator.ActiveSessions.Count, "四宠全连通只合成一个会话");
            CollectionAssert.AreEqual(new[] { PetCalm, PetClassic, PetDivine, PetRage },
                _coordinator.ActiveSessions[0].Members);
            Assert.AreEqual(1, _coordinator.StartedThisTick.Count);
        }

        // ── 部分预留失败：等待、超时、整组原子 ─────────────────────────────────

        [Test]
        public void BusyMember_BlocksWholeGroup_ThenStartsWhenItReturnsIdle()
        {
            SetPairs(Pair(PetClassic, PetCalm));

            // classic 先被点了一下：0.7 s 的 TouchReact 执行中。
            Assert.IsTrue(PetOf(PetClassic).TrySubmit(Tap(PetClassic, 1)));
            Assert.AreEqual(PetBehaviorId.Petted, PetOf(PetClassic).CurrentBehavior);
            Assert.IsFalse(PetOf(PetClassic).CanStartSocial);

            Step(0.1f);
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "有人在演一次性动作时整组都不开始");
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetCalm).CurrentBehavior, "不允许半套：另一只不得先笑");

            // TouchReact 播完，仍在 2 s 等待窗口内 → 同一帧里整组一起开始。
            Step(0.7f);
            Assert.AreEqual(1, _coordinator.ActiveSessions.Count, "成员回到 Idle 后整组开始");
            Assert.AreEqual(PetBehaviorId.Social, PetOf(PetClassic).CurrentBehavior, "刚忙完的那只直接进入共同大笑");
            Assert.AreEqual(PetBehaviorId.Social, PetOf(PetCalm).CurrentBehavior);
        }

        [Test]
        public void BusyMemberBeyondCandidateHold_IsDroppedWithoutStarting()
        {
            // calm 的专属演出 2.5 s 长于候选等待上限 2 s：等不到就只能丢弃候选。
            SetPairs(Pair(PetCalm, PetClassic));
            var calm = PetOf(PetCalm);
            calm.RestoreFrom(new PetState { Happiness = 0.99f });
            Assert.IsTrue(calm.TrySubmit(Tap(PetCalm, 1)));
            Assert.AreEqual(PetBehaviorId.Exclusive, calm.CurrentBehavior);
            Assert.AreEqual(2.5f, calm.HappyCycle.ExclusiveDurationSeconds, 0.0001f);

            Step(0.1f);
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count);
            Assert.AreEqual(0, _coordinator.DroppedThisTick.Count, "刚出现的新候选不立刻丢弃");

            Step(1.7f);   // 累计等待 1.7 s，未到上限
            Assert.AreEqual(0, _coordinator.DroppedThisTick.Count);
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count);

            // 继续小步推进直到越过 2 s 上限：必须丢弃候选，且全程不开始。
            for (var i = 0; i < 6 && _coordinator.DroppedThisTick.Count == 0; i++) Step(0.1f);
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "等待超时也不得开始");
            Assert.AreEqual(1, _coordinator.DroppedThisTick.Count, "超时后必须把关系对交回接近规则");
            Assert.AreEqual(Pair(PetCalm, PetClassic), _coordinator.DroppedThisTick[0]);
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetClassic).CurrentBehavior, "另一只全程没有半套演出");
        }

        // ── 与满值演出的仲裁 ────────────────────────────────────────────────────

        [Test]
        public void PendingAutoRequest_OutranksSocial()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            var classic = PetOf(PetClassic);
            // 冷却内已经攒满：一条尚未开始的满值请求挂在周期上（冷却还剩 4 s，不会立刻开始）。
            classic.RestoreFrom(new PetState
            {
                Happiness = 0.99f,
                HappyCycle = HappyCycleState.PendingAuto,
                ExclusiveCooldownRemaining = 4f,
            });

            Assert.AreEqual(HappyCycleState.PendingAuto, classic.Snapshot.HappyCycle);
            Assert.IsFalse(classic.CanStartSocial, "PendingAuto 优先于共同大笑");

            Step(0.1f);

            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "有未开始的满值请求时不起共同大笑");
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetCalm).CurrentBehavior);
            Assert.AreEqual(0, _coordinator.DroppedThisTick.Count, "优先不等于丢弃，仍可等满值演出结束");
        }

        [Test]
        public void ReadingFullValuePendingConfirm_DoesNotBlockSocial()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            var classic = PetOf(PetClassic);
            // 读档时已满值 → 待确认。它要等玩家点一下，不能无限期挡住别人的共同大笑。
            classic.RestoreFrom(new PetState { Happiness = 1f });
            Assert.AreEqual(HappyCycleState.PendingConfirm, classic.Snapshot.HappyCycle);

            Step(0.1f);

            Assert.AreEqual(1, _coordinator.ActiveSessions.Count, "待确认不阻塞共同大笑");
            Assert.AreEqual(PetBehaviorId.Social, classic.CurrentBehavior);
        }

        [Test]
        public void TapDuringSocial_IsRefusedWithoutScoring()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);
            var classic = PetOf(PetClassic);
            Assert.AreEqual(PetBehaviorId.Social, classic.CurrentBehavior);
            Assert.IsTrue(classic.Behavior.IsBusy);

            var before = classic.Snapshot.Happiness;
            Assert.IsFalse(classic.TrySubmit(Tap(PetClassic, 1)), "共同大笑期间点按被拒绝");
            Assert.AreEqual(before, classic.Snapshot.Happiness, 0.0001f, "被拒绝的点按不得加分");
            Assert.AreEqual(PetBehaviorId.Social, classic.CurrentBehavior, "仍在演共同大笑");

            Step(2.5f);   // 2.4 s 播完
            Assert.AreEqual(PetBehaviorId.Idle, classic.CurrentBehavior, "拒绝不排队，播完也不会补发触屏反应");
        }

        [Test]
        public void AutoStartRequest_DoesNotInterruptSocial_ButRunsAfterwards()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);
            var classic = PetOf(PetClassic);
            Assert.AreEqual(PetBehaviorId.Social, classic.CurrentBehavior);

            // 模拟"冷却已经到点的满值请求"晚于会话开始：周期挂起一条自动开始请求。
            // TryAutoStartExclusive 只在默认行为时生效，因此不会打断进行中的共同大笑。
            classic.HappyCycle.ArmPendingAuto();
            Assert.IsTrue(classic.HappyCycle.IsAutoStartReady);

            Step(0.2f);
            Assert.AreEqual(PetBehaviorId.Social, classic.CurrentBehavior, "满值自动请求不得打断共同大笑");
            Assert.AreEqual(0, classic.Snapshot.ExclusiveStartCount);

            Step(2.2f);   // 共同大笑共 2.5 s > 2.4 s，播完回 Idle
            Assert.AreEqual(1, classic.Snapshot.ExclusiveStartCount, "共同大笑回 Idle 后才自动开始专属演出");
            Assert.AreEqual(PetBehaviorId.Exclusive, classic.CurrentBehavior);
        }

        // ── 取消：丢失 / 移走 / 外部打断 ───────────────────────────────────────

        [Test]
        public void MemberLost_CancelsWholeSession_WithoutCountingWatched()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);
            Assert.AreEqual(1, _coordinator.ActiveSessions.Count);

            _canInteract.Remove(PetCalm);   // 跟踪丢失：这一帧起不可交互
            Step(0.1f);

            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "任一成员丢失即整组取消");
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetClassic).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetCalm).CurrentBehavior);
            Assert.AreEqual(0, PetOf(PetClassic).Snapshot.SocialWatchedCount, "取消不计看完");
            Assert.AreEqual(0, PetOf(PetCalm).Snapshot.SocialWatchedCount);

            // 恢复后：这一次接近已被消费（关系对仍锁着），不得重开。
            _canInteract.Add(PetCalm);
            _locked.Add(Pair(PetClassic, PetCalm));
            Step(1f);
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "恢复不伪造重新进入，锁着的关系对不再触发");
        }

        [Test]
        public void MemberSeparated_CancelsWholeSession()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);
            Assert.AreEqual(1, _coordinator.ActiveSessions.Count);

            _separated.Add(Pair(PetClassic, PetCalm));   // 接近规则确认其中一只已经移走
            Step(0.1f);

            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "成员移走即整组取消");
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetClassic).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetCalm).CurrentBehavior);
            Assert.AreEqual(0, PetOf(PetClassic).Snapshot.SocialWatchedCount);
        }

        [Test]
        public void ExternalInterruptOfOneMember_CancelsWholeSession()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);
            Assert.AreEqual(1, _coordinator.ActiveSessions.Count);

            // 例如切后台只打断了其中一只：整组都要收，不能留一只独自笑完。
            PetOf(PetCalm).Interrupt("应用切到后台");
            Step(0.1f);

            Assert.AreEqual(0, _coordinator.ActiveSessions.Count);
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetClassic).CurrentBehavior);
            Assert.AreEqual(0, PetOf(PetCalm).LastCompletedSocialSessionId, "被打断不写完成会话 ID");
            Assert.AreEqual(0, PetOf(PetCalm).Snapshot.SocialWatchedCount);
        }

        // ── 正常完成与记账 ──────────────────────────────────────────────────────

        [Test]
        public void NaturalCompletion_CountsWatchedExactlyOncePerMember()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);
            var sessionId = _coordinator.ActiveSessions[0].SessionId;

            Step(2.4f);

            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "正常播完后会话结束");
            Assert.AreEqual(1, PetOf(PetClassic).Snapshot.SocialWatchedCount);
            Assert.AreEqual(1, PetOf(PetCalm).Snapshot.SocialWatchedCount);
            Assert.AreEqual(0, PetOf(PetRage).Snapshot.SocialWatchedCount, "非成员不记账");
            Assert.AreEqual(sessionId, PetOf(PetClassic).LastCompletedSocialSessionId);

            // 再推进不会重复记账：计数由协调器按会话只提交一次，运行时对同一 ID 也幂等。
            Step(1f);
            Assert.AreEqual(1, PetOf(PetClassic).Snapshot.SocialWatchedCount);
            Assert.IsFalse(PetOf(PetClassic).RecordSocialCompleted(sessionId),
                "同一 sessionId 第二次记账必须返回 false");
            Assert.AreEqual(1, PetOf(PetClassic).Snapshot.SocialWatchedCount);
        }

        // ── 个人冷却与关系对锁 ──────────────────────────────────────────────────

        [Test]
        public void NewPartnerWithinCooldown_WaitsThenStartsAfterFiveSeconds()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);    // t = 0.1 会话开始 → 个人冷却到 t = 5.1
            Step(2.4f);    // t = 2.5 正常完成
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count);
            _locked.Add(Pair(PetClassic, PetCalm));   // 两卡还没真实分开

            SetPairs(Pair(PetClassic, PetDivine));    // 新伙伴靠近
            Step(0.1f);    // t = 2.6，冷却未过
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "个人冷却内不开始");

            Step(1.7f);    // t = 4.3，等待 1.7 s，未到上限
            Assert.AreEqual(0, _coordinator.DroppedThisTick.Count);

            // 越过 2 s 上限后必须丢弃候选（接近规则会让这些对重新累计停留）。
            for (var i = 0; i < 8 && _coordinator.DroppedThisTick.Count == 0; i++) Step(0.1f);
            Assert.AreEqual(1, _coordinator.DroppedThisTick.Count, "超时后必须把关系对交回接近规则");
            Assert.AreEqual(Pair(PetClassic, PetDivine), _coordinator.DroppedThisTick[0]);
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "个人冷却内不开始");

            SetPairs();    // 上一层把这些对的停留计时清零
            Step(0.1f);
            SetPairs(Pair(PetClassic, PetDivine));
            Step(0.5f);    // 冷却已过（5.1）

            Assert.AreEqual(1, _coordinator.ActiveSessions.Count, "冷却结束后重新合格即可开始");
            CollectionAssert.AreEqual(new[] { PetClassic, PetDivine },
                _coordinator.ActiveSessions[0].Members);
        }

        [Test]
        public void AfterABC_NewPartnerOnlyFormsGroupWithFirstMergedPair()
        {
            // ABC 三只演过一轮，三对全部上锁；随后 D 同时靠近 A 与 B。
            SetPairs(Pair(PetCalm, PetClassic), Pair(PetClassic, PetDivine));
            Step(0.1f);    // t = 0.1 三宠会话开始
            Assert.AreEqual(1, _coordinator.ActiveSessions.Count);
            Assert.AreEqual(3, _coordinator.ActiveSessions[0].Members.Count);
            Step(2.4f);    // t = 2.5 正常完成

            _locked.Add(Pair(PetCalm, PetClassic));
            _locked.Add(Pair(PetClassic, PetDivine));
            _locked.Add(Pair(PetCalm, PetDivine));

            Step(2.7f);    // t = 5.2：个人冷却（到 5.1）已过，避免与冷却判定混在一起
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count);

            SetPairs(Pair(PetCalm, PetRage), Pair(PetClassic, PetRage));
            Step(0.1f);    // t = 5.3

            Assert.AreEqual(1, _coordinator.ActiveSessions.Count, "只形成一个新会话");
            // 只能形成排序靠前的 {A,D}，不得把已锁的 A-B 重新包进新组。
            CollectionAssert.AreEqual(new[] { PetCalm, PetRage }, _coordinator.ActiveSessions[0].Members);
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetClassic).CurrentBehavior, "B 不参与这次会话");
        }

        [Test]
        public void CooldownExpiredButPairStillLocked_DoesNotReplay()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);
            Step(2.4f);    // t = 2.5 正常完成
            _locked.Add(Pair(PetClassic, PetCalm));

            Step(5f);      // t = 7.5，个人冷却早已结束

            Assert.AreEqual(0, _coordinator.ActiveSessions.Count, "关系对没真实分开过，冷却到期也不重播");
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetClassic).CurrentBehavior);
            Assert.AreEqual(1, PetOf(PetClassic).Snapshot.SocialWatchedCount, "没重播就没有第二次记账");
        }

        [Test]
        public void CancelAll_CancelsSessionsWithoutCountingWatched()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            Step(0.1f);
            Assert.AreEqual(1, _coordinator.ActiveSessions.Count);

            _coordinator.CancelAll("应用切到后台");

            Assert.AreEqual(0, _coordinator.ActiveSessions.Count);
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetClassic).CurrentBehavior);
            Assert.AreEqual(PetBehaviorId.Idle, PetOf(PetCalm).CurrentBehavior);
            Assert.AreEqual(0, PetOf(PetClassic).Snapshot.SocialWatchedCount, "取消不计看完");
        }

        [Test]
        public void CancelAll_ClearsWaitingCandidate()
        {
            SetPairs(Pair(PetClassic, PetCalm));
            var calm = PetOf(PetCalm);
            calm.RestoreFrom(new PetState { Happiness = 0.99f });
            Assert.IsTrue(calm.TrySubmit(Tap(PetCalm, 1)));   // 专属 2.5 s
            Assert.AreEqual(PetBehaviorId.Exclusive, calm.CurrentBehavior);

            Step(0.1f);    // t = 0.1，候选开始等待
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count);
            Assert.AreEqual(1, _coordinator.PendingCandidateCount, "有成员正忙时候选进入等待");

            _coordinator.CancelAll("应用切到后台");

            Assert.AreEqual(0, _coordinator.PendingCandidateCount, "CancelAll 必须清空等待中的候选");

            // 下一帧会以新的（更晚的）首次出现时刻重新等待，而不是沿用旧计时提前超时。
            Step(0.1f);
            Assert.AreEqual(1, _coordinator.PendingCandidateCount);
            Assert.AreEqual(0, _coordinator.DroppedThisTick.Count, "重新计时的候选不该被立刻丢弃");
            Assert.AreEqual(0, _coordinator.ActiveSessions.Count);
        }

        // ── 能力清单 ────────────────────────────────────────────────────────────

        [Test]
        public void MissingSocialLaugh_ExcludesOnlyThatPet()
        {
            var noSocial = ScriptableObject.CreateInstance<PetConfig>();
            _extraConfigs.Add(noSocial);
            noSocial.AnimatorStates = Without(noSocial.AnimatorStates, "social_laugh");
            Rebuild(petId => petId == PetRage ? noSocial : _config);

            var rage = PetOf(PetRage);
            Assert.IsFalse(rage.IsSocialAvailable);
            CollectionAssert.DoesNotContain(rage.Behavior.AllowedBehaviors, PetBehaviorId.Social);
            // 只有社交这一项被禁用，触屏反应仍在。
            CollectionAssert.Contains(rage.Behavior.AllowedBehaviors, PetBehaviorId.Petted);

            SetPairs(Pair(PetRage, PetCalm), Pair(PetClassic, PetCalm));
            Step(0.1f);

            Assert.AreEqual(1, _coordinator.ActiveSessions.Count, "缺片段的宠不纳入组，其余两只照常");
            CollectionAssert.AreEqual(new[] { PetCalm, PetClassic }, _coordinator.ActiveSessions[0].Members);
            Assert.AreEqual(PetBehaviorId.Idle, rage.CurrentBehavior);
            Assert.AreEqual(0, rage.Snapshot.SocialWatchedCount);
        }

        // ── 配置清洗 ────────────────────────────────────────────────────────────

        [Test]
        public void SocialTuning_FallsBackToDefaultsAndKeepsHysteresis()
        {
            var tuning = new SocialTuning
            {
                EnterDistance = 0.2f,
                ExitDistance = 0.1f,
                MaxHeightDelta = 0f,
                MaxNormalAngleDegrees = float.NaN,
                EnterDwellSeconds = -1f,
                ExitDwellSeconds = 0.3f,
                CandidateHoldSeconds = 0f,
                MemberCooldownSeconds = -2f,
            }.Sanitised();

            var defaults = SocialTuning.Default;
            Assert.AreEqual(0.2f, tuning.EnterDistance, 0.0001f);
            Assert.AreEqual(0.2f, tuning.ExitDistance, 0.0001f, "退出距离不得小于进入距离");
            Assert.AreEqual(defaults.MaxHeightDelta, tuning.MaxHeightDelta, 0.0001f);
            Assert.AreEqual(defaults.MaxNormalAngleDegrees, tuning.MaxNormalAngleDegrees, 0.0001f);
            Assert.AreEqual(defaults.EnterDwellSeconds, tuning.EnterDwellSeconds, 0.0001f);
            Assert.AreEqual(0.3f, tuning.ExitDwellSeconds, 0.0001f, "合法值原样保留");
            Assert.AreEqual(defaults.CandidateHoldSeconds, tuning.CandidateHoldSeconds, 0.0001f);
            Assert.AreEqual(defaults.MemberCooldownSeconds, tuning.MemberCooldownSeconds, 0.0001f);

            // 协调器读到的就是校验过的副本。
            Assert.AreEqual(2f, _coordinator.Tuning.CandidateHoldSeconds, 0.0001f);
            Assert.AreEqual(5f, _coordinator.Tuning.MemberCooldownSeconds, 0.0001f);
        }

        // ── 存档 ────────────────────────────────────────────────────────────────

        [Test]
        public void SaveRoundTrip_KeepsSocialWatchedCount()
        {
            var path = Path.Combine(_saveDirectory, SaveService.FileName);
            var classic = PetOf(PetClassic);

            Assert.IsTrue(classic.RecordSocialCompleted(1));
            Assert.IsFalse(classic.RecordSocialCompleted(1), "同一 sessionId 第二次记账返回 false");
            Assert.AreEqual(1, classic.Snapshot.SocialWatchedCount);

            var coordinator = new SaveCoordinator(new[] { classic }, SaveFile.CreateNew(1000),
                (file, utc) => SaveService.Save(file, utc, path));
            Assert.IsTrue(coordinator.Flush(0f));

            var reread = SaveService.Load(out var restored, path);
            Assert.IsTrue(restored);
            Assert.IsTrue(reread.TryGetPet(PetClassic, out var entry));
            Assert.AreEqual(1, entry.SocialWatchedCount);

            var revived = new PetRuntime(IdentityOf(PetClassic), _config, new RecordingPresenter());
            revived.Start();
            SaveCoordinator.ApplyTo(reread, new[] { revived }, reread.SavedAtUnixSeconds, 0f);

            Assert.AreEqual(1, revived.Snapshot.SocialWatchedCount, "读档要还原共同大笑看完次数");
        }

        [Test]
        public void V2EntryWithoutSocialWatchedCount_LoadsAsZero()
        {
            var path = Path.Combine(_saveDirectory, SaveService.FileName);
            File.WriteAllText(path,
                "{ \"SchemaVersion\": 2, \"SavedAtUnixSeconds\": 1000, " +
                "\"Pets\": [ { \"PetId\": \"pet_classic\", \"Happiness\": 0.42 } ] }");

            var loaded = SaveService.Load(out var restored, path);

            Assert.IsTrue(restored);
            Assert.IsTrue(loaded.TryGetPet(PetClassic, out var entry));
            Assert.AreEqual(0, entry.SocialWatchedCount, "旧 v2 条目缺字段读成 0");
            Assert.AreEqual(0.42f, entry.Happiness, 0.0001f);
        }

        [Test]
        public void NegativeSocialWatchedCount_IsSanitisedOnRestore()
        {
            var classic = PetOf(PetClassic);
            classic.RestoreFrom(new PetState { Happiness = 0.5f, SocialWatchedCount = -3 });

            Assert.AreEqual(0, classic.Snapshot.SocialWatchedCount);
        }

        // ── PetPair ─────────────────────────────────────────────────────────────

        [Test]
        public void PetPair_OrdersMembersByOrdinalAndComparesByValue()
        {
            var pair = new PetPair(PetClassic, PetCalm);

            Assert.AreEqual(PetCalm, pair.A, "Ordinal 较小的成员排在前面");
            Assert.AreEqual(PetClassic, pair.B);
            Assert.AreEqual(pair, new PetPair(PetCalm, PetClassic), "传参顺序不影响相等性");
            Assert.AreEqual(pair.GetHashCode(), new PetPair(PetCalm, PetClassic).GetHashCode());
            Assert.AreEqual("pet_calm+pet_classic", pair.ToString());
            Assert.IsTrue(pair.Involves(PetClassic));
            Assert.AreEqual(PetCalm, pair.Other(PetClassic));
            Assert.IsNull(pair.Other(PetRage), "不在关系里的宠物没有对方");
            Assert.AreNotEqual(pair, new PetPair(PetCalm, PetRage));
        }

        [Test]
        public void PetPair_RejectsEmptyAndIdenticalIds()
        {
            AssertThrowsArgumentException(() => new PetPair(null, PetCalm), "空 ID 必须抛 ArgumentException");
            AssertThrowsArgumentException(() => new PetPair(string.Empty, PetCalm), "空 ID 必须抛 ArgumentException");
            AssertThrowsArgumentException(() => new PetPair("   ", PetCalm), "空白 ID 必须抛 ArgumentException");
            AssertThrowsArgumentException(() => new PetPair(PetClassic, PetClassic), "同一只宠物不能配对");
            AssertThrowsArgumentException(() => new PetPair(PetClassic, null), "第二个成员为空也要拒绝");
        }

        // ── 辅助 ────────────────────────────────────────────────────────────────

        /// <summary>按给定配置重建四只宠物与协调器（缺能力用例需要逐只换配置）。</summary>
        private void Rebuild(Func<string, PetConfig> configOf)
        {
            _pets.Clear();
            _byId.Clear();
            _presenters.Clear();
            _canInteract.Clear();
            _locked.Clear();
            _separated.Clear();
            _qualified.Clear();

            var ids = new[] { PetClassic, PetCalm, PetRage, PetDivine };
            for (var i = 0; i < ids.Length; i++)
            {
                var petId = ids[i];
                var presenter = new RecordingPresenter();
                var runtime = new PetRuntime(IdentityOf(petId), configOf(petId), presenter);
                runtime.Start();

                _pets.Add(runtime);
                _byId[petId] = runtime;
                _presenters[petId] = presenter;
                _canInteract.Add(petId);
            }

            _coordinator = new SocialCoordinator(_pets, _config);
        }

        /// <summary>推进一帧：先按"可交互"逐只推进行为时钟，再把本帧的关系交给协调器。</summary>
        private void Step(float deltaTime)
        {
            _now += deltaTime;

            for (var i = 0; i < _pets.Count; i++)
            {
                var pet = _pets[i];
                if (!_canInteract.Contains(pet.PetId)) continue;   // 丢失：行为时钟冻结
                pet.Tick(deltaTime);
            }

            _coordinator.Tick(_now, _qualified,
                pair => _locked.Contains(pair),
                petId => _canInteract.Contains(petId),
                pair => _separated.Contains(pair));
        }

        private void SetPairs(params PetPair[] pairs)
        {
            _qualified.Clear();
            _qualified.AddRange(pairs);
        }

        private static PetPair Pair(string first, string second) => new PetPair(first, second);

        private PetRuntime PetOf(string petId) => _byId[petId];

        private static string[] Sorted(string first, string second) =>
            string.CompareOrdinal(first, second) <= 0 ? new[] { first, second } : new[] { second, first };

        private static InteractionEvent Tap(string petId, int id) =>
            InteractionEvent.Create(id, InteractionEventKind.Pet, petId, 0f);

        private void AssertThrowsArgumentException(Action action, string message)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }

            Assert.Fail(message);
        }

        private static string[] Without(string[] states, string removed)
        {
            var kept = new List<string>();
            for (var i = 0; i < states.Length; i++)
            {
                if (!string.Equals(states[i], removed, StringComparison.Ordinal)) kept.Add(states[i]);
            }

            return kept.ToArray();
        }

        private static PetIdentity IdentityOf(string petId)
        {
            foreach (var identity in PetIdentityCatalog.Default)
            {
                if (string.Equals(identity.PetId, petId, StringComparison.Ordinal)) return identity;
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
