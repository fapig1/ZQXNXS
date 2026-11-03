using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.UI
{
    /// <summary>
    /// "按住说话"麦克风按钮。按下开始聆听、松开（或手指移出 / 被系统取消）停止。
    /// 只依赖 Core 的 <see cref="IVoiceCommandSource"/>：不知道宠物、不结算任何数值。
    /// 按钮本身是 UI，点它不会穿透成一次对宠物的触屏点按（TouchInputBridge 会判 UI 遮挡）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("实现 IVoiceCommandSource 的组件（VoiceKeywordListener）。")]
        [SerializeField] private MonoBehaviour sourceBehaviour;

        [SerializeField] private Image background;
        [SerializeField] private Text label;

        [SerializeField] private Color idleColor = new(0.16f, 0.55f, 0.95f, 0.92f);
        [SerializeField] private Color listeningColor = new(0.95f, 0.35f, 0.30f, 0.95f);
        [SerializeField] private Color disabledColor = new(0.45f, 0.45f, 0.45f, 0.70f);

        private IVoiceCommandSource _source;
        private bool _held;
        private VoiceListenState _shownState = (VoiceListenState)(-1);
        private string _shownText;

        private void Awake() => _source = sourceBehaviour as IVoiceCommandSource;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_source == null) return;
            _held = true;
            _source.BeginListening();
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_held && eventData.pointerPress == gameObject) Release();
        }

        private void OnDisable() => Release();

        private void Release()
        {
            if (!_held) return;
            _held = false;
            _source?.EndListening();
        }

        private void Update()
        {
            var state = _source?.State ?? VoiceListenState.Unavailable;
            var text = state switch
            {
                VoiceListenState.Listening => "正在听…",
                VoiceListenState.Idle => "按住说话",
                VoiceListenState.RequestingPermission => "请允许麦克风",
                _ => _source?.StatusText ?? "语音不可用",
            };

            if (state == _shownState && text == _shownText) return;
            _shownState = state;
            _shownText = text;

            if (label != null) label.text = text;
            if (background != null)
            {
                background.color = state switch
                {
                    VoiceListenState.Listening => listeningColor,
                    VoiceListenState.Idle => idleColor,
                    _ => disabledColor,
                };
            }
        }
    }
}
