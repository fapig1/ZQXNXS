using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.App
{
    /// <summary>
    /// 多宠运行容器：一只宠物一个槽，槽里有独立的 <see cref="PetRuntime"/>、独立的
    /// <see cref="TrackingStateMachine"/>、表现层引用和"本帧能否交互"标记。
    ///
    /// 为什么单独抽成纯 C# 类：多宠的关键规则（逐宠丢失/恢复、事件路由、全局暂停）
    /// 必须能在 EditMode 里脱离场景验证，不能只靠真机看画面。
    ///
    /// 三条纪律：
    /// <list type="number">
    /// <item><b>丢失与恢复都不重建运行时。</b>同一只宠物的 <see cref="PetRuntime"/> 引用在
    ///       丢失前、丢失中、恢复后始终是同一个对象，数值与事件去重状态不会被重置。</item>
    /// <item><b>跟踪只决定"能不能交互"和"行为时钟走不走"。</b>行为时钟在跟踪无效时冻结，
    ///       而不是快进补帧；开心值的时间衰减照常进行。</item>
    /// <item><b>事件按 PetId 路由。</b>交互事件只送到归属宠物，别的宠物不受影响；
    ///       当前不可交互的宠物的事件直接拒绝，不排队等恢复后补发。</item>
    /// </list>
    ///
    /// <b>共同大笑的分层：</b><see cref="ProximityRule"/>（Interaction）只读位姿判"够近 / 已分开 / 已锁"，
    /// <see cref="SocialCoordinator"/>（Pet）只做组划分与开始/取消仲裁，本类只负责按顺序把它们接起来
    /// （<see cref="TickSocial"/>），并保证整组开始时锁住组内全部关系对。
    /// </summary>
    public sealed class PetSession
    {
        private readonly ITrackingSource _tracking;
        private readonly IMarkerRegistry _registry;
        private readonly float _staleSeconds;

        private readonly List<Slot> _slots = new();
        private readonly List<string> _petIds = new();
        private readonly List<PetRuntime> _runtimes = new();

        // 复用同一个列表装配接近规则的输入，避免每帧分配。只在本帧内有效。
        private readonly List<ProximityInput> _socialInputs = new();

        /// <summary>共同大笑组协调器（只做组划分与仲裁，不读位姿）。只有一只宠物时也能构造，只是永远没有对。</summary>
        private SocialCoordinator _social;

        /// <summary>接近规则（只读位姿，不改任何数值）。由 <see cref="TickSocial"/> 每帧推进。</summary>
        private ProximityRule _proximity;
        private Func<PetPair, bool> _isLockedCallback;
        private Func<string, bool> _canInteractCallback;
        private Func<PetPair, bool> _isSeparatedCallback;

        /// <summary>
        /// 组装是否至少有一只可用宠物。目标卡未注册的宠物会被跳过并报错，
        /// 其余宠物继续运行；全部不可用时为 false。
        /// </summary>
        public bool IsReady { get; }

        /// <summary>可用宠物的稳定 ID，顺序与构造时传入的身份目录一致。</summary>
        public IReadOnlyList<string> PetIds => _petIds;

        /// <summary>可用宠物的运行时，顺序与 <see cref="PetIds"/> 一致。存档层按 PetId 逐只读写。</summary>
        public IReadOnlyList<PetRuntime> Runtimes => _runtimes;

        /// <summary>当前选中的宠物（界面高亮与状态面板显示的那一只）。选择本身不加分。</summary>
        public string SelectedPetId { get; private set; }

        public PetSession(PetConfig config, IReadOnlyList<PetIdentity> identities, ITrackingSource tracking,
            IMarkerRegistry registry, Func<string, IPetPresenter> presenterFor)
        {
            _tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (config == null) throw new ArgumentNullException(nameof(config));

            SelectedPetId = string.Empty;

            if (!PetIdentityCatalog.TryValidate(identities, out var identityError))
            {
                Debug.LogError($"[PetSession] 宠物身份目录不可用：{identityError}");
                IsReady = false;
                return;
            }

            // 过期阈值与跟踪源保持一致：注册表配了自定义值就用它，否则用默认值。
            _staleSeconds = registry is MarkerRegistryBase registryBase
                ? registryBase.StaleSeconds
                : PoseFreshness.DefaultStaleSeconds;

            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (!registry.IsRegistered(identity.TargetId))
                {
                    // 单只缺卡不应拖垮整场：记录是哪只不可用，其余宠物继续。
                    Debug.LogError($"[PetSession] 宠物 “{identity.PetId}” 的目标卡 “{identity.TargetId}” " +
                                   "未在跟踪源中注册，已跳过该宠物（其余宠物继续运行）。");
                    continue;
                }

                var presenter = presenterFor?.Invoke(identity.PetId) ?? NullPresenter.Instance;
                var runtime = new PetRuntime(identity, config, presenter);
                var slot = new Slot(identity, runtime, new TrackingStateMachine(), presenter);

                _slots.Add(slot);
                _petIds.Add(identity.PetId);
                _runtimes.Add(runtime);
            }

            if (_slots.Count == 0)
            {
                Debug.LogError("[PetSession] 没有任何可用的宠物：请检查跟踪源的 targets 是否覆盖身份目录中的目标卡。");
                IsReady = false;
                return;
            }

            for (var i = 0; i < _slots.Count; i++) _slots[i].Runtime.Start();

            // 共同大笑：接近规则给出"哪一对够近 / 是否已分开 / 是否已锁"，
            // 协调器只接收结论并做组划分与仲裁。只有一只宠物时也构造（永远没有关系对）。
            _social = new SocialCoordinator(_runtimes, config);
            _proximity = new ProximityRule(config.ResolveSocial());

            SelectedPetId = _slots[0].Identity.PetId;
            IsReady = true;
        }

        /// <summary>
        /// 每帧推进所有宠物。
        /// <paramref name="trackingRunning"/> 为 false 时按"暂停"采样，所有宠物立即不可交互，
        /// 行为时钟冻结但开心值仍按时间衰减。
        /// </summary>
        public void Tick(float now, float deltaTime, bool trackingRunning)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];

                // ① 每只宠物看各自目标卡的采样，状态机互不干扰。
                var sample = trackingRunning
                    ? _tracking.GetSample(slot.Identity.TargetId)
                    : PoseSample.Unavailable(slot.Identity.TargetId, TrackingStatus.Paused, now);
                slot.Tracking.Tick(sample, now, _staleSeconds);

                var canInteract = trackingRunning && slot.Tracking.CanInteract;
                slot.CanInteract = canInteract;

                // ② 开心值衰减对所有宠物照常：跟踪丢了，数值照样随时间缓慢下降。
                slot.Runtime.ApplyDecay(deltaTime, now);

                // ③ 行为时钟只在可交互时推进。冻结而不是快进，避免恢复瞬间动作跳帧。
                if (canInteract) slot.Runtime.Tick(deltaTime);

                // ④ 表现层连续量：新版不再按困倦放慢，固定 1 倍速。
                slot.Presenter.SetPlaybackSpeed(1f);
                slot.Presenter.SetInteractable(canInteract);
                // 显示比交互宽一档：LIMITED 时照常显示、不开放交互（碰撞体随 SetInteractable 关闭）。
                slot.Presenter.SetTrackingVisibility(trackingRunning && slot.Tracking.CanShow);
            }
        }

        /// <summary>
        /// 全部宠物进入暂停（切后台用）。不重建运行时，不重置数值；
        /// 恢复由下一次 <see cref="Tick"/> 正常完成，不伪造"离开/重新进入"。
        /// </summary>
        public void PauseAll(string reason, float now)
        {
            var pauseReason = string.IsNullOrEmpty(reason) ? "跟踪暂停" : reason;

            // 社交先收：整组取消（带会话 ID）比逐只 Interrupt 更明确，也不会漏掉等待中的候选；
            // 接近规则的停留计时一并清零，但<b>锁保持不变</b>——暂停不是离开。
            _social?.CancelAll(pauseReason);
            _proximity?.ResetTransient();

            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.Tracking.Tick(PoseSample.Unavailable(slot.Identity.TargetId, TrackingStatus.Paused, now),
                    now, _staleSeconds);
                slot.CanInteract = false;
                slot.Runtime.Interrupt(pauseReason);
                slot.Presenter.SetInteractable(false);
                slot.Presenter.SetTrackingVisibility(false);
            }
        }

        /// <summary>
        /// 提交一个交互事件。按 <see cref="InteractionEvent.PetId"/> 路由到对应宠物；
        /// 未知宠物或该宠当前不可交互时拒绝。
        /// </summary>
        public bool TrySubmit(in InteractionEvent interactionEvent)
        {
            if (!IsReady) return false;
            if (string.IsNullOrEmpty(interactionEvent.PetId)) return false;

            var slot = FindSlot(interactionEvent.PetId);
            if (slot == null) return false;

            // 跟踪无效 / 过期时不接受新交互，也不排队补发。
            if (!slot.CanInteract) return false;

            if (!slot.Runtime.TrySubmit(interactionEvent)) return false;

            // 成功交互的宠物成为选中（不额外加分）。
            SelectedPetId = slot.Identity.PetId;
            return true;
        }

        /// <summary>
        /// 该宠当前是否可接受空间交互：跟踪状态允许，且这一帧的采样确实新鲜。
        /// </summary>
        public bool CanInteract(string petId, float now)
        {
            if (!IsReady) return false;
            var slot = FindSlot(petId);
            if (slot == null || !slot.Tracking.CanInteract) return false;

            return PoseFreshness.IsSettleable(_tracking.GetSample(slot.Identity.TargetId), now, _staleSeconds);
        }

        /// <summary>切换选中宠物。选择不产生数值变化，也不产生交互事件。</summary>
        public bool Select(string petId)
        {
            if (FindSlot(petId) == null) return false;
            SelectedPetId = petId;
            return true;
        }

        /// <summary>按 PetId 取运行时（界面、调试与测试用）。</summary>
        public bool TryGetRuntime(string petId, out PetRuntime runtime)
        {
            var slot = FindSlot(petId);
            runtime = slot?.Runtime;
            return slot != null;
        }

        /// <summary>该宠的跟踪界面状态。未知或未注册的宠物返回 <see cref="TrackingUiState.Searching"/>。</summary>
        public TrackingUiState TrackingStateOf(string petId) =>
            FindSlot(petId)?.Tracking.State ?? TrackingUiState.Searching;

        /// <summary>该宠本帧能否交互（与最近一次 <see cref="Tick"/> 一致）。</summary>
        public bool CanInteractThisFrame(string petId) => FindSlot(petId)?.CanInteract ?? false;

        /// <summary>共同大笑组协调器（只读观察；测试与调试面板用）。</summary>
        public SocialCoordinator Social => _social;

        /// <summary>接近规则（只读观察；测试与调试面板用）。</summary>
        public ProximityRule Proximity => _proximity;

        /// <summary>该宠此刻是否属于某个进行中的共同大笑会话。</summary>
        public bool IsInSocial(string petId) => _social != null && _social.IsInSession(petId);

        /// <summary>
        /// 推进共同大笑：装配本帧的空间输入 → 接近规则判"够近 / 已分开 / 已锁" →
        /// 协调器做组划分与仲裁 → 把本帧的开始/丢弃结论回写给接近规则。
        ///
        /// <b>必须在 <see cref="Tick"/> 之后、同一帧内调用</b>：
        /// 它依赖 <see cref="Tick"/> 算好的"本帧能否交互"，也依赖应用层先提交点按，
        /// 这样被点按占用的宠物只会让候选等待，不会两边同时生效。
        /// </summary>
        /// <param name="now">单调时钟（秒），与会话开始时刻、个人冷却共用。</param>
        /// <param name="deltaTime">本帧时长，只用于累计停留时间。</param>
        /// <param name="trackingRunning">全局跟踪是否在运行（暂停时全部视为不可用）。</param>
        public void TickSocial(float now, float deltaTime, bool trackingRunning)
        {
            if (!IsReady || _proximity == null || _social == null) return;

            // ① 逐宠装配输入：可用 = 全局跟踪在跑 + 本帧可交互 + 位姿可结算（Tracked 且新鲜）。
            _socialInputs.Clear();
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var sample = trackingRunning
                    ? _tracking.GetSample(slot.Identity.TargetId)
                    : PoseSample.Unavailable(slot.Identity.TargetId, TrackingStatus.Paused, now);

                _socialInputs.Add(new ProximityInput
                {
                    PetId = slot.Identity.PetId,
                    Usable = trackingRunning &&
                             slot.CanInteract &&
                             PoseFreshness.IsSettleable(sample, now, _staleSeconds),
                    // 新鲜的 LIMITED 卡仍在画面里：不能判距离，也不算"拿走"。
                    Present = trackingRunning && slot.Tracking.CanShow,
                    Position = sample.Position,
                    Rotation = sample.Rotation,
                });
            }

            // ② 接近规则只判几何与停留，不结算任何数值。
            //    全局暂停期间不推进：否则暂停时长会被当成"把卡拿出画面"而解锁。
            if (trackingRunning) _proximity.Tick(deltaTime, _socialInputs);
            else _proximity.ResetTransient();

            // ③ 协调器只接收结论：合格关系对、是否已锁、是否可交互、是否已真实分开。
            //    三个回调在首次使用时缓存成委托字段：方法组每次转换都会新建委托对象。
            _isLockedCallback ??= _proximity.IsLocked;
            _canInteractCallback ??= CanInteractThisFrame;
            _isSeparatedCallback ??= _proximity.IsSeparated;
            _social.Tick(now, _proximity.QualifiedPairs, _isLockedCallback, _canInteractCallback, _isSeparatedCallback);

            // ④ 会话成立时锁住组内全部关系对（同一次接近只触发一次）；
            //    候选超时被丢弃时把这些对打回重新累计停留时间。
            var started = _social.StartedThisTick;
            for (var i = 0; i < started.Count; i++) _proximity.LockGroup(started[i]);

            var dropped = _social.DroppedThisTick;
            for (var i = 0; i < dropped.Count; i++) _proximity.ResetDwell(dropped[i]);
        }

        private Slot FindSlot(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return null;
            for (var i = 0; i < _slots.Count; i++)
            {
                if (string.Equals(_slots[i].Identity.PetId, petId, StringComparison.Ordinal)) return _slots[i];
            }

            return null;
        }

        /// <summary>一只宠物的全部运行时依赖。只在构造时创建，之后不替换。</summary>
        private sealed class Slot
        {
            public Slot(PetIdentity identity, PetRuntime runtime, TrackingStateMachine tracking, IPetPresenter presenter)
            {
                Identity = identity;
                Runtime = runtime;
                Tracking = tracking;
                Presenter = presenter;
            }

            public PetIdentity Identity { get; }

            public PetRuntime Runtime { get; }

            public TrackingStateMachine Tracking { get; }

            public IPetPresenter Presenter { get; }

            /// <summary>本帧能否交互。与跟踪状态机的 <c>CanInteract</c> 一起受全局跟踪开关约束。</summary>
            public bool CanInteract { get; set; }
        }

        /// <summary>没有配置表现层时的空实现，保证行为层不必到处判空。</summary>
        private sealed class NullPresenter : IPetPresenter
        {
            public static readonly NullPresenter Instance = new NullPresenter();

            public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds) { }

            public void SetExpression(PetExpression expression, float intensity) { }

            public void PlaySfx(string sfxId) { }

            public void PlayFx(string fxId) { }

            public void SetPlaybackSpeed(float speed) { }

            public void SetInteractable(bool interactable) { }

            public void SetTrackingVisibility(bool visible) { }
        }
    }
}
