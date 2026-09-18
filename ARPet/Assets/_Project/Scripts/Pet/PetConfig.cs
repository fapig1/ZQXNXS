using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 数值变化规则。行为层和表现层都只发出需求名，具体增减数值在这里统一配置，
    /// 避免同一个阈值散落在多个脚本里硬编码。
    /// </summary>
    [Serializable]
    public struct NeedAdjust
    {
        public PetNeed Need;
        public float PerSecond;
        public float OnEnter;
    }

    /// <summary>一条行为的定义：要播什么、播多久、算不算完成、进出时改哪些数值。循环行为永不"完成"。</summary>
    [Serializable]
    public struct BehaviorDefinition
    {
        public PetBehaviorId Behavior;

        /// <summary>表现层使用的动画名。留空表示使用行为名本身。</summary>
        public string AnimationName;

        public bool Loop;

        /// <summary>单次行为的时长（秒）。循环行为填 0，由动画自身循环。</summary>
        public float DurationSeconds;

        /// <summary>进入该行为时立刻施加的数值变化。</summary>
        public NeedAdjust[] OnEnter;

        /// <summary>行为进行中每秒的数值变化。</summary>
        public NeedAdjust[] PerSecond;

        /// <summary>行为结束时的一次性结算（如进食完成加饱腹）。<b>只在正常完成时触发一次</b>。</summary>
        public NeedAdjust[] OnExitSettled;

        /// <summary>被打断时施加的结算（通常比正常完成轻，或不做结算）。</summary>
        public NeedAdjust[] OnInterrupted;

        public string ResolvedAnimationName =>
            string.IsNullOrWhiteSpace(AnimationName) ? Behavior.ToString() : AnimationName;

        /// <summary>
        /// 单次行为必须有有限正时长且禁止循环；循环行为必须声明 Loop。
        /// None 永远不可用，避免配置错误令进食无法完成。
        /// </summary>
        public bool IsUsable() => Behavior != PetBehaviorId.None &&
            (BehaviorPolicy.IsOneShot(Behavior)
                ? !Loop && DurationSeconds > 0f && !float.IsInfinity(DurationSeconds)
                : Loop);
    }

    /// <summary>
    /// 宠物行为的全部可调参数。
    /// 这是"设计数据"资产（ScriptableObject），行为逻辑本身在 <c>BehaviorStateMachine</c> 里，
    /// 改阈值不需要改代码，也可以在 EditMode 测试里构造临时实例。
    /// </summary>
    [CreateAssetMenu(menuName = "桌上有龙/宠物行为配置", fileName = "PetBehaviorConfig")]
    public sealed class PetConfig : ScriptableObject
    {
        [Header("初始数值")]
        public NeedSeed[] NeedSeeds =
        {
            new NeedSeed { Need = PetNeed.Hunger, Initial = 0.30f, DecayPerSecond = 0.0035f, Max = 1f },
            new NeedSeed { Need = PetNeed.Happiness, Initial = 0.60f, DecayPerSecond = -0.0015f, Max = 1f },
            new NeedSeed { Need = PetNeed.Energy, Initial = 0.85f, DecayPerSecond = -0.0010f, Max = 1f },
        };

        [Header("数值离线上限")]
        [Tooltip("应用未运行期间最多补算多少秒的衰减，避免离线数天后数值直接见底。")]
        public float MaxOfflineSeconds = 12f * 3600f;

        [Header("阈值")]
        [Tooltip("饥饿度高于该值时进入进食需求。")]
        [Range(0f, 1f)] public float HungryThreshold = 0.65f;

        [Tooltip("精力低于该值时倾向于休息。")]
        [Range(0f, 1f)] public float SleepyThreshold = 0.25f;

        [Header("行为表")]
        public BehaviorDefinition[] Behaviors =
        {
            new BehaviorDefinition
            {
                Behavior = PetBehaviorId.Idle,
                AnimationName = "Idle",
                Loop = true,
                DurationSeconds = 0f,
            },
            new BehaviorDefinition
            {
                Behavior = PetBehaviorId.Eating,
                AnimationName = "Eat",
                Loop = false,
                DurationSeconds = 3.2f,
                OnEnter = new[] { new NeedAdjust { Need = PetNeed.Happiness, OnEnter = 0.02f } },
                OnExitSettled = new[]
                {
                    new NeedAdjust { Need = PetNeed.Hunger, OnEnter = -0.45f },
                },
                OnInterrupted = Array.Empty<NeedAdjust>(),
            },
            new BehaviorDefinition
            {
                Behavior = PetBehaviorId.Petted,
                AnimationName = "TouchReact",
                Loop = false,
                DurationSeconds = 0.7f,
                OnEnter = new[] { new NeedAdjust { Need = PetNeed.Happiness, OnEnter = 0.06f } },
            },
            new BehaviorDefinition
            {
                Behavior = PetBehaviorId.Resting,
                AnimationName = "DozeLoop",
                Loop = true,
                DurationSeconds = 0f,
                PerSecond = new[] { new NeedAdjust { Need = PetNeed.Energy, PerSecond = 0.02f } },
            },
            new BehaviorDefinition
            {
                Behavior = PetBehaviorId.Chasing,
                AnimationName = "WalkLoop",
                Loop = true,
                DurationSeconds = 0f,
                PerSecond = new[] { new NeedAdjust { Need = PetNeed.Energy, PerSecond = -0.01f } },
            },
        };

        [Header("表现")]
        [Tooltip("行为切换时的默认过渡时间（秒）。首版建议 0.12～0.20。")]
        public float DefaultCrossFadeSeconds = 0.15f;

        [Header("进食行为")]
        public EatConfig Eat = EatConfig.Default;

        /// <summary>行为表中的默认行为（Idle），也是打断回落的目标。</summary>
        public PetBehaviorId DefaultBehavior = PetBehaviorId.Idle;

        [Serializable]
        public struct NeedSeed
        {
            public PetNeed Need;
            public float Initial;
            public float DecayPerSecond;
            public float Max;
        }

        /// <summary>
        /// 投喂判定参数。判定发生"是否产生一个投喂事件"，
        /// 与"宠物接不接受"（由行为层决定）分开配置。
        /// </summary>
        [Serializable]
        public struct EatConfig
        {
            [Tooltip("两个目标中心的最大水平距离（米）。")]
            public float MaxHorizontalDistance;

            [Tooltip("食物卡相对小窝平面的允许高度差（米）。")]
            public float MaxHeightDelta;

            [Tooltip("需要连续满足条件的停留时长（秒）。")]
            public float RequiredDwellSeconds;

            [Tooltip("同一张卡结算后的冷却（秒）。")]
            public float CooldownSeconds;

            [Tooltip("目标离开判定区多远后，才允许视为新的「再次进入」（米）。")]
            public float ReArmDistance;

            public static EatConfig Default => new EatConfig
            {
                MaxHorizontalDistance = 0.10f,
                MaxHeightDelta = 0.06f,
                RequiredDwellSeconds = 0.6f,
                CooldownSeconds = 4f,
                ReArmDistance = 0.16f,
            };
        }

        /// <summary>按行为 Id 查找定义；找不到返回 <c>false</c>，调用方应回落到默认行为。</summary>
        public bool TryGetBehavior(PetBehaviorId behavior, out BehaviorDefinition definition)
        {
            if (Behaviors != null)
            {
                for (var i = 0; i < Behaviors.Length; i++)
                {
                    if (Behaviors[i].Behavior == behavior)
                    {
                        definition = Behaviors[i];
                        return true;
                    }
                }
            }
            definition = default;
            return false;
        }

        /// <summary>
        /// 该行为是否真的可以播放：既要有定义，也要有动画资产。
        /// 尚未接入 Unity 的行为（Petted / Resting / Chasing）返回 <c>false</c>，
        /// 于是状态机不会盲选一个没有动画的状态。
        /// </summary>
        public bool IsBehaviorReady(PetBehaviorId behavior, out BehaviorDefinition definition)
        {
            if (!TryGetBehavior(behavior, out definition)) return false;
            if (!definition.IsUsable()) return false;
            return Aspect.HasAnimatorClip(behavior);
        }

        /// <summary>
        /// 动画资产可用性。
        /// 首版只有 Idle 与 Eat 两段动画（见 Docs/奶蛙最终版资产说明.md），
        /// 其余动作在动画做出来之前必须在代码里显式关闭，避免状态机切到空动画。
        /// </summary>
        public static class Aspect
        {
            public static bool HasAnimatorClip(PetBehaviorId behavior) => behavior switch
            {
                PetBehaviorId.Idle => true,
                PetBehaviorId.Eating => true,
                PetBehaviorId.Petted => false,   // TouchReact 尚未接入 Unity
                PetBehaviorId.Resting => false,  // DozeLoop 尚未接入 Unity
                PetBehaviorId.Chasing => false,  // WalkLoop 尚未接入 Unity，且穿地检查未通过
                _ => false,
            };
        }
    }
}
