using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.AR
{
    /// <summary>
    /// 标记注册表与坐标换算的公共基类。
    /// 子类只需提供"每个槽位的世界位姿"，换算与有效性判断都在这里统一实现，
    /// 保证 Vuforia 版与模拟版的行为一致。
    ///
    /// 轴约定：Unity 中角色朝向为局部 +Z、上方为 +Y，单位米。
    /// 小窝局部坐标 = 小窝卡的世界位姿求逆 × 目标世界坐标。
    /// </summary>
    public abstract class MarkerRegistryBase : MonoBehaviour, IMarkerRegistry
    {
        [Header("目标实际尺寸（米）")]
        [Tooltip("必须填**印刷图像本身**的有效尺寸，不是纸张外框。该值直接决定虚拟物体与真实卡片的比例。")]
        [SerializeField] private Vector3 denTargetSize = new Vector3(0.08f, 0f, 0.08f);

        [SerializeField] private Vector3 foodTargetSize = new Vector3(0.05f, 0f, 0.05f);

        [Header("位姿过期阈值")]
        [Tooltip("超过该时长没有新采样，位姿视为过期，不再用于结算。")]
        [SerializeField] private float staleSeconds = PoseFreshness.DefaultStaleSeconds;

        public float StaleSeconds => staleSeconds;

        /// <summary>该槽位是否已配置跟踪目标。</summary>
        public abstract bool IsRegistered(MarkerSlot marker);

        /// <summary>取槽位的世界位姿。未跟踪时返回 false。</summary>
        public abstract bool TryGetMarkerPose(MarkerSlot marker, out Vector3 position, out Quaternion rotation);

        public Vector3 GetTargetSize(MarkerSlot marker) => marker switch
        {
            MarkerSlot.Den => denTargetSize,
            MarkerSlot.Food => foodTargetSize,
            _ => Vector3.zero,
        };

        /// <summary>
        /// 位姿是否有效且未过期。
        /// 因为子类各自持有更新时刻，这里用"是否跟踪中"作为代理，
        /// 再由调用方通过 <see cref="PoseFreshness"/> 做时间判断。
        /// </summary>
        public virtual bool IsPoseValid(MarkerSlot marker, float now) =>
            IsRegistered(marker) && TryGetMarkerPose(marker, out _, out _);

        /// <summary>世界坐标 → 小窝卡局部坐标（米制）。小窝卡本身未跟踪时返回世界坐标原样。</summary>
        public Vector3 ToDenLocal(MarkerSlot marker, Vector3 worldPosition, bool useRotation = true)
        {
            if (marker == MarkerSlot.Den) return Vector3.zero;
            if (!TryGetDenPose(out var denPosition, out var denRotation)) return worldPosition;

            var relative = worldPosition - denPosition;
            return useRotation ? Quaternion.Inverse(denRotation) * relative : relative;
        }

        /// <summary>小窝卡当前世界位姿。</summary>
        public bool TryGetDenPose(out Vector3 worldPosition, out Quaternion worldRotation) =>
            TryGetMarkerPose(MarkerSlot.Den, out worldPosition, out worldRotation);

        /// <summary>
        /// 尺寸自检：Vincent 卡片的有效尺寸填错是最常见的"模型比例不对"原因。
        /// 在编辑器里调用一次，把实际配置打印出来对照印刷尺寸。
        /// </summary>
        [ContextMenu("打印目标尺寸自检")]
        public void LogTargetSizes()
        {
            foreach (MarkerSlot slot in Enum.GetValues(typeof(MarkerSlot)))
            {
                var size = GetTargetSize(slot);
                Debug.Log($"[MarkerRegistry] {slot} 有效尺寸 = {size.x:F4} m × {size.z:F4} m" +
                          $"（注册状态：{(IsRegistered(slot) ? "已配置" : "未配置")}）");
            }
        }
    }
}
