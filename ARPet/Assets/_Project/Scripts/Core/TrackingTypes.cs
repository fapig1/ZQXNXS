using System;
using UnityEngine;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 图像跟踪目标的可用性状态。
    /// 跟踪层只输出这个状态，不直接修改宠物数值；业务层依据它决定是否暂停新的空间交互。
    /// </summary>
    public enum TrackingStatus
    {
        /// <summary>跟踪器尚未启动或正在初始化，此时任何位姿都不可信。</summary>
        Initializing = 0,

        /// <summary>目标在画面中被检出，但位姿尚未稳定，不应据此结算交互。</summary>
        Detected = 1,

        /// <summary>目标可跟踪但位姿置信度有限（如倾斜、过远、光照不足）。可以显示，但不宜精确判定。</summary>
        Limited = 2,

        /// <summary>目标正常跟踪，位姿可用于结算。</summary>
        Tracked = 3,

        /// <summary>目标已丢失。位姿进入过期状态，不能再用于计时或空间交互结算。</summary>
        Lost = 4,

        /// <summary>跟踪被业务层主动暂停（如切后台、权限被拒、用户关闭 AR）。与 Lost 区分。</summary>
        Paused = 5,

        /// <summary>延伸跟踪的估计位姿，不代表图像卡仍可见，不得用于实体交互结算。</summary>
        ExtendedTracked = 6,
    }

    /// <summary>
    /// 一次跟踪采样。位姿是世界坐标下的米制结果，所有目标共用同一世界坐标系，
    /// 后续接近/组互动判定直接使用世界位姿，不再换算到某张卡的局部坐标。
    /// <see cref="TargetId"/> 是稳定字符串标识（如 <c>card_classic</c>），不用场景对象名索引。
    /// <see cref="SampleTime"/> 由跟踪源打点，业务层只用它判断新鲜度，不使用 Unity 的帧计数。
    /// </summary>
    [Serializable]
    public struct PoseSample
    {
        /// <summary>目标稳定 ID：对应身份目录里的目标名，也是配置与存档索引键。</summary>
        public string TargetId;

        public TrackingStatus Status;
        public Vector3 Position;

        /// <summary>真实世界的目标朝向。默认的 identity 表示朝向未知，不要当成"朝向正确"。</summary>
        public Quaternion Rotation;

        public float SampleTime;

        /// <summary>本次采样是否可用于结算：必须跟踪正常，且位姿确实被跟踪器给出过。</summary>
        public bool IsUsable =>
            Status == TrackingStatus.Tracked;

        public static PoseSample Unavailable(string targetId, TrackingStatus status, float sampleTime) => new PoseSample
        {
            TargetId = targetId,
            Status = status,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            SampleTime = sampleTime,
        };

        public override string ToString() =>
            $"{TargetId}:{Status} pos=({Position.x:F3},{Position.y:F3},{Position.z:F3}) t={SampleTime:F3}";
    }

    /// <summary>
    /// 位姿的新鲜度判定。卡片移出镜头后，跟踪器给出的位置会停在上一次结果，
    /// 用"最后更新时间"而不是"最后一次已知位置"来决定是否还能继续结算。
    /// </summary>
    public static class PoseFreshness
    {
        /// <summary>默认过期时间（秒）。超过该时长没有新采样的位姿视为过期。</summary>
        public const float DefaultStaleSeconds = 0.35f;

        public static bool IsStale(in PoseSample sample, float now, float staleSeconds = DefaultStaleSeconds) =>
            (now - sample.SampleTime) > staleSeconds;

        /// <summary>可用于空间结算 = 状态可用且位姿未过期。</summary>
        public static bool IsSettleable(in PoseSample sample, float now, float staleSeconds = DefaultStaleSeconds) =>
            sample.IsUsable && !IsStale(sample, now, staleSeconds);
    }
}
