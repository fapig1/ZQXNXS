using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.AR
{
    /// <summary>
    /// <b>模拟跟踪源。</b>在没有导入 Vuforia、也没有打印卡片的阶段，用它把整条业务链路先跑通：
    /// 可以在编辑器里手动拖动每张卡的代理物体，位姿与交互判定和真机走同一套代码。
    ///
    /// 它<b>不</b>代表 AR 跟踪可用，也不能用来证明识别率、稳定性或性能。
    /// 一旦接入真实 SDK，把 <c>AR 场景根</c> 上的跟踪源换成 <c>VuforiaTrackingSource</c> 即可。
    /// </summary>
    public sealed class StubTrackingSource : MarkerRegistryBase, ITrackingSource
    {
        /// <summary>一张模拟卡的配置。targetId 必须与身份目录里的目标名一致。</summary>
        [Serializable]
        public struct StubTarget
        {
            [Tooltip("稳定目标 ID，与身份目录/真实目标名一致，如 card_classic。不能重复或为空。")]
            public string targetId;

            [Tooltip("代表该卡世界参考点的代理物体。为空时该目标恒为 Lost。")]
            public Transform proxy;

            [Tooltip("印刷图像有效尺寸（米）：x=宽、z=高，y 留 0。")]
            public Vector3 size;

            [Tooltip("该目标的模拟状态。默认值 0（Initializing）视为未设置，按 Tracked 处理。")]
            public TrackingStatus simulatedStatus;
        }

        [Header("模拟目标")]
        [Tooltip("每个条目对应一张卡；顺序稳定，AllSamples 按此顺序返回。")]
        [SerializeField] private StubTarget[] targets = Array.Empty<StubTarget>();

        [Header("运行开关")]
        [SerializeField] private bool running = true;

        [Tooltip("全局模拟状态。非 Tracked 时覆盖所有目标，用于演练丢失 / 恢复 / 暂停的界面表现。")]
        [SerializeField] private TrackingStatus simulatedStatus = TrackingStatus.Tracked;

        [SerializeField] private string pauseReason = string.Empty;

        // 恢复跟踪后先等一帧新采样，不能复用暂停前的 Tracked。
        private bool _awaitFirstSample;

        /// <inheritdoc />
        public bool IsRunning => running;

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
            running = true;
            _awaitFirstSample = true;
            PauseReason = string.Empty;
        }

        /// <inheritdoc />
        public void PauseTracking(string reason)
        {
            running = false;
            PauseReason = reason ?? string.Empty;
            Debug.Log($"[StubTrackingSource] 跟踪暂停：{PauseReason}");
        }

        /// <summary>
        /// 测试与调试入口：单独把某个目标设成指定模拟状态，
        /// 用来在编辑器里演练"只有一只丢失、其余仍正常"的情形。
        /// </summary>
        public void SetSimulatedStatus(string targetId, TrackingStatus status)
        {
            if (!TryGetIndex(targetId, out var index))
            {
                Debug.LogError($"[StubTrackingSource] 未注册的目标 ID：{targetId ?? "<null>"}");
                return;
            }

            var entry = targets[index];
            entry.simulatedStatus = status;
            targets[index] = entry;
        }

        /// <summary>
        /// 替换整份目标表并重建 ID 索引。
        ///
        /// <b>测试 / 调试入口。</b>不要在测试里用反射直接写 <c>targets</c> 字段：
        /// 真实 Unity 的 <c>AddComponent</c> 会立即调用 <see cref="Awake"/>，
        /// 空表时索引已经建好并把 <c>_indexBuilt</c> 置为 true，之后字段再变也不会重建，
        /// 结果就是"所有 targetId 都未注册"。走本方法一定同步索引。
        /// </summary>
        public void SetTargets(StubTarget[] entries)
        {
            targets = entries ?? Array.Empty<StubTarget>();
            RebuildIndex();
        }

        /// <summary>
        /// 设置全局模拟状态（覆盖所有目标）。测试 / 调试入口；
        /// 它不参与 ID 索引，但同样不建议用反射写私有字段。
        /// </summary>
        public void SetGlobalSimulatedStatus(TrackingStatus status) => simulatedStatus = status;

        /// <summary>
        /// 确保某个目标 ID 有条目。已存在时只补齐缺失的代理 / 尺寸，不覆盖已有配置。
        /// 应用入口在自动创建模拟源时用它按宠物绑定补齐目标，避免"多宠但模拟源只有一张卡"。
        /// </summary>
        public void EnsureTarget(string targetId, Transform proxy, Vector3 size)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                Debug.LogError("[StubTrackingSource] EnsureTarget 的 targetId 为空，已忽略。");
                return;
            }

            if (TryGetIndex(targetId, out var index))
            {
                var existing = targets[index];
                if (existing.proxy == null && proxy != null) existing.proxy = proxy;
                if (existing.size.sqrMagnitude <= 0f && size.sqrMagnitude > 0f) existing.size = size;
                targets[index] = existing;
                return;
            }

            var list = new List<StubTarget>(targets ?? Array.Empty<StubTarget>())
            {
                new StubTarget
                {
                    targetId = targetId,
                    proxy = proxy,
                    size = size,
                    simulatedStatus = TrackingStatus.Tracked,
                },
            };

            targets = list.ToArray();
            RebuildIndex();
        }

        /// <summary>第 index 个条目的当前采样：位姿来自代理物体，采样时刻取当前时间。</summary>
        protected override PoseSample ReadSample(int index)
        {
            var now = Now;
            var targetId = GetEntryId(index);

            if (targets[index].proxy == null) return UnavailableFor(targetId, now);
            if (!running) return PoseSample.Unavailable(targetId, TrackingStatus.Paused, now);
            if (_awaitFirstSample) return PoseSample.Unavailable(targetId, TrackingStatus.Initializing, now);

            var status = simulatedStatus != TrackingStatus.Tracked
                ? simulatedStatus
                : ResolveEntryStatus(targets[index].simulatedStatus);

            var proxy = targets[index].proxy;
            return new PoseSample
            {
                TargetId = targetId,
                Status = status,
                Position = proxy.position,
                Rotation = proxy.rotation,
                SampleTime = now,
            };
        }

        /// <summary>条目未显式设状态（默认 0 = Initializing）时按 Tracked 处理。</summary>
        private static TrackingStatus ResolveEntryStatus(TrackingStatus status) =>
            status == TrackingStatus.Initializing ? TrackingStatus.Tracked : status;

        private void Update() => _awaitFirstSample = false;

        protected override void Awake()
        {
            base.Awake();
            if (string.IsNullOrEmpty(pauseReason)) PauseReason = string.Empty;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (string.IsNullOrEmpty(pauseReason)) PauseReason = string.Empty;
        }
    }
}
