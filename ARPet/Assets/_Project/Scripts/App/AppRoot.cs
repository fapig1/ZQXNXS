using System;
using System.Collections.Generic;
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
    /// [跟踪层] ITrackingSource（按 TargetId 出采样）──┐
    ///                                                  ├─→ [交互层] TouchPokeRule ─→ TapGestureRecognizer ─→ InteractionPipe
    /// [身份] PetIdentityCatalog ─→ PetSession ─────────┘                                  │
    ///                                │  每只宠物一份 PetRuntime / TrackingStateMachine     │
    ///                                ├─→ [交互层] ProximityRule（只读位姿：够近 / 已分开 / 已锁）
    ///                                │        └─→ [Pet] SocialCoordinator（组划分、整组开始/取消、记账）
    ///                                ├─→ [表现层] IPetPresenter ←──────────────────────────┤
    ///                                └─→ [数据层] SaveCoordinator                          │
    /// </code>
    ///
    /// 场景里只有这一个 MonoBehaviour 需要手工挂；其余对象都由它 new 出来或从序列化字段取。
    /// 这样做的直接好处：行为规则可以在 EditMode 测试里脱离场景运行，
    /// 也避免"每个脚本各自 Update 一遍、重复结算"的经典问题。
    ///
    /// <b>多宠边界：</b>相机/输入采集/存档写入仍然全局各一份，<see cref="AppRoot"/> 也只有一份；
    /// 每只宠物的差异全部收进 <see cref="PetSession"/> 的独立槽里，不复制 AppRoot，
    /// 也不让跟踪层结算任何业务数值。
    ///
    /// <b>存档：</b>schema 2 按稳定 PetId 逐只保存开心值与计数，<see cref="SaveFile"/> 里一个
    /// <see cref="PetSaveEntry"/> 对应一只宠物。没有 v2 时一次性迁移旧 v1（v1 原文件保留），
    /// 读到更高版本 schema 时只读保护、不覆盖。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AppRoot : MonoBehaviour, IInteractionEventSink, IAppUiState
    {
        /// <summary>
        /// 一条宠物绑定：稳定 petId + 该宠的表现层。
        /// 目标卡与变体不在这里重复配置，统一由 <see cref="PetIdentityCatalog"/> 按 petId 解析，
        /// 避免"App 里写一份、资产映射里写一份"的漂移。
        /// </summary>
        [Serializable]
        public struct PetBinding
        {
            [Tooltip("宠物稳定 ID，必须能在 PetIdentityCatalog.Default 中查到，例如 pet_classic。")]
            public string petId;

            [Tooltip("该宠的表现层。为空时该宠仍参与业务，只是没有画面（会用空实现并给出警告）。")]
            public PetPresenter presenter;
        }

        [Header("配置")]
        [Tooltip("宠物行为与阈值配置。为空时使用代码内置默认值（仅用于调试）。")]
        [SerializeField] private PetConfig config;

        [Header("宠物绑定")]
        [Tooltip("每一条对应一只宠物。留空时默认只装 pet_classic（取场景中第一个 PetPresenter），便于冒烟场景直接跑。")]
        [SerializeField] private PetBinding[] pets = Array.Empty<PetBinding>();

        [Header("跟踪源（二选一）")]
        [Tooltip("未导入 Vuforia 时填 StubTrackingSource，用于在编辑器里先跑通业务链路。")]
        [SerializeField] private MonoBehaviour trackingSourceBehaviour;

        [Tooltip("目标位姿来源。留空则复用 trackingSourceBehaviour。")]
        [SerializeField] private MonoBehaviour markerRegistryBehaviour;

        [Header("语音口令（可选）")]
        [Tooltip("实现 IVoiceCommandSource 的组件（VoiceKeywordListener）。留空则不启用语音。")]
        [SerializeField] private MonoBehaviour voiceSourceBehaviour;

        [Header("调试")]
        [SerializeField] private bool logInteractions = true;

        [Tooltip("场景里没有跟踪源时，自动创建一个模拟源，便于直接按 Play 看到状态机运转。")]
        [SerializeField] private bool autoCreateStubSource = true;

        /// <summary>冒烟场景/旧场景默认装的宠物；旧 v1 存档的单份数值也由它承接。</summary>
        private const string DefaultPetId = "pet_classic";

        private ITrackingSource _tracking;
        private IMarkerRegistry _registry;

        private PetSession _session;
        private TouchPokeRule _touch;
        private TapGestureRecognizer _gestures;
        private InteractionPipe _pipe;
        private SaveCoordinator _save;
        private SaveFile _saveFile;

        // 复用同一个列表构建 UI 快照，避免界面每帧分配。
        private readonly List<PetUiSnapshot> _uiPets = new();

        // 复用同一个列表承接识别器立刻产生的事件（只有双击会立刻产生），避免每次点按分配。
        private readonly List<InteractionEvent> _gestureEvents = new();

        private readonly VoiceCommandRule _voiceRule = new();
        private readonly List<InteractionEvent> _voiceEvents = new();
        private IVoiceCommandSource _voice;

        private string _pointerPetId;
        private bool _hasFocus = true;
        private bool _isPaused;
        private bool _started;
        private bool _foreground;
        private bool _permissionRequestPending;

        /// <summary>多宠运行容器。公开只读给测试与调试面板使用。</summary>
        public PetSession Session => _session;

        /// <summary>选中宠物的跟踪界面状态，供 UI 层读取。</summary>
        public TrackingUiState TrackingState =>
            _session != null ? _session.TrackingStateOf(SelectedPetId) : TrackingUiState.Searching;

        /// <summary>选中宠物的数值快照，供 UI 层读取（只读）。</summary>
        public PetState PetSnapshot =>
            TryGetSelectedRuntime(out var runtime) ? runtime.Snapshot : PetState.CreateDefault();

        /// <summary>选中宠物的当前行为，供 UI 层读取。</summary>
        public PetBehaviorId CurrentBehavior =>
            TryGetSelectedRuntime(out var runtime) ? runtime.CurrentBehavior : PetBehaviorId.None;

        /// <summary>全部可用宠物的逐行快照。返回的列表由本对象复用，调用方不要保存引用。</summary>
        public IReadOnlyList<PetUiSnapshot> Pets
        {
            get
            {
                _uiPets.Clear();
                if (_session == null) return _uiPets;

                var ids = _session.PetIds;
                for (var i = 0; i < ids.Count; i++)
                {
                    var petId = ids[i];
                    if (!_session.TryGetRuntime(petId, out var runtime)) continue;

                    _uiPets.Add(new PetUiSnapshot
                    {
                        PetId = petId,
                        Variant = runtime.Identity.Variant,
                        Tracking = _session.TrackingStateOf(petId),
                        State = runtime.Snapshot,
                        Behavior = runtime.CurrentBehavior,
                    });
                }

                return _uiPets;
            }
        }

        /// <summary>当前选中的宠物稳定 ID；没有可用宠物时为空字符串。</summary>
        public string SelectedPetId => _session?.SelectedPetId ?? string.Empty;

        /// <summary>交互判定层的调试说明（触屏点按的最近原因）。</summary>
        public string TouchReason => _touch?.LastReason ?? string.Empty;

        /// <summary>还在等待双击窗口的宠物数量（调试与测试用）。</summary>
        public int PendingGestureCount => _gestures?.PendingCount ?? 0;

        /// <summary>双击窗口长度（秒），由 <see cref="TapGestureRecognizer.TapGestureSettings"/> 决定。</summary>
        public float DoubleTapWindowSeconds => _gestures?.Settings.DoubleTapWindowSeconds ?? 0f;

        public string TrackingPauseReason => _tracking?.PauseReason ?? string.Empty;

        /// <summary>组装是否成功。失败时界面应显示明确原因，而不是静默不动。</summary>
        public bool IsReady { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            if (config == null)
            {
                // 不在代码里硬编码业务阈值：临时配置只用于让工程在缺少资产时不崩。
                config = ScriptableObject.CreateInstance<PetConfig>();
                Debug.LogWarning("[AppRoot] 未指定 PetConfig，已使用内存中的默认值。" +
                                 "请在 Assets/_Project/Configs 下创建配置资产并挂上。");
            }

            // ── 身份：petId → 身份目录条目，重复/未知条目跳过并报错 ──
            var presenterByPetId = ResolveBindings(out var identities);
            if (identities.Count == 0)
            {
                Debug.LogError("[AppRoot] 组装失败：没有任何可用的宠物绑定（petId 为空、未知或重复）。");
                IsReady = false;
                return;
            }

            // ── 跟踪源：自动创建的模拟源需要知道要为哪些目标卡建代理 ──
            if (!ResolveTracking(identities, presenterByPetId, out var trackingError))
            {
                Debug.LogError($"[AppRoot] 组装失败：{trackingError}");
                IsReady = false;
                return;
            }

            // ── 业务：每只宠物一个独立运行时；目标卡未注册的宠物由 PetSession 跳过并报错 ──
            _session = new PetSession(config, identities, _tracking, _registry,
                petId => presenterByPetId.TryGetValue(petId, out var presenter) ? presenter : null);

            if (!_session.IsReady)
            {
                IsReady = false;
                return;
            }

            // ── 数据层：读档（v2；没有则一次性迁移 v1）→ 逐只应用 + 离线补算 ──
            _saveFile = SaveService.Load(out var restoredFromDisk);

            if (restoredFromDisk)
            {
                var applied = SaveCoordinator.ApplyTo(_saveFile, _session.Runtimes);
                if (applied == 0)
                {
                    Debug.LogWarning("[AppRoot] 读到了存档，但没有任何条目对应当前绑定的宠物，" +
                                     "已全部使用 PetConfig 的初始值。");
                }
                else if (applied < _session.Runtimes.Count)
                {
                    Debug.Log($"[AppRoot] 存档恢复了 {applied}/{_session.Runtimes.Count} 只宠物，其余使用配置初始值。");
                }
            }

            if (_saveFile.WriteBlocked)
            {
                Debug.LogWarning("[AppRoot] " + SaveService.LastError);
            }

            _save = new SaveCoordinator(_session.Runtimes, _saveFile);

            // 旧档迁移：v1 原文件保留，这里只把迁移结果立刻写成 v2，避免杀进程后重复迁移。
            if (_saveFile.MigratedFromV1)
            {
                if (_save.Flush(Time.time))
                {
                    Debug.Log("[AppRoot] 已将旧版 v1 存档迁移为 v2（v1 原文件保留，未删除也未改写）。");
                }
                else
                {
                    Debug.LogWarning("[AppRoot] v1 → v2 迁移写盘失败：" + SaveService.LastError);
                }
            }

            // ── 交互层：触屏点按判定 → 单/双击识别 → 统一管道 ──
            // DPI 一律由这里注入：纯逻辑不直接读 Screen，EditMode 测试才能用固定 DPI 验证阈值。
            _touch = new TouchPokeRule(new TouchPokeRule.TouchPokeSettings(), Screen.dpi);
            _gestures = new TapGestureRecognizer(new TapGestureRecognizer.TapGestureSettings(),
                // 单击在窗口到期时才结算，那时本帧的 PetSession.Tick 已经跑过，
                // 所以用"本帧能否交互"而不是重新采样位姿（避免同一帧两次采样口径不一致）。
                petId => _session != null && _session.CanInteractThisFrame(petId));
            _pipe = new InteractionPipe(this) { ArAvailable = false };
            _voice = voiceSourceBehaviour as IVoiceCommandSource;

            IsReady = true;
            Debug.Log($"[AppRoot] 组装完成，共 {_session.PetIds.Count} 只宠物：" +
                      $"{string.Join("、", _session.PetIds)}；选中 {_session.SelectedPetId}。");
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
                else _tracking.PauseTracking(granted ? "应用仍在后台" : "摄像头权限被拒绝\n请在系统设置中允许相机后返回，或重新打开应用");
            });
        }

        private void Update()
        {
            if (!IsReady || !_foreground) return;

            var now = Time.time;
            var dt = Time.deltaTime;

            // ① 跟踪 + 行为 + 数值 + 表现：全部逐只宠物独立推进。
            //    跟踪无效的宠物行为时钟冻结，其开心值仍照常衰减，丢失/恢复都不重建运行时。
            _session.Tick(now, dt, _tracking.IsRunning);

            // ② 总闸门只表达"全局跟踪在运行"；某一只是否在场由 PetSession 逐只判。
            _pipe.ArAvailable = _tracking.IsRunning;

            // ③ 先作废再结算：丢弃优先于补发。
            //    全局暂停（切后台/权限被拒）取消所有未完成点击与待判单击；
            //    单只丢失只取消它自己的窗口，别的宠物照常。
            if (!_tracking.IsRunning)
            {
                CancelPointer();
                CancelAllGestures();
            }
            else
            {
                if (!string.IsNullOrEmpty(_pointerPetId) && !_session.CanInteractThisFrame(_pointerPetId))
                {
                    // 按下时在场、按下期间丢失：这次点击作废，避免"丢了又回来"凭空结算一次。
                    CancelPointer();
                }

                _gestures.CancelUnavailable();
            }

            // ④ 手势窗口结算：到期的单击在这里发出并经管道提交（②③ 已经准备好闸门与在场判断）。
            //    点按先于社交提交：被点按占用的宠物只会让共同大笑候选等待，不会两边同时生效。
            TickGestures(now);

            // ④b 语音口令：与点按同一优先级层，排在社交之前；只发给本帧可交互的宠物。
            PumpVoice(now);

            // ⑤ 共同大笑：接近规则读位姿判"够近 / 已分开"，协调器整组开始/取消。
            //    必须在 ① 之后（要用本帧算好的可交互标记）与 ④ 之后（点按优先占用行为通道）。
            _session.TickSocial(now, dt, _tracking.IsRunning);

            // ⑥ 存档节奏（内部自带 5 秒节流，不会每帧写盘）。
            _save.Tick(dt, now);
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
                // 这里不重新弹权限框：权限弹窗本身就会造成一次失焦/回焦，回焦时再弹会形成"拒绝→立刻再弹"。
                StartTrackingIfAllowed(requestPermission: false);
                return;
            }

            // 切后台：暂停跟踪 + 打断所有宠物当前行为 + 取消未完成点击与待判单击 + 强制落盘。
            var now = Time.time;
            _tracking.PauseTracking("应用切到后台");
            _pipe.ArAvailable = false;
            CancelPointer();
            CancelAllGestures();
            _session.PauseAll("应用切到后台", now);
            _save.Flush(now);
        }

        private void OnApplicationQuit()
        {
            if (IsReady) _save.Flush(Time.time);
        }

        /// <summary>
        /// 触屏按下。由 <c>TouchInputBridge</c> 或测试调用。
        /// <paramref name="hitPetId"/> 是按下位置命中的宠物；为空时这次按下不成立。
        /// <paramref name="overUi"/> 告知本次点击是否落在 UI 上，防止输入穿透。
        /// </summary>
        public void OnTouchPointerDown(Vector2 screenPosition, bool overUi, string hitPetId)
        {
            if (!IsReady || !_foreground) return;

            if (string.IsNullOrEmpty(hitPetId) || !_session.CanInteract(hitPetId, Time.time))
            {
                // 没点到任何宠物，或点到了当前不在场/位姿过期的宠物：不记录按下。
                CancelPointer();
                return;
            }

            _pointerPetId = hitPetId;
            if (!_touch.PointerDown(screenPosition, Time.time, overUi)) CancelPointer();
        }

        /// <summary>
        /// 触屏抬起。返回<b>本次抬起是否被识别器接收为合格点按</b>
        /// （不等同于"已经加分"：单击要等双击窗口到期才由 <see cref="TickGestures"/> 提交，
        /// 双击则立刻提交；被行为层拒绝时这里仍然是 <c>true</c>）。
        /// <paramref name="hitPetId"/> 必须与按下时命中的是同一只，否则本次点击作废。
        /// </summary>
        public bool OnTouchPointerUp(Vector2 screenPosition, string hitPetId)
        {
            if (!IsReady) return false;

            var pressedPetId = _pointerPetId;
            _pointerPetId = null;

            if (string.IsNullOrEmpty(pressedPetId) || !string.Equals(pressedPetId, hitPetId, StringComparison.Ordinal))
            {
                // 一次点击只属于一只宠物：按下与抬起不是同一只（或都没命中）就不结算。
                _touch.CancelPointer();
                return false;
            }

            var now = Time.time;
            var petVisible = _session.CanInteract(pressedPetId, now);
            _pipe.ArAvailable = _tracking.IsRunning;

            // 第一步：这次抬起算不算合格点按（位移 / 按住时长 / 命中 / 在场）。
            if (!_touch.PointerUp(screenPosition, now, hitPet: true, petVisible: petVisible)) return false;

            // 第二步：手势识别。单击只开窗口、不加分；双击会立刻产生一条事件。
            // 触屏事件没有可用的世界交点，位置留零向量（事件里不参与任何结算）。
            _gestureEvents.Clear();
            var accepted = _gestures.Tap(pressedPetId, Vector3.zero, now, _gestureEvents);
            SubmitGestureEvents();
            return accepted;
        }

        /// <summary>
        /// 推进一步手势识别窗口：到期的单击在这里发出并经 <see cref="InteractionPipe"/> 提交。
        /// <see cref="Update"/> 每帧在 <see cref="PetSession.Tick"/> 之后调用一次；
        /// EditMode 测试可以不走 MonoBehaviour 生命周期，直接传假时钟调用。
        /// </summary>
        public void TickGestures(float now)
        {
            if (!IsReady || _gestures == null) return;

            _gestureEvents.Clear();
            _gestures.Tick(now, _gestureEvents);
            SubmitGestureEvents();
        }

        /// <summary>系统取消（来电、手势返回、切后台）。只丢弃按下，不当作一次抬起。</summary>
        public void OnTouchPointerCanceled() => CancelPointer();

        private void SubmitGestureEvents()
        {
            for (var i = 0; i < _gestureEvents.Count; i++) _pipe.Submit(_gestureEvents[i]);
            _gestureEvents.Clear();
        }

        private void CancelAllGestures() => _gestures?.CancelAll();

        /// <summary>
        /// 一句语音口令进入业务：把识别出的原文换成稳定口令 ID，给<b>每只此刻可交互</b>的宠物各发一个
        /// 语音事件。返回被接受的宠物数。加分、冷却、忙碌拒绝都在各自的行为状态机里结算。
        /// <see cref="Update"/> 每帧从语音源取结果后调用；EditMode 测试可直接调用。
        /// </summary>
        public int SubmitVoicePhrase(string phrase, float now)
        {
            if (!IsReady || _session == null || string.IsNullOrWhiteSpace(phrase)) return 0;
            if (!config.TryGetVoiceCommand(phrase, out var command))
            {
                if (logInteractions) Debug.Log($"[AppRoot] 语音“{phrase}”不是已知口令，忽略。");
                return 0;
            }

            _voiceEvents.Clear();
            _voiceRule.Build(command.CommandId, now, _session.PetIds, _session.CanInteractThisFrame, _voiceEvents);
            if (_voiceEvents.Count == 0 && logInteractions)
                Debug.Log($"[AppRoot] 语音“{command.Phrase}”：没有在场的宠物。");

            var accepted = 0;
            for (var i = 0; i < _voiceEvents.Count; i++)
            {
                if (_pipe.Submit(_voiceEvents[i])) accepted++;
            }

            _voiceEvents.Clear();
            return accepted;
        }

        private void PumpVoice(float now)
        {
            if (_voice == null) return;
            while (_voice.TryDequeue(out var phrase)) SubmitVoicePhrase(phrase, now);
        }

        /// <summary>语音源（可选）。界面通过 <see cref="Voice"/> 读状态并转发按住 / 松开。</summary>
        public IVoiceCommandSource Voice => _voice;

        private void CancelPointer()
        {
            _pointerPetId = null;
            _touch?.CancelPointer();
        }

        /// <inheritdoc />
        bool IInteractionEventSink.TrySubmit(in InteractionEvent interactionEvent)
        {
            if (!IsReady) return false;

            // 路由到事件归属的宠物：别的宠物不受影响；不在场的宠物直接拒绝。
            var accepted = _session.TrySubmit(interactionEvent);
            if (accepted) _session.Select(interactionEvent.PetId);

            if (logInteractions)
            {
                Debug.Log($"[AppRoot] 交互 {interactionEvent} → {(accepted ? "接受" : "拒绝")}" +
                          $"（选中：{_session.SelectedPetId}）");
            }

            return accepted;
        }

        private bool TryGetSelectedRuntime(out PetRuntime runtime)
        {
            runtime = null;
            return _session != null && _session.TryGetRuntime(_session.SelectedPetId, out runtime);
        }

        /// <summary>
        /// 解析序列化绑定：petId → 身份目录条目。未知或重复的条目报错后跳过，
        /// 其余宠物继续组装（单条配错不应让整场不可用）。
        /// </summary>
        private Dictionary<string, IPetPresenter> ResolveBindings(out List<PetIdentity> identities)
        {
            identities = new List<PetIdentity>();
            var presenters = new Dictionary<string, IPetPresenter>(StringComparer.Ordinal);

            if (pets == null || pets.Length == 0)
            {
                // 冒烟场景与旧场景兼容：没有绑定配置时默认只装暖黄奶蛙。
                // 先看 AppRoot 自己的子树，再看整个场景（旧场景的 PetPresenter 挂在角色卡下的 PetRoot 上，
                // 与 App 物体是平级的），这样没重跑过生成工具的旧场景也能继续跑起来。
                var fallbackPresenter = GetComponentInChildren<PetPresenter>();
                if (fallbackPresenter == null) fallbackPresenter = FindFirstObjectByType<PetPresenter>();

                if (fallbackPresenter == null)
                {
                    Debug.LogWarning("[AppRoot] 没有配置任何宠物绑定，场景里也没有 PetPresenter；" +
                                     "已默认只装 pet_classic（无表现层）。请在 pets 数组里逐只绑定。");
                }
                else
                {
                    Debug.LogWarning("[AppRoot] 没有配置任何宠物绑定，已默认只装 pet_classic，" +
                                     "并复用场景中唯一的 PetPresenter。多宠请在 pets 数组里逐只绑定。");
                }

                pets = new[] { new PetBinding { petId = DefaultPetId, presenter = fallbackPresenter } };
            }

            for (var i = 0; i < pets.Length; i++)
            {
                var binding = pets[i];
                if (!TryFindIdentity(binding.petId, out var identity))
                {
                    Debug.LogError($"[AppRoot] 第 {i} 条宠物绑定的 petId “{binding.petId ?? "<null>"}” " +
                                   "不在身份目录中，已跳过该条目。");
                    continue;
                }

                if (presenters.ContainsKey(identity.PetId))
                {
                    Debug.LogError($"[AppRoot] petId “{identity.PetId}” 重复绑定，已跳过第 {i} 条" +
                                   "（同一只宠物不能独占两份状态）。");
                    continue;
                }

                if (binding.presenter == null)
                {
                    Debug.LogWarning($"[AppRoot] 宠物 “{identity.PetId}” 没有配置 PetPresenter，" +
                                     "将使用空表现层（业务与数值仍可运行）。");
                }

                identities.Add(identity);
                presenters.Add(identity.PetId, binding.presenter);
            }

            return presenters;
        }

        private static bool TryFindIdentity(string petId, out PetIdentity identity)
        {
            identity = default;
            if (string.IsNullOrWhiteSpace(petId)) return false;

            var catalog = PetIdentityCatalog.Default;
            for (var i = 0; i < catalog.Count; i++)
            {
                if (!string.Equals(catalog[i].PetId, petId, StringComparison.Ordinal)) continue;
                identity = catalog[i];
                return true;
            }

            return false;
        }

        private bool ResolveTracking(IReadOnlyList<PetIdentity> identities,
            IReadOnlyDictionary<string, IPetPresenter> presenters, out string error)
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

                // 自动创建的模拟源默认没有任何目标条目：按绑定补齐，
                // 让"直接按 Play"的冒烟场景也能看到每只宠物各自在场/丢失。
                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    if (stub.IsRegistered(identity.TargetId)) continue;
                    stub.EnsureTarget(identity.TargetId, ResolveProxy(identity.PetId, presenters),
                        new Vector3(0.08f, 0f, 0.08f));
                }
            }

            if (_tracking == null)
            {
                error = "缺少 ITrackingSource。请挂 VuforiaTrackingSource 或 StubTrackingSource。";
                return false;
            }

            if (_registry == null)
            {
                error = "缺少 IMarkerRegistry（按目标 ID 查询位姿）。";
                return false;
            }

            return true;
        }

        /// <summary>模拟卡的代理物体：优先复用该宠表现层所在变换，没有就在世界原点新建一个空物体。</summary>
        private static Transform ResolveProxy(string petId, IReadOnlyDictionary<string, IPetPresenter> presenters)
        {
            if (presenters != null && presenters.TryGetValue(petId, out var presenter) &&
                presenter is PetPresenter concrete && concrete != null)
            {
                return concrete.transform;
            }

            return new GameObject((petId ?? "pet") + "Proxy").transform;
        }
    }
}
