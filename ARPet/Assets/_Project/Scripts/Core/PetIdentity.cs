using System;
using System.Collections.Generic;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 新版角色的稳定形象标识。枚举值一旦发布不得重排，存档只使用对应的字符串 ID。
    /// </summary>
    public enum PetVariantId
    {
        Classic = 0,
        Calm = 1,
        Rage = 2,
        Divine = 3,
    }

    /// <summary>
    /// 目标卡到本地角色实例的稳定绑定。它不保存场景引用或运行时位姿。
    /// </summary>
    [Serializable]
    public struct PetIdentity : IEquatable<PetIdentity>
    {
        public string PetId;
        public string TargetId;
        public PetVariantId Variant;

        public PetIdentity(string petId, string targetId, PetVariantId variant)
        {
            PetId = petId;
            TargetId = targetId;
            Variant = variant;
        }

        public bool IsValid => IsStableId(PetId) && IsStableId(TargetId);

        public bool Equals(PetIdentity other) =>
            string.Equals(PetId, other.PetId, StringComparison.Ordinal) &&
            string.Equals(TargetId, other.TargetId, StringComparison.Ordinal) &&
            Variant == other.Variant;

        public override bool Equals(object obj) => obj is PetIdentity other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(PetId, TargetId, Variant);

        public override string ToString() => $"{PetId} ({Variant}, target={TargetId})";

        internal static bool IsStableId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
            }
            return true;
        }

        /// <summary>
        /// 稳定 ID 规则的公开只读入口。存档层（Persistence）校验 PetId 时用它，
        /// 保证"什么算合法 PetId"只有 Core 这一处定义。
        /// </summary>
        public static bool IsValidStableId(string value) => IsStableId(value);
    }

    /// <summary>
    /// 当前四卡的单一身份目录。实际 Vuforia 数据库接入后仍通过这些稳定 ID 绑定，
    /// 数据库名称和资源路径不参与存档身份。
    /// </summary>
    public static class PetIdentityCatalog
    {
        public static readonly IReadOnlyList<PetIdentity> Default = new[]
        {
            new PetIdentity("pet_classic", "card_classic", PetVariantId.Classic),
            new PetIdentity("pet_calm", "card_calm", PetVariantId.Calm),
            new PetIdentity("pet_rage", "card_rage", PetVariantId.Rage),
            new PetIdentity("pet_divine", "card_divine", PetVariantId.Divine),
        };

        public static bool TryValidate(IReadOnlyList<PetIdentity> identities, out string error)
        {
            if (identities == null || identities.Count == 0)
            {
                error = "角色目录不能为空";
                return false;
            }

            var petIds = new HashSet<string>(StringComparer.Ordinal);
            var targetIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (!identity.IsValid)
                {
                    error = $"第 {i} 项包含非法稳定 ID";
                    return false;
                }

                if (!petIds.Add(identity.PetId))
                {
                    error = $"重复 PetId：{identity.PetId}";
                    return false;
                }

                if (!targetIds.Add(identity.TargetId))
                {
                    error = $"重复 TargetId：{identity.TargetId}";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        public static List<PetIdentity> SortedCopy(IReadOnlyList<PetIdentity> identities)
        {
            var result = new List<PetIdentity>(identities ?? Array.Empty<PetIdentity>());
            result.Sort((left, right) => string.CompareOrdinal(left.PetId, right.PetId));
            return result;
        }
    }
}
