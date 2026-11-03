using System;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Persistence
{
    /// <summary>
    /// 旧版单宠存档结构（schema 1）。<b>只用于迁移解析</b>：
    /// 新代码只写 <see cref="SaveFile"/>（schema 2），v1 文件在读完后原样保留，作为历史备份。
    ///
    /// 单独留一个类型而不是复用 <see cref="SaveFile"/>，是为了让"读到 v1"和"读到 v2"成为两条
    /// 明确的路径：v1 里的 <c>State</c>（含已退出的 Hunger/Energy/FeedCount）不会混进 v2 的字段。
    /// </summary>
    [Serializable]
    public sealed class SaveFileV1
    {
        public const int SchemaVersionValue = 1;

        public int SchemaVersion = SchemaVersionValue;

        public long SavedAtUnixSeconds;

        /// <summary>旧版单份数值。只有 Happiness 与计数会被迁移，饥饿/精力/投喂次数随旧玩法一起丢弃。</summary>
        public PetState State = PetState.CreateDefault();

        /// <summary>旧版反应标识。迁移时按 <see cref="SaveService.KnownReactionIds"/> 过滤。</summary>
        public string[] UnlockedReactions = Array.Empty<string>();

        public float TotalPlaySeconds;
    }
}
