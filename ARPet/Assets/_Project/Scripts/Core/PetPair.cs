using System;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 一对宠物之间的<b>无序</b>关系，也是"接近规则"的最小判定单位：
    /// 两张卡的一次合格靠近只对应一个关系对，锁定与解锁都以它为准。
    ///
    /// 两个成员在构造时按 <see cref="string.CompareOrdinal"/> 排序并存成 <see cref="A"/> / <see cref="B"/>
    /// （保证 <c>A &lt; B</c>），因此同一个关系无论以哪种顺序传入都得到相同的键：
    /// 相等性、哈希与字符串表示全部使用 Ordinal，与存档里的稳定 PetId 写法一致。
    ///
    /// 它放在 Core 而不是 Pet 或 Interaction：接近判定属于 Interaction 层，
    /// 组协调属于 Pet 层，Pet 不能反向引用 Interaction，所以"关系对"这个共享词汇必须住在 Core。
    /// </summary>
    public readonly struct PetPair : IEquatable<PetPair>
    {
        /// <summary>按 Ordinal 较小的一方。</summary>
        public string A { get; }

        /// <summary>按 Ordinal 较大的一方。</summary>
        public string B { get; }

        /// <summary>
        /// 构造一个关系对。两个 ID 必须非空且不同；顺序不影响结果。
        /// </summary>
        /// <exception cref="ArgumentException">任一 ID 为空，或两者相同。</exception>
        public PetPair(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
                throw new ArgumentException("关系对的成员 ID 不能为空", nameof(first));
            if (string.IsNullOrWhiteSpace(second))
                throw new ArgumentException("关系对的成员 ID 不能为空", nameof(second));

            var order = string.CompareOrdinal(first, second);
            if (order == 0)
                throw new ArgumentException($"关系对的两个成员必须是不同的宠物，收到两个 “{first}”", nameof(second));

            if (order < 0)
            {
                A = first;
                B = second;
            }
            else
            {
                A = second;
                B = first;
            }
        }

        /// <summary>该关系是否包含某只宠物。</summary>
        public bool Involves(string petId) =>
            string.Equals(A, petId, StringComparison.Ordinal) ||
            string.Equals(B, petId, StringComparison.Ordinal);

        /// <summary>取关系对里的另一方；<paramref name="petId"/> 不在关系里时返回 <c>null</c>。</summary>
        public string Other(string petId)
        {
            if (string.Equals(A, petId, StringComparison.Ordinal)) return B;
            if (string.Equals(B, petId, StringComparison.Ordinal)) return A;
            return null;
        }

        public bool Equals(PetPair other) =>
            string.Equals(A, other.A, StringComparison.Ordinal) &&
            string.Equals(B, other.B, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PetPair other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(A, B);

        public override string ToString() => A + "+" + B;
    }
}
