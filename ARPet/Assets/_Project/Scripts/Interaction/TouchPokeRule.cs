using UnityEngine;

namespace ZQXNXS.ARPet.Interaction
{
    /// <summary>
    /// 触屏点按判定。<b>只回答一个问题：这次按下/抬起算不算一次合格点按。</b>
    /// 事件 Id 与单击/双击的区分交给 <see cref="TapGestureRecognizer"/>，本类不产生任何交互事件。
    ///
    /// 命名纪律：这是<b>触屏输入</b>，实现里没有手部检测，
    /// 因此在界面文案、报告和答辩中都只能说"点击 / 滑动角色"，不能说"识别到真实手部接触"。
    ///
    /// 三条判定条件：
    /// <list type="bullet">
    /// <item>按下与抬起都命中同一只角色，且该宠当前可见（跟踪有效且位姿未过期）；</item>
    /// <item>位移不超过阈值。阈值按屏幕 DPI 可回退换算：DPI 可用时取
    ///       <c>max(MaxTravelPixels, dpi × MaxTravelInches)</c>，否则退回固定像素值——
    ///       固定 40 px 在高 DPI 手机上过严、在低 DPI 平板上过松；</item>
    /// <item>按住时长不超过上限，长按不产生开心值。</item>
    /// </list>
    ///
    /// <b>已删除 0.45 s 冷却：</b>它会挡掉双击的第二下。交互冷却由行为层的 0.7 s TouchReact
    /// 忙碌状态实现（单次行为执行期间新的点按一律拒绝且不排队），不在输入层重复节流。
    /// </summary>
    public sealed class TouchPokeRule
    {
        private Vector2 _pointerDownPosition;
        private float _pointerDownTime;
        private bool _pointerActive;
        private int _qualifiedTaps;

        public TouchPokeRule(TouchPokeSettings settings = null, float dpi = 0f)
        {
            Settings = settings ?? new TouchPokeSettings();
            Dpi = dpi > 0f && !float.IsInfinity(dpi) && !float.IsNaN(dpi) ? dpi : 0f;
        }

        public TouchPokeSettings Settings { get; }

        /// <summary>
        /// 本次运行注入的屏幕 DPI（<c>Screen.dpi</c>）。0 表示未知或无效，此时只用像素阈值。
        /// 由调用方注入而不是在这里读 <c>Screen</c>，是为了让纯逻辑能在 EditMode 里用固定 DPI 验证。
        /// </summary>
        public float Dpi { get; }

        /// <summary>本次实际生效的位移阈值（像素）。</summary>
        public float MaxTravelPixels => Settings.ResolveMaxTravelPixels(Dpi);

        /// <summary>最近一次判定的说明，供调试面板显示。</summary>
        public string LastReason { get; private set; } = "尚未判定";

        /// <summary>累计判定通过的合格点按次数（不含被识别器合成或丢弃的），仅用于调试与测试。</summary>
        public int QualifiedTapCount => _qualifiedTaps;

        /// <summary>记录按下。返回 <c>false</c> 表示这次按下不会被当成点按（例如落在 UI 上）。</summary>
        public bool PointerDown(Vector2 screenPosition, float time, bool overUi)
        {
            if (overUi)
            {
                // 界面输入不得穿透到角色。这里直接吞掉，不产生任何点按。
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
        /// 记录抬起并判定是否为合格点按。
        /// </summary>
        /// <param name="hitPet">抬起位置是否命中了角色的可交互区域。</param>
        /// <param name="petVisible">角色当前是否可见（跟踪有效且位姿未过期）。</param>
        /// <returns>合格返回 <c>true</c>，调用方应把 <paramref name="now"/> 与归属 petId 交给识别器。</returns>
        public bool PointerUp(Vector2 screenPosition, float time, bool hitPet, bool petVisible)
        {
            if (!_pointerActive) return false;
            _pointerActive = false;

            var travel = Vector2.Distance(screenPosition, _pointerDownPosition);
            var held = time - _pointerDownTime;
            var maxTravel = MaxTravelPixels;

            if (travel > maxTravel)
            {
                LastReason = Dpi > 0f
                    ? $"位移 {travel:F0}px 超过 {maxTravel:F0}px（DPI {Dpi:F0}），视为滑动，不触发逗弄"
                    : $"位移 {travel:F0}px 超过 {maxTravel:F0}px，视为滑动，不触发逗弄";
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

            _qualifiedTaps++;
            LastReason = "合格点按，已交给手势识别";
            return true;
        }

        /// <summary>跟踪丢失、切后台或按下与抬起不是同一只时取消未完成点击，恢复后不补发。</summary>
        public void CancelPointer() => _pointerActive = false;

        public void Reset()
        {
            _pointerActive = false;
            LastReason = "已重置";
        }

        /// <summary>触屏点按的可调参数。</summary>
        public sealed class TouchPokeSettings
        {
            /// <summary>位移阈值下限（屏幕像素）。DPI 不可用时的唯一阈值。</summary>
            public float MaxTravelPixels = 40f;

            /// <summary>
            /// 允许的位移（英寸）。DPI 可用时阈值 = max(<see cref="MaxTravelPixels"/>, dpi × 本值)，
            /// 让不同像素密度的手机/平板得到相近的物理容差。取 0 等同于只用像素阈值。
            /// </summary>
            public float MaxTravelInches = 0.25f;

            /// <summary>按住超过该时长视为长按，不触发逗弄。</summary>
            public float MaxHoldSeconds = 0.5f;

            /// <summary>按 DPI 计算实际生效的位移阈值（像素）；DPI 无效时回退到 <see cref="MaxTravelPixels"/>。</summary>
            public float ResolveMaxTravelPixels(float dpi)
            {
                if (float.IsNaN(dpi) || float.IsInfinity(dpi) || dpi <= 0f) return MaxTravelPixels;
                var physical = dpi * Mathf.Max(0f, MaxTravelInches);
                return Mathf.Max(MaxTravelPixels, physical);
            }
        }
    }
}
