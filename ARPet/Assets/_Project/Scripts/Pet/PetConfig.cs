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

        /// <summary>行为结束时的一次性结算（如逗弄反应结束加开心值）。<b>只在正常完成时触发一次</b>。</summary>
        public NeedAdjust[] OnExitSettled;

        /// <summary>被打断时施加的结算（通常比正常完成轻，或不做结算）。</summary>
        public NeedAdjust[] OnInterrupted;

        public string ResolvedAnimationName =>
            string.IsNullOrWhiteSpace(AnimationName) ? Behavior.ToString() : AnimationName;

        /// <summary>
        /// 单次行为必须有有限正时长且禁止循环；循环行为必须声明 Loop。
        /// None 永远不可用，避免配置错误令单次行为无法完成。
        /// </summary>
        public bool IsUsable() => Behavior != PetBehaviorId.None &&
            (BehaviorPolicy.IsOneShot(Behavior)
                ? !Loop && DurationSeconds > 0f && !float.IsInfinity(DurationSeconds)
                : Loop);
    }

    /// <summary>
    /// 一个形象变体的调参条目：四色奶蛙的点击得分、满值回落、重新武装与专属演出全部在这里。
    /// 数值属于设计初值，改手感只改配置，不改业务代码。
    /// </summary>
    [Serializable]
    public struct VariantTuning
    {
        public PetVariantId Variant;

        /// <summary>单击加分（0～1 的开心值增量）。</summary>
        public float SingleTapGain;

        /// <summary>双击加分。<b>双击只结算这一份</b>，不会再叠加一次单击。</summary>
        public float DoubleTapGain;

        /// <summary>专属演出播完后开心值回落到该值。</summary>
        public float FallbackHappiness;

        /// <summary>重新武装阈值：回落值 ≤ 该值时，本次演出算作"资格已消费完毕"，可以再次攒满。</summary>
        public float RearmThreshold;

        /// <summary>满值冷却（秒），从上次专属演出<b>开始</b>算起。</summary>
        public float ExclusiveCooldownSeconds;

        /// <summary>专属演出的 Animator 状态名（classic_proud / calm_zen / rage_roar / divine_awaken）。</summary>
        public string ExclusiveStateName;

        /// <summary>专属演出时长（秒），按该段动作的实际长度配置。</summary>
        public float ExclusiveDurationSeconds;
    }

    /// <summary>
    /// 一条语音口令：用户说什么、宠物用什么动作 / 表情 / 音效回应。
    /// 识别模型只认 <see cref="Phrase"/>（关键词文件由它生成），业务只认稳定的 <see cref="CommandId"/>。
    /// </summary>
    [Serializable]
    public struct VoiceCommand
    {
        /// <summary>稳定 ID（如 <c>andi</c>）。事件、日志与音效都用它，不用中文原文。</summary>
        public string CommandId;

        /// <summary>口令原文（如"安迪"）。关键词识别命中后返回的就是这串文字。</summary>
        public string Phrase;

        /// <summary>回应动作的 Animator 状态名（必须在 <see cref="PetConfig.AnimatorStates"/> 里）。</summary>
        public string AnimationName;

        /// <summary>回应动作时长（秒），按所用动作片段的实际长度填写。</summary>
        public float DurationSeconds;

        /// <summary>回应时的表情。</summary>
        public PetExpression Expression;

        /// <summary>回应时播放的音效 ID（<c>Resources/Audio/</c> 下的文件名，不含扩展名）。</summary>
        public string SfxId;
    }

    /// <summary>
    /// 靠近共同大笑的空间与节奏参数。<b>几何判定本身在交互层</b>（ProximityRule），
    /// 本结构只是这些阈值的唯一配置来源，同时供 Pet 层的 <c>SocialCoordinator</c> 读取
    /// "候选最多等多久""参与者冷却多长"，避免两处各写一份数字。
    ///
    /// 全部是设计初值（2026-10-27 定稿），改手感只改配置：
    /// 进入与退出距离构成迟滞（退出必须比进入更远），否则卡在边界上会反复触发。
    /// </summary>
    [Serializable]
    public struct SocialTuning
    {
        /// <summary>进入条件：两卡中心的水平距离上限（米）。</summary>
        public float EnterDistance;

        /// <summary>退出条件：水平距离超过该值才算开始分开（米，必须 ≥ <see cref="EnterDistance"/>）。</summary>
        public float ExitDistance;

        /// <summary>进入与保持条件的双向高度差上限（米）。</summary>
        public float MaxHeightDelta;

        /// <summary>进入与保持条件的目标法向夹角上限（度），避免一张竖卡与一张横卡误触发。</summary>
        public float MaxNormalAngleDegrees;

        /// <summary>进入条件需要连续满足的时长（秒）。</summary>
        public float EnterDwellSeconds;

        /// <summary>退出条件需要连续保持的时长（秒）；确认分开后该关系对才重新武装。</summary>
        public float ExitDwellSeconds;

        /// <summary>
        /// 任一张卡连续不可用（拿出画面 / 丢失）满这么久，就把该关系对当作已分开、解锁（秒）。
        /// 短于它的丢失按跟踪抖动处理，锁保持不变。竖屏视野装不下两卡同时拉开 0.23m，靠它重新武装。
        /// </summary>
        public float LostSeparationSeconds;

        /// <summary>
        /// 拿出画面满 <see cref="LostSeparationSeconds"/> 后，放回时两卡相对位置至少要变化这么多才解锁（米）。
        /// 原地没动的卡（四卡同屏时边缘卡掉跟踪又回来）不算换了摆法，不能反复重演（2026-10-30 O-10）。
        /// </summary>
        public float RelocationDistance;

        /// <summary>
        /// 候选最多等待成员空闲的时长（秒）。组内有成员正在 TouchReact / 专属演出时，
        /// 只能按这个上限占位，否则一只忙的宠物会无期限挡住别人的演出机会。
        /// </summary>
        public float CandidateHoldSeconds;

        /// <summary>参与者冷却（秒），从本人上一次会话<b>开始</b>的时刻算起。取消不归还。</summary>
        public float MemberCooldownSeconds;

        /// <summary>设计初值。代码默认值、PetBehaviorConfig.asset 与模板 JSON 三处必须一致。</summary>
        public static SocialTuning Default => new SocialTuning
        {
            EnterDistance = 0.18f,
            ExitDistance = 0.23f,
            MaxHeightDelta = 0.035f,
            MaxNormalAngleDegrees = 20f,
            EnterDwellSeconds = 0.6f,
            ExitDwellSeconds = 0.3f,
            LostSeparationSeconds = 2f,
            RelocationDistance = 0.03f,
            CandidateHoldSeconds = 2f,
            MemberCooldownSeconds = 5f,
        };

        /// <summary>
        /// 范围校验：非正 / NaN / ±∞ 的字段回退到设计默认值并记一条警告；
        /// 退出距离小于进入距离时按进入距离处理（否则迟滞失效，关系对会立刻被判定分开）。
        /// 手改资产写错不会让共同大笑彻底停摆，但也不会静默生效。
        /// </summary>
        public SocialTuning Sanitised()
        {
            var fallback = Default;
            var tuning = this;

            tuning.EnterDistance = Positive(tuning.EnterDistance, fallback.EnterDistance, nameof(EnterDistance));
            tuning.ExitDistance = Positive(tuning.ExitDistance, fallback.ExitDistance, nameof(ExitDistance));
            tuning.MaxHeightDelta = Positive(tuning.MaxHeightDelta, fallback.MaxHeightDelta, nameof(MaxHeightDelta));
            tuning.MaxNormalAngleDegrees = Positive(tuning.MaxNormalAngleDegrees, fallback.MaxNormalAngleDegrees,
                nameof(MaxNormalAngleDegrees));
            tuning.EnterDwellSeconds = NonNegative(tuning.EnterDwellSeconds, fallback.EnterDwellSeconds,
                nameof(EnterDwellSeconds));
            tuning.ExitDwellSeconds = NonNegative(tuning.ExitDwellSeconds, fallback.ExitDwellSeconds,
                nameof(ExitDwellSeconds));
            tuning.LostSeparationSeconds = Positive(tuning.LostSeparationSeconds, fallback.LostSeparationSeconds,
                nameof(LostSeparationSeconds));
            tuning.RelocationDistance = NonNegative(tuning.RelocationDistance, fallback.RelocationDistance,
                nameof(RelocationDistance));
            tuning.CandidateHoldSeconds = Positive(tuning.CandidateHoldSeconds, fallback.CandidateHoldSeconds,
                nameof(CandidateHoldSeconds));
            tuning.MemberCooldownSeconds = NonNegative(tuning.MemberCooldownSeconds, fallback.MemberCooldownSeconds,
                nameof(MemberCooldownSeconds));

            if (tuning.ExitDistance < tuning.EnterDistance)
            {
                Debug.LogWarning($"[SocialTuning] ExitDistance {tuning.ExitDistance:F3}m 小于 EnterDistance " +
                                 $"{tuning.EnterDistance:F3}m，迟滞会失效；已按 EnterDistance 处理。");
                tuning.ExitDistance = tuning.EnterDistance;
            }

            return tuning;
        }

        private static float Positive(float value, float fallback, string field)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                Debug.LogWarning($"[SocialTuning] {field} 非法（{value}），已回退设计默认值 {fallback}。");
                return fallback;
            }

            return value;
        }

        private static float NonNegative(float value, float fallback, string field)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                Debug.LogWarning($"[SocialTuning] {field} 非法（{value}），已回退设计默认值 {fallback}。");
                return fallback;
            }

            return value;
        }
    }

    /// <summary>
    /// 宠物行为的全部可调参数。
    /// 这是"设计数据"资产（ScriptableObject），行为逻辑本身在 <c>BehaviorStateMachine</c> 里，
    /// 改阈值不需要改代码，也可以在 EditMode 测试里构造临时实例。
    ///
    /// 新版运行只结算开心值（<see cref="PetNeed.Happiness"/>）：饥饿、精力与食物卡投喂已退出运行期，
    /// 相关字段仅作为旧存档 v1 迁移数据保留在 <see cref="PetState"/> 中。
    /// </summary>
    [CreateAssetMenu(menuName = "桌上有龙/宠物行为配置", fileName = "PetBehaviorConfig")]
    public sealed class PetConfig : ScriptableObject
    {
        [Header("初始数值")]
        [Tooltip("开心值不随时间自然衰减（2026-10-27 用户决定）：DecayPerSecond 必须为 0。")]
        public NeedSeed[] NeedSeeds =
        {
            new NeedSeed { Need = PetNeed.Happiness, Initial = 0.60f, DecayPerSecond = 0f, Max = 1f },
        };

        [Header("数值离线上限")]
        [Tooltip("应用未运行期间最多补算多少秒的衰减，避免离线数天后数值直接见底。衰减为 0 时该上限不产生效果。")]
        public float MaxOfflineSeconds = 12f * 3600f;

        [Header("四色调参表")]
        [Tooltip("按形象变体索引：单击 / 双击加分、满值回落、重新武装阈值、冷却、专属状态名与时长。未知变体回退 classic。")]
        public VariantTuning[] VariantTunings = CreateDefaultVariantTunings();

        [Header("靠近共同大笑")]
        [Tooltip("两卡中心水平距离 / 高度差 / 法向夹角、进入与退出停留、候选等待上限与参与者冷却。" +
                 "几何判定在交互层，这里只是唯一数值来源。")]
        public SocialTuning Social = SocialTuning.Default;

        [Header("语音口令（2026-10-29）")]
        [Tooltip("按住麦克风说出口令，所有在场的宠物用同一句回应；加分等同该宠一次单击。音频为开发者自录。")]
        public VoiceCommand[] VoiceCommands = CreateDefaultVoiceCommands();

        [Tooltip("同一只宠物两次语音回应之间的最短间隔（秒），防止连续喊口令刷开心值。")]
        public float VoiceCooldownSeconds = 3f;

        [Header("演出音效")]
        [Tooltip("共同大笑开始时播放的音效 ID；同一组只由一只宠物播放，避免四只叠音。留空表示不播。")]
        public string SocialSfxId = "leiting";

        [Tooltip("满值专属演出开始时播放的音效 ID，按变体（Classic/Calm/Rage/Divine 顺序）。留空表示该色不播。")]
        public string[] ExclusiveSfxIds = { "andi", string.Empty, "oula", string.Empty };

        [Header("Animator 能力清单")]
        [Tooltip("Animator Controller 里真实存在的状态名。行为只有在自己的动画名出现在这里时才可进入；" +
                 "缺片段只禁用对应行为。不要加入 DozeLoop / WalkLoop / Eat 等未接线的动作。")]
        public string[] AnimatorStates =
        {
            "Idle",
            "TouchReact",
            "classic_proud",
            "calm_zen",
            "rage_roar",
            "divine_awaken",
            "social_laugh",
        };

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
                // 加分不在这里结算：任何开心值增量都只在行为状态机接受事件的那一处结算一次。
                Behavior = PetBehaviorId.Petted,
                AnimationName = "TouchReact",
                Loop = false,
                DurationSeconds = 0.7f,
            },
            new BehaviorDefinition
            {
                // 动画名与时长由变体调参表覆盖，这里的字段值不参与可用性判定。
                Behavior = PetBehaviorId.Exclusive,
                AnimationName = string.Empty,
                Loop = false,
                DurationSeconds = 0f,
            },
            new BehaviorDefinition
            {
                // 共同大笑：只演出，不结算开心值（OnEnter / PerSecond 等表全部为空）。
                // 会话成员、开始时刻与完成记账由 SocialCoordinator 统一提交。
                Behavior = PetBehaviorId.Social,
                AnimationName = "social_laugh",
                Loop = false,
                DurationSeconds = 2.4f,
            },
            new BehaviorDefinition
            {
                Behavior = PetBehaviorId.Resting,
                AnimationName = "DozeLoop",
                Loop = true,
                DurationSeconds = 0f,
            },
            new BehaviorDefinition
            {
                Behavior = PetBehaviorId.Chasing,
                AnimationName = "WalkLoop",
                Loop = true,
                DurationSeconds = 0f,
            },
        };

        [Header("表现")]
        [Tooltip("行为切换时的默认过渡时间（秒）。首版建议 0.12～0.20。")]
        public float DefaultCrossFadeSeconds = 0.15f;

        /// <summary>行为表中的默认行为（Idle），也是打断回落的目标。</summary>
        public PetBehaviorId DefaultBehavior = PetBehaviorId.Idle;

        // 未知变体只警告一次，避免每帧 / 每只宠物刷屏。
        // 私有字段且没有 [SerializeField]，不会被写进资产。
        [NonSerialized] private bool _variantFallbackWarned;

        // 共同大笑参数已校验副本：只算一次，避免每帧重复警告。
        [NonSerialized] private bool _socialResolved;
        [NonSerialized] private SocialTuning _resolvedSocial;

        /// <summary>调参表完全缺失时使用的兜底条目：与 classic 的设计初值一致。</summary>
        private static readonly VariantTuning ClassicFallback = new VariantTuning
        {
            Variant = PetVariantId.Classic,
            SingleTapGain = 0.06f,
            DoubleTapGain = 0.10f,
            FallbackHappiness = 0.70f,
            RearmThreshold = 0.80f,
            ExclusiveCooldownSeconds = 8f,
            ExclusiveStateName = "classic_proud",
            ExclusiveDurationSeconds = 2.0f,
        };

        [Serializable]
        public struct NeedSeed
        {
            public PetNeed Need;
            public float Initial;
            public float DecayPerSecond;
            public float Max;
        }

        /// <summary>四色调参表的设计初值。代码默认值与资产、模板 JSON 必须三处一致。</summary>
        public static VariantTuning[] CreateDefaultVariantTunings() => new[]
        {
            new VariantTuning
            {
                Variant = PetVariantId.Classic,
                SingleTapGain = 0.06f,
                DoubleTapGain = 0.10f,
                FallbackHappiness = 0.70f,
                RearmThreshold = 0.80f,
                ExclusiveCooldownSeconds = 8f,
                ExclusiveStateName = "classic_proud",
                ExclusiveDurationSeconds = 2.0f,
            },
            new VariantTuning
            {
                Variant = PetVariantId.Calm,
                SingleTapGain = 0.04f,
                DoubleTapGain = 0.07f,
                FallbackHappiness = 0.75f,
                RearmThreshold = 0.80f,
                ExclusiveCooldownSeconds = 8f,
                ExclusiveStateName = "calm_zen",
                ExclusiveDurationSeconds = 2.5f,
            },
            new VariantTuning
            {
                Variant = PetVariantId.Rage,
                SingleTapGain = 0.08f,
                DoubleTapGain = 0.12f,
                FallbackHappiness = 0.65f,
                RearmThreshold = 0.85f,
                ExclusiveCooldownSeconds = 6f,
                ExclusiveStateName = "rage_roar",
                ExclusiveDurationSeconds = 2.0f,
            },
            new VariantTuning
            {
                Variant = PetVariantId.Divine,
                SingleTapGain = 0.06f,
                DoubleTapGain = 0.10f,
                FallbackHappiness = 0.65f,
                RearmThreshold = 0.75f,
                ExclusiveCooldownSeconds = 12f,
                ExclusiveStateName = "divine_awaken",
                ExclusiveDurationSeconds = 2.5f,
            },
        };

        /// <summary>
        /// 四条口令的设计初值（2026-10-29 开发者提供四段自录音频）。按含义配动作，不新做动画：
        /// 安迪 → 打招呼；嘎嘎爹拉筛 → 小反应 + o 嘴；雷霆大笑 → 共同大笑的动作 + 眯眼大笑；欧拉 → 怒吼 + 怒眼。
        /// 四只宠物用同一个动作片段回应（不按颜色换专属动作，避免和满值演出混淆）。
        /// </summary>
        public static VoiceCommand[] CreateDefaultVoiceCommands() => new[]
        {
            new VoiceCommand
            {
                CommandId = "andi", Phrase = "安迪", AnimationName = "classic_proud", DurationSeconds = 2.0f,
                Expression = PetExpression.Happy, SfxId = "andi",
            },
            new VoiceCommand
            {
                CommandId = "gaga", Phrase = "嘎嘎爹拉筛", AnimationName = "TouchReact", DurationSeconds = 0.7f,
                Expression = PetExpression.Surprised, SfxId = "gaga",
            },
            new VoiceCommand
            {
                CommandId = "leiting", Phrase = "雷霆大笑", AnimationName = "social_laugh", DurationSeconds = 2.4f,
                Expression = PetExpression.VeryHappy, SfxId = "leiting",
            },
            new VoiceCommand
            {
                CommandId = "oula", Phrase = "欧拉", AnimationName = "rage_roar", DurationSeconds = 2.0f,
                Expression = PetExpression.Roar, SfxId = "oula",
            },
        };

        /// <summary>按口令原文或稳定 ID 查找。识别结果可能带空格，比较前去掉空白。</summary>
        public bool TryGetVoiceCommand(string phraseOrId, out VoiceCommand command)
        {
            command = default;
            if (string.IsNullOrWhiteSpace(phraseOrId) || VoiceCommands == null) return false;
            var key = phraseOrId.Replace(" ", string.Empty).Trim();
            for (var i = 0; i < VoiceCommands.Length; i++)
            {
                var c = VoiceCommands[i];
                if (string.Equals(c.CommandId, key, StringComparison.Ordinal) ||
                    string.Equals(c.Phrase, key, StringComparison.Ordinal))
                {
                    command = c;
                    return true;
                }
            }

            return false;
        }

        /// <summary>口令能否被回应：动作在能力清单里、时长有限且为正。</summary>
        public bool IsVoiceCommandReady(in VoiceCommand command) =>
            !string.IsNullOrWhiteSpace(command.CommandId) &&
            command.DurationSeconds > 0f && !float.IsInfinity(command.DurationSeconds) &&
            HasAnimatorState(command.AnimationName);

        /// <summary>该变体满值专属演出的音效 ID；没有配置时为空串。</summary>
        public string ExclusiveSfxFor(PetVariantId variant)
        {
            var index = (int)variant;
            if (ExclusiveSfxIds == null || index < 0 || index >= ExclusiveSfxIds.Length) return string.Empty;
            return ExclusiveSfxIds[index] ?? string.Empty;
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
        /// 按变体取调参条目。表里没有该变体时回退到 classic 的数值，并只警告一次。
        /// 连 classic 条目都没有时使用代码内置的 classic 初值，保证运行不中断。
        /// </summary>
        public bool TryGetVariantTuning(PetVariantId variant, out VariantTuning tuning)
        {
            if (TryFindTuning(variant, out tuning)) return true;
            if (TryFindTuning(PetVariantId.Classic, out tuning))
            {
                WarnVariantFallback(variant, "变体调参表缺少该变体");
                return true;
            }

            tuning = ClassicFallback;
            WarnVariantFallback(variant, "变体调参表为空");
            return false;
        }

        /// <summary>
        /// 已校验的共同大笑参数（同一份配置只算一次，避免每次读取都刷警告）。
        /// 接近规则与协调器都从这里取数值，不要在别处另写默认值。
        /// </summary>
        public SocialTuning ResolveSocial()
        {
            if (_socialResolved) return _resolvedSocial;
            _resolvedSocial = Social.Sanitised();
            _socialResolved = true;
            return _resolvedSocial;
        }

        /// <summary>Animator Controller 里是否真的存在该状态。空名一律视为不存在。</summary>
        public bool HasAnimatorState(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName) || AnimatorStates == null) return false;
            for (var i = 0; i < AnimatorStates.Length; i++)
            {
                if (string.Equals(AnimatorStates[i], stateName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// 该行为在当前配置下是否真的可以进入：既要有可用的定义，动画名也要在能力清单里。
        /// 不带变体的重载按 classic 判定；多宠请用带 <paramref name="variant"/> 的重载。
        /// </summary>
        public bool IsBehaviorReady(PetBehaviorId behavior, out BehaviorDefinition definition) =>
            IsBehaviorReady(behavior, PetVariantId.Classic, out definition);

        /// <summary>
        /// 该行为对该形象变体是否真的可以进入。
        /// 普通行为：定义可用（单次行为需正时长且不循环）且 <see cref="BehaviorDefinition.ResolvedAnimationName"/>
        /// 在 <see cref="AnimatorStates"/> 里。
        /// 专属演出：调参表条目存在、时长 &gt; 0，且该条目的状态名在 <see cref="AnimatorStates"/> 里；
        /// 动画名与时长以调参表为准，行为表里的同名字段不参与判定。
        /// 缺片段只会禁用这一个行为，不影响其他行为。
        /// </summary>
        public bool IsBehaviorReady(PetBehaviorId behavior, PetVariantId variant, out BehaviorDefinition definition)
        {
            if (!TryGetBehavior(behavior, out definition)) return false;

            if (behavior == PetBehaviorId.Exclusive)
            {
                if (!TryGetVariantTuning(variant, out var tuning)) return false;
                if (tuning.ExclusiveDurationSeconds <= 0f || float.IsInfinity(tuning.ExclusiveDurationSeconds))
                    return false;
                if (!HasAnimatorState(tuning.ExclusiveStateName)) return false;

                definition.Loop = false;
                definition.AnimationName = tuning.ExclusiveStateName;
                definition.DurationSeconds = tuning.ExclusiveDurationSeconds;
                return true;
            }

            return definition.IsUsable() && HasAnimatorState(definition.ResolvedAnimationName);
        }

        /// <summary>该变体的满值专属演出是否可以播。不可播时满值只停在 100%，不消费周期。</summary>
        public bool IsExclusiveAvailable(PetVariantId variant) =>
            IsBehaviorReady(PetBehaviorId.Exclusive, variant, out _);

        private bool TryFindTuning(PetVariantId variant, out VariantTuning tuning)
        {
            if (VariantTunings != null)
            {
                for (var i = 0; i < VariantTunings.Length; i++)
                {
                    if (VariantTunings[i].Variant != variant) continue;
                    tuning = VariantTunings[i];
                    return true;
                }
            }

            tuning = default;
            return false;
        }

        private void WarnVariantFallback(PetVariantId variant, string reason)
        {
            if (_variantFallbackWarned) return;
            _variantFallbackWarned = true;
            Debug.LogWarning($"[PetConfig] {reason}（{variant}），已回退到 classic 的调参数值。");
        }
    }
}
