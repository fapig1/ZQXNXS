using System;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 一只宠物的独立运行状态：身份 + 开心值 + 行为状态机。
    ///
    /// 存在的理由：多宠同屏时，每只宠物必须有自己的行为时钟和数值，
    /// 不能把四只塞进同一个 <c>AppRoot</c> 字段里。这里刻意<b>不</b>是 MonoBehaviour，
    /// 也不引用任何场景对象，因此可以在 EditMode 测试里直接 <c>new</c> 出来逐帧推进。
    ///
    /// 职责边界：
    /// <list type="bullet">
    /// <item>只接受归属自己的交互事件（<see cref="InteractionEvent.PetId"/> 必须等于 <see cref="PetId"/>）。</item>
    /// <item>接受后由行为状态机在唯一一处结算开心值（单击 / 双击增量 + 满值回落），这里只补一次性计数。</item>
    /// <item>共同大笑由 <c>SocialCoordinator</c> 发起与取消，本类只透传状态机入口并记账"看完一次"。</item>
    /// <item>不读跟踪位姿、不判断距离与新鲜度——那是跟踪层 / 交互层的事。</item>
    /// </list>
    /// </summary>
    public sealed class PetRuntime : IInteractionEventSink
    {
        private readonly BehaviorStateMachine _behavior;
        private readonly IPetPresenter _presenter;

        /// <summary>最近一次已经记账的共同大笑会话 ID，用来保证同一会话只计一次。</summary>
        private int _lastSocialAccountedSessionId;

        /// <summary>该宠物的身份绑定（稳定 PetId / TargetId / 变体）。</summary>
        public PetIdentity Identity { get; }

        /// <summary>长期状态数值。存档只读写这一份。</summary>
        public PetNeeds Needs { get; }

        /// <summary>行为状态机。对外只读暴露当前行为与运行状态，不接受外部直接驱动。</summary>
        public BehaviorStateMachine Behavior => _behavior;

        /// <summary>开心值满值周期（由本运行时持有，交给行为状态机使用，一只宠物只有一份）。</summary>
        public HappyCycle HappyCycle { get; }

        public string PetId => Identity.PetId;

        public string TargetId => Identity.TargetId;

        /// <summary>当前行为。</summary>
        public PetBehaviorId CurrentBehavior => _behavior.Current;

        /// <summary>行为状态机是否已经启动。</summary>
        public bool IsRunning => _behavior.IsRunning;

        /// <summary>本变体的共同大笑是否可以播（social_laugh 缺片段时为 false）。</summary>
        public bool IsSocialAvailable => _behavior.IsSocialAvailable;

        /// <summary>现在能否被共同大笑占用（Idle、能力可用、无未开始的满值自动请求）。</summary>
        public bool CanStartSocial => _behavior.CanStartSocial;

        /// <summary>当前共同大笑会话 ID；不在共同大笑时为 0。</summary>
        public int ActiveSocialSessionId => _behavior.ActiveSocialSessionId;

        /// <summary>最近一次正常播完的共同大笑会话 ID；没播完过为 0。</summary>
        public int LastCompletedSocialSessionId => _behavior.LastCompletedSocialSessionId;

        /// <summary>数值快照，供界面与存档读取。</summary>
        public PetState Snapshot => Needs.Snapshot;

        public PetRuntime(PetIdentity identity, PetConfig config, IPetPresenter presenter)
        {
            if (!identity.IsValid)
                throw new ArgumentException($"宠物身份非法：{identity}", nameof(identity));
            if (config == null) throw new ArgumentNullException(nameof(config));

            Identity = identity;
            Needs = new PetNeeds(config);
            // 变体从身份取：四色的点击得分 / 回落值 / 冷却 / 专属动画名全部按变体查调参表。
            HappyCycle = new HappyCycle(config, identity.Variant);
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            _behavior = new BehaviorStateMachine(config, Needs, _presenter, HappyCycle);
        }

        /// <summary>启动行为状态机。重复调用不会重置正在执行的行为。</summary>
        public void Start() => _behavior.Start();

        /// <summary>推进行为时钟。跟踪无效时调用方不应调用它（行为需要冻结而不是快进）。</summary>
        public void Tick(float deltaTime) => _behavior.Tick(deltaTime);

        /// <summary>开心值的时间衰减。与行为时钟分开：跟踪丢失期间数值照常缓慢下降。</summary>
        public void ApplyDecay(float deltaTime, float now) => Needs.ApplyDecay(deltaTime, now);

        /// <summary>打断当前行为（跟踪丢失、切后台）。不做额外数值结算。</summary>
        public void Interrupt(string reason) => _behavior.Interrupt(reason);

        /// <summary>
        /// 由 <c>SocialCoordinator</c> 提交共同大笑。返回 false 表示这只此刻不能让出行为通道，
        /// 协调器据此整组回滚（不允许"一半成员在笑"）。
        /// </summary>
        public bool TryStartSocial(int sessionId) => _behavior.TryStartSocial(sessionId);

        /// <summary>
        /// 共同大笑开始时由协调器对<b>组内一只</b>调用，播放共同大笑音效（四只一起笑只出一次声）。
        /// </summary>
        public void PlaySocialSfx(string sfxId)
        {
            if (!string.IsNullOrEmpty(sfxId)) _presenter.PlaySfx(sfxId);
        }

        /// <summary>打断共同大笑（成员丢失、移走、全局暂停）。会话 ID 不匹配时静默忽略。</summary>
        public void CancelSocial(int sessionId, string reason) => _behavior.CancelSocial(sessionId, reason);

        /// <summary>
        /// 记一次"共同大笑完整看完"。协调器在整组正常结束后对每个成员<b>只调用一次</b>，
        /// 这里再加一层幂等保护：同一 sessionId 重复调用只记一次（返回 false），
        /// 更小的 ID（旧的会话）也不会被重复记账。
        /// </summary>
        public bool RecordSocialCompleted(int sessionId)
        {
            if (sessionId <= 0) return false;
            if (sessionId <= _lastSocialAccountedSessionId) return false;

            _lastSocialAccountedSessionId = sessionId;
            Needs.IncrementSocialWatched();
            return true;
        }

        /// <summary>
        /// 从存档恢复数值与满值周期。由数据层（<c>SaveCoordinator.ApplyTo</c>）按稳定 PetId 逐只调用，
        /// 业务代码不要直接改数值。恢复不会因为开心值已满而自动开始专属演出。
        /// </summary>
        public void RestoreFrom(PetState saved)
        {
            Needs.RestoreFrom(saved);
            _behavior.RestoreFrom(saved);
        }

        /// <summary>离线衰减补算。同样只在读档时调用一次。</summary>
        public void ApplyOfflineDecay(float offlineSeconds, float now) => Needs.ApplyOfflineDecay(offlineSeconds, now);

        /// <inheritdoc />
        /// <remarks>归属别的宠物的事件直接拒绝，避免"点 A 让 B 加开心值"。</remarks>
        public bool TrySubmit(in InteractionEvent interactionEvent)
        {
            if (!string.Equals(interactionEvent.PetId, PetId, StringComparison.Ordinal)) return false;
            if (!_behavior.TrySubmit(interactionEvent)) return false;

            Needs.IncrementCount(interactionEvent.Kind);
            return true;
        }
    }
}
