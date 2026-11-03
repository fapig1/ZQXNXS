using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 长期状态数值的持有者。新版运行只有<b>开心值</b>一个数值：
    /// 饥饿、精力与食物卡投喂已退出运行期，旧字段留在 <see cref="PetState"/> 里仅供存档 v1 迁移。
    /// 设计要点：
    /// <list type="bullet">
    /// <item>数值只在 <see cref="Apply"/>（行为结算）与 <see cref="ApplyDecay"/>（时间流逝）两处变化。</item>
    /// <item>变化时打 <see cref="Dirty"/> 标记，由应用入口决定何时写盘，不在每帧写文件。</item>
    /// <item>离线衰减在读取存档时补算一次，并有上限，避免久未打开后数值直接见底。
    ///       <b>2026-10-27 起配置把每秒衰减设为 0（在线与离线都不衰减）</b>，机制与上限保留，改回非零即可恢复。</item>
    /// </list>
    /// </summary>
    public sealed class PetNeeds : IPetStateProvider
    {
        private readonly PetConfig _config;
        private float _happiness = 0.5f;
        private float _happinessMax = 1f;
        private float _happinessDecayPerSecond;

        private PetState _snapshot;

        /// <summary>距离上次落盘是否有未保存的变化。</summary>
        public bool Dirty { get; private set; }

        /// <summary>最近一次数值变化的时刻，供界面做动效与测试断言。</summary>
        public float LastChangeTime { get; private set; }

        public PetNeeds(PetConfig config)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            InitialiseFromConfig();
            _snapshot = BuildSnapshot(PetState.CreateDefault());
        }

        /// <summary>数值的只读快照。<see cref="IPetStateProvider"/> 的实现。</summary>
        public PetState Snapshot => _snapshot;

        /// <summary>
        /// 读取数值。新版只有开心值参与运行；旧版需要（Hunger / Energy）不再有运行时数值，返回 0。
        /// </summary>
        public float Get(PetNeed need) => need == PetNeed.Happiness ? _happiness : 0f;

        /// <summary>
        /// 施加一次数值增量。返回是否真的发生变化（用于避免无意义的存档写入与界面刷新）。
        /// 旧版需要（Hunger / Energy）的增量一律忽略，不参与新版运行。
        /// </summary>
        public bool Apply(PetNeed need, float delta, float now)
        {
            if (need != PetNeed.Happiness) return false;

            var after = Mathf.Clamp(_happiness + delta, 0f, _happinessMax);
            // 保留每个可表示的增量。写盘频率由 SaveCoordinator 控制，不能在这里丢弃小增量。
            if (after == _happiness) return false;

            _happiness = after;
            _snapshot = BuildSnapshot(_snapshot);
            Dirty = true;
            LastChangeTime = now;
            return true;
        }

        /// <summary>按真实流逝时间做开心值衰减。由应用入口每帧调用，deltaTime 由外部传入。</summary>
        public void ApplyDecay(float deltaTime, float now)
        {
            if (deltaTime <= 0f) return;
            if (Mathf.Approximately(_happinessDecayPerSecond, 0f)) return;
            Apply(PetNeed.Happiness, _happinessDecayPerSecond * deltaTime, now);
        }

        /// <summary>
        /// 离线补算。应用启动读取存档后调用一次，
        /// <paramref name="offlineSeconds"/> 由存档时间戳与当前时间之差得到，并受配置上限约束。
        /// </summary>
        public void ApplyOfflineDecay(float offlineSeconds, float now)
        {
            if (offlineSeconds <= 0f) return;
            var capped = Mathf.Min(offlineSeconds, Mathf.Max(0f, _config.MaxOfflineSeconds));
            ApplyDecay(capped, now);
            // 离线补算也需要保存，沿用 Apply 设置的 Dirty 标记。
        }

        /// <summary>
        /// 从存档恢复数值。缺失或异常字段回落到配置的初始值。
        /// 旧版字段（Hunger / Energy / FeedCount）原样带过，供存档 v1 → v2 迁移读取，不参与运行。
        /// 满值周期字段只做范围清洗；"已满值读成待确认"这类语义迁移由
        /// <c>BehaviorStateMachine.RestoreFrom</c> 负责，避免数值层知道业务规则。
        /// </summary>
        public void RestoreFrom(PetState saved)
        {
            _happiness = Sanitise(saved.Happiness);

            _snapshot = saved;
            _snapshot.FeedCount = Math.Max(0, saved.FeedCount);
            _snapshot.PetCount = Math.Max(0, saved.PetCount);
            _snapshot.PlayCount = Math.Max(0, saved.PlayCount);
            _snapshot.PhotoCount = Math.Max(0, saved.PhotoCount);
            _snapshot.ExclusiveCooldownRemaining = SanitiseCooldown(saved.ExclusiveCooldownRemaining);
            _snapshot.ExclusiveStartCount = Math.Max(0, saved.ExclusiveStartCount);
            _snapshot.ExclusiveWatchedCount = Math.Max(0, saved.ExclusiveWatchedCount);
            _snapshot.SocialWatchedCount = Math.Max(0, saved.SocialWatchedCount);
            _snapshot = BuildSnapshot(_snapshot);
            Dirty = false;
        }

        /// <summary>
        /// 写入开心值满值周期的可保存字段。行为状态机是唯一调用方，
        /// 这样存档快照里只有一份周期数据，不必为它再维护一个状态副本。
        /// </summary>
        public void StoreHappyCycle(HappyCycleState state, float cooldownRemaining, int startCount, int watchedCount,
            bool markDirty)
        {
            _snapshot.HappyCycle = state;
            _snapshot.ExclusiveCooldownRemaining = SanitiseCooldown(cooldownRemaining);
            _snapshot.ExclusiveStartCount = Math.Max(0, startCount);
            _snapshot.ExclusiveWatchedCount = Math.Max(0, watchedCount);
            if (markDirty) Dirty = true;
        }

        /// <summary>一次性计数的自增（逗弄、追球、拍照次数）。投喂已不在新版事件种类中。</summary>
        public void IncrementCount(InteractionEventKind kind)
        {
            switch (kind)
            {
                case InteractionEventKind.Pet: _snapshot.PetCount++; break;
                case InteractionEventKind.Play: _snapshot.PlayCount++; break;
                case InteractionEventKind.Photo: _snapshot.PhotoCount++; break;
                default: return;
            }
            Dirty = true;
        }

        /// <summary>落盘完成后清除脏标记。由 <c>SaveCoordinator</c> 调用。</summary>
        public void ClearDirty() => Dirty = false;

        /// <summary>
        /// 共同大笑"完整看完"计数加一。由 <c>PetRuntime.RecordSocialCompleted</c> 在协调器
        /// 提交会话完成时调用一次，本类不判断会话是否重复（那是运行时的职责）。
        /// </summary>
        public void IncrementSocialWatched()
        {
            _snapshot.SocialWatchedCount = Math.Max(0, _snapshot.SocialWatchedCount) + 1;
            Dirty = true;
        }

        private void InitialiseFromConfig()
        {
            _happiness = 0.5f;
            _happinessMax = 1f;
            _happinessDecayPerSecond = 0f;

            if (_config.NeedSeeds == null) return;
            foreach (var seed in _config.NeedSeeds)
            {
                // 旧版 Hunger / Energy 即便仍留在资产里也不会被读取。
                if (seed.Need != PetNeed.Happiness) continue;
                _happinessMax = seed.Max > 0f ? seed.Max : 1f;
                _happiness = Mathf.Clamp(seed.Initial, 0f, _happinessMax);
                _happinessDecayPerSecond = seed.DecayPerSecond;
            }
        }

        private float Sanitise(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return FindInitial(PetNeed.Happiness);
            return Mathf.Clamp(value, 0f, _happinessMax);
        }

        /// <summary>冷却剩余清洗：NaN / ±∞ / 负数一律归零。上限由变体调参表在行为层处理。</summary>
        private static float SanitiseCooldown(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;

        private float FindInitial(PetNeed need)
        {
            if (_config.NeedSeeds != null)
            {
                foreach (var seed in _config.NeedSeeds)
                {
                    if (seed.Need == need) return Mathf.Clamp(seed.Initial, 0f, 1f);
                }
            }
            return 0.5f;
        }

        /// <summary>把开心值写进快照，其余字段（含旧版字段与计数）保持传入副本的现值。</summary>
        private PetState BuildSnapshot(PetState basis)
        {
            var s = basis;
            s.Happiness = _happiness;
            return s;
        }
    }
}
