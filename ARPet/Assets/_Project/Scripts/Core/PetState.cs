using System;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 宠物的长期状态数值。与"正在执行的动画"分离：
    /// 数值回答"它现在有多饿"，行为回答"它现在在干什么"，两者用各自的时间尺度更新。
    /// </summary>
    public enum PetNeed
    {
        /// <summary>饥饿度：0 = 很饱，1 = 非常饿。随时间上升。</summary>
        Hunger = 0,

        /// <summary>开心度：0 = 低落，1 = 很高兴。逗弄与投喂提升，随时间缓慢下降。</summary>
        Happiness = 1,

        /// <summary>精力：0 = 困倦，1 = 精神。随时间下降，休息恢复。</summary>
        Energy = 2,
    }

    /// <summary>
    /// 宠物状态数值的只读快照。行为层之外的模块（界面、存档、表现）只看快照，不直接改数值。
    /// </summary>
    [Serializable]
    public struct PetState
    {
        public float Hunger;
        public float Happiness;
        public float Energy;

        /// <summary>行为层之外只递增的一次性计数，用于反应解锁与统计。</summary>
        public int FeedCount;
        public int PetCount;
        public int PlayCount;

        /// <summary>拍照留念次数。</summary>
        public int PhotoCount;

        public static PetState CreateDefault() => new PetState
        {
            Hunger = 0.30f,
            Happiness = 0.60f,
            Energy = 0.85f,
            FeedCount = 0,
            PetCount = 0,
            PlayCount = 0,
            PhotoCount = 0,
        };

        public float Get(PetNeed need) => need switch
        {
            PetNeed.Hunger => Hunger,
            PetNeed.Happiness => Happiness,
            PetNeed.Energy => Energy,
            _ => 0f,
        };

        public int GetCount(InteractionEventKind kind) => kind switch
        {
            InteractionEventKind.Feed => FeedCount,
            InteractionEventKind.Pet => PetCount,
            InteractionEventKind.Play => PlayCount,
            InteractionEventKind.Photo => PhotoCount,
            _ => 0,
        };

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
            $"Hunger={Hunger:F2} Happiness={Happiness:F2} Energy={Energy:F2} " +
            $"(feed={FeedCount} pet={PetCount} play={PlayCount} photo={PhotoCount})";
    }

    /// <summary>
    /// 宠物当前正在执行的行为。空闲态是安全默认值。
    /// 后续扩展的 WalkLoop / TouchReact / Happy / DozeLoop 等动作会作为新的枚举值加入，
    /// 但只有真正做好动画的才允许进入配置表。
    /// </summary>
    public enum PetBehaviorId
    {
        None = 0,

        /// <summary>待机（对应 Pet01_Idle，3.0 s 循环）。</summary>
        Idle = 1,

        /// <summary>进食（对应 Pet01_Eat，3.2 s 单次）。</summary>
        Eating = 2,

        /// <summary>被逗弄后的短促反应（动作尚未制作）。</summary>
        Petted = 3,

        /// <summary>休息 / 打盹（动作尚未制作）。</summary>
        Resting = 4,

        /// <summary>追球移动（动作尚未制作）。</summary>
        Chasing = 5,
    }
}
