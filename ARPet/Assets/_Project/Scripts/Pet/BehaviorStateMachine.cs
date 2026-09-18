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
    /// </list>
    /// 它<b>不</b>做距离、高度、停留时间判定（那是交互层的事），也<b>不</b>直接读跟踪位姿。
    /// </summary>
    public sealed class BehaviorStateMachine : IInteractionEventSink
    {
        private readonly PetConfig _config;
        private readonly IPetStateProvider _state;
        private readonly IPetPresenter _presenter;
        private readonly PetBehaviorId _defaultBehavior;
        private readonly Dictionary<PetBehaviorId, BehaviorDefinition> _definitions = new();

        /// <summary>当前允许进入的行为集合。只包含"动画已制作完成"的行为。</summary>
        private readonly List<PetBehaviorId> _allowed = new();

        // 每个交互流独立递增。触屏的高序号不能吞掉食物卡的后续事件。
        private readonly Dictionary<(InteractionEventKind Kind, MarkerSlot Source), int> _lastConsumedIds = new();
        private bool _behaviorActive;

        public BehaviorStateMachine(PetConfig config, IPetStateProvider state, IPetPresenter presenter)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

            RebuildAllowedBehaviors();
            // 默认行为不得绕过动画白名单，也不能用单次进食构成无交互的自动结算循环。
            _defaultBehavior = _allowed.Contains(config.DefaultBehavior) && !BehaviorPolicy.IsOneShot(config.DefaultBehavior)
                ? config.DefaultBehavior
                : (_allowed.Contains(PetBehaviorId.Idle) ? PetBehaviorId.Idle : PetBehaviorId.None);
            if (_defaultBehavior != config.DefaultBehavior)
                Debug.LogWarning($"[BehaviorStateMachine] 默认行为 {config.DefaultBehavior} 不可用，回落到 {_defaultBehavior}。");
        }

        /// <summary>当前行为。</summary>
        public PetBehaviorId Current { get; private set; } = PetBehaviorId.None;

        /// <summary>当前行为已经运行的时间（秒）。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>当前行为是否为单次行为。</summary>
        public bool CurrentIsOneShot => BehaviorPolicy.IsOneShot(Current);

        /// <summary>状态机是否已经启动并处于某个行为中。</summary>
        public bool IsRunning => _behaviorActive;

        /// <summary>被拒绝的交互事件数，仅用于调试与测试观察。</summary>
        public int RefusedCount { get; private set; }

        /// <summary>已接受并结算的交互事件数。</summary>
        public int AcceptedCount { get; private set; }

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
            var stream = (interactionEvent.Kind, interactionEvent.Source);
            if (_lastConsumedIds.TryGetValue(stream, out var lastId) && interactionEvent.Id <= lastId)
            {
                RefusedCount++;
                return false;
            }

            var target = BehaviorPolicy.MapInteraction(interactionEvent.Kind);
            var request = BuildRequest(hasPending: true, interactionEvent.Kind);

            // 记录事件 Id 之后才检查可接受性：被拒绝的事件同样不应被重复处理。
            _lastConsumedIds[stream] = interactionEvent.Id;

            if (!BehaviorPolicy.CanAccept(request, target))
            {
                RefusedCount++;
                _presenter.SetExpression(PetExpression.Refuse, 1f);
                return false;
            }

            AcceptedCount++;
            ElapsedSeconds = 0f;
            EnterBehavior(target);
            return true;
        }

        /// <summary>
        /// 每帧推进。由应用入口调用一次，内部不使用协程，
        /// 以便在 EditMode 测试里用假时间步进。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_behaviorActive || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;

            var dt = deltaTime < 0f ? 0f : deltaTime;
            if (!_definitions.TryGetValue(Current, out var definition)) return;

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

        /// <summary>
        /// 外部请求打断当前行为（例如跟踪丢失、切后台）。
        /// 打断会走 <c>OnInterrupted</c> 结算表，通常比正常完成轻甚至不结算。
        /// </summary>
        public void Interrupt(string reason)
        {
            if (!_behaviorActive || Current == _defaultBehavior) return;
            CompleteCurrent(interrupted: true, reason);
        }

        /// <summary>结束状态机（跟踪暂停、应用退出）。不做数值结算。</summary>
        public void Stop()
        {
            _behaviorActive = false;
            Current = PetBehaviorId.None;
            ElapsedSeconds = 0f;
        }

        /// <summary>当前可进入的行为集合，供界面展示"已解锁动作"。</summary>
        public IReadOnlyList<PetBehaviorId> AllowedBehaviors => _allowed;

        private void RebuildAllowedBehaviors()
        {
            _allowed.Clear();
            _definitions.Clear();

            if (_config.Behaviors != null)
            {
                foreach (var definition in _config.Behaviors)
                {
                    if (!definition.IsUsable()) continue;
                    _definitions[definition.Behavior] = definition;
                }
            }

            foreach (var pair in _definitions)
            {
                if (PetConfig.Aspect.HasAnimatorClip(pair.Key)) _allowed.Add(pair.Key);
            }

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

            // 进入时结算一次。
            ApplyDelta(definition.OnEnter, 1f);

            // 表现层只收到"播什么、是否循环、过渡多久"。
            _presenter.PlayBehavior(behavior, definition.ResolvedAnimationName,
                definition.Loop, _config.DefaultCrossFadeSeconds);
            _presenter.SetExpression(ExpressionFor(behavior), 1f);
        }

        private void CompleteCurrent(bool interrupted, string reason = null)
        {
            if (!TryGetDefinition(Current, out var definition)) return;

            var table = interrupted ? definition.OnInterrupted : definition.OnExitSettled;
            ApplyDelta(table, 1f);

            // 结算后无论如何都离开当前行为。
            if (interrupted)
            {
                Debug.Log($"[BehaviorStateMachine] 行为 {Current} 被打断：{reason ?? "未说明"}");
            }

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

        private BehaviorPolicy.Request BuildRequest(bool hasPending, InteractionEventKind kind)
        {
            var snapshot = _state.Snapshot;
            return new BehaviorPolicy.Request
            {
                Current = Current,
                CurrentFinished = _behaviorActive ? (!CurrentIsOneShot) : true,
                HasPendingInteraction = hasPending,
                PendingKind = kind,
                Hunger = snapshot.Hunger,
                Energy = snapshot.Energy,
                HungryThreshold = _config.HungryThreshold,
                SleepyThreshold = _config.SleepyThreshold,
                Allowed = _allowed,
            };
        }

        private bool TryGetDefinition(PetBehaviorId behavior, out BehaviorDefinition definition)
        {
            if (_definitions.TryGetValue(behavior, out definition)) return true;
            definition = default;
            return false;
        }

        /// <summary>行为 → 表情的默认映射。具体贴图由表现层决定。</summary>
        public static PetExpression ExpressionFor(PetBehaviorId behavior) => behavior switch
        {
            PetBehaviorId.Eating => PetExpression.Happy,
            PetBehaviorId.Petted => PetExpression.Surprised,
            PetBehaviorId.Resting => PetExpression.Sleepy,
            PetBehaviorId.Chasing => PetExpression.Happy,
            _ => PetExpression.Neutral,
        };
    }
}
