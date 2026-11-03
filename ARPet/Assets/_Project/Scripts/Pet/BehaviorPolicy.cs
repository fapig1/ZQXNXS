using System.Collections.Generic;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 行为决策的纯函数部分：给定当前状态、请求和允许的行为集合，选出下一个行为。
    /// 抽成静态类是为了能在 EditMode 测试里直接构造场景验证优先级，
    /// 不需要场景对象、Animator 或 AR 跟踪。
    ///
    /// 新版运行不再读饥饿 / 精力，也不再有"饿了 / 困了"的环境需求分支：
    /// 行为只由交互事件驱动，其余时间回落到默认行为。
    /// </summary>
    public static class BehaviorPolicy
    {
        /// <summary>一次决策的输入。</summary>
        public struct Request
        {
            /// <summary>当前正在执行的行为。</summary>
            public PetBehaviorId Current;

            /// <summary>当前行为是否已经结束（单次行为播完，或循环行为被要求退出）。</summary>
            public bool CurrentFinished;

            /// <summary>是否有待处理的高优先级交互事件（触屏逗弄 / 追球）。</summary>
            public bool HasPendingInteraction;

            /// <summary>待处理交互的类型，用于决定进入哪个行为。</summary>
            public InteractionEventKind PendingKind;

            /// <summary>允许进入的行为集合（对应"动画已制作完成"的行为）。</summary>
            public IReadOnlyCollection<PetBehaviorId> Allowed;
        }

        /// <summary>
        /// 决策规则，按优先级从高到低：
        /// <list type="number">
        /// <item>交互事件优先，但要先通过 <c>CanAccept</c> 的可打断检查（在状态机里做）。</item>
        /// <item>单次行为未播完时不被打断——否则一次逗弄反应会被压成半截。</item>
        /// <item>回落默认行为。</item>
        /// </list>
        /// </summary>
        public static PetBehaviorId Decide(in Request r)
        {
            // ① 交互事件：逗弄 → 反应；追球 → 移动。
            if (r.HasPendingInteraction && CanAccept(r, MapInteraction(r.PendingKind)))
            {
                return MapInteraction(r.PendingKind);
            }

            // ② 单次行为未结束时保持当前行为，不被其他请求抢占。
            if (!r.CurrentFinished && IsOneShot(r.Current)) return r.Current;

            // ③ 回落默认行为。
            if (Allowed(r, PetBehaviorId.Idle)) return PetBehaviorId.Idle;

            // ④ 极端兜底：只允许一个行为时也返回它；一个都没有则返回 None。
            if (r.Allowed != null)
            {
                foreach (var b in r.Allowed) return b;
            }
            return PetBehaviorId.None;
        }

        /// <summary>
        /// 事件能否被接受。AlreadyPlayingInterruptible 规则：
        /// 正在做单次行为时默认不被打断；空闲或循环行为可以被交互打断。
        /// 后续若要支持"逗弄反应期间不接第二次点击"，在这里加分支，而不是在 Update 里加 if。
        /// </summary>
        public static bool CanAccept(in Request r, PetBehaviorId target)
        {
            if (target == PetBehaviorId.None) return false;
            if (!Allowed(r, target)) return false;
            // 新 Id 也不能重入尚未完成的单次行为，否则会丢掉前一次结算。
            return !IsOneShot(r.Current) || r.CurrentFinished;
        }

        public static bool IsOneShot(PetBehaviorId behavior) => behavior switch
        {
            PetBehaviorId.Idle => false,
            PetBehaviorId.Resting => false,
            PetBehaviorId.Chasing => false,
            PetBehaviorId.Eating => true,   // 旧资产，保留枚举值以免序列化错位
            PetBehaviorId.Petted => true,
            PetBehaviorId.Exclusive => true, // 满值专属演出：单次、不循环、到时长即结束
            // 共同大笑：单次、不循环。执行中占用行为通道（点按被拒且不排队），
            // 但只由 SocialCoordinator 发起，不经过交互事件。
            PetBehaviorId.Social => true,
            PetBehaviorId.VoiceReply => true,
            _ => false,
        };

        /// <summary>
        /// 交互事件 → 行为。食物卡投喂（Feed）已删除，因此不再有 Feed → Eating 的映射。
        /// </summary>
        public static PetBehaviorId MapInteraction(InteractionEventKind kind) => kind switch
        {
            InteractionEventKind.Pet => PetBehaviorId.Petted,
            InteractionEventKind.Play => PetBehaviorId.Chasing,
            _ => PetBehaviorId.None,
        };

        private static bool Allowed(in Request r, PetBehaviorId behavior)
        {
            if (r.Allowed == null) return false;
            foreach (var b in r.Allowed)
            {
                if (b == behavior) return true;
            }
            return false;
        }
    }
}
