using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.UI
{
    /// <summary>
    /// 安全区适配。手机与平板的刘海、挖孔与手势条都会侵占画面边缘，
    /// 把状态面板与按钮限制在 <c>Screen.safeArea</c> 之内。
    ///
    /// 挂在需要适配的界面根节点（通常是 Canvas 下的一个容器）上。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            Apply();
        }

        private void Update()
        {
            // 旋转屏幕、折叠屏展开、分屏都会改变安全区，因此每帧只做一次廉价比较。
            if (Screen.safeArea == _lastSafeArea &&
                Screen.width == _lastScreenSize.x &&
                Screen.height == _lastScreenSize.y)
            {
                return;
            }

            Apply();
        }

        private void Apply()
        {
            if (_rect == null) return;

            var safeArea = Screen.safeArea;
            _lastSafeArea = safeArea;
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);

            if (Screen.width <= 0 || Screen.height <= 0) return;

            var min = safeArea.position;
            var max = safeArea.position + safeArea.size;

            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// 界面输入隔离。挂在每个全屏面板 / 挡板 / 弹窗上，
    /// 用于告诉交互层"这次点击落在 UI 上"，从而不穿透到角色。
    ///
    /// 首版只需要标记区域；真正的拦截由 <c>TouchPokeRule.PointerDown(overUi)</c> 完成。
    /// </summary>
    public sealed class UiInputBlocker : MonoBehaviour
    {
        [Tooltip("该面板是否覆盖全屏。半透明面板同样会拦截点击。")]
        [SerializeField] private bool coversWholeScreen = true;

        [Tooltip("面板当前是否可见。不可见时不拦截。")]
        [SerializeField] private bool visible = true;

        public bool Blocks => coversWholeScreen && visible && gameObject.activeInHierarchy;

        public void SetVisible(bool value) => visible = value;
    }

    /// <summary>
    /// 追踪状态提示。<see cref="TrackingUiState"/> 的四态映射到具体文案与颜色，
    /// 文案集中在这里，避免散落在多个界面脚本中。
    /// </summary>
    public static class TrackingHintText
    {
        public static string For(TrackingUiState state) => state switch
        {
            TrackingUiState.Searching => "Point camera at the den card",
            TrackingUiState.Tracking => "Den card tracked",
            TrackingUiState.Lost => "Tracking lost - find the den card",
            TrackingUiState.Recovered => "Tracking recovered",
            _ => string.Empty,
        };

        public static Color ColorFor(TrackingUiState state) => state switch
        {
            TrackingUiState.Searching => new Color(1f, 1f, 1f, 0.85f),
            TrackingUiState.Tracking => new Color(0.6f, 1f, 0.7f, 0.9f),
            TrackingUiState.Lost => new Color(1f, 0.75f, 0.4f, 1f),
            TrackingUiState.Recovered => new Color(0.7f, 0.9f, 1f, 1f),
            _ => Color.white,
        };
    }
}
