using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Persistence
{
    /// <summary>
    /// 一只宠物在存档里的记录。只写稳定 <see cref="PetId"/> 与数值：
    /// 位姿、场景对象引用、<c>Time.time</c> 一律不入档，由跟踪层在每次启动时重新获取。
    /// 满值周期（P3）是新增字段：schema 仍是 2，旧档缺这些字段时读成默认值
    /// （<see cref="HappyCycleState.Armed"/> / 0 / 0 / 0），因此不需要再升一次文件名。
    /// 共同大笑看完次数（P4）同理：缺字段读成 0。
    /// </summary>
    [Serializable]
    public sealed class PetSaveEntry
    {
        /// <summary>稳定宠物 ID（如 pet_classic）。未知 ID 会被原样保留，但不参与本局运行。</summary>
        public string PetId = string.Empty;

        /// <summary>开心值 0～1。读取时会清洗 NaN / 越界。</summary>
        public float Happiness;

        public int PetCount;
        public int PlayCount;
        public int PhotoCount;

        /// <summary>满值周期状态，按 <see cref="HappyCycleState"/> 的 int 值存储（0 = Armed）。</summary>
        public int HappyCycle;

        /// <summary>专属演出冷却剩余秒数。读取时清洗 NaN / 负数，上限由变体调参表在运行层收紧。</summary>
        public float ExclusiveCooldownRemaining;

        /// <summary>专属演出"开始过"的次数。</summary>
        public int ExclusiveStartCount;

        /// <summary>专属演出"完整看完"的次数（被打断不计）。</summary>
        public int ExclusiveWatchedCount;

        /// <summary>
        /// 共同大笑"完整看完"的次数。P4 新增字段，schema 仍是 2：
        /// 旧档缺该字段时读成 0，不需要再升一次文件名或版本号。
        /// </summary>
        public int SocialWatchedCount;
    }

    /// <summary>
    /// 存档结构，schema 2（多宠）。纪律：
    /// <list type="bullet">
    /// <item>只保存<b>稳定标识与数值</b>，绝不保存场景对象引用、文件名或临时世界坐标；</item>
    /// <item>带 <see cref="SchemaVersion"/>，升级时做迁移而不是直接读崩；</item>
    /// <item>字段缺失时保持默认值，读取后仍要经过范围与重复校验（见 <see cref="SaveService"/>）。</item>
    /// </list>
    /// 旧版单宠结构见 <see cref="SaveFileV1"/>：它只用于迁移解析，不参与写入。
    /// </summary>
    [Serializable]
    public sealed class SaveFile
    {
        /// <summary>当前代码写出的存档版本。改变字段语义时必须递增。</summary>
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>最近一次写入的 Unix 时间戳（秒）。用于离线衰减补算。</summary>
        public long SavedAtUnixSeconds;

        /// <summary>累计游玩时长（秒），用于报告里的体验统计。</summary>
        public float TotalPlaySeconds;

        /// <summary>已解锁的反应标识。使用稳定字符串而不是枚举序号，避免以后插入枚举值导致错位。</summary>
        public string[] UnlockedReactions = Array.Empty<string>();

        /// <summary>逐只宠物的数值。顺序即写入顺序；本局没绑定的宠物条目原样保留，不丢别人的数据。</summary>
        public PetSaveEntry[] Pets = Array.Empty<PetSaveEntry>();

        /// <summary>
        /// 本次读取是否来自旧版 v1 的一次性迁移。<b>不是存档字段</b>：这里是属性而不是字段，
        /// JsonUtility 只序列化字段，所以它不会写进文件；每次 <see cref="SaveService.Load"/> 都会显式重置。
        /// </summary>
        public bool MigratedFromV1 { get; set; }

        /// <summary>
        /// 读到比当前代码更新的 schema 时为 true。此时运行数据是默认值，
        /// 写盘必须被拒绝，避免用旧结构覆盖玩家的新版存档。同样不是存档字段。
        /// </summary>
        public bool WriteBlocked { get; set; }

        /// <summary>被 <see cref="WriteBlocked"/> 拦下的存档版本号；0 表示没有拦下任何存档。</summary>
        public int BlockedSchemaVersion { get; set; }

        public static SaveFile CreateNew(long nowUnix) => new SaveFile
        {
            SchemaVersion = CurrentSchemaVersion,
            SavedAtUnixSeconds = nowUnix,
            TotalPlaySeconds = 0f,
            UnlockedReactions = Array.Empty<string>(),
            Pets = Array.Empty<PetSaveEntry>(),
        };

        /// <summary>结构层面的自检。返回 false 时调用方应回退到默认存档而不是继续使用。</summary>
        public bool IsStructurallyValid() => SchemaVersion > 0;

        /// <summary>按稳定 PetId 找条目。找不到返回 false，调用方应保留 PetConfig 的初始值。</summary>
        public bool TryGetPet(string petId, out PetSaveEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(petId) || Pets == null) return false;

            for (var i = 0; i < Pets.Length; i++)
            {
                var candidate = Pets[i];
                if (candidate == null) continue;
                if (!string.Equals(candidate.PetId, petId, StringComparison.Ordinal)) continue;

                entry = candidate;
                return true;
            }

            return false;
        }
    }
}
