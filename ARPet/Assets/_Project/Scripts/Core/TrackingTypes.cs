using System;
using UnityEngine;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 标记卡槽位。项目固定使用两张卡：小窝卡建立角色所在局部坐标系，食物卡作为投喂交互的输入道具。
    /// 槽位是稳定标识，存档与配置里都用它索引，不使用场景里的对象名或文件路径。
    /// </summary>
    public enum MarkerSlot
    {
        /// <summary>小窝卡：定义宠物、食物判定与追球区域所在的局部坐标系。</summary>
        Den = 0,

        /// <summary>食物卡：实体投喂道具，需与小窝卡同时有效才参与判定。</summary>
        Food = 1,
    }

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

        /// <summary>目标已丢失。位姿进入过期状态，不能再用于计时或投喂。</summary>
        Lost = 4,

        /// <summary>跟踪被业务层主动暂停（如切后台、权限被拒、用户关闭 AR）。与 Lost 区分。</summary>
        Paused = 5,

        /// <summary>延伸跟踪的估计位姿，不代表图像卡仍可见，不得用于实体交互结算。</summary>
        ExtendedTracked = 6,
    }

    /// <summary>
    /// 一次跟踪采样。位姿是世界坐标下的米制结果，交互层通过注册表换算到小窝局部坐标。
    /// <see cref="SampleTime"/> 由跟踪源打点，业务层只用它判断新鲜度，不使用 Unity 的帧计数。
    /// </summary>
    [Serializable]
    public struct PoseSample
    {
        public MarkerSlot Marker;
        public TrackingStatus Status;
        public Vector3 Position;

        /// <summary>真实世界的目标朝向。默认的 identity 表示朝向未知，不要当成"朝向正确"。</summary>
        public Quaternion Rotation;

        public float SampleTime;

        /// <summary>本次采样是否可用于结算：必须跟踪正常，且位姿确实被跟踪器给出过。</summary>
        public bool IsUsable =>
            Status == TrackingStatus.Tracked;

        public static PoseSample Unavailable(MarkerSlot marker, TrackingStatus status, float sampleTime) => new PoseSample
        {
            Marker = marker,
            Status = status,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            SampleTime = sampleTime,
        };

        public override string ToString() =>
            $"{Marker}:{Status} pos=({Position.x:F3},{Position.y:F3},{Position.z:F3}) t={SampleTime:F3}";
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
