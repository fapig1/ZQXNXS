using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Persistence
{
    /// <summary>
    /// 存档节奏控制：把"数值变了"和"什么时候写盘"分开，并负责把多只宠物的数值
    /// 合并进同一份 <see cref="SaveFile"/>。
    ///
    /// 规则：
    /// <list type="bullet">
    /// <item>任意一只宠物数值变化只置脏标记，不立刻写盘；</item>
    /// <item>距上次尝试超过 <see cref="MinIntervalSeconds"/> 且确实有变化时才写，失败同样节流；</item>
    /// <item>切后台、退出、拍照等关键节点强制写一次；</item>
    /// <item>写盘前按稳定 PetId 把每只宠物写回自己的条目；本局没绑定的未知条目原样保留。</item>
    /// </list>
    ///
    /// 这样既不会每帧写文件，也不会因为玩家直接杀进程而丢掉整轮互动。
    /// </summary>
    public sealed class SaveCoordinator
    {
        /// <summary>两次自动保存之间的最小间隔（秒）。</summary>
        public float MinIntervalSeconds { get; set; } = 5f;

        private readonly IReadOnlyList<PetRuntime> _pets;
        private readonly SaveFile _file;
        private readonly Func<SaveFile, long, bool> _write;

        private float _lastAttemptTime = float.NegativeInfinity;
        private bool _playTimeDirty;

        /// <summary>
        /// <paramref name="pets"/> 是本局参与运行的全部宠物（顺序不影响结果，按 PetId 对应）。
        /// <paramref name="write"/> 只为测试注入；运行时直接用 <see cref="SaveService.Save"/>。
        /// </summary>
        public SaveCoordinator(IReadOnlyList<PetRuntime> pets, SaveFile file, Func<SaveFile, long, bool> write = null)
        {
            if (pets == null) throw new ArgumentNullException(nameof(pets));
            if (pets.Count == 0) throw new ArgumentException("至少要有一只宠物才能建立存档协调器", nameof(pets));
            for (var i = 0; i < pets.Count; i++)
            {
                if (pets[i] == null) throw new ArgumentException("宠物运行时不能为空", nameof(pets));
            }

            _pets = pets;
            _file = file ?? throw new ArgumentNullException(nameof(file));
            _write = write ?? ((snapshot, utc) => SaveService.Save(snapshot, utc));

            // 只补结构兜底，不在这里给缺失的宠物造条目：条目在真正写盘时按当前数值补齐。
            _file.Pets ??= Array.Empty<PetSaveEntry>();
            TotalPlaySeconds = float.IsNaN(file.TotalPlaySeconds) || float.IsInfinity(file.TotalPlaySeconds)
                ? 0f : Mathf.Max(0f, file.TotalPlaySeconds);
        }

        /// <summary>累计游玩时长，随存档一起写出。</summary>
        public float TotalPlaySeconds { get; private set; }

        /// <summary>成功写入次数，便于测试断言"没有每帧写盘"。</summary>
        public int SaveCount { get; private set; }

        public int SaveAttemptCount { get; private set; }

        /// <summary>存档来自更高 schema 时为 true：此时读到的数据不可信，也绝不允许写盘。</summary>
        public bool WriteBlocked => _file.WriteBlocked;

        /// <summary>被拦下时的具体原因（供界面提示），正常情况下为空。</summary>
        public string WriteBlockReason { get; private set; } = string.Empty;

        /// <summary>每帧调用。只在满足间隔与脏标记时真正写盘。</summary>
        public bool Tick(float deltaTime, float now)
        {
            if (deltaTime > 0f && !float.IsInfinity(deltaTime))
            {
                TotalPlaySeconds += deltaTime;
                _playTimeDirty = true;
            }

            if (!AnyDirty() && !_playTimeDirty) return false;
            if (now - _lastAttemptTime < MinIntervalSeconds) return false;

            return Flush(now);
        }

        /// <summary>强制写盘。切后台 / 退出 / 拍照成功后调用。存档被只读保护时返回 false 且不写。</summary>
        public bool Flush(float now)
        {
            _lastAttemptTime = now;

            if (_file.WriteBlocked)
            {
                WriteBlockReason = $"存档来自 schema {_file.BlockedSchemaVersion} 的更高版本，已拒绝写盘";
                return false;
            }

            MergePetStates();
            _file.TotalPlaySeconds = TotalPlaySeconds;

            SaveAttemptCount++;
            var ok = _write(_file, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (ok)
            {
                for (var i = 0; i < _pets.Count; i++) _pets[i].Needs.ClearDirty();
                _playTimeDirty = false;
                SaveCount++;
            }

            return ok;
        }

        /// <summary>供界面显示"上次保存失败"的提示文案。</summary>
        public string LastError => SaveService.LastError;

        /// <summary>当前存档快照，供反应解锁等模块读取。</summary>
        public SaveFile File => _file;

        /// <summary>
        /// 把存档里的数值按 PetId 恢复到对应宠物；缺少的宠物保持 <see cref="PetConfig"/> 的初始值，
        /// 不伪造 0 分。恢复到的宠物再按 <see cref="SaveService.OfflineSeconds"/> 统一补算离线衰减
        /// （上限仍由 PetConfig.MaxOfflineSeconds 控制）。
        /// 返回真正恢复到数值的宠物数量。
        /// </summary>
        public static int ApplyTo(SaveFile file, IReadOnlyList<PetRuntime> pets) =>
            ApplyTo(file, pets, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Time.time);

        /// <param name="nowUnix">当前 Unix 时间（秒），与存档时间戳比较得出离线时长。</param>
        /// <param name="now">行为层使用的时间轴（运行时是 <c>Time.time</c>），只用于数值变化的时刻标记。</param>
        public static int ApplyTo(SaveFile file, IReadOnlyList<PetRuntime> pets, long nowUnix, float now)
        {
            if (file == null || pets == null || pets.Count == 0) return 0;

            var offlineSeconds = SaveService.OfflineSeconds(file, nowUnix);
            var applied = 0;
            for (var i = 0; i < pets.Count; i++)
            {
                var pet = pets[i];
                if (pet == null) continue;
                if (!file.TryGetPet(pet.PetId, out var entry)) continue;

                pet.RestoreFrom(ToState(entry));
                if (offlineSeconds > 0f) pet.ApplyOfflineDecay(offlineSeconds, now);
                applied++;
            }

            return applied;
        }

        /// <summary>
        /// 把存档条目转成运行时数值。旧版字段（饥饿/精力/投喂）不参与 v2，保持 0。
        /// 满值周期字段只做范围清洗；"已满值读成待确认"由行为层在 RestoreFrom 里完成。
        /// </summary>
        private static PetState ToState(PetSaveEntry entry) => new PetState
        {
            Happiness = SaveService.SanitiseHappiness(entry.Happiness),
            PetCount = Math.Max(0, entry.PetCount),
            PlayCount = Math.Max(0, entry.PlayCount),
            PhotoCount = Math.Max(0, entry.PhotoCount),
            HappyCycle = SaveService.SanitiseHappyCycle(entry.HappyCycle),
            ExclusiveCooldownRemaining = SaveService.SanitiseCooldown(entry.ExclusiveCooldownRemaining),
            ExclusiveStartCount = Math.Max(0, entry.ExclusiveStartCount),
            ExclusiveWatchedCount = Math.Max(0, entry.ExclusiveWatchedCount),
            SocialWatchedCount = Math.Max(0, entry.SocialWatchedCount),
        };

        private bool AnyDirty()
        {
            for (var i = 0; i < _pets.Count; i++)
            {
                if (_pets[i].Needs.Dirty) return true;
            }

            return false;
        }

        /// <summary>
        /// 写盘前把每只宠物的最新数值合并进存档：
        /// 已有条目按原顺序更新；本局没有的未知 PetId 原样保留；本局新出现的宠物追加到末尾。
        /// </summary>
        private void MergePetStates()
        {
            var existing = _file.Pets ?? Array.Empty<PetSaveEntry>();
            var merged = new List<PetSaveEntry>(existing.Length + _pets.Count);

            for (var i = 0; i < existing.Length; i++)
            {
                var entry = existing[i];
                if (entry == null) continue;
                merged.Add(TryFindRuntime(entry.PetId, out var runtime) ? SnapshotOf(runtime) : entry);
            }

            for (var i = 0; i < _pets.Count; i++)
            {
                var pet = _pets[i];
                if (ContainsPet(existing, pet.PetId)) continue;
                merged.Add(SnapshotOf(pet));
            }

            _file.Pets = merged.ToArray();
        }

        private bool TryFindRuntime(string petId, out PetRuntime runtime)
        {
            runtime = null;
            if (string.IsNullOrEmpty(petId)) return false;
            for (var i = 0; i < _pets.Count; i++)
            {
                if (!string.Equals(_pets[i].PetId, petId, StringComparison.Ordinal)) continue;
                runtime = _pets[i];
                return true;
            }

            return false;
        }

        private static bool ContainsPet(PetSaveEntry[] entries, string petId)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry != null && string.Equals(entry.PetId, petId, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static PetSaveEntry SnapshotOf(PetRuntime pet)
        {
            var state = pet.Needs.Snapshot;
            return new PetSaveEntry
            {
                PetId = pet.PetId,
                Happiness = SaveService.SanitiseHappiness(state.Happiness),
                PetCount = Math.Max(0, state.PetCount),
                PlayCount = Math.Max(0, state.PlayCount),
                PhotoCount = Math.Max(0, state.PhotoCount),
                HappyCycle = (int)SaveService.SanitiseHappyCycle((int)state.HappyCycle),
                ExclusiveCooldownRemaining = SaveService.SanitiseCooldown(state.ExclusiveCooldownRemaining),
                ExclusiveStartCount = Math.Max(0, state.ExclusiveStartCount),
                ExclusiveWatchedCount = Math.Max(0, state.ExclusiveWatchedCount),
                SocialWatchedCount = Math.Max(0, state.SocialWatchedCount),
            };
        }
    }
}
