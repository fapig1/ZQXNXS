using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Persistence
{
    /// <summary>
    /// 存档节奏控制：把"数值变了"和"什么时候写盘"分开。
    ///
    /// 规则：
    /// <list type="bullet">
    /// <item>数值变化只置脏标记，不立刻写盘；</item>
    /// <item>距上次尝试超过 <see cref="MinIntervalSeconds"/> 且确实有变化时才写，失败同样节流；</item>
    /// <item>切后台、退出、拍照等关键节点强制写一次。</item>
    /// </list>
    ///
    /// 这样既不会每帧写文件，也不会因为玩家直接杀进程而丢掉整轮互动。
    /// </summary>
    public sealed class SaveCoordinator
    {
        /// <summary>两次自动保存之间的最小间隔（秒）。</summary>
        public float MinIntervalSeconds { get; set; } = 5f;

        private readonly PetNeeds _needs;
        private readonly SaveFile _file;
        private readonly Func<SaveFile, long, bool> _write;

        private float _lastAttemptTime = float.NegativeInfinity;
        private bool _playTimeDirty;

        public SaveCoordinator(PetNeeds needs, SaveFile file, Func<SaveFile, long, bool> write = null)
        {
            _needs = needs ?? throw new ArgumentNullException(nameof(needs));
            _file = file ?? throw new ArgumentNullException(nameof(file));
            _write = write ?? ((snapshot, utc) => SaveService.Save(snapshot, utc));
            _file.State = _needs.Snapshot;
            TotalPlaySeconds = float.IsNaN(file.TotalPlaySeconds) || float.IsInfinity(file.TotalPlaySeconds)
                ? 0f : Mathf.Max(0f, file.TotalPlaySeconds);
        }

        /// <summary>累计游玩时长，随存档一起写出。</summary>
        public float TotalPlaySeconds { get; private set; }

        /// <summary>成功写入次数，便于测试断言"没有每帧写盘"。</summary>
        public int SaveCount { get; private set; }

        public int SaveAttemptCount { get; private set; }

        /// <summary>每帧调用。只在满足间隔与脏标记时真正写盘。</summary>
        public bool Tick(float deltaTime, float now)
        {
            if (deltaTime > 0f && !float.IsInfinity(deltaTime))
            {
                TotalPlaySeconds += deltaTime;
                _playTimeDirty = true;
            }

            if (!_needs.Dirty && !_playTimeDirty) return false;
            if (now - _lastAttemptTime < MinIntervalSeconds) return false;

            return Flush(now);
        }

        /// <summary>强制写盘。切后台 / 退出 / 拍照成功后调用。</summary>
        public bool Flush(float now)
        {
            _file.State = _needs.Snapshot;
            _file.TotalPlaySeconds = TotalPlaySeconds;

            _lastAttemptTime = now;
            SaveAttemptCount++;
            var ok = _write(_file, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (ok)
            {
                _needs.ClearDirty();
                _playTimeDirty = false;
                SaveCount++;
            }

            return ok;
        }

        /// <summary>供界面显示"上次保存失败"的提示文案。</summary>
        public string LastError => SaveService.LastError;

        /// <summary>当前存档快照，供反应解锁等模块读取。</summary>
        public SaveFile File => _file;
    }
}
