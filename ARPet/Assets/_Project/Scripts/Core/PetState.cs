using System;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 宠物的长期状态数值。与"正在执行的动画"分离：
    /// 数值回答"它现在有多高兴"，行为回答"它现在在干什么"，两者用各自的时间尺度更新。
    /// 新版运行只结算 <see cref="Happiness"/>（统一叫"开心值"）。
    /// </summary>
    public enum PetNeed
    {
        /// <summary>
        /// <b>旧版字段</b>：饥饿度。食物卡玩法已删除，新版运行不读不写，仅为旧存档 v1 迁移保留。
        /// </summary>
        Hunger = 0,

        /// <summary>开心值：0 = 低落，1 = 很高兴。触屏逗弄提升，随时间缓慢下降。</summary>
        Happiness = 1,

        /// <summary>
        /// <b>旧版字段</b>：精力。困倦放慢已退出新版运行，仅为旧存档 v1 迁移保留。
        /// </summary>
        Energy = 2,
    }

    /// <summary>
    /// 开心值满值周期的状态。它回答"这次满值演出要不要播、什么时候播"，
    /// 与"正在播哪个动作"分开：演出本身是 <see cref="PetBehaviorId.Exclusive"/>。
    /// <para><b>0 必须是 <see cref="Armed"/></b>：旧 v2 存档没有该字段，读成 0 就等于"重新武装、尚未满值"，
    /// 不需要额外迁移。</para>
    /// </summary>
    public enum HappyCycleState
    {
        /// <summary>可以再次触发满值演出（数值与冷却各自独立判定）。</summary>
        Armed = 0,

        /// <summary>冷却内已经攒满：等冷却结束且行为回到默认行为时自动开始专属演出。</summary>
        PendingAuto = 1,

        /// <summary>读档时已满值：等玩家点一下确认再开始专属演出（读档不自动播）。</summary>
        PendingConfirm = 2,

        /// <summary>资格已消费（专属演出已开始过）。回落值已满足重新武装条件时立即转回 <see cref="Armed"/>。</summary>
        Released = 3,
    }

    /// <summary>
    /// 宠物状态数值的只读快照。行为层之外的模块（界面、存档、表现）只看快照，不直接改数值。
    /// </summary>
    [Serializable]
    public struct PetState
    {
        /// <summary>旧版字段，新版运行不读写；保留仅为旧存档 v1 → v2 迁移。</summary>
        public float Hunger;

        public float Happiness;

        /// <summary>旧版字段，新版运行不读写；保留仅为旧存档 v1 → v2 迁移。</summary>
        public float Energy;

        /// <summary>旧版字段，新版运行不读写；投喂次数仅用于旧存档迁移与历史统计。</summary>
        public int FeedCount;

        public int PetCount;
        public int PlayCount;

        /// <summary>拍照留念次数。</summary>
        public int PhotoCount;

        /// <summary>开心值满值周期状态。缺省 0 = <see cref="HappyCycleState.Armed"/>。</summary>
        public HappyCycleState HappyCycle;

        /// <summary>专属演出冷却剩余秒数。只随行为时钟递减，跟踪丢失时冻结。</summary>
        public float ExclusiveCooldownRemaining;

        /// <summary>专属演出"开始过"的次数（每次进入 <see cref="PetBehaviorId.Exclusive"/> 加一）。</summary>
        public int ExclusiveStartCount;

        /// <summary>专属演出"完整看完"的次数（正常播完才加一，被打断不计）。</summary>
        public int ExclusiveWatchedCount;

        /// <summary>
        /// 共同大笑"完整看完"的次数。会话正常结束才加一，任何成员丢失 / 移走导致的整组取消不计。
        /// 缺省 0：旧 v2 存档没有该字段，读成 0 即可，不需要升 schema。
        /// </summary>
        public int SocialWatchedCount;

        public static PetState CreateDefault() => new PetState
        {
            Hunger = 0.30f,
            Happiness = 0.60f,
            Energy = 0.85f,
            FeedCount = 0,
            PetCount = 0,
            PlayCount = 0,
            PhotoCount = 0,
            HappyCycle = HappyCycleState.Armed,
            ExclusiveCooldownRemaining = 0f,
            ExclusiveStartCount = 0,
            ExclusiveWatchedCount = 0,
            SocialWatchedCount = 0,
        };

        /// <summary>读取旧版需要时返回快照里的历史值；新版只有 <see cref="PetNeed.Happiness"/> 参与运行。</summary>
        public float Get(PetNeed need) => need switch
        {
            PetNeed.Hunger => Hunger,
            PetNeed.Happiness => Happiness,
            PetNeed.Energy => Energy,
            _ => 0f,
        };

        /// <summary>按事件种类取一次性计数。旧版投喂（Feed）已不在新版事件种类中，FeedCount 只能从旧存档读取。</summary>
        public int GetCount(InteractionEventKind kind) => kind switch
        {
            InteractionEventKind.Pet => PetCount,
            InteractionEventKind.Play => PlayCount,
            InteractionEventKind.Photo => PhotoCount,
            _ => 0,
        };

        /// <summary>写入一份数值，返回修改后的副本（结构体语义）。旧版需要仍可写，但新版运行不会调用。</summary>
        public PetState With(PetNeed need, float value)
        {
            switch (need)
            {
                case PetNeed.Hunger: Hunger = Clamp01(value); break;
                case PetNeed.Happiness: Happiness = Clamp01(value); break;
                case PetNeed.Energy: Energy = Clamp01(value); break;
            }
            return this;
        }

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public override string ToString() =>
            $"Happiness={Happiness:F2} (pet={PetCount} play={PlayCount} photo={PhotoCount}) " +
            $"cycle={HappyCycle} cd={ExclusiveCooldownRemaining:F2}s " +
            $"exclusive={ExclusiveStartCount}/{ExclusiveWatchedCount} social={SocialWatchedCount} " +
            $"[legacy: hunger={Hunger:F2} energy={Energy:F2} feed={FeedCount}]";
    }

    /// <summary>
    /// 宠物当前正在执行的行为。空闲态是安全默认值。
    /// 后续扩展的 WalkLoop / TouchReact / DozeLoop 等动作会作为新的枚举值加入，
    /// 但只有真正做好动画的才允许进入配置表。
    /// </summary>
    public enum PetBehaviorId
    {
        None = 0,

        /// <summary>待机（对应 Pet01_Idle，3.0 s 循环）。</summary>
        Idle = 1,

        /// <summary>
        /// <b>旧资产</b>：进食（对应 Pet01_Eat，3.2 s 单次）。食物卡玩法已删除，
        /// 该值不进入新版行为配置；Eat 片段仍留在 Animator 中，供旧资产回归检查。
        /// </summary>
        Eating = 2,

        /// <summary>被逗弄后的短促反应（动作尚未制作）。</summary>
        Petted = 3,

        /// <summary>休息 / 打盹（动作尚未制作）。</summary>
        Resting = 4,

        /// <summary>追球移动（动作尚未制作）。</summary>
        Chasing = 5,

        /// <summary>
        /// 满值专属演出（四色各一段正式片段）。单一 ID，具体动画名与时长由
        /// <c>PetConfig</c> 的变体调参表给出（classic_proud / calm_zen / rage_roar / divine_awaken）。
        /// </summary>
        Exclusive = 6,

        /// <summary>
        /// 共同大笑（共享片段 social_laugh，2.4 s 单次）。只在两只以上角色的靠近组协调成功时进入，
        /// <b>不结算开心值</b>：靠近本身已是反馈，且不能与满值专属演出互相自动触发。
        /// 由 <c>SocialCoordinator</c> 统一提交会话 ID，单宠状态机只负责执行与完成。
        /// </summary>
        Social = 7,

        /// <summary>
        /// 语音回应：用户说出口令后，在场的宠物用同一句话回应。单次行为；动画名、时长、
        /// 表情与音效由 <c>PetConfig.VoiceCommands</c> 按口令给出。加分与一次单击相同，并受口令冷却限制。
        /// </summary>
        VoiceReply = 8,
    }
}
