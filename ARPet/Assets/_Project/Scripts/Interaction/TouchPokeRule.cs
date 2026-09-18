using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Interaction
{
    /// <summary>
    /// 触屏逗弄判定。
    ///
    /// 命名纪律：这是<b>触屏输入</b>，实现里没有手部检测，
    /// 因此在界面文案、报告和答辩中都只能说"点击 / 滑动角色"，不能说"识别到真实手部接触"。
    ///
    /// 另外两点：
    /// <list type="bullet">
    /// <item>只有当角色当前可见（跟踪有效且未过期）时，点击才算逗弄；</item>
    /// <item>存在滑动阈值与冷却，避免连续点按刷出大量事件。</item>
    /// </list>
    /// </summary>
    public sealed class TouchPokeRule
    {
        private Vector2 _pointerDownPosition;
        private float _pointerDownTime;
        private bool _pointerActive;

        private float _cooldownUntil;
        private int _nextEventId = 1000;

        public TouchPokeRule(TouchPokeSettings settings)
        {
            Settings = settings ?? new TouchPokeSettings();
        }

        public TouchPokeSettings Settings { get; }

        /// <summary>最近一次判定的说明，供调试面板显示。</summary>
        public string LastReason { get; private set; } = "尚未判定";

        public int IssuedEventCount => _nextEventId - 1000;

        /// <summary>记录按下。返回 <c>false</c> 表示这次按下不会被当成逗弄（例如落在 UI 上）。</summary>
        public bool PointerDown(Vector2 screenPosition, float time, bool overUi)
        {
            if (overUi)
            {
                // 界面输入不得穿透到角色。这里直接吞掉，不产生任何事件。
                _pointerActive = false;
                LastReason = "按下落在 UI 上，已拦截";
                return false;
            }

            _pointerDownPosition = screenPosition;
            _pointerDownTime = time;
            _pointerActive = true;
            return true;
        }

        /// <summary>
        /// 记录抬起并尝试结算。
        /// </summary>
        /// <param name="hitPet">抬起位置是否命中了角色的可交互区域。</param>
        /// <param name="petVisible">角色当前是否可见（跟踪有效且位姿未过期）。</param>
        /// <param name="now">当前时刻。</param>
        public bool PointerUp(Vector2 screenPosition, float time, bool hitPet, bool petVisible, float now,
            out InteractionEvent produced)
        {
            produced = default; 

            if (!_pointerActive) return false;
            _pointerActive = false;

            var travel = Vector2.Distance(screenPosition, _pointerDownPosition);
            var held = time - _pointerDownTime;

            if (travel > Settings.MaxTravelPixels)
            {
                LastReason = $"位移 {travel:F0}px 超过 {Settings.MaxTravelPixels:F0}px，视为滑动，不触发逗弄";
                return false;
            }

            if (held > Settings.MaxHoldSeconds)
            {
                LastReason = $"按住 {held:F2}s 超过上限，视为长按，不触发逗弄";
                return false;
            }

            if (!hitPet)
            {
                LastReason = "未命中角色可交互区域";
                return false;
            }

            if (!petVisible)
            {
                // 角色不可见时点击不应产生事件——这与"跟踪丢失就暂停新空间交互"同一条纪律。
                LastReason = "角色当前不可见，忽略点击";
                return false;
            }

            if (now < _cooldownUntil)
            {
                LastReason = $"逗弄冷却中，剩余 {_cooldownUntil - now:F2}s";
                return false;
            }

            _cooldownUntil = now + Settings.CooldownSeconds;
            produced = InteractionEvent.Create(_nextEventId++, InteractionEventKind.Pet, MarkerSlot.Den, now);
            LastReason = "逗弄事件已产生";
            return true;
        }

        /// <summary>跟踪丢失或切后台时取消未完成点击，恢复后不补发触屏事件。</summary>
        public void CancelPointer() => _pointerActive = false;

        public void Reset()
        {
            _pointerActive = false;
            _cooldownUntil = 0f;
            LastReason = "已重置";
        }

        /// <summary>触屏逗弄的可调参数。</summary>
        public sealed class TouchPokeSettings
        {
            /// <summary>超过该位移（屏幕像素）视为滑动，不触发逗弄。</summary>
            public float MaxTravelPixels = 40f;

            /// <summary>按住超过该时长视为长按，不触发逗弄。</summary>
            public float MaxHoldSeconds = 0.5f;

            /// <summary>两次逗弄之间的最小间隔。</summary>
            public float CooldownSeconds = 0.45f;
        }
    }
}
