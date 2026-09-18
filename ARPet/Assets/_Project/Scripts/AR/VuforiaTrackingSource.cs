using UnityEngine;
using ZQXNXS.ARPet.Core;

#if VUFORIA_ENGINE
using Vuforia;
#endif

namespace ZQXNXS.ARPet.AR
{
    /// <summary>
    /// Vuforia Engine 适配器。把 SDK 的 <c>ObserverBehaviour</c> 状态映射成项目自己的
    /// <see cref="PoseSample"/>，让上层完全不认识 Vuforia 类型。
    ///
    /// <b>当前状态：条件编译，尚未编译验证。</b>
    /// 本机还没有安装 Vuforia Engine，因此 <c>VUFORIA_ENGINE</c> 宏未定义，
    /// 整个类只保留一个占位组件；导入 SDK 并让 asmdef 的 versionDefines 生效后，
    /// 下面的真实实现才会参与编译。因此：
    /// <list type="bullet">
    /// <item>不要声称这段适配代码已经跑通；</item>
    /// <item>导入 SDK 后第一件事是核对 <c>ObserverBehaviour</c> / <c>Status</c> 的成员名与枚举名，
    ///       Vuforia 各版本之间这些 API 有过调整；</item>
    /// <item>校验通过后把 <c>#if</c> 保留（而不是删掉），以便没有 SDK 的机器仍能打开工程。</item>
    /// </list>
    ///
    /// 使用方式：挂在 AR 场景根物体上，把两张 ImageTarget 拖到对应字段，
    /// 然后在 <c>AppRoot</c> 的 trackingSourceBehaviour 里指定本组件。
    /// </summary>
    public sealed class VuforiaTrackingSource : MarkerRegistryBase, ITrackingSource
    {
        [Header("Vuforia 图像目标")]
        [Tooltip("小窝卡对应的 ImageTarget 物体。")]
        [SerializeField] private GameObject denTargetObject;

        [Tooltip("食物卡对应的 ImageTarget 物体。")]
        [SerializeField] private GameObject foodTargetObject;

        private float _lastSampleTime = float.NegativeInfinity;
        private bool _startRequested;
        private TrackingStatus _unavailableStatus = TrackingStatus.Initializing;

        /// <inheritdoc />
        public bool IsRunning { get; private set; }

        /// <inheritdoc />
        public string PauseReason { get; private set; } = string.Empty;

        /// <inheritdoc />
        public PoseSample[] AllMarkers { get; } = new PoseSample[2];

        /// <inheritdoc />
        public override bool IsRegistered(MarkerSlot marker) => marker switch
        {
            MarkerSlot.Den => denTargetObject != null,
            MarkerSlot.Food => foodTargetObject != null,
            _ => false,
        };

        /// <inheritdoc />
        public override bool TryGetMarkerPose(MarkerSlot marker, out Vector3 position, out Quaternion rotation)
        {
            var sample = GetSample(marker);
            if (!PoseFreshness.IsSettleable(sample, Time.time, StaleSeconds))
            {
                position = default;
                rotation = default;
                return false;
            }

            position = sample.Position;
            rotation = sample.Rotation;
            return true;
        }

        /// <inheritdoc />
        public PoseSample GetSample(MarkerSlot marker) => IsRunning
            ? AllMarkers[(int)marker]
            : PoseSample.Unavailable(marker, _unavailableStatus, Time.time);

        /// <inheritdoc />
        public void StartTracking()
        {
#if VUFORIA_ENGINE
            if (_startRequested) return;
            _startRequested = true;
            PublishUnavailable(TrackingStatus.Initializing);
            PauseReason = string.Empty;
            VuforiaApplication.Instance.OnVuforiaStarted -= HandleVuforiaStarted;
            VuforiaApplication.Instance.OnVuforiaStarted += HandleVuforiaStarted;

            if (VuforiaBehaviour.Instance != null) VuforiaBehaviour.Instance.enabled = true;

            if (VuforiaApplication.Instance.IsRunning)
            {
                HandleVuforiaStarted();
                return;
            }

            VuforiaApplication.Instance.Initialize();
#else
            PauseReason = "未导入 Vuforia Engine（VUFORIA_ENGINE 未定义）";
            IsRunning = false;
            PublishUnavailable(TrackingStatus.Paused);
            Debug.LogWarning("[VuforiaTrackingSource] " + PauseReason);
#endif
        }

        /// <inheritdoc />
        public void PauseTracking(string reason)
        {
            PauseReason = reason ?? string.Empty;
            _startRequested = false;
            IsRunning = false;
            PublishUnavailable(TrackingStatus.Paused);

#if VUFORIA_ENGINE
            VuforiaApplication.Instance.OnVuforiaStarted -= HandleVuforiaStarted;
            // 只停止摄像机与跟踪，不销毁 Observer，便于回前台后快速恢复识别。
            if (VuforiaBehaviour.Instance != null) VuforiaBehaviour.Instance.enabled = false;
#endif
        }

        private void PublishUnavailable(TrackingStatus status)
        {
            _unavailableStatus = status;
            AllMarkers[(int)MarkerSlot.Den] = PoseSample.Unavailable(MarkerSlot.Den, status, Time.time);
            AllMarkers[(int)MarkerSlot.Food] = PoseSample.Unavailable(MarkerSlot.Food, status, Time.time);
        }

        private void OnDisable() => PauseTracking("跟踪组件已停用");

#if VUFORIA_ENGINE
        private void HandleVuforiaStarted()
        {
            // 初始化回调可能晚于暂停/权限拒绝，不能据此重新开放交互。
            if (!_startRequested) return;
            IsRunning = true;
            PauseReason = string.Empty;
            _lastSampleTime = float.NegativeInfinity;
            if (VuforiaBehaviour.Instance != null) VuforiaBehaviour.Instance.enabled = true;
            Debug.Log("[VuforiaTrackingSource] Vuforia 已启动。");
        }

        private void Update()
        {
            if (!IsRunning) return;
            var now = Time.time;
            if (now - _lastSampleTime < 0.01f) return;
            _lastSampleTime = now;

            AllMarkers[(int)MarkerSlot.Den] = SampleFrom(MarkerSlot.Den, denTargetObject, now);
            AllMarkers[(int)MarkerSlot.Food] = SampleFrom(MarkerSlot.Food, foodTargetObject, now);
        }

        /// <summary>
        /// 读取 ObserverBehaviour 的状态并换算成项目状态。
        ///
        /// 需要核对：Vuforia 的 <c>Status</c> 枚举（NO_POSE / TRACKED /
        /// LIMITED / EXTENDED_TRACKED）与 <c>TargetStatus</c> 的读取方式。
        /// 仅直接图像跟踪允许结算，延伸跟踪与未知状态均不用于实体投喂，
        /// 宁可少结算一次交互，也不要依据不可靠位姿结算。
        /// </summary>
        private PoseSample SampleFrom(MarkerSlot marker, GameObject targetObject, float now)
        {
            if (!IsRunning)
                return PoseSample.Unavailable(marker, _unavailableStatus, now);

            if (targetObject == null)
            {
                return PoseSample.Unavailable(marker, TrackingStatus.Initializing, now);
            }

            var observer = targetObject.GetComponent<ObserverBehaviour>();
            if (observer == null || !observer.isActiveAndEnabled)
            {
                return PoseSample.Unavailable(marker, TrackingStatus.Initializing, now);
            }

            var status = MapStatus(observer.TargetStatus.Status);
            var t = targetObject.transform;

            return new PoseSample
            {
                Marker = marker,
                Status = status,
                Position = t.position,
                Rotation = t.rotation,
                SampleTime = now,
            };
        }

        private static TrackingStatus MapStatus(Status status)
        {
            switch (status)
            {
                case Status.TRACKED:
                    return TrackingStatus.Tracked;
                case Status.EXTENDED_TRACKED:
                    return TrackingStatus.ExtendedTracked;
                case Status.LIMITED:
                    return TrackingStatus.Limited;
                case Status.NO_POSE:
                default:
                    return TrackingStatus.Lost;
            }
        }
#endif
    }
}
