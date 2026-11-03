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

        [Header("表情（二维嘴眼贴片）")]
        [Tooltip("本宠物的形象，决定满值专属演出用哪组眼/嘴。由场景装配写入。")]
        [SerializeField] private PetVariantId variant = PetVariantId.Classic;

        [Tooltip("挂在 Head 骨骼下的眼区贴片（Pet01_FaceEyes）。")]
        [SerializeField] private Renderer eyesDecal;

        [Tooltip("挂在 Head 骨骼下的嘴区贴片（Pet01_FaceMouth）。")]
        [SerializeField] private Renderer mouthDecal;

        [Tooltip("角色本体的 SkinnedMeshRenderer。贴片显示时把 Eyes / Oral 槽换成同色皮肤遮盖材质。")]
        [SerializeField] private Renderer bodyRenderer;

        [Tooltip("bodyRenderer 的原始材质（Skin / Oral / Eyes 顺序）。")]
        [SerializeField] private Material[] bodyMaterials;

        [Tooltip("眼区遮盖材质：本色 Skin 副本，_MainTex_ST=(0,0,u,v) 钉在眼周皮肤的一个纹素上。")]
        [SerializeField] private Material eyesCoverMaterial;

        [Tooltip("嘴区遮盖材质：本色 Skin 副本，钉在嘴周皮肤的一个纹素上。")]
        [SerializeField] private Material mouthCoverMaterial;

        [Tooltip("Blender 生成的 face_overlay_layout.json（每格 _MainTex_ST）。")]
        [SerializeField] private TextAsset faceLayout;

        [Tooltip("bodyRenderer.sharedMaterials 中 Oral 与 Eyes 槽的下标（Pet01.fbx 顺序 Skin/Oral/Eyes）。")]
        [SerializeField] private int oralSlot = 1;

        [SerializeField] private int eyesSlot = 2;

        [Header("音效")]
        [SerializeField] private AudioSource sfxSource;

        [Tooltip("音效资源尚未整理；未接入时保持为空即可，调用会被安全忽略。")]
        [SerializeField] private string audioResourcesPrefix = "Audio/";

        [Header("可见性与可交互")]
        [Tooltip("跟踪丢失时隐藏的渲染根。留空则用本物体自身的整棵子树。")]
        [SerializeField] private GameObject visualRoot;

        /// <summary>最近一次播放的行为，供调试面板显示。</summary>
        public PetBehaviorId CurrentBehavior { get; private set; } = PetBehaviorId.None;

        /// <summary>当前渲染子树是否可见（跟踪丢失时隐藏）。供测试与调试观察。</summary>
        public bool IsVisible { get; private set; } = true;

        /// <summary>当前是否允许交互（碰撞体开关）。供测试与调试观察。</summary>
        public bool IsInteractable { get; private set; } = true;

        private Renderer[] _visualRenderers;
        private Collider[] _visualColliders;
        private bool _visibilityApplied;
        private bool _appliedVisible = true;
        private bool _interactableApplied;
        private bool _appliedInteractable = true;

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

        /// <summary>最近一次请求的表情（不论贴片是否已装配）。</summary>
        public PetExpression CurrentExpression { get; private set; } = PetExpression.Neutral;

        /// <summary>当前实际显示的眼/嘴格子；贴片未装配或被隐藏时为空格。</summary>
        public FaceExpressionTable.Cells CurrentCells { get; private set; }

        public PetVariantId Variant => variant;

        // 表情只在行为切换时设置，按名字写属性即可，不必缓存 PropertyToID。
        private const string MainTexSt = "_MainTex_ST";
        private const string ColorId = "_Color";
        private FaceOverlayLayout _layout;
        private bool _coverApplied;
        private bool _coverEyes;
        private bool _coverMouth;
        private bool _layoutFailed;
        private MaterialPropertyBlock _block;

        /// <inheritdoc />
        /// <remarks>
        /// 只动本宠物自己的两片贴片和本体渲染器：选格用 per-Renderer 的 MaterialPropertyBlock，
        /// 四只宠物即使共用同一个贴片材质也不会串表情。intensity 只作贴片透明度。
        /// </remarks>
        public void SetExpression(PetExpression expression, float intensity)
        {
            CurrentExpression = expression;
            var cells = FaceExpressionTable.Resolve(variant, expression);
            var layout = EnsureLayout();

            var eyes = ApplyCell(eyesDecal, cells.Eyes, layout, intensity, isEyes: true);
            var mouth = ApplyCell(mouthDecal, cells.Mouth, layout, intensity, isEyes: false);
            CurrentCells = new FaceExpressionTable.Cells(eyes, mouth);
            ApplyBodyCover();
        }

        private string ApplyCell(Renderer decal, string cell, FaceOverlayLayout layout, float intensity, bool isEyes)
        {
            if (decal == null) return null;
            var found = false;
            var st = Vector4.zero;
            if (cell != null && layout != null)
                found = isEyes ? layout.TryGetEyeCell(cell, out st) : layout.TryGetMouthCell(cell, out st);

            if (cell != null && layout != null && !found)
                Debug.LogWarning($"[PetPresenter] 表情图集里没有格子 “{cell}”，该贴片保持隐藏。");

            var shown = found ? cell : null;
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            if (shown != null)
            {
                _block.SetVector(MainTexSt, st);
                _block.SetColor(ColorId, new Color(1f, 1f, 1f, Mathf.Clamp01(intensity)));
            }

            decal.SetPropertyBlock(_block);
            decal.enabled = shown != null && IsVisible;
            return shown;
        }

        /// <summary>贴片显示时，原模型对应部位换成同色皮肤遮盖材质；贴片隐藏时恢复原材质。</summary>
        private void ApplyBodyCover()
        {
            if (bodyRenderer == null || bodyMaterials == null || bodyMaterials.Length == 0) return;
            var coverEyes = CurrentCells.Eyes != null;
            var coverMouth = CurrentCells.Mouth != null;
            // 遮盖组合没变就不重挂材质数组（也不分配）。
            if (_coverApplied && coverEyes == _coverEyes && coverMouth == _coverMouth) return;
            _coverApplied = true;
            _coverEyes = coverEyes;
            _coverMouth = coverMouth;
            var materials = (Material[])bodyMaterials.Clone();
            if (CurrentCells.Eyes != null && eyesCoverMaterial != null && eyesSlot >= 0 && eyesSlot < materials.Length)
                materials[eyesSlot] = eyesCoverMaterial;
            if (CurrentCells.Mouth != null && mouthCoverMaterial != null && oralSlot >= 0 && oralSlot < materials.Length)
                materials[oralSlot] = mouthCoverMaterial;
            bodyRenderer.sharedMaterials = materials;
        }

        private FaceOverlayLayout EnsureLayout()
        {
            if (_layout != null || _layoutFailed || faceLayout == null) return _layout;
            try
            {
                _layout = FaceOverlayLayout.Parse(faceLayout.text);
            }
            catch (System.Exception exception)
            {
                _layoutFailed = true;
                Debug.LogWarning("[PetPresenter] face_overlay_layout.json 解析失败，表情贴片不显示：" + exception.Message);
            }

            return _layout;
        }

        /// <inheritdoc />
        public void PlaySfx(string sfxId)
        {
            LastSfx = sfxId;
            if (string.IsNullOrEmpty(sfxId)) return;
            if (sfxSource == null) sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null) return;

            var clip = LoadClip(sfxId);
            if (clip == null)
            {
                // 缺资源不影响主流程；只在第一次缺失时提示，避免刷屏。
                if (_missingSfx.Add(sfxId))
                    Debug.LogWarning($"[PetPresenter] 找不到音效 Resources/{audioResourcesPrefix}{sfxId}，已忽略。");
                return;
            }

            // 新一句打断上一句：同一只宠物不叠音（多只宠物的笑声叠音由协调器只让一只出声解决）。
            sfxSource.Stop();
            sfxSource.clip = clip;
            sfxSource.Play();
        }

        private static readonly System.Collections.Generic.Dictionary<string, AudioClip> ClipCache = new();
        private readonly System.Collections.Generic.HashSet<string> _missingSfx = new();

        private AudioClip LoadClip(string sfxId)
        {
            if (ClipCache.TryGetValue(sfxId, out var cached) && cached != null) return cached;
            var clip = Resources.Load<AudioClip>(audioResourcesPrefix + sfxId);
            if (clip != null) ClipCache[sfxId] = clip;
            return clip;
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
            IsInteractable = interactable;
            if (_interactableApplied && interactable == _appliedInteractable) return;

            EnsureVisualCaches();
            for (var i = 0; i < _visualColliders.Length; i++)
            {
                var collider = _visualColliders[i];
                if (collider != null) collider.enabled = interactable;
            }

            _appliedInteractable = interactable;
            _interactableApplied = true;
        }

        /// <summary>
        /// 跟踪丢失时的显示策略：隐藏渲染、保留 Animator 与层级。
        ///
        /// 三条纪律（多宠同屏必备）：
        /// <list type="number">
        /// <item><b>只切 Renderer.enabled</b>，不 SetActive 本物体，也不碰任何 Vuforia Observer
        ///       或父级 ImageTarget——那会让跟踪本身失效，等于"为了隐藏而丢卡"。</item>
        /// <item>不重置 Animator：恢复跟踪时动画从当前时间继续，不能重新从第 0 帧开始。</item>
        /// <item>渲染器列表在首次调用时收集并缓存，之后只在状态真正变化时切换，避免每帧遍历子树。</item>
        /// </list>
        /// </summary>
        public void SetTrackingVisibility(bool visible)
        {
            IsVisible = visible;
            if (_visibilityApplied && visible == _appliedVisible) return;

            EnsureVisualCaches();
            for (var i = 0; i < _visualRenderers.Length; i++)
            {
                var renderer = _visualRenderers[i];
                if (renderer == null) continue;
                // 表情贴片只在"可见且当前格子有图"时打开，恢复跟踪时不能把隐藏的贴片一并点亮。
                if (renderer == eyesDecal) renderer.enabled = visible && CurrentCells.Eyes != null;
                else if (renderer == mouthDecal) renderer.enabled = visible && CurrentCells.Mouth != null;
                else renderer.enabled = visible;
            }

            _appliedVisible = visible;
            _visibilityApplied = true;
        }

        private void EnsureVisualCaches()
        {
            if (_visualRenderers == null)
            {
                var root = visualRoot != null ? visualRoot : gameObject;
                _visualRenderers = root.GetComponentsInChildren<Renderer>(true);
            }

            if (_visualColliders == null)
            {
                _visualColliders = gameObject.GetComponentsInChildren<Collider>(true);
            }
        }
    }
}
