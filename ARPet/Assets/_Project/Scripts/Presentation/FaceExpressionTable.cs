using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Presentation
{
    /// <summary>
    /// 二维嘴眼表情的选格规则：形象 × 表情 → 眼格名 + 嘴格名。
    ///
    /// 贴片只有两片（眼区、嘴区），表情靠 <c>_MainTex_ST</c> 在图集里换格子；格子坐标来自
    /// Blender 生成的 <c>face_overlay_layout.json</c>，这里只管"用哪一格"，不再写死一份坐标。
    /// 眼格为空表示不显示眼贴片（露出原模型眼睛），嘴格为空同理。
    /// </summary>
    public static class FaceExpressionTable
    {
        public const string Squint = "squint";
        public const string Closed = "closed";
        public const string Divine = "divine";
        public const string Rage = "rage";

        public const string Smile = "smile";
        public const string Laugh = "laugh";
        public const string Roar = "roar";
        public const string O = "o";
        public const string Serene = "serene";
        public const string Flat = "flat";

        /// <summary>一次选格结果。<see cref="Eyes"/> / <see cref="Mouth"/> 为 null 表示该贴片隐藏。</summary>
        public readonly struct Cells : IEquatable<Cells>
        {
            public readonly string Eyes;
            public readonly string Mouth;

            public Cells(string eyes, string mouth)
            {
                Eyes = eyes;
                Mouth = mouth;
            }

            public bool ShowsAnything => Eyes != null || Mouth != null;

            public bool Equals(Cells other) =>
                string.Equals(Eyes, other.Eyes, StringComparison.Ordinal) &&
                string.Equals(Mouth, other.Mouth, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is Cells other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Eyes, Mouth);
            public override string ToString() => $"eyes={Eyes ?? "-"} mouth={Mouth ?? "-"}";
        }

        /// <summary>
        /// 规则（参数建议稿 v0.1 §2.1～2.5）：
        /// <list type="bullet">
        /// <item>专属演出（<see cref="PetExpression.Signature"/>）：classic 眯眼+微笑、calm 闭眼+舒展、
        ///       rage 怒眼+怒吼（嘴角不上扬）、divine 纯白发光眼+舒展。</item>
        /// <item>共同大笑 / 非常开心：四色统一眯眼+大笑。</item>
        /// <item>开心：眯眼+微笑。被点（惊讶）：不换眼，只换 o 嘴。怒吼：四色统一怒眼+怒吼嘴。</item>
        /// <item>拒绝 / 饿（旧值）：不换眼，平直嘴。困：闭眼，不换嘴。中性：全部隐藏。</item>
        /// </list>
        /// </summary>
        public static Cells Resolve(PetVariantId variant, PetExpression expression) => expression switch
        {
            PetExpression.Signature => variant switch
            {
                PetVariantId.Calm => new Cells(Closed, Serene),
                PetVariantId.Rage => new Cells(Rage, Roar),
                PetVariantId.Divine => new Cells(Divine, Serene),
                _ => new Cells(Squint, Smile),
            },
            PetExpression.VeryHappy => new Cells(Squint, Laugh),
            PetExpression.Roar => new Cells(Rage, Roar),
            PetExpression.Happy => new Cells(Squint, Smile),
            PetExpression.Surprised => new Cells(null, O),
            PetExpression.Refuse => new Cells(null, Flat),
            PetExpression.Hungry => new Cells(null, Flat),
            PetExpression.Sleepy => new Cells(Closed, null),
            _ => new Cells(null, null),
        };
    }

    /// <summary>
    /// <c>face_overlay_layout.json</c> 的运行时副本：每格的 <c>_MainTex_ST</c>、
    /// 每色遮盖用的皮肤 UV，以及着色器的视空间前移量。字段名与 JSON 一致，供 <see cref="JsonUtility"/> 读取。
    /// </summary>
    [Serializable]
    public sealed class FaceOverlayLayout
    {
        [Serializable]
        public sealed class Cell
        {
            public string name;
            public float[] st;

            public Vector4 ToVector() => st != null && st.Length == 4 ? new Vector4(st[0], st[1], st[2], st[3]) : Vector4.zero;
        }

        [Serializable]
        public sealed class Cover
        {
            public string variant;
            public float[] eyesUv;
            public float[] mouthUv;
        }

        public string generator;
        public float viewOffsetMeters;
        public string eyesPatch;
        public string mouthPatch;
        public string headBone;
        public Cell[] eyeCells;
        public Cell[] mouthCells;
        public Cover[] covers;

        public static FaceOverlayLayout Parse(string json)
        {
            var layout = JsonUtility.FromJson<FaceOverlayLayout>(json);
            if (layout == null || layout.eyeCells == null || layout.mouthCells == null)
                throw new FormatException("face_overlay_layout.json 缺少 eyeCells / mouthCells");
            return layout;
        }

        public bool TryGetEyeCell(string cellName, out Vector4 st) => TryFind(eyeCells, cellName, out st);

        public bool TryGetMouthCell(string cellName, out Vector4 st) => TryFind(mouthCells, cellName, out st);

        public Cover CoverFor(PetVariantId variant)
        {
            if (covers == null) return null;
            var key = variant.ToString().ToLowerInvariant();
            for (var i = 0; i < covers.Length; i++)
            {
                if (covers[i] != null && string.Equals(covers[i].variant, key, StringComparison.Ordinal)) return covers[i];
            }

            return null;
        }

        private static bool TryFind(Cell[] cells, string cellName, out Vector4 st)
        {
            st = Vector4.zero;
            if (cells == null || cellName == null) return false;
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null || !string.Equals(cell.name, cellName, StringComparison.Ordinal)) continue;
                if (cell.st == null || cell.st.Length != 4) return false;
                st = cell.ToVector();
                return true;
            }

            return false;
        }
    }
}
