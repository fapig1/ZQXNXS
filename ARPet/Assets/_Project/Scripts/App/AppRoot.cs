using UnityEngine;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;
using ZQXNXS.ARPet.Persistence;
using ZQXNXS.ARPet.Pet;
using ZQXNXS.ARPet.Platform;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.App
{
    /// <summary>
    /// 应用组装点（composition root）。整个工程<b>只有这里</b>知道各层如何拼在一起：
    ///
    /// <code>
    /// [跟踪层] ITrackingSource ──┐
    ///                            ├─→ [交互层] FeedingRule / TouchPokeRule ─→ InteractionPipe
    /// [配置] PetConfig ──────────┤                                                  │
    ///                            └─→ [行为层] BehaviorStateMachine ←────────────────┘
    ///                                            │
    ///                                            ├─→ [表现层] IPetPresenter
    ///                                            └─→ [数据层] SaveCoordinator
    /// </code>
    ///
    /// 场景里只有这一个 MonoBehaviour 需要手工挂；其余对象都由它 new 出来或从序列化字段取。
    /// 这样做的直接好处：行为规则可以在 EditMode 测试里脱离场景运行，
    /// 也避免"每个脚本各自 Update 一遍、重复结算"的经典问题。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AppRoot : MonoBehaviour, IInteractionEventSink, IAppUiState
    {
        [Header("配置")]
        [Tooltip("宠物行为与阈值配置。为空时使用代码内置默认值（仅用于调试）。")]
        [SerializeField] private PetConfig config;

        [Header("跟踪源（二选一）")]
        [Tooltip("未导入 Vuforia 时填 StubTrackingSource，用于在编辑器里先跑通业务链路。")]
        [SerializeField] private MonoBehaviour trackingSourceBehaviour;

        [Tooltip("小窝卡的位姿来源。留空则复用 trackingSourceBehaviour。")]
        [SerializeField] private MonoBehaviour markerRegistryBehaviour;

        [Header("表现")]
        [SerializeField] private PetPresenter presenter;

        [Header("场景挂点")]
        [Tooltip("宠物可视根节点。AR 主场景中应是 DenCardTarget 的子物体，以小窝卡局部坐标定位。")]
        [SerializeField] private Transform anchoredPetRoot;

        [Header("调试")]
        [SerializeField] private bool logInteractions = true;

        [Tooltip("场景里没有跟踪源时，自动创建一个模拟源，便于直接按 Play 看到状态机运转。")]
        [SerializeField] private bool autoCreateStubSource = true;

        private ITrackingSource _tracking;
        private IMarkerRegistry _registry;

        private PetNeeds _needs;
        private BehaviorStateMachine _behavior;
        private FeedingRule _feeding;
        private TouchPokeRule _touch;
        private InteractionPipe _pipe;
        private SaveCoordinator _save;
        private SaveFile _saveFile;
        private TrackingStateMachine _trackingState;
        private bool _hasFocus = true;
        private bool _isPaused;
        private bool _started;
        private bool _foreground;
        private bool _permissionRequestPending;

        private float StaleSeconds => _registry is MarkerRegistryBase registry
            ? registry.StaleSeconds : PoseFreshness.DefaultStaleSeconds;

        /// <summary>当前跟踪界面状态，供 UI 层读取。</summary>
        public TrackingUiState TrackingState => _trackingState?.State ?? TrackingUiState.Searching;

        /// <summary>当前宠物状态数值快照，供 UI 层读取（只读）。</summary>
        public PetState PetSnapshot => _needs?.Snapshot ?? PetState.CreateDefault();

        /// <summary>当前行为，供 UI 层读取。</summary>
        public PetBehaviorId CurrentBehavior => _behavior?.Current ?? PetBehaviorId.None;

        /// <summary>交互判定层的调试说明（投喂 / 逗弄各自的最近原因）。</summary>
        public string FeedingReason => _feeding?.LastReason ?? string.Empty;

        public string TouchReason => _touch?.LastReason ?? string.Empty;

        public string TrackingPauseReason => _tracking?.PauseReason ?? string.Empty;

        /// <summary>组装是否成功。失败时界面应显示明确原因，而不是静默不动。</summary>
        public bool IsReady { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            if (!ResolveTracking(out var error))
            {
                Debug.LogError($"[AppRoot] 组装失败：{error}");
                IsReady = false;
                return;
            }

            if (config == null)
            {
                // 不在代码里硬编码业务阈值：临时配置只用于让工程在缺少资产时不崩。
                config = ScriptableObject.CreateInstance<PetConfig>();
                Debug.LogWarning("[AppRoot] 未指定 PetConfig，已使用内存中的默认值。" +
                                 "请在 Assets/_Project/Configs 下创建配置资产并挂上。");
            }

            // ── 数据层：读档 → 补算离线衰减 ──
            _needs = new PetNeeds(config);
            _saveFile = SaveService.Load(out var restoredFromDisk);

            if (restoredFromDisk)
            {
                _needs.RestoreFrom(_saveFile.State);
                var offline = SaveService.OfflineSeconds(_saveFile, System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (offline > 0f) _needs.ApplyOfflineDecay(offline, Time.time);
            }

            _save = new SaveCoordinator(_needs, _saveFile);

            // ── 表现层 ──
            if (presenter == null) presenter = GetComponentInChildren<PetPresenter>();
            var presenterContract = (IPetPresenter)presenter ?? new NullPresenter();

            // ── 行为层：状态数值 + 行为状态机 ──
            _behavior = new BehaviorStateMachine(config, _needs, presenterContract);

            // ── 交互层：两条判定规则 + 统一管道 ──
            _feeding = new FeedingRule(PetConfigEatSettings.From(config));
            _touch = new TouchPokeRule(new TouchPokeRule.TouchPokeSettings());
            _pipe = new InteractionPipe(this) { ArAvailable = false };

            _trackingState = new TrackingStateMachine();

            _behavior.Start();
            if (!_behavior.IsRunning) return;
            IsReady = true;

            Debug.Log($"[AppRoot] 组装完成。可用行为：{string.Join(", ", _behavior.AllowedBehaviors)}");
        }

        private void Start()
        {
            if (!IsReady) return;
            _started = true;
            _foreground = _hasFocus && !_isPaused;
            StartTrackingIfAllowed(requestPermission: true);
        }

        private void StartTrackingIfAllowed(bool requestPermission)
        {
            if (!IsReady || !_foreground) return;

            // 模拟场景不使用摄像头，不应触发 Android 权限弹窗。
            if (_tracking is StubTrackingSource || CameraPermission.HasPermission)
            {
                _tracking.StartTracking();
                return;
            }

            _tracking.PauseTracking("摄像头权限未获准");
            if (!requestPermission || _permissionRequestPending) return;
            _permissionRequestPending = true;
            CameraPermission.Request(granted =>
            {
                if (this == null) return;
                _permissionRequestPending = false;
                if (granted && _foreground) _tracking.StartTracking();
                else _tracking.PauseTracking(granted ? "应用仍在后台" : "摄像头权限被拒绝");
            });
        }

        private void Update()
        {
            if (!IsReady || !_foreground) return;

            var now = Time.time;
            var dt = Time.deltaTime;

            // ① 跟踪：只在跟踪有效时才允许产生新的空间交互。
            var den = _tracking.GetSample(MarkerSlot.Den);
            var food = _tracking.GetSample(MarkerSlot.Food);
            if (!_tracking.IsRunning)
            {
                den = PoseSample.Unavailable(MarkerSlot.Den, TrackingStatus.Paused, now);
                food = PoseSample.Unavailable(MarkerSlot.Food, TrackingStatus.Paused, now);
            }
            _trackingState.Tick(den, now, StaleSeconds);

            var arAvailable = _tracking.IsRunning && _trackingState.CanInteract;
            _pipe.ArAvailable = arAvailable;

            // ② deltaTime 属于上一帧已经在执行的行为。先推进，再接受本帧的新交互，
            //    避免新 Eat 一进入就消耗上一帧时间，导致不足 3.2 秒便结束。
            _behavior.Tick(dt);

            // ③ 无效帧也必须传给规则：清空停留计时，但不能解除已结算的离开锁。
            if (!arAvailable) _touch.CancelPointer();
            var foodLocal = arAvailable && PoseFreshness.IsSettleable(food, now, StaleSeconds)
                ? _registry.ToDenLocal(MarkerSlot.Food, food.Position)
                : Vector3.zero;

            if (_feeding.Evaluate(den, food, now, dt, foodLocal, StaleSeconds, out var feedEvent))
            {
                _pipe.Submit(feedEvent);
            }

            // ④ 数值随时间流逝。
            _needs.ApplyDecay(dt, now);

            // ⑤ 存档节奏（内部自带 5 秒节流，不会每帧写盘）。
            _save.Tick(dt, now);

            // ⑥ 表现层的连续量（困倦时放慢）。
            if (presenter != null)
            {
                presenter.SetPlaybackSpeed(_behavior.Current == PetBehaviorId.Idle && _needs.IsSleepy ? 0.75f : 1f);
                presenter.SetInteractable(arAvailable);
                presenter.SetTrackingVisibility(arAvailable);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            UpdateForegroundState();
        }

        private void OnApplicationPause(bool paused)
        {
            _isPaused = paused;
            UpdateForegroundState();
        }

        private void UpdateForegroundState()
        {
            if (!IsReady || !_started) return;
            var active = _hasFocus && !_isPaused;
            if (active == _foreground) return;
            _foreground = active;

            if (active)
            {
                // 从系统设置回来需重新检查权限，拒绝后的焦点恢复不能直接启动摄像头。
                StartTrackingIfAllowed(requestPermission: false);
                return;
            }

            // 切后台：暂停跟踪 + 打断当前行为 + 强制落盘。
            _tracking.PauseTracking("应用切到后台");
            _pipe.ArAvailable = false;
            _feeding.SuspendDwell();
            _touch.CancelPointer();
            _trackingState.Tick(PoseSample.Unavailable(MarkerSlot.Den, TrackingStatus.Paused, Time.time), Time.time);
            _behavior.Interrupt("应用切到后台");
            _save.Flush(Time.time);
        }

        private void OnApplicationQuit()
        {
            if (IsReady) _save.Flush(Time.time);
        }

        private bool CanUseSpatialInput(float now) => IsReady && _foreground && _tracking.IsRunning &&
            PoseFreshness.IsSettleable(_tracking.GetSample(MarkerSlot.Den), now, StaleSeconds);

        /// <summary>
        /// 触屏输入入口。由 <c>TouchInputBridge</c> 或测试调用。
        /// 界面通过 <paramref name="overUi"/> 告知本次点击是否落在 UI 上，防止输入穿透。
        /// </summary>
        public void FeedTouchPointerDown(Vector2 screenPosition, bool overUi)
        {
            if (!CanUseSpatialInput(Time.time)) return;
            _touch.PointerDown(screenPosition, Time.time, overUi);
        }

        /// <summary>触屏抬起。返回是否触发了一次逗弄。</summary>
        public bool FeedTouchPointerUp(Vector2 screenPosition, bool hitPet)
        {
            if (!IsReady) return false;

            var petVisible = CanUseSpatialInput(Time.time);
            _pipe.ArAvailable = petVisible;

            if (!_touch.PointerUp(screenPosition, Time.time, hitPet, petVisible, Time.time, out var petEvent))
            {
                return false;
            }

            return _pipe.Submit(petEvent);
        }

        /// <inheritdoc />
        bool IInteractionEventSink.TrySubmit(in InteractionEvent interactionEvent)
        {
            if (!CanUseSpatialInput(Time.time)) return false;
            var accepted = _behavior != null && _behavior.TrySubmit(interactionEvent);

            if (accepted)
            {
                _needs.IncrementCount(interactionEvent.Kind);
            }

            if (logInteractions)
            {
                Debug.Log($"[AppRoot] 交互 {interactionEvent} → {(accepted ? "接受" : "拒绝")}");
            }

            return accepted;
        }

        private bool ResolveTracking(out string error)
        {
            error = string.Empty;

            _tracking = trackingSourceBehaviour as ITrackingSource;
            _registry = (markerRegistryBehaviour ?? trackingSourceBehaviour) as IMarkerRegistry;

            if (_tracking == null && autoCreateStubSource)
            {
                var stub = gameObject.GetComponent<StubTrackingSource>() ??
                           gameObject.AddComponent<StubTrackingSource>();
                _tracking = stub;
                _registry ??= stub;
                Debug.LogWarning("[AppRoot] 场景未指定跟踪源，已自动挂载 StubTrackingSource。" +
                                 "它只用于编辑器联调，不代表真实 AR 跟踪。");
            }

            if (_tracking == null)
            {
                error = "缺少 ITrackingSource。请挂 VuforiaTrackingSource 或 StubTrackingSource。";
                return false;
            }

            if (_registry == null)
            {
                error = "缺少 IMarkerRegistry（用于小窝局部坐标换算）。";
                return false;
            }

            return true;
        }

        /// <summary>没有任何表现层时的空实现，保证行为层不必到处判空。</summary>
        private sealed class NullPresenter : IPetPresenter
        {
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
