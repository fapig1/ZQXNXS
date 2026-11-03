using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Presentation
{
    /// <summary>专属演出资源映射。时长取实际 Clip，不包含开心值结算或冷却策略。</summary>
    [CreateAssetMenu(menuName = "桌上有龙/四色专属动画", fileName = "PetExclusiveAnimations")]
    public sealed class PetExclusiveAnimations : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public PetVariantId Variant;
            public string StateName;
            public AnimationClip Clip;
        }

        public Entry[] Entries = Array.Empty<Entry>();

        public bool TryGet(PetVariantId variant, out Entry entry)
        {
            if (Entries != null)
                foreach (var candidate in Entries)
                    if (candidate.Variant == variant && candidate.Clip != null &&
                        !string.IsNullOrWhiteSpace(candidate.StateName))
                    {
                        entry = candidate;
                        return true;
                    }
            entry = default;
            return false;
        }
    }
}
