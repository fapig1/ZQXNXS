using System;
using UnityEngine;

namespace ZQXNXS.ARPet.UI
{
    /// <summary>
    /// 横竖屏两套锚点。应用同时允许竖屏与左右横屏（2026-10-30 起），状态面板在竖屏铺满顶部宽度，
    /// 横屏时只占左上角一块，避免压住大半个相机画面。
    ///
    /// 只看 <c>Screen.width &gt; Screen.height</c> 判断朝向，变化时才重写锚点；安全区由父级
    /// <see cref="SafeAreaFitter"/> 负责，两者互不干扰。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class OrientationAnchors : MonoBehaviour
    {
        /// <summary>一套锚点与偏移，对应 RectTransform 的四个字段。</summary>
        [Serializable]
        public struct Layout
        {
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 OffsetMin;
            public Vector2 OffsetMax;

            public Layout(Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
            {
                AnchorMin = anchorMin;
                AnchorMax = anchorMax;
                OffsetMin = offsetMin;
                OffsetMax = offsetMax;
            }
        }

        [SerializeField] private Layout portrait;
        [SerializeField] private Layout landscape;

        private RectTransform _rect;
        private int _appliedLandscape = -1;

        public Layout Portrait => portrait;
        public Layout Landscape => landscape;

        /// <summary>编辑器装配用：一次写入两套布局。</summary>
        public void SetLayouts(Layout portraitLayout, Layout landscapeLayout)
        {
            portrait = portraitLayout;
            landscape = landscapeLayout;
            _appliedLandscape = -1;
        }

        private void OnEnable()
        {
            _appliedLandscape = -1;
            ApplyForScreen();
        }

        private void Update() => ApplyForScreen();

        private void ApplyForScreen()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            Apply(Screen.width > Screen.height);
        }

        /// <summary>按指定朝向套用布局；朝向没变时不重写。公开给测试直接驱动。</summary>
        public void Apply(bool isLandscape)
        {
            var key = isLandscape ? 1 : 0;
            if (key == _appliedLandscape) return;

            if (_rect == null) _rect = GetComponent<RectTransform>();
            var layout = isLandscape ? landscape : portrait;
            _rect.anchorMin = layout.AnchorMin;
            _rect.anchorMax = layout.AnchorMax;
            _rect.offsetMin = layout.OffsetMin;
            _rect.offsetMax = layout.OffsetMax;
            _appliedLandscape = key;
        }
    }
}
