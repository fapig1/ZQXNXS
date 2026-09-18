using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 行为决策的纯函数部分：给定当前状态、请求和允许的行为集合，选出下一个行为。
    /// 抽成静态类是为了能在 EditMode 测试里直接构造场景验证优先级，
    /// 不需要场景对象、Animator 或 AR 跟踪。
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

            /// <summary>是否有待处理的高优先级交互事件（投喂 / 逗弄）。</summary>
            public bool HasPendingInteraction;

            /// <summary>待处理交互的类型，用于决定进入哪个行为。</summary>
            public InteractionEventKind PendingKind;

            public float Hunger;
            public float Energy;

            public float HungryThreshold;
            public float SleepyThreshold;

            /// <summary>允许进入的行为集合（对应"动画已制作完成"的行为）。</summary>
            public IReadOnlyCollection<PetBehaviorId> Allowed;
        }

        /// <summary>
        /// 决策规则，按优先级从高到低：
        /// <list type="number">
        /// <item>交互事件优先，但要先通过 <c>CanAccept</c> 的可打断检查（在状态机里做）。</item>
        /// <item>单次行为未播完时不被环境需求打断——否则进食会被"困了"打断成半截。</item>
        /// <item>精力过低 → 休息。</item>
        /// <item>饥饿度高于阈值 → 进食需求（在 AR 场景里表现为"等你投喂"，而不是自己找吃的）。</item>
        /// <item>否则回到默认行为。</item>
        /// </list>
        /// </summary>
        public static PetBehaviorId Decide(in Request r)
        {
            // ① 交互事件：投喂 → 进食；逗弄 → 反应；追球 → 移动。
            if (r.HasPendingInteraction && CanAccept(r, MapInteraction(r.PendingKind)))
            {
                return MapInteraction(r.PendingKind);
            }

            // ② 单次行为未结束时保持当前行为，不被环境需求抢占。
            if (!r.CurrentFinished && IsOneShot(r.Current)) return r.Current;

            // ③ 精力不足 → 休息（只有做了动画才可能被允许）。
            if (r.Energy < r.SleepyThreshold && Allowed(r, PetBehaviorId.Resting))
            {
                return PetBehaviorId.Resting;
            }

            // ④ 饥饿：状态数值本身不需要"动作"，由界面提示玩家投喂；
            //    这里不选 Eating，因为进食必须由真实投喂事件触发（同一次进入只结算一次）。
            //    如果将来要做"主动讨食"表演，应新增一个独立行为，而不是复用 Eating。

            // ⑤ 回落默认行为。
            if (Allowed(r, PetBehaviorId.Idle)) return PetBehaviorId.Idle;

            // ⑥ 极端兜底：只允许一个行为时也返回它；一个都没有则返回 None。
            if (r.Allowed != null)
            {
                foreach (var b in r.Allowed) return b;
            }
            return PetBehaviorId.None;
        }

        /// <summary>
        /// 事件能否被接受。AlreadyPlayingInterruptible 规则：
        /// 正在做单次行为（如进食）时，默认不被打断；空闲或循环行为可以被交互打断。
        /// 后续若要支持"吃饭时被摸会不高兴"，在这里加分支，而不是在 Update 里加 if。
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
            PetBehaviorId.Eating => true,
            PetBehaviorId.Petted => true,
            _ => false,
        };

        public static PetBehaviorId MapInteraction(InteractionEventKind kind) => kind switch
        {
            InteractionEventKind.Feed => PetBehaviorId.Eating,
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
