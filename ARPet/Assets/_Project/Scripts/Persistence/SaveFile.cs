using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Persistence
{
    /// <summary>
    /// 存档结构。纪律：
    /// <list type="bullet">
    /// <item>只保存<b>稳定标识与数值</b>，绝不保存场景对象引用、文件名或临时世界坐标；</item>
    /// <item>带 <see cref="SchemaVersion"/>，升级时做迁移而不是直接读崩；</item>
    /// <item>字段缺失时保持默认值，读取后仍要经过范围校验。</item>
    /// </list>
    /// </summary>
    [Serializable]
    public sealed class SaveFile
    {
        /// <summary>当前代码写出的存档版本。改变字段语义时必须递增。</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>最近一次写入的 Unix 时间戳（秒）。用于离线衰减补算。</summary>
        public long SavedAtUnixSeconds;

        public PetState State = PetState.CreateDefault();

        /// <summary>已解锁的反应标识。使用字符串常量而不是枚举序号，避免以后插入枚举值导致错位。</summary>
        public string[] UnlockedReactions = Array.Empty<string>();

        /// <summary>累计游玩时长（秒），用于报告里的体验统计。</summary>
        public float TotalPlaySeconds;

        public static SaveFile CreateNew(long nowUnix)
        {
            var file = new SaveFile
            {
                SchemaVersion = CurrentSchemaVersion,
                SavedAtUnixSeconds = nowUnix,
                State = PetState.CreateDefault(),
            };
            return file;
        }

        /// <summary>结构层面的自检。返回 false 时调用方应回退到默认存档而不是继续使用。</summary>
        public bool IsStructurallyValid() => SchemaVersion > 0 && SchemaVersion <= CurrentSchemaVersion;
    }
}
