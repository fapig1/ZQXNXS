using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Presentation
{
    /// <summary>
    /// 表现层实现：把行为层的指令翻译成 Animator 参数、表情贴图和音频播放。
    ///
    /// 三条纪律：
    /// <list type="number">
    /// <item><b>这里没有业务规则。</b>不判断饿不饿、不修改数值、不读跟踪位姿，只负责"播放"。</item>
    /// <item>动画名必须与 Animator Controller 里的状态名一致；缺失时给出明确警告而不是静默失败。</item>
    /// <item>进食的一次性结算由行为层在动画时长处触发，<b>不使用 Animation Event 重复结算</b>。
    ///       如果后续改用 Animation Event，必须先把行为层的时长结算关掉，两者只能留一个。</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetPresenter : MonoBehaviour, IPetPresenter
    {
        [Header("动画")]
        [Tooltip("角色根物体上的 Animator。骨骼类型应为 Generic。")]
        [SerializeField] private Animator animator;

        [Tooltip("Animator 中用于区分行为的整数参数名；留空表示直接用状态名切换。")]
        [SerializeField] private string behaviorParameter = "Behavior";

        [Tooltip("是否使用 Animator 参数切换状态。关闭时按状态名直接 CrossFade。")]
        [SerializeField] private bool useAnimatorParameter;

        [Header("表情")]
        [Tooltip("二维表情渲染器（SpriteRenderer 或 RawImage 的贴图出口）。")]
        [SerializeField] private Renderer expressionRenderer;

        [SerializeField] private string expressionProperty = "_BaseColor";

        [Header("音效")]
        [SerializeField] private AudioSource sfxSource;

        [Tooltip("音效资源尚未整理；未接入时保持为空即可，调用会被安全忽略。")]
        [SerializeField] private string audioResourcesPrefix = "Audio/";

        /// <summary>最近一次播放的行为，供调试面板显示。</summary>
        public PetBehaviorId CurrentBehavior { get; private set; } = PetBehaviorId.None;

        /// <summary>最近一次请求的特效 / 音效标识，供未接入资源时观察。</summary>
        public string LastSfx { get; private set; } = string.Empty;

        public string LastFx { get; private set; } = string.Empty;

        /// <inheritdoc />
        public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds)
        {
            // 注意：即使 behavior 与当前相同也要继续往下走。
            // 进食是单次动作，重播同一个行为时必须真正重新播放，不能因为"名字没变"而跳过。
            CurrentBehavior = behavior;

            if (animator == null)
            {
                // 尚未挂 Animator 时不要抛异常：模拟场景与 EditMode 测试都可能没有它。
                return;
            }

            var stateName = string.IsNullOrWhiteSpace(animationName) ? behavior.ToString() : animationName;
            var hash = Animator.StringToHash(stateName);

            if (!animator.HasState(0, hash))
            {
                Debug.LogWarning($"[PetPresenter] Animator 第 0 层没有状态 “{stateName}”。" +
                                 "请确认 Pet01 的 AnimationClip 已导入并命名一致（Idle / Eat）。");
                return;
            }

            animator.speed = 1f;

            if (useAnimatorParameter && !string.IsNullOrEmpty(behaviorParameter))
            {
                animator.SetInteger(Animator.StringToHash(behaviorParameter), (int)behavior);
            }

            // 固定秒数过渡，并明确从目标状态的 0 秒开始播放。
            animator.CrossFadeInFixedTime(hash, Mathf.Max(0f, crossFadeSeconds), 0, 0f);
        }

        /// <inheritdoc />
        public void SetExpression(PetExpression expression, float intensity)
        {
            // 表情贴图与骨骼表情尚未接入。这里只记录请求，避免"看起来做了但画面没变化"的误解。
            if (expressionRenderer == null) return;
            if (string.IsNullOrEmpty(expressionProperty)) return;

            var block = new MaterialPropertyBlock();
            var tint = ExpressionTint(expression);
            tint.a *= Mathf.Clamp01(intensity);
            block.SetColor(expressionProperty, tint);
            expressionRenderer.SetPropertyBlock(block);
        }

        /// <inheritdoc />
        public void PlaySfx(string sfxId)
        {
            LastSfx = sfxId;
            if (sfxSource == null || string.IsNullOrEmpty(sfxId)) return;

            var clip = Resources.Load<AudioClip>(audioResourcesPrefix + sfxId);
            if (clip == null)
            {
                // 音效属于后续增强项，缺资源不应影响主流程。
                return;
            }

            sfxSource.PlayOneShot(clip);
        }

        /// <inheritdoc />
        public void PlayFx(string fxId)
        {
            LastFx = fxId;
            // 轻量特效与二维表情同批接入，当前只记录请求。
        }

        /// <inheritdoc />
        public void SetPlaybackSpeed(float speed)
        {
            if (animator == null) return;
            animator.speed = Mathf.Clamp(speed, 0.05f, 3f);
        }

        /// <inheritdoc />
        public void SetInteractable(bool interactable)
        {
            // 可交互高亮（描边 / 缩放呼吸）在界面阶段接入。
        }

        /// <inheritdoc />
        public void SetTrackingVisibility(bool visible)
        {
            // 跟踪丢失时保留最后姿态还是隐藏，需要在真机上对比；
            // 默认保留（避免画面突然空掉），由界面负责提示重新对准。
            if (animator == null) return;
        }

        private static Color ExpressionTint(PetExpression expression) => expression switch
        {
            PetExpression.Happy => new Color(1f, 0.97f, 0.85f, 1f),
            PetExpression.VeryHappy => new Color(1f, 0.92f, 0.70f, 1f),
            PetExpression.Sleepy => new Color(0.88f, 0.90f, 1f, 1f),
            PetExpression.Hungry => new Color(1f, 0.90f, 0.80f, 1f),
            PetExpression.Surprised => new Color(1f, 1f, 1f, 1f),
            PetExpression.Refuse => new Color(0.95f, 0.85f, 0.85f, 1f),
            _ => Color.white,
        };
    }
}
