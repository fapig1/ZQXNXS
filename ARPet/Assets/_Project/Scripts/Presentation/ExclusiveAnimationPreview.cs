using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Presentation
{
    /// <summary>仅供独立开发预览场景使用。生产行为调度仍由行为层负责。</summary>
    public sealed class ExclusiveAnimationPreview : MonoBehaviour
    {
        public Animator Animator;
        public PetExclusiveAnimations Catalog;
        public PetVariantId Variant;
        private float _returnAt;

        private void Update()
        {
            if (_returnAt <= 0f || Time.time < _returnAt) return;
            _returnAt = 0f;
            Animator.CrossFadeInFixedTime("Idle", 0.15f, 0, 0f);
        }

        public void Play()
        {
            if (Animator == null || Catalog == null || !Catalog.TryGet(Variant, out var entry)) return;
            if (!Animator.HasState(0, UnityEngine.Animator.StringToHash(entry.StateName))) return;
            Animator.CrossFadeInFixedTime(entry.StateName, 0.1f, 0, 0f);
            _returnAt = Time.time + entry.Clip.length;
        }

        private void OnGUI()
        {
            if (GUI.Button(new Rect(20 + (int)Variant * 185, 20, 175, 42), Variant + " 专属动作")) Play();
            if (Variant == PetVariantId.Classic)
                GUI.Label(new Rect(20, 70, 1000, 26), "开发预览：rage/divine张臂待完善；开心值触发、表情/特效待接入。");
        }
    }
}
