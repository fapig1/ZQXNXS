using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.AR
{
    /// <summary>
    /// 标记注册表与位姿查询的公共基类。
    /// 子类只提供"目标条目"（稳定 ID、尺寸、位姿来源与状态），
    /// 按 ID 查找、索引重建、有效性/过期判断都在这里统一实现，
    /// 保证 Vuforia 版与模拟版的语义一致。
    ///
    /// 轴约定：Unity 中角色朝向为局部 +Z、上方为 +Y，单位米。
    /// 所有目标共用同一世界坐标系，接近判定直接使用世界位姿。
    /// </summary>
    public abstract class MarkerRegistryBase : MonoBehaviour, IMarkerRegistry
    {
        [Header("位姿过期阈值")]
        [Tooltip("超过该时长没有新采样，位姿视为过期，不再用于结算。")]
        [SerializeField] private float staleSeconds = PoseFreshness.DefaultStaleSeconds;

        private readonly Dictionary<string, int> _indexById = new(StringComparer.Ordinal);
        private readonly List<string> _targetIds = new();
        private bool _indexBuilt;

        public float StaleSeconds => staleSeconds;

        /// <summary>
        /// 可注入时钟（<b>仅供 EditMode 测试</b>，运行期保持为 null）。EditMode 下 <c>Time.time</c>
        /// 基本不前进，测试必须用显式递增的时钟，否则采样时刻恒定、位姿会被误判过期。
        /// </summary>
        public Func<float> Clock { get; set; }

        /// <summary>本帧时间：有注入时钟用注入值，否则用 <c>Time.time</c>（真机 / 播放模式行为不变）。</summary>
        protected float Now => Clock != null ? Clock() : Time.time;

        /// <summary>已注册的目标 ID，顺序与子类配置一致。重复或空 ID 的条目会被忽略。</summary>
        public IReadOnlyList<string> TargetIds
        {
            get
            {
                EnsureIndex();
                return _targetIds;
            }
        }

        /// <summary>子类配置的条目数量（含被忽略的非法条目）。</summary>
        protected abstract int EntryCount { get; }

        protected abstract string GetEntryId(int index);

        /// <summary>目标有效尺寸：x=宽、z=高，单位米。</summary>
        protected abstract Vector3 GetEntrySize(int index);

        /// <summary>读取第 index 个条目的当前采样（自带状态与采样时刻）。</summary>
        protected abstract PoseSample ReadSample(int index);

        /// <inheritdoc />
        public bool IsRegistered(string targetId) => TryGetIndex(targetId, out _);

        /// <inheritdoc />
        public Vector3 GetTargetSize(string targetId) =>
            TryGetIndex(targetId, out var index) ? GetEntrySize(index) : Vector3.zero;

        /// <inheritdoc />
        public bool IsPoseValid(string targetId, float now) =>
            TryGetIndex(targetId, out var index) && IsSampleSettleable(ReadSample(index), now);

        /// <inheritdoc />
        public bool TryGetTargetPose(string targetId, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = default;
            worldRotation = default;

            if (!TryGetIndex(targetId, out var index)) return false;

            var sample = ReadSample(index);
            if (!IsSampleSettleable(sample, Now)) return false;

            worldPosition = sample.Position;
            worldRotation = sample.Rotation;
            return true;
        }

        /// <summary>位姿是否可用于结算：跟踪正常且未过期。子类可收紧（不能放宽）。</summary>
        protected virtual bool IsSampleSettleable(in PoseSample sample, float now) =>
            PoseFreshness.IsSettleable(sample, now, staleSeconds);

        /// <summary>按 <see cref="TargetIds"/> 顺序读取所有已注册目标的采样。</summary>
        protected PoseSample[] ReadAllSamples()
        {
            var ids = TargetIds;
            var samples = new PoseSample[ids.Count];
            for (var i = 0; i < ids.Count; i++)
            {
                _indexById.TryGetValue(ids[i], out var index);
                samples[i] = ReadSample(index);
            }

            return samples;
        }

        /// <summary>按 ID 查条目下标。未注册返回 false，不抛异常。</summary>
        protected bool TryGetIndex(string targetId, out int index)
        {
            EnsureIndex();
            index = -1;
            if (string.IsNullOrEmpty(targetId)) return false;
            return _indexById.TryGetValue(targetId, out index);
        }

        /// <summary>未注册目标的统一回答：Lost，且原样带回调用方给的 ID。</summary>
        protected static PoseSample UnavailableFor(string targetId, float now) =>
            PoseSample.Unavailable(targetId, TrackingStatus.Lost, now);

        /// <summary>
        /// 重建 ID 索引。空 ID 与重复 ID 只保留第一条，后者报错并忽略，避免"两只宠物抢同一张卡"。
        /// 在 <see cref="OnValidate"/>/<see cref="Awake"/> 以及首次访问时调用。
        /// </summary>
        protected void RebuildIndex()
        {
            _indexById.Clear();
            _targetIds.Clear();

            var count = EntryCount;
            for (var i = 0; i < count; i++)
            {
                var targetId = GetEntryId(i);
                if (string.IsNullOrEmpty(targetId))
                {
                    Debug.LogError($"[{GetType().Name}] 第 {i} 个目标的 targetId 为空，已忽略该条目。");
                    continue;
                }

                if (_indexById.ContainsKey(targetId))
                {
                    Debug.LogError($"[{GetType().Name}] 目标 ID “{targetId}” 重复，已忽略第 {i} 个条目（只保留第一条）。");
                    continue;
                }

                _indexById.Add(targetId, i);
                _targetIds.Add(targetId);
            }

            _indexBuilt = true;
        }

        protected void EnsureIndex()
        {
            if (!_indexBuilt) RebuildIndex();
        }

        protected virtual void Awake() => RebuildIndex();

        protected virtual void OnValidate() => RebuildIndex();

        /// <summary>
        /// 尺寸自检：卡片的有效尺寸填错是最常见的"模型比例不对"原因。
        /// 在编辑器里调用一次，把实际配置打印出来对照印刷尺寸。
        /// </summary>
        [ContextMenu("打印目标尺寸自检")]
        public void LogTargetSizes()
        {
            EnsureIndex();
            for (var i = 0; i < _targetIds.Count; i++)
            {
                var size = GetEntrySize(_indexById[_targetIds[i]]);
                Debug.Log($"[MarkerRegistry] {_targetIds[i]} 有效尺寸 = {size.x:F4} m × {size.z:F4} m");
            }
        }
    }
}
