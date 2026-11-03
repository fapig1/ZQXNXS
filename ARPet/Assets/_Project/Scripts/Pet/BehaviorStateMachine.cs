using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 行为状态机。职责边界：
    /// <list type="bullet">
    /// <item>接收已经过交互层判定的事件，决定<b>接不接受</b>。</item>
    /// <item>维护"当前正在执行的行为"与它的计时 / 完成状态。</item>
    /// <item>在确定的时刻结算数值一次，并把播放指令交给 <see cref="IPetPresenter"/>。</item>
    /// <item>驱动开心值满值周期（<see cref="HappyCycle"/>）：满值专属演出的开始、冷却与计数。</item>
    /// </list>
    /// 它<b>不</b>做距离、高度、停留时间判定（那是交互层的事），也<b>不</b>直接读跟踪位姿。
    ///
    /// <b>唯一结算点：</b>开心值只在 <see cref="TrySubmit"/> 接受触屏事件的那一处结算一次
    /// （单击 / 双击各自的增量来自变体调参表，满值回落也在这里发生）。
    /// 动画事件与 <see cref="Tick"/> 都不加分。
    /// </summary>
    public sealed class BehaviorStateMachine : IInteractionEventSink
    {
        private readonly PetConfig _config;
        private readonly IPetStateProvider _state;
        private readonly IPetPresenter _presenter;
        private readonly PetBehaviorId _defaultBehavior;
        private readonly Dictionary<PetBehaviorId, BehaviorDefinition> _definitions = new();

        /// <summary>当前允许进入的行为集合。只包含"定义可用且动画名在能力清单里"的行为。</summary>
        private readonly List<PetBehaviorId> _allowed = new();

        // 每个 (宠物, 事件种类) 独立递增。不同宠物的同名事件流互不吞并，
        // string 键使用默认比较器（Ordinal），与存档 ID 的写法保持一致。
        private readonly Dictionary<(InteractionEventKind Kind, string PetId), int> _lastConsumedIds = new();
        private bool _behaviorActive;

        // 共同大笑会话：只有协调器给出的会话 ID，行为机自己不生成 ID、不记账。
        private int _activeSocialSessionId;
        private int _lastCompletedSocialSessionId;

        /// <summary>旧签名（默认 classic 变体），保留以免大面积改测试与旧调用点。</summary>
        public BehaviorStateMachine(PetConfig config, IPetStateProvider state, IPetPresenter presenter)
            : this(config, state, presenter, PetVariantId.Classic)
        {
        }

        /// <summary>按形象变体构造：点击加分、回落值、冷却与专属动画名都取该变体的调参条目。</summary>
        public BehaviorStateMachine(PetConfig config, IPetStateProvider state, IPetPresenter presenter,
            PetVariantId variant)
            : this(config, state, presenter, new HappyCycle(config, variant))
        {
        }

        /// <summary>
        /// 由 <see cref="PetRuntime"/> 注入它持有的 <see cref="HappyCycle"/>，
        /// 保证一只宠物只有一份满值周期状态（冷却与计数不会出现两个副本）。
        /// </summary>
        public BehaviorStateMachine(PetConfig config, IPetStateProvider state, IPetPresenter presenter,
            HappyCycle cycle)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

            if (cycle == null)
            {
                Debug.LogWarning("[BehaviorStateMachine] 未提供满值周期，已按 classic 变体新建一份。");
                cycle = new HappyCycle(config, PetVariantId.Classic);
            }

            Cycle = cycle;

            RebuildAllowedBehaviors();
            // 默认行为不得绕过动画白名单，也不能用单次行为构成无交互的自动结算循环。
            _defaultBehavior = _allowed.Contains(config.DefaultBehavior) && !BehaviorPolicy.IsOneShot(config.DefaultBehavior)
                ? config.DefaultBehavior
                : (_allowed.Contains(PetBehaviorId.Idle) ? PetBehaviorId.Idle : PetBehaviorId.None);
            if (_defaultBehavior != config.DefaultBehavior)
                Debug.LogWarning($"[BehaviorStateMachine] 默认行为 {config.DefaultBehavior} 不可用，回落到 {_defaultBehavior}。");

            // 让快照从一开始就带着周期字段（默认 Armed / 0 / 0 / 0）。
            SyncCycle(markDirty: false);
        }

        /// <summary>该宠物的满值周期（冷却、开始 / 看完次数、周期状态）。</summary>
        public HappyCycle Cycle { get; }

        /// <summary>当前行为。</summary>
        public PetBehaviorId Current { get; private set; } = PetBehaviorId.None;

        /// <summary>当前行为已经运行的时间（秒）。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>当前行为是否为单次行为。</summary>
        public bool CurrentIsOneShot => BehaviorPolicy.IsOneShot(Current);

        /// <summary>是否正忙：单次行为（触屏反应 / 专属演出）执行中，新点按一律拒绝且不排队。</summary>
        public bool IsBusy => _behaviorActive && CurrentIsOneShot;

        /// <summary>状态机是否已经启动并处于某个行为中。</summary>
        public bool IsRunning => _behaviorActive;

        /// <summary>被拒绝的交互事件数，仅用于调试与测试观察。</summary>
        public int RefusedCount { get; private set; }

        /// <summary>已接受并结算的交互事件数。每个被接受的事件只结算一次。</summary>
        public int AcceptedCount { get; private set; }

        /// <summary>本变体的共同大笑是否可以播（行为定义可用且 social_laugh 在能力清单里）。</summary>
        public bool IsSocialAvailable => _allowed.Contains(PetBehaviorId.Social);

        /// <summary>
        /// 当前是否可以被共同大笑占用。四个条件全部成立才算：
        /// <list type="number">
        /// <item>状态机已启动；</item>
        /// <item>正处于默认行为（Idle）——执行中的单次演出不被打断；</item>
        /// <item>social_laugh 能力可用（缺片段只让这只退出社交，不影响别的行为）；</item>
        /// <item>满值周期<b>不是</b> <see cref="HappyCycleState.PendingAuto"/>。</item>
        /// </list>
        /// <b>PendingConfirm（读档后已满、等玩家点一下确认）不阻塞社交</b>：它要等用户操作，
        /// 若也算占位，读档后一直不点按的宠物会把别人的共同大笑无限期挡住。
        /// 它也不会被社交吞掉——确认点按仍然是玩家点一次就播一次。
        /// </summary>
        public bool CanStartSocial =>
            _behaviorActive &&
            Current == _defaultBehavior &&
            _defaultBehavior != PetBehaviorId.None &&
            IsSocialAvailable &&
            Cycle.State != HappyCycleState.PendingAuto;

        /// <summary>
        /// 当前会话 ID；不在共同大笑时为 0。
        /// </summary>
        public int ActiveSocialSessionId => Current == PetBehaviorId.Social ? _activeSocialSessionId : 0;

        /// <summary>
        /// 最近一次<b>正常播完</b>的共同大笑会话 ID（被打断不写，0 表示还没播完过）。
        /// 协调器用它判断"整组都演完了"，并据此对每个成员只提交一次观看计数。
        /// </summary>
        public int LastCompletedSocialSessionId => _lastCompletedSocialSessionId;

        /// <summary>
        /// 启动状态机，进入默认行为。
        /// 重复调用不会重置正在执行的行为。仅首次启动应用默认行为的 OnEnter 配置。
        /// </summary>
        public void Start()
        {
            if (_behaviorActive) return;
            if (_defaultBehavior == PetBehaviorId.None)
            {
                Debug.LogError("[BehaviorStateMachine] 缺少可用的默认循环行为（Idle），无法启动。");
                return;
            }
            _behaviorActive = true;
            ElapsedSeconds = 0f;
            EnterBehavior(_defaultBehavior);
        }

        /// <summary>
        /// 提交一个交互事件。返回是否被接受。
        /// 同一次有效进入只会产生一个事件 Id，因此重复提交同一 Id 会被直接忽略，
        /// 这是"同一次进入只结算一次"的实现点。
        /// </summary>
        public bool TrySubmit(in InteractionEvent interactionEvent)
        {
            if (!_behaviorActive) return false;

            // 事件去重：恢复跟踪、重复触发或界面重复点击都不应产生第二次结算。
            var stream = (interactionEvent.Kind, interactionEvent.PetId);
            if (_lastConsumedIds.TryGetValue(stream, out var lastId) && interactionEvent.Id <= lastId)
            {
                RefusedCount++;
                return false;
            }

            // 记录事件 Id 之后才检查可接受性：被拒绝的事件同样不应被重复处理。
            _lastConsumedIds[stream] = interactionEvent.Id;

            return interactionEvent.Kind switch
            {
                InteractionEventKind.Pet => TryAcceptPet(interactionEvent.Gesture),
                InteractionEventKind.Voice => TryAcceptVoice(interactionEvent.CommandId, interactionEvent.Time),
                _ => TryAcceptInteraction(interactionEvent.Kind),
            };
        }

        /// <summary>上一次接受语音回应的时刻（事件时钟）；用于口令冷却。</summary>
        private float _lastVoiceTime = float.NegativeInfinity;

        /// <summary>最近一次被接受的语音口令 ID，供调试与测试观察。</summary>
        public string LastVoiceCommandId { get; private set; } = string.Empty;

        /// <summary>
        /// 语音口令。加分等同一次单击（同样走满值周期：跨越满值时直接开始专属演出），
        /// 否则播口令配置的回应动作、表情与音效。三条拒绝规则：
        /// <list type="number">
        /// <item>单次行为执行中（触屏反应、专属、共同大笑、上一句回应）拒绝且不排队；</item>
        /// <item>距上次语音回应不足 <see cref="PetConfig.VoiceCooldownSeconds"/> 拒绝，防止连喊刷分；</item>
        /// <item>口令未知或其动作不在能力清单里拒绝，且不结算任何数值。</item>
        /// </list>
        /// </summary>
        private bool TryAcceptVoice(string commandId, float time)
        {
            if (IsBusy) return Refuse();
            if (!_config.TryGetVoiceCommand(commandId, out var command) || !_config.IsVoiceCommandReady(command))
                return Refuse();

            var cooldown = Mathf.Max(0f, _config.VoiceCooldownSeconds);
            if (!float.IsNaN(time) && time - _lastVoiceTime < cooldown) return Refuse();

            var happinessBefore = _state.Get(PetNeed.Happiness);
            var exclusiveReady = _allowed.Contains(PetBehaviorId.Exclusive);
            var decision = Cycle.DecideTap(PetGesture.SingleTap, happinessBefore, exclusiveReady);

            _lastVoiceTime = time;
            LastVoiceCommandId = command.CommandId;

            // 跨越满值：和单击一样直接开始专属演出（专属自带音效，口令音效不再叠加）。
            if (decision.StartExclusive && TryStartExclusive(decision.Gain))
            {
                AcceptedCount++;
                return true;
            }

            AcceptedCount++;
            if (decision.AddsGain) _state.Apply(PetNeed.Happiness, decision.Gain, ElapsedSeconds);
            if (decision.SetPendingAuto)
            {
                Cycle.ArmPendingAuto();
                SyncCycle(markDirty: true);
            }

            _voiceDefinition = new BehaviorDefinition
            {
                Behavior = PetBehaviorId.VoiceReply,
                AnimationName = command.AnimationName,
                Loop = false,
                DurationSeconds = command.DurationSeconds,
            };
            Current = PetBehaviorId.VoiceReply;
            ElapsedSeconds = 0f;
            _presenter.PlayBehavior(PetBehaviorId.VoiceReply, command.AnimationName, false,
                _config.DefaultCrossFadeSeconds);
            _presenter.SetExpression(command.Expression, 1f);
            if (!string.IsNullOrEmpty(command.SfxId)) _presenter.PlaySfx(command.SfxId);
            return true;
        }

        // 语音回应的定义按口令动态给出（动作与时长随口令变），不放进静态行为表。
        private BehaviorDefinition _voiceDefinition;

        /// <summary>
        /// 每帧推进。由应用入口调用一次，内部不使用协程，
        /// 以便在 EditMode 测试里用假时间步进。
        /// 跟踪无效时调用方不应调用它：行为时钟与满值冷却会一起冻结。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_behaviorActive || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;

            var dt = deltaTime < 0f ? 0f : deltaTime;

            // 冷却只随行为时钟递减。每帧变化不打存档脏标记，但快照仍会带着最新值。
            if (Cycle.AdvanceCooldown(dt)) SyncCycle(markDirty: false);

            if (TryGetDefinition(Current, out var definition))
            {
                var activeSeconds = CurrentIsOneShot
                    ? Mathf.Min(dt, Mathf.Max(0f, definition.DurationSeconds - ElapsedSeconds)) : dt;
                ElapsedSeconds += dt;
                ApplyDelta(definition.PerSecond, activeSeconds, perSecond: true);

                // 单次行为到时长即结算一次，并回到默认行为。
                if (CurrentIsOneShot && definition.DurationSeconds > 0f &&
                    ElapsedSeconds >= definition.DurationSeconds)
                {
                    CompleteCurrent(interrupted: false);
                }
            }

            TryAutoStartExclusive();
        }

        /// <summary>
        /// 外部请求打断当前行为（例如跟踪丢失、切后台）。
        /// 打断会走 <c>OnInterrupted</c> 结算表，通常比正常完成轻甚至不结算。
        /// 专属演出被打断时：已消费的资格不返还、不计"看完"，恢复后也不会自动重播。
        /// </summary>
        public void Interrupt(string reason)
        {
            if (!_behaviorActive || Current == _defaultBehavior) return;
            CompleteCurrent(interrupted: true, reason);
        }

        /// <summary>
        /// 由 <c>SocialCoordinator</c> 提交一次共同大笑会话。<b>原子</b>：不满足
        /// <see cref="CanStartSocial"/> 时返回 <c>false</c> 且不留任何副作用，
        /// 这样协调器可以"先检查全部成员、再逐个进入"，失败时整组回滚。
        /// 共同大笑<b>不加开心值</b>，也不消费满值周期。
        /// </summary>
        /// <param name="sessionId">协调器分配的会话 ID，必须 &gt; 0。</param>
        public bool TryStartSocial(int sessionId)
        {
            if (sessionId <= 0) return false;
            if (!CanStartSocial) return false;
            if (!TryGetDefinition(PetBehaviorId.Social, out _)) return false;

            _activeSocialSessionId = sessionId;
            ElapsedSeconds = 0f;
            EnterBehavior(PetBehaviorId.Social);
            return Current == PetBehaviorId.Social;
        }

        /// <summary>
        /// 打断共同大笑：只有"当前正在演且会话 ID 匹配"才生效，其他情况静默忽略
        /// （协调器整组取消时不必先确认每只的状态）。
        /// 走被打断的结算路径，因此<b>不</b>写 <see cref="LastCompletedSocialSessionId"/>。
        /// </summary>
        public void CancelSocial(int sessionId, string reason)
        {
            if (Current != PetBehaviorId.Social) return;
            if (sessionId <= 0 || _activeSocialSessionId != sessionId) return;

            CompleteCurrent(interrupted: true, reason);
        }

        /// <summary>结束状态机（跟踪暂停、应用退出）。不做数值结算。</summary>
        public void Stop()
        {
            _behaviorActive = false;
            Current = PetBehaviorId.None;
            ElapsedSeconds = 0f;
            _activeSocialSessionId = 0;
        }
        /// <summary>
        /// 读档恢复满值周期。不因 100% 自动开始：已满值只会变成"待确认"，
        /// 等玩家点一下才播专属演出。
        /// </summary>
        public void RestoreFrom(PetState saved)
        {
            Cycle.RestoreFrom(saved, _state.Get(PetNeed.Happiness));
            SyncCycle(markDirty: false);
        }

        /// <summary>当前可进入的行为集合，供界面展示"已解锁动作"。</summary>
        public IReadOnlyList<PetBehaviorId> AllowedBehaviors => _allowed;

        private void RebuildAllowedBehaviors()
        {
            _allowed.Clear();
            _definitions.Clear();

            if (_config.Behaviors == null) return;

            for (var i = 0; i < _config.Behaviors.Length; i++)
            {
                var behavior = _config.Behaviors[i].Behavior;
                if (behavior == PetBehaviorId.None) continue;

                // 能力清单是唯一判据：缺片段只禁用这一条行为，不影响其他行为。
                // 专属演出的动画名与时长从本变体的调参条目取，所以这里必须带上变体。
                if (!_config.IsBehaviorReady(behavior, Cycle.Variant, out var definition)) continue;

                _definitions[behavior] = definition;
                if (!_allowed.Contains(behavior)) _allowed.Add(behavior);
            }
        }

        /// <summary>
        /// 触屏事件（单击 / 双击）。这里是开心值的唯一结算点。
        /// 单次行为执行中一律拒绝且不排队；接受后只结算一次增量。
        /// </summary>
        private bool TryAcceptPet(PetGesture gesture)
        {
            if (IsBusy) return Refuse();

            var happinessBefore = _state.Get(PetNeed.Happiness);
            var exclusiveReady = _allowed.Contains(PetBehaviorId.Exclusive);

            // 决策是纯函数：先算清"要不要直接开始专属 / 要不要挂起自动开始"，再提交。
            var decision = Cycle.DecideTap(gesture, happinessBefore, exclusiveReady);

            // ① 直接开始专属演出（跨越满值且无冷却，或待确认状态下的一次确认点按）。
            if (decision.StartExclusive && TryStartExclusive(decision.Gain))
            {
                AcceptedCount++;
                return true;
            }

            // ② 普通触屏反应。TouchReact 缺片段时整条点按被拒绝，且不结算任何数值。
            if (!_allowed.Contains(PetBehaviorId.Petted)) return Refuse();

            AcceptedCount++;
            if (decision.AddsGain) _state.Apply(PetNeed.Happiness, decision.Gain, ElapsedSeconds);

            if (decision.SetPendingAuto)
            {
                // 冷却内已经攒满：只挂起一条自动开始请求，本次播触屏反应。
                Cycle.ArmPendingAuto();
                SyncCycle(markDirty: true);
            }

            ElapsedSeconds = 0f;
            EnterBehavior(PetBehaviorId.Petted);
            return true;
        }

        /// <summary>追球等非触屏事件：没有手势，不结算开心值。</summary>
        private bool TryAcceptInteraction(InteractionEventKind kind)
        {
            var target = BehaviorPolicy.MapInteraction(kind);
            var request = BuildRequest(hasPending: true, kind);

            if (!BehaviorPolicy.CanAccept(request, target)) return Refuse();

            AcceptedCount++;
            ElapsedSeconds = 0f;
            EnterBehavior(target);
            return true;
        }

        /// <summary>
        /// 开始一次专属演出。<b>原子完成</b>：加分（如有）、开心值回落、周期置为已消费、
        /// 冷却重新计时、开始次数加一、进入专属行为，全部在同一次调用里结束。
        /// </summary>
        private bool TryStartExclusive(float gain)
        {
            if (!_allowed.Contains(PetBehaviorId.Exclusive)) return false;
            if (!TryGetDefinition(PetBehaviorId.Exclusive, out _)) return false;

            // ① 本次点按的增量先结算（确认点按的增量为 0）。
            if (gain > 0f) _state.Apply(PetNeed.Happiness, gain, ElapsedSeconds);

            // ② 开心值回落到本变体的回落值。
            var fallback = Cycle.FallbackHappiness;
            var current = _state.Get(PetNeed.Happiness);
            if (!Mathf.Approximately(current, fallback))
                _state.Apply(PetNeed.Happiness, fallback - current, ElapsedSeconds);

            // ③ 周期：资格已消费 + 冷却从这一刻开始计时 + 开始次数加一。
            Cycle.BeginExclusive();
            SyncCycle(markDirty: true);

            ElapsedSeconds = 0f;
            EnterBehavior(PetBehaviorId.Exclusive);
            var sfx = _config.ExclusiveSfxFor(Cycle.Variant);
            if (!string.IsNullOrEmpty(sfx)) _presenter.PlaySfx(sfx);
            return true;
        }

        /// <summary>冷却结束且已回到默认行为时，自动开始挂起的那一次专属演出。</summary>
        private void TryAutoStartExclusive()
        {
            if (!Cycle.IsAutoStartReady) return;
            if (Current != _defaultBehavior) return;
            TryStartExclusive(0f);
        }

        /// <summary>
        /// 拒绝一次已经通过去重检测的事件。只有处在默认行为（Idle）时才换"拒绝"表情；
        /// 单次演出（专属 / 共同大笑 / 语音回应 / 触屏反应）期间拒绝不得改写正在演的表情，
        /// 否则专属演出中多点一下，招牌眼就被关掉、动作却继续播（2026-10-30 真机 BUG-05）。
        /// </summary>
        private bool Refuse()
        {
            RefusedCount++;
            if (Current == _defaultBehavior) _presenter.SetExpression(PetExpression.Refuse, 1f);
            return false;
        }

        private void EnterBehavior(PetBehaviorId behavior)
        {
            if (!_allowed.Contains(behavior) || !TryGetDefinition(behavior, out var definition))
            {
                Debug.LogWarning($"[BehaviorStateMachine] 试图进入不可用行为 {behavior}，已忽略。");
                return;
            }

            Current = behavior;
            ElapsedSeconds = 0f;

            // 进入时结算一次（Petted 的加分已移到触屏事件处，这里对触屏反应为空表）。
            ApplyDelta(definition.OnEnter, 1f);

            // 表现层只收到"播什么、是否循环、过渡多久"。
            _presenter.PlayBehavior(behavior, definition.ResolvedAnimationName,
                definition.Loop, _config.DefaultCrossFadeSeconds);
            _presenter.SetExpression(ExpressionFor(behavior), 1f);
        }

        private void CompleteCurrent(bool interrupted, string reason = null)
        {
            if (!TryGetDefinition(Current, out var definition)) return;

            var finished = Current;
            var socialSessionId = _activeSocialSessionId;
            var table = interrupted ? definition.OnInterrupted : definition.OnExitSettled;
            ApplyDelta(table, 1f);

            if (interrupted)
            {
                Debug.Log($"[BehaviorStateMachine] 行为 {finished} 被打断：{reason ?? "未说明"}");
            }
            else if (finished == PetBehaviorId.Exclusive)
            {
                // 正常播完才算"看完"；被打断不返还已消费的资格，也不计看完。
                Cycle.MarkExclusiveCompleted();
                SyncCycle(markDirty: true);
            }
            else if (finished == PetBehaviorId.Social)
            {
                // 只有"由行为时钟正常播完"才写会话 ID，协调器据此提交一次观看计数。
                // 打断（切后台、成员丢失、CancelAll）一律不写，也不能由动画事件补上。
                _lastCompletedSocialSessionId = socialSessionId;
            }

            // 离开共同大笑时清掉会话 ID：ActiveSocialSessionId 对外只在 Social 期间有值。
            _activeSocialSessionId = 0;
            // 语音回应结束后清掉临时定义：下次进入由新口令重新给出。
            if (finished == PetBehaviorId.VoiceReply) _voiceDefinition = default;

            // 结算后无论如何都离开当前行为。
            EnterBehavior(_defaultBehavior);
        }

        /// <summary>
        /// 应用一张数值变化表。
        /// 进入/退出表只执行一次性项；每帧表只累计 PerSecond，不能重复执行 OnEnter。
        /// </summary>
        private void ApplyDelta(NeedAdjust[] table, float seconds, bool perSecond = false)
        {
            if (table == null || seconds <= 0f) return;

            for (var i = 0; i < table.Length; i++)
            {
                var adjust = table[i];
                var delta = perSecond ? adjust.PerSecond * seconds : adjust.OnEnter;
                if (Mathf.Approximately(delta, 0f)) continue;
                _state.Apply(adjust.Need, delta, ElapsedSeconds);
            }
        }

        /// <summary>把满值周期同步进数值快照（存档只读这一份）。</summary>
        private void SyncCycle(bool markDirty) =>
            _state.StoreHappyCycle(Cycle.State, Cycle.CooldownRemaining, Cycle.Starts, Cycle.Completions, markDirty);

        private BehaviorPolicy.Request BuildRequest(bool hasPending, InteractionEventKind kind)
        {
            return new BehaviorPolicy.Request
            {
                Current = Current,
                CurrentFinished = _behaviorActive ? (!CurrentIsOneShot) : true,
                HasPendingInteraction = hasPending,
                PendingKind = kind,
                Allowed = _allowed,
            };
        }

        private bool TryGetDefinition(PetBehaviorId behavior, out BehaviorDefinition definition)
        {
            if (behavior == PetBehaviorId.VoiceReply && _voiceDefinition.Behavior == PetBehaviorId.VoiceReply)
            {
                definition = _voiceDefinition;
                return true;
            }

            if (_definitions.TryGetValue(behavior, out definition)) return true;
            definition = default;
            return false;
        }

        /// <summary>行为 → 表情的默认映射。具体贴图由表现层决定。</summary>
        public static PetExpression ExpressionFor(PetBehaviorId behavior) => behavior switch
        {
            PetBehaviorId.Petted => PetExpression.Surprised,
            PetBehaviorId.Exclusive => PetExpression.Signature,
            PetBehaviorId.Social => PetExpression.VeryHappy,
            PetBehaviorId.Resting => PetExpression.Sleepy,
            PetBehaviorId.Chasing => PetExpression.Happy,
            _ => PetExpression.Neutral,
        };
    }
}
