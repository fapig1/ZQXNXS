using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 长期状态数值的持有者：饥饿、开心、精力。
    /// 设计要点：
    /// <list type="bullet">
    /// <item>数值只在 <see cref="Apply"/>（行为结算）与 <see cref="ApplyDecay"/>（时间流逝）两处变化。</item>
    /// <item>变化时打 <see cref="Dirty"/> 标记，由应用入口决定何时写盘，不在每帧写文件。</item>
    /// <item>离线衰减在读取存档时补算一次，并有上限，避免久未打开后数值直接见底。</item>
    /// </list>
    /// </summary>
    public sealed class PetNeeds : IPetStateProvider
    {
        private readonly PetConfig _config;
        private readonly float[] _values = new float[3];
        private readonly float[] _decayPerSecond = new float[3];
        private readonly float[] _max = new float[3];

        private PetState _snapshot;

        /// <summary>距离上次落盘是否有未保存的变化。</summary>
        public bool Dirty { get; private set; }

        /// <summary>最近一次数值变化的时刻，供界面做动效与测试断言。</summary>
        public float LastChangeTime { get; private set; }

        public PetNeeds(PetConfig config)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            InitialiseFromConfig();
            _snapshot = BuildSnapshot();
        }

        /// <summary>数值的只读快照。<see cref="IPetStateProvider"/> 的实现。</summary>
        public PetState Snapshot => _snapshot;

        public float Get(PetNeed need) => _values[(int)need];

        /// <summary>
        /// 施加一次数值增量。返回是否真的发生变化（用于避免无意义的存档写入与界面刷新）。
        /// </summary>
        public bool Apply(PetNeed need, float delta, float now)
        {
            var index = (int)need;
            var before = _values[index];
            var after = Mathf.Clamp(before + delta, 0f, _max[index]);
            // 保留每个可表示的增量。写盘频率由 SaveCoordinator 控制，不能在这里丢弃小增量。
            if (after == before) return false;

            _values[index] = after;
            _snapshot = BuildSnapshot();
            Dirty = true;
            LastChangeTime = now;
            return true;
        }

        /// <summary>按真实流逝时间做衰减 / 恢复。由应用入口每帧调用，deltaTime 由外部传入。</summary>
        public void ApplyDecay(float deltaTime, float now)
        {
            if (deltaTime <= 0f) return;
            for (var i = 0; i < _values.Length; i++)
            {
                var rate = _decayPerSecond[i];
                if (Mathf.Approximately(rate, 0f)) continue;
                Apply((PetNeed)i, rate * deltaTime, now);
            }
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

        /// <summary>从存档恢复数值。缺失或异常字段回落到配置的初始值。</summary>
        public void RestoreFrom(PetState saved)
        {
            _values[(int)PetNeed.Hunger] = Sanitise(saved.Hunger, PetNeed.Hunger);
            _values[(int)PetNeed.Happiness] = Sanitise(saved.Happiness, PetNeed.Happiness);
            _values[(int)PetNeed.Energy] = Sanitise(saved.Energy, PetNeed.Energy);
            _snapshot = saved;
            _snapshot.FeedCount = Math.Max(0, saved.FeedCount);
            _snapshot.PetCount = Math.Max(0, saved.PetCount);
            _snapshot.PlayCount = Math.Max(0, saved.PlayCount);
            _snapshot.PhotoCount = Math.Max(0, saved.PhotoCount);
            _snapshot = BuildSnapshot();
            Dirty = false;
        }

        /// <summary>一次性计数的自增（投喂、逗弄、追球、拍照次数）。</summary>
        public void IncrementCount(InteractionEventKind kind)
        {
            switch (kind)
            {
                case InteractionEventKind.Feed: _snapshot.FeedCount++; break;
                case InteractionEventKind.Pet: _snapshot.PetCount++; break;
                case InteractionEventKind.Play: _snapshot.PlayCount++; break;
                case InteractionEventKind.Photo: _snapshot.PhotoCount++; break;
                default: return;
            }
            Dirty = true;
        }

        /// <summary>落盘完成后清除脏标记。由 <c>SaveCoordinator</c> 调用。</summary>
        public void ClearDirty() => Dirty = false;

        /// <summary>当前状态是否处于"饥饿"区间，供界面提示（不驱动行为）。</summary>
        public bool IsHungry => Get(PetNeed.Hunger) >= _config.HungryThreshold;

        /// <summary>当前状态是否处于"困倦"区间。</summary>
        public bool IsSleepy => Get(PetNeed.Energy) <= _config.SleepyThreshold;

        private void InitialiseFromConfig()
        {
            for (var i = 0; i < _values.Length; i++)
            {
                _values[i] = 0.5f;
                _max[i] = 1f;
                _decayPerSecond[i] = 0f;
            }

            if (_config.NeedSeeds == null) return;
            foreach (var seed in _config.NeedSeeds)
            {
                var index = (int)seed.Need;
                _values[index] = Mathf.Clamp(seed.Initial, 0f, seed.Max > 0f ? seed.Max : 1f);
                _decayPerSecond[index] = seed.DecayPerSecond;
                _max[index] = seed.Max > 0f ? seed.Max : 1f;
            }
        }

        private float Sanitise(float value, PetNeed need)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return _config.NeedSeeds != null
                ? FindInitial(need)
                : 0.5f;
            return Mathf.Clamp(value, 0f, _max[(int)need]);
        }

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

        private PetState BuildSnapshot()
        {
            var s = _snapshot;
            s.Hunger = _values[(int)PetNeed.Hunger];
            s.Happiness = _values[(int)PetNeed.Happiness];
            s.Energy = _values[(int)PetNeed.Energy];
            return s;
        }
    }
}
