using System;
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
    /// 使用方式：挂在 AR 场景根物体上，在 <c>targets</c> 数组里登记每张卡的
    /// ImageTarget 物体与印刷尺寸，然后在 <c>AppRoot</c> 的 trackingSourceBehaviour 里指定本组件。
    /// 目标用稳定字符串 ID 索引，不再有固定的槽位枚举。
    /// </summary>
    public sealed class VuforiaTrackingSource : MarkerRegistryBase, ITrackingSource
    {
        /// <summary>一张实体卡的跟踪配置。</summary>
        [Serializable]
        public struct TargetEntry
        {
            [Tooltip("稳定目标 ID，与身份目录/StreamingAssets 里的目标名一致，如 card_classic。不能重复或为空。")]
            public string targetId;

            [Tooltip("该卡对应的 ImageTarget 物体（带 ObserverBehaviour）。")]
            public GameObject targetObject;

            [Tooltip("印刷图像有效尺寸（米）：x=宽、z=高，y 留 0。必须填图像本身，不是纸张外框。")]
            public Vector3 size;
        }

        [Header("Vuforia 图像目标")]
        [Tooltip("每个条目对应一张卡；顺序稳定，AllSamples 按此顺序返回。")]
        [SerializeField] private TargetEntry[] targets = Array.Empty<TargetEntry>();

        private PoseSample[] _samples = Array.Empty<PoseSample>();
        private float _lastSampleTime = float.NegativeInfinity;
        private bool _startRequested;
        private TrackingStatus _unavailableStatus = TrackingStatus.Initializing;

        /// <inheritdoc />
        public bool IsRunning { get; private set; }

        /// <inheritdoc />
        public string PauseReason { get; private set; } = string.Empty;

        protected override int EntryCount => targets?.Length ?? 0;

        protected override string GetEntryId(int index) => targets[index].targetId;

        protected override Vector3 GetEntrySize(int index) => targets[index].size;

        /// <inheritdoc />
        public PoseSample GetSample(string targetId) =>
            TryGetIndex(targetId, out var index) ? ReadSample(index) : UnavailableFor(targetId, Now);

        /// <inheritdoc />
        public PoseSample[] AllSamples => ReadAllSamples();

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

        /// <summary>
        /// 替换整份目标表并重建 ID 索引（<b>测试 / 调试入口</b>）。
        /// 理由同 <c>StubTrackingSource.SetTargets</c>：真实 Unity 的 <c>AddComponent</c> 会立即
        /// 调用 <see cref="Awake"/>，此时空表索引已建好；之后再用反射改字段不会重建索引，
        /// 会让所有 targetId 变成"未注册"。项目里写入目标表一律走本方法。
        /// </summary>
        public void SetTargets(TargetEntry[] entries)
        {
            targets = entries ?? Array.Empty<TargetEntry>();
            RebuildIndex();
            // 采样缓存按条目数对齐，避免索引与缓存长度不一致。
            EnsureSamples();
        }

        /// <summary>按条目数量对齐采样缓存，新增条目先给"不可用"。</summary>
        private void EnsureSamples()
        {
            var count = EntryCount;
            if (_samples.Length == count) return;

            _samples = new PoseSample[count];
            for (var i = 0; i < count; i++)
                _samples[i] = PoseSample.Unavailable(GetEntryId(i), _unavailableStatus, Now);
        }

        private void PublishUnavailable(TrackingStatus status)
        {
            _unavailableStatus = status;
            EnsureSamples();
            for (var i = 0; i < _samples.Length; i++)
                _samples[i] = PoseSample.Unavailable(GetEntryId(i), status, Now);
        }

        /// <summary>第 index 个条目的最新采样。未启动时统一回答"不可用"，不复用旧位姿。</summary>
        protected override PoseSample ReadSample(int index)
        {
            var targetId = GetEntryId(index);
            if (!IsRunning) return PoseSample.Unavailable(targetId, _unavailableStatus, Now);

            EnsureSamples();
            return index < _samples.Length ? _samples[index] : PoseSample.Unavailable(targetId, _unavailableStatus, Now);
        }

        private void OnDisable() => PauseTracking("跟踪组件已停用");

        protected override void Awake()
        {
            base.Awake();
            EnsureSamples();
        }

#if VUFORIA_ENGINE
        private void HandleVuforiaStarted()
        {
            // 初始化回调可能晚于暂停/权限拒绝，不能据此重新开放交互。
            if (!_startRequested) return;
            IsRunning = true;
            PauseReason = string.Empty;
            _lastSampleTime = float.NegativeInfinity;
            EnsureSamples();
            if (VuforiaBehaviour.Instance != null) VuforiaBehaviour.Instance.enabled = true;
            Debug.Log("[VuforiaTrackingSource] Vuforia 已启动。");
        }

        private void Update()
        {
            if (!IsRunning) return;
            // 真机/播放模式走 Time.time；测试可注入 Clock，运行期不设置。
            var now = Now;
            if (now - _lastSampleTime < 0.01f) return;
            _lastSampleTime = now;

            EnsureSamples();
            for (var i = 0; i < _samples.Length; i++) _samples[i] = SampleFrom(i, now);
        }

        /// <summary>
        /// 读取 ObserverBehaviour 的状态并换算成项目状态。
        ///
        /// 需要核对：Vuforia 的 <c>Status</c> 枚举（NO_POSE / TRACKED /
        /// LIMITED / EXTENDED_TRACKED）与 <c>TargetStatus</c> 的读取方式。
        /// 仅直接图像跟踪允许结算，延伸跟踪与未知状态均不用于实体交互，
        /// 宁可少结算一次交互，也不要依据不可靠位姿结算。
        /// </summary>
        private PoseSample SampleFrom(int index, float now)
        {
            var targetId = GetEntryId(index);
            if (!IsRunning)
                return PoseSample.Unavailable(targetId, _unavailableStatus, now);

            var targetObject = targets[index].targetObject;
            if (targetObject == null)
            {
                return PoseSample.Unavailable(targetId, TrackingStatus.Initializing, now);
            }

            var observer = targetObject.GetComponent<ObserverBehaviour>();
            if (observer == null || !observer.isActiveAndEnabled)
            {
                return PoseSample.Unavailable(targetId, TrackingStatus.Initializing, now);
            }

            var status = MapStatus(observer.TargetStatus.Status);
            var t = targetObject.transform;

            return new PoseSample
            {
                TargetId = targetId,
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
