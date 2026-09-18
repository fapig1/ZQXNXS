using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.AR
{
    /// <summary>
    /// <b>模拟跟踪源。</b>在没有导入 Vuforia、也没有打印卡片的阶段，用它把整条业务链路先跑通：
    /// 可以在编辑器里手动拖动"小窝"和"食物"两个代理物体，位姿与交互判定和真机走同一套代码。
    ///
    /// 它<b>不</b>代表 AR 跟踪可用，也不能用来证明识别率、稳定性或性能。
    /// 一旦接入真实 SDK，把 <c>AR 场景根</c> 上的跟踪源换成 <c>VuforiaTrackingSource</c> 即可。
    /// </summary>
    public sealed class StubTrackingSource : MarkerRegistryBase, ITrackingSource
    {
        [Header("模拟代理")]
        [Tooltip("代表小窝卡的世界参考点。")]
        [SerializeField] private Transform denProxy;

        [Tooltip("代表食物卡的世界参考点。")]
        [SerializeField] private Transform foodProxy;

        [Header("运行开关")]
        [SerializeField] private bool running = true;

        [Tooltip("模拟状态。切换它可以在编辑器里演练丢失 / 恢复 / 暂停的界面表现。")]
        [SerializeField] private TrackingStatus simulatedStatus = TrackingStatus.Tracked;

        [SerializeField] private string pauseReason = string.Empty;

        private float _lastSampleTime;

        /// <inheritdoc />
        public bool IsRunning => running;

        /// <inheritdoc />
        public string PauseReason { get; private set; } = string.Empty;

        /// <inheritdoc />
        public PoseSample[] AllMarkers { get; } = new PoseSample[2];

        public override bool IsRegistered(MarkerSlot marker) => marker switch
        {
            MarkerSlot.Den => denProxy != null,
            MarkerSlot.Food => foodProxy != null,
            _ => false,
        };

        public override bool TryGetMarkerPose(MarkerSlot marker, out Vector3 position, out Quaternion rotation)
        {
            var t = marker switch
            {
                MarkerSlot.Den => denProxy,
                MarkerSlot.Food => foodProxy,
                _ => null,
            };

            if (t == null)
            {
                position = default;
                rotation = default;
                return false;
            }

            position = t.position;
            rotation = t.rotation;
            return true;
        }

        /// <inheritdoc />
        public PoseSample GetSample(MarkerSlot marker) => running
            ? AllMarkers[(int)marker]
            : PoseSample.Unavailable(marker, TrackingStatus.Paused, Time.time);

        /// <inheritdoc />
        public void StartTracking()
        {
            running = true;
            PauseReason = string.Empty;
            // 恢复后先等待新的 Update 采样，不能复用暂停前的 TRACKED。
            AllMarkers[0] = PoseSample.Unavailable(MarkerSlot.Den, TrackingStatus.Initializing, Time.time);
            AllMarkers[1] = PoseSample.Unavailable(MarkerSlot.Food, TrackingStatus.Initializing, Time.time);
            _lastSampleTime = float.NegativeInfinity;
        }

        /// <inheritdoc />
        public void PauseTracking(string reason)
        {
            running = false;
            PauseReason = reason ?? string.Empty;
            AllMarkers[0] = PoseSample.Unavailable(MarkerSlot.Den, TrackingStatus.Paused, Time.time);
            AllMarkers[1] = PoseSample.Unavailable(MarkerSlot.Food, TrackingStatus.Paused, Time.time);
            Debug.Log($"[StubTrackingSource] 跟踪暂停：{PauseReason}");
        }

        private void Update()
        {
            var now = Time.time;
            if (now - _lastSampleTime < 0.01f) return;
            _lastSampleTime = now;

            var status = running ? simulatedStatus : TrackingStatus.Paused;

            AllMarkers[(int)MarkerSlot.Den] = BuildSample(MarkerSlot.Den, status, now);
            AllMarkers[(int)MarkerSlot.Food] = BuildSample(MarkerSlot.Food, status, now);
        }

        private PoseSample BuildSample(MarkerSlot marker, TrackingStatus status, float now)
        {
            if (!TryGetMarkerPose(marker, out var position, out var rotation))
            {
                return PoseSample.Unavailable(marker, TrackingStatus.Initializing, now);
            }

            return new PoseSample
            {
                Marker = marker,
                Status = status,
                Position = position,
                Rotation = rotation,
                SampleTime = now,
            };
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(pauseReason)) PauseReason = string.Empty;
        }
    }
}
