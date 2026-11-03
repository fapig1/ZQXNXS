using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 一次已经开始的共同大笑会话的只读视图。成员清单在会话成立时冻结，
    /// 动作中途不会增补成员（新伙伴要靠新一轮合格接近另开会话）。
    /// </summary>
    public sealed class SocialSessionInfo
    {
        private readonly string[] _members;

        internal SocialSessionInfo(int sessionId, float startTime, List<string> members)
        {
            SessionId = sessionId;
            StartTime = startTime;
            _members = members.ToArray();
        }

        /// <summary>会话 ID，从 1 开始递增；同一会话的所有成员记录的是同一个 ID。</summary>
        public int SessionId { get; }

        /// <summary>会话开始的时刻（与 <see cref="SocialCoordinator.Tick"/> 使用同一条时钟）。</summary>
        public float StartTime { get; }

        /// <summary>成员稳定 PetId，按 Ordinal 排序。</summary>
        public IReadOnlyList<string> Members => _members;

        /// <summary>该成员是否属于本次会话。</summary>
        public bool Involves(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return false;
            for (var i = 0; i < _members.Length; i++)
            {
                if (string.Equals(_members[i], petId, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        public override string ToString() => $"#{SessionId} @{StartTime:F2}s [{string.Join(", ", _members)}]";
    }

    /// <summary>
    /// 共同大笑的组协调器（纯 C#，不读位姿、不改开心值、不碰场景对象）。
    ///
    /// <b>它只做三件事：</b>把交互层给出的"合格关系对"合成为演出组、按同一会话 ID
    /// 同步开始/取消、在整组正常结束时提交一次观看计数。距离、高度、法向与停留时间
    /// 全部由交互层的接近规则判定，这里只接收结论。
    ///
    /// <b>仲裁顺序</b>（从高到低）：
    /// <list type="number">
    /// <item>跟踪丢失 / 后台暂停：整组取消，不等演出结束。</item>
    /// <item>已经开始的一次性演出不被打断（TouchReact 只有 0.7 s，满值专属 2～2.5 s）。</item>
    /// <item>尚未开始的满值请求（<see cref="HappyCycleState.PendingAuto"/>）优先于共同大笑：
    ///       它的行为时间一到就自动开始专属演出，共同大笑只能等它。</item>
    /// <item>共同大笑：所有成员都可让出行为通道才整组开始，否则按
    ///       <see cref="SocialTuning.CandidateHoldSeconds"/> 等待。</item>
    /// <item>TouchReact 与 Idle。</item>
    /// </list>
    /// 同帧内点按先于社交提交（由 App 保证更新顺序），所以被点按占用的宠只会让候选等待，
    /// 不会两边同时生效；等待超过上限就丢弃候选，让接近规则重新累计停留时间。
    ///
    /// <b>三条纪律：</b>
    /// <list type="bullet">
    /// <item>成员清单在开始时冻结；一个参与者同一时刻只属于一个会话。</item>
    /// <item>开始与取消都是整组的：任何一步失败全部回滚，不出现"一半成员在笑"。</item>
    /// <item>取消<b>不</b>归还个人冷却，也<b>不</b>解锁关系对（锁由接近规则在真实分开后解除）。</item>
    /// </list>
    /// </summary>
    public sealed class SocialCoordinator
    {
        /// <summary>候选键的分隔符：成员集合本身决定键，集合变了就是新候选。</summary>
        private const char PendingSeparator = '|';

        private readonly Dictionary<string, PetRuntime> _byId = new(StringComparer.Ordinal);
        private readonly SocialTuning _tuning;

        private readonly List<SocialSessionInfo> _sessions = new();
        private readonly List<IReadOnlyList<string>> _startedThisTick = new();
        private readonly List<PetPair> _droppedThisTick = new();

        /// <summary>每个成员"上一次参与会话的开始时刻"，用于个人冷却（取消不归还）。</summary>
        private readonly Dictionary<string, float> _cooldownStart = new(StringComparer.Ordinal);

        /// <summary>等待中的候选：键 = 排序后成员集合，值 = 首次出现的时刻。</summary>
        private readonly Dictionary<string, float> _pendingSince = new(StringComparer.Ordinal);

        private readonly HashSet<string> _seenPending = new(StringComparer.Ordinal);

        // EvaluateCandidates 每帧复用的临时集合（只在该方法内使用，进入时清空）。
        private readonly Dictionary<string, bool> _availabilityBuffer = new(StringComparer.Ordinal);
        private readonly HashSet<PetPair> _qualifiedBuffer = new();
        private readonly List<PetPair> _edgeBuffer = new();
        private static readonly List<List<string>> NoGroups = new();

        private int _nextSessionId = 1;
        private readonly string _socialSfxId;

        /// <summary>
        /// <paramref name="pets"/> 是本局参与运行的全部宠物（顺序不影响结果，内部按 PetId 索引）。
        /// <paramref name="config"/> 提供候选等待上限与参与者冷却。
        /// </summary>
        public SocialCoordinator(IReadOnlyList<PetRuntime> pets, PetConfig config)
        {
            if (pets == null) throw new ArgumentNullException(nameof(pets));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (pets.Count == 0) throw new ArgumentException("至少要有一只宠物才能建立共同大笑协调器", nameof(pets));

            _tuning = config.ResolveSocial();
            _socialSfxId = config.SocialSfxId ?? string.Empty;

            for (var i = 0; i < pets.Count; i++)
            {
                var pet = pets[i];
                if (pet == null) throw new ArgumentException("宠物运行时不能为空", nameof(pets));
                _byId[pet.PetId] = pet;
            }
        }

        /// <summary>已校验的共同大笑参数（含候选等待上限与个人冷却）。</summary>
        public SocialTuning Tuning => _tuning;

        /// <summary>进行中的会话，顺序即开始顺序。协调器内部列表，只读暴露。</summary>
        public IReadOnlyList<SocialSessionInfo> ActiveSessions => _sessions;

        /// <summary>
        /// 本帧开始的会话成员（每项按 Ordinal 排序）。下一层据此锁住组内<b>所有</b>关系对，
        /// 不只是连通树上的边——否则 ABC 之后 AB / BC 会立刻各自重开一次。
        /// </summary>
        public IReadOnlyList<IReadOnlyList<string>> StartedThisTick => _startedThisTick;

        /// <summary>
        /// 本帧因等待超时被丢弃的候选所含关系对。下一层据此把这些对的停留计时清零，
        /// 让它们重新累计 <see cref="SocialTuning.EnterDwellSeconds"/> 才有资格再试。
        /// </summary>
        public IReadOnlyList<PetPair> DroppedThisTick => _droppedThisTick;

        /// <summary>
        /// 正在等待成员空闲的候选数量（只读观察值，不参与业务判定）。
        /// 界面可以据此提示"再等一下"，测试也用它确认 <see cref="CancelAll"/> 真的清空了候选。
        /// </summary>
        public int PendingCandidateCount => _pendingSince.Count;

        /// <summary>该宠是否属于某个进行中的会话。</summary>
        public bool IsInSession(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return false;
            for (var i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].Involves(petId)) return true;
            }

            return false;
        }

        /// <summary>
        /// 每帧推进一步。先处理已有会话（完成 / 取消），再找新候选；两条路径互不干扰。
        /// </summary>
        /// <param name="now">单调时钟（秒），与会话开始时刻、个人冷却共用。</param>
        /// <param name="qualifiedPairs">本帧"两卡都有效且新鲜、已连续满足进入条件"的关系对，由接近规则给出。</param>
        /// <param name="isLocked">该关系对是否已被以前的会话锁住（同一次接近只触发一次）。<c>null</c> 视为未锁。</param>
        /// <param name="canInteract">该宠本帧能否交互（丢失、过期、全局暂停时为 false）。<c>null</c> 视为可交互。</param>
        /// <param name="isSeparated">该关系对是否已确认真实分开（两卡都有效且离开退出区并保持）。<c>null</c> 视为未分开。</param>
        public void Tick(float now, IReadOnlyCollection<PetPair> qualifiedPairs,
            Func<PetPair, bool> isLocked, Func<string, bool> canInteract, Func<PetPair, bool> isSeparated)
        {
            _startedThisTick.Clear();
            _droppedThisTick.Clear();
            if (float.IsNaN(now) || float.IsInfinity(now)) return;

            AdvanceSessions(isSeparated, canInteract);
            EvaluateCandidates(now, qualifiedPairs, isLocked, canInteract);
        }

        /// <summary>
        /// 全局取消（切后台、暂停）。所有进行中的会话整组取消，等待中的候选一并清空。
        /// 个人冷却与关系对锁都不归还：<b>已上锁</b>的关系对回来之后要靠真实的分开再靠近才能再演一次；
        /// 尚未上锁、只是在停留或等待中的对，回来后若仍贴在一起，重新累计满停留时间即可开演。
        /// </summary>
        public void CancelAll(string reason)
        {
            var text = string.IsNullOrEmpty(reason) ? "全局取消共同大笑" : reason;

            for (var i = 0; i < _sessions.Count; i++)
            {
                var session = _sessions[i];
                for (var m = 0; m < session.Members.Count; m++)
                {
                    var pet = RuntimeOf(session.Members[m]);
                    if (pet == null) continue;
                    pet.CancelSocial(session.SessionId, text);
                }

                Debug.Log($"[SocialCoordinator] 会话 {session.SessionId} 已取消：{text}");
            }

            _sessions.Clear();
            _pendingSince.Clear();
        }

        // ── 已开始的会话 ────────────────────────────────────────────────────────

        private void AdvanceSessions(Func<PetPair, bool> isSeparated, Func<string, bool> canInteract)
        {
            for (var i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];

                // ① 整组正常播完：协调器只在这里提交一次"看完"，每只加一。
                if (AllMembersCompleted(session))
                {
                    CommitCompleted(session);
                    _sessions.RemoveAt(i);
                    continue;
                }

                // ② 取消：丢失 / 暂停 / 被外部打断 / 成员移走。
                var reason = CancellationReason(session, isSeparated, canInteract);
                if (reason == null) continue;

                CancelSession(session, reason);
                _sessions.RemoveAt(i);
            }
        }

        private bool AllMembersCompleted(SocialSessionInfo session)
        {
            for (var i = 0; i < session.Members.Count; i++)
            {
                var pet = RuntimeOf(session.Members[i]);
                if (pet == null || pet.LastCompletedSocialSessionId != session.SessionId) return false;
            }

            return true;
        }

        private void CommitCompleted(SocialSessionInfo session)
        {
            for (var i = 0; i < session.Members.Count; i++)
            {
                var pet = RuntimeOf(session.Members[i]);
                pet?.RecordSocialCompleted(session.SessionId);
            }

            Debug.Log($"[SocialCoordinator] 会话 {session.SessionId} 正常完成：{string.Join(", ", session.Members)}");
        }

        private string CancellationReason(SocialSessionInfo session, Func<PetPair, bool> isSeparated,
            Func<string, bool> canInteract)
        {
            // ① 成员丢失或全局暂停：立刻整组取消，不等演出结束（丢失者由别处隐藏）。
            for (var i = 0; i < session.Members.Count; i++)
            {
                if (RuntimeOf(session.Members[i]) == null) return "会话成员已不在本局运行";
                if (canInteract != null && !canInteract(session.Members[i])) return "成员丢失或全局暂停";
            }

            // ② 有成员已经不在本会话的 Social（被切后台之类打断），且不是正常播完。
            for (var i = 0; i < session.Members.Count; i++)
            {
                var pet = RuntimeOf(session.Members[i]);
                if (pet == null) return "会话成员已不在本局运行";
                if (pet.LastCompletedSocialSessionId == session.SessionId) continue;
                if (pet.CurrentBehavior != PetBehaviorId.Social ||
                    pet.ActiveSocialSessionId != session.SessionId)
                {
                    return "成员演出被外部打断";
                }
            }

            // ③ 成员移走：某一成员与其他所有成员的关系都已确认真实分开。
            if (isSeparated != null && AnyMemberSeparated(session, isSeparated)) return "成员移走";

            return null;
        }

        private static bool AnyMemberSeparated(SocialSessionInfo session, Func<PetPair, bool> isSeparated)
        {
            if (session.Members.Count < 2) return false;

            for (var i = 0; i < session.Members.Count; i++)
            {
                var separatedFromAll = true;
                for (var j = 0; j < session.Members.Count; j++)
                {
                    if (i == j) continue;
                    var pair = new PetPair(session.Members[i], session.Members[j]);
                    if (isSeparated(pair)) continue;
                    separatedFromAll = false;
                    break;
                }

                if (separatedFromAll) return true;
            }

            return false;
        }

        private void CancelSession(SocialSessionInfo session, string reason)
        {
            for (var i = 0; i < session.Members.Count; i++)
            {
                var pet = RuntimeOf(session.Members[i]);
                pet?.CancelSocial(session.SessionId, reason);
            }

            Debug.Log($"[SocialCoordinator] 会话 {session.SessionId} 取消：{reason}");
        }

        // ── 组划分与候选 ────────────────────────────────────────────────────────

        private void EvaluateCandidates(float now, IReadOnlyCollection<PetPair> qualifiedPairs,
            Func<PetPair, bool> isLocked, Func<string, bool> canInteract)
        {
            _seenPending.Clear();

            // 每帧调用：三份临时集合复用字段，不在热路径上分配。
            var availability = _availabilityBuffer;
            var qualified = _qualifiedBuffer;
            var edges = _edgeBuffer;
            availability.Clear();
            qualified.Clear();
            edges.Clear();

            if (qualifiedPairs != null)
            {
                foreach (var pair in qualifiedPairs)
                {
                    // 已在会话中 / 本帧不可交互 / 缺 social_laugh 能力的宠直接退出本帧分组。
                    // "正忙"（TouchReact、专属演出）不剔除：它是候选等待的原因，而不是退出的原因。
                    if (!IsMemberAvailable(pair.A, canInteract, availability)) continue;
                    if (!IsMemberAvailable(pair.B, canInteract, availability)) continue;

                    qualified.Add(pair);

                    // 已锁的关系对不能再构成候选：同一次接近只触发一次演出。
                    if (isLocked != null && isLocked(pair)) continue;
                    edges.Add(pair);
                }
            }

            // 没有任何可成组的边时（绝大多数帧）不进入并查集，也就不分配。
            var groups = edges.Count == 0 ? NoGroups : ComposeGroups(edges, isLocked);

            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                var key = string.Join(PendingSeparator, group);
                _seenPending.Add(key);

                if (AllReadyToStart(group, now))
                {
                    TryStartSession(now, group, key);
                    continue;
                }

                TrackPending(now, group, key, qualified);
            }

            ForgetUnseenPending();
        }

        /// <summary>成员可参与本帧分组的三个前提（不含"正忙"）。</summary>
        private bool IsMemberAvailable(string petId, Func<string, bool> canInteract,
            Dictionary<string, bool> cache)
        {
            if (cache.TryGetValue(petId, out var cached)) return cached;

            var pet = RuntimeOf(petId);
            var available = pet != null &&
                            !IsInSession(petId) &&
                            pet.IsSocialAvailable &&
                            (canInteract == null || canInteract(petId));

            cache[petId] = available;
            return available;
        }

        /// <summary>
        /// 用并查集把关系对贪心合并成演出组，返回成员数 ≥ 2 的组（组内与组间顺序都稳定）：
        /// <list type="number">
        /// <item>边按 (A,B) 的 Ordinal 排序后依次处理，同一输入快照总是得到同一次划分；</item>
        /// <item>合并两个集合前检查合并后集合内<b>任意两成员</b>之间都没有已锁的关系对，
        ///       有就跳过这条边——这样"A 与 B 已演过、D 又靠近 A 和 B"只能得到 {A,D}，
        ///       不会把已锁的 A-B 重新包进新组；</item>
        /// <item>链式相连（AB、BC 合格而 AC 不合格）会自然合并成一个 {A,B,C} 组。</item>
        /// </list>
        /// </summary>
        private static List<List<string>> ComposeGroups(List<PetPair> edges, Func<PetPair, bool> isLocked)
        {
            var groups = new List<List<string>>();
            if (edges.Count == 0) return groups;

            edges.Sort(ComparePairs);

            var memberSet = new SortedSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < edges.Count; i++)
            {
                memberSet.Add(edges[i].A);
                memberSet.Add(edges[i].B);
            }

            if (memberSet.Count < 2) return groups;

            var members = new List<string>(memberSet);
            var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < members.Count; i++) indexOf[members[i]] = i;

            var parent = new int[members.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;

            for (var i = 0; i < edges.Count; i++)
            {
                var left = Find(parent, indexOf[edges[i].A]);
                var right = Find(parent, indexOf[edges[i].B]);
                if (left == right) continue;
                if (ContainsLockedPair(parent, members, left, right, isLocked)) continue;

                // 根始终挂到较小的下标上：同一集合的根就是集合里最小的成员。
                if (left < right) parent[right] = left;
                else parent[left] = right;
            }

            var byRoot = new Dictionary<int, List<string>>();
            for (var i = 0; i < members.Count; i++)
            {
                var root = Find(parent, i);
                if (!byRoot.TryGetValue(root, out var group))
                {
                    group = new List<string>();
                    byRoot[root] = group;
                    groups.Add(group);
                }

                group.Add(members[i]);
            }

            // 成员集合大小 ≥ 2 才是演出组；单只宠物的"组"没有意义。
            groups.RemoveAll(group => group.Count < 2);
            return groups;
        }

        private static bool ContainsLockedPair(int[] parent, List<string> members, int left, int right,
            Func<PetPair, bool> isLocked)
        {
            if (isLocked == null) return false;

            for (var i = 0; i < members.Count; i++)
            {
                if (Find(parent, i) != left) continue;
                for (var j = 0; j < members.Count; j++)
                {
                    if (Find(parent, j) != right) continue;
                    if (isLocked(new PetPair(members[i], members[j]))) return true;
                }
            }

            return false;
        }

        private static int Find(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        private static int ComparePairs(PetPair left, PetPair right)
        {
            var order = string.CompareOrdinal(left.A, right.A);
            return order != 0 ? order : string.CompareOrdinal(left.B, right.B);
        }

        private bool AllReadyToStart(List<string> group, float now)
        {
            for (var i = 0; i < group.Count; i++)
            {
                var pet = RuntimeOf(group[i]);
                if (pet == null || !pet.CanStartSocial) return false;

                // 个人冷却从本人上一次会话<b>开始</b>的时刻算起，取消也不提前结束。
                if (_cooldownStart.TryGetValue(group[i], out var startedAt) &&
                    now < startedAt + _tuning.MemberCooldownSeconds)
                {
                    return false;
                }
            }

            return true;
        }

        private void TrackPending(float now, List<string> group, string key, HashSet<PetPair> qualified)
        {
            if (!_pendingSince.TryGetValue(key, out var firstSeen))
            {
                // 集合第一次出现（或换了成员组合）：重新开始计时，不消费任何机会。
                _pendingSince[key] = now;
                return;
            }

            if (now - firstSeen < _tuning.CandidateHoldSeconds) return;

            // 等待超时：丢弃候选，并把组内所有仍合格的关系对交回上一层重新累计停留时间。
            _pendingSince.Remove(key);
            for (var i = 0; i < group.Count; i++)
            {
                for (var j = i + 1; j < group.Count; j++)
                {
                    var pair = new PetPair(group[i], group[j]);
                    if (qualified.Contains(pair)) _droppedThisTick.Add(pair);
                }
            }
        }

        private void TryStartSession(float now, List<string> group, string key)
        {
            var sessionId = _nextSessionId;
            var started = new List<string>(group.Count);

            // 先检查全部、再逐个进入：任何一只意外失败都整组回滚，不允许半套。
            for (var i = 0; i < group.Count; i++)
            {
                var pet = RuntimeOf(group[i]);
                if (pet != null && pet.TryStartSocial(sessionId))
                {
                    started.Add(group[i]);
                    continue;
                }

                for (var k = 0; k < started.Count; k++)
                {
                    RuntimeOf(started[k])?.CancelSocial(sessionId, "组启动失败回滚");
                }

                Debug.LogError($"[SocialCoordinator] 会话 {sessionId} 有成员未能进入共同大笑，已整组回滚（{key}）。");
                return;
            }

            _nextSessionId++;

            // 会话开始即记录个人冷却；取消不归还。
            for (var i = 0; i < group.Count; i++) _cooldownStart[group[i]] = now;

            // 整组只由排序后的第一只播一次笑声：四只同时笑也只出一个声音，避免叠音过响。
            RuntimeOf(group[0])?.PlaySocialSfx(_socialSfxId);

            var members = new List<string>(group);
            _sessions.Add(new SocialSessionInfo(sessionId, now, members));
            _startedThisTick.Add(members);
            _pendingSince.Remove(key);
        }

        private void ForgetUnseenPending()
        {
            if (_pendingSince.Count == 0 || _pendingSince.Count == _seenPending.Count) return;

            var stale = new List<string>();
            foreach (var key in _pendingSince.Keys)
            {
                if (!_seenPending.Contains(key)) stale.Add(key);
            }

            // 本帧不再出现的候选直接忘掉：不结算、不记冷却，也不产生丢弃事件。
            stale.Sort(StringComparer.Ordinal);
            for (var i = 0; i < stale.Count; i++) _pendingSince.Remove(stale[i]);
        }

        private PetRuntime RuntimeOf(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return null;
            return _byId.TryGetValue(petId, out var pet) ? pet : null;
        }
    }
}
