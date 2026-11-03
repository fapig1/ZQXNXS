using UnityEngine;
using UnityEngine.EventSystems;

namespace ZQXNXS.ARPet.App
{
    /// <summary>
    /// 触屏输入入口：把 Unity 的触屏 / 鼠标事件转换成 <see cref="AppRoot"/> 需要的
    /// 屏幕坐标与"命中哪一只宠物"，别的地方都不直接读 <c>Input</c>。
    ///
    /// 命名纪律与 <c>TouchPokeRule</c> 一致：这是<b>触屏输入</b>，不是现实手部接触识别，
    /// 界面文案与报告不能称为"识别到真实手部接触"。
    ///
    /// 命中判定用一条从摄像机出发的射线与场景里的 <see cref="PetHitTarget"/> 求交，
    /// 而不是 UI 事件系统的屏幕矩形碰撞，因为角色是 3D AR 内容、不是 Canvas 元素。
    /// 是否落在 UI 上则反过来交给 <see cref="EventSystem.IsPointerOverGameObject"/> 判断，
    /// 两者不会互相依赖。
    ///
    /// 多宠：一次按下/抬起会遍历所有启用的 <see cref="PetHitTarget"/>，取沿射线最近的那个，
    /// 把它的 petId 交给 <see cref="AppRoot"/>；没命中则传 <c>null</c>。
    ///
    /// 多指：Android 分支在 Began 时记住该触点的 fingerId，之后每帧按 fingerId 查找这一个触点，
    /// 其他手指的 Began / Moved / Ended 一律忽略。不能固定 <c>GetTouch(0)</c>——
    /// 触点顺序会变，那样第二根手指抬起会被当成这次点击结束。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TouchInputBridge : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("触屏事件最终要转发到的应用组装点。")]
        [SerializeField] private AppRoot appRoot;

        [Tooltip("命中判定使用的摄像机。留空则用 Camera.main。")]
        [SerializeField] private Camera raycastCamera;

        [Header("命中判定")]
        [Tooltip("命中目标列表。留空则在运行时自动查找场景中启用的 PetHitTarget（便于多宠场景不用逐一手拖）。")]
        [SerializeField] private PetHitTarget[] hitTargets = System.Array.Empty<PetHitTarget>();

        [Tooltip("自动查找 PetHitTarget 的刷新间隔（秒）。场景里宠物是运行时生成的时靠它发现新目标。")]
        [SerializeField] private float hitTargetRefreshSeconds = 1f;

        [Tooltip("射线检测的最远距离（米）。远大于桌面 AR 的常见使用距离即可。")]
        [SerializeField] private float maxRayDistance = 5f;

        private PetHitTarget[] _cachedTargets;
        private float _nextRefreshTime;

        /// <summary>
        /// 正在跟踪的触点的 fingerId；<c>-1</c> 表示当前没有跟踪任何触点。
        /// 必须记住它再按 id 查找，而不是一直用 <c>GetTouch(0)</c>：
        /// 多指同时按下时 Unity 的触点顺序会变化，固定取 0 会把别的手指的移动/抬起当成这次点击。
        /// </summary>
        private int _activeFingerId = -1;

        /// <summary>最近一次抬起时命中的宠物稳定 ID；没命中为 <c>null</c>。供测试与调试观察。</summary>
        public string LastHitPetId { get; private set; }

        private void Reset()
        {
            appRoot = FindFirstObjectByType<AppRoot>(FindObjectsInactive.Include);
        }

        private void OnEnable()
        {
            // 场景切换或重新启用后重新收集，避免拿着已销毁的目标。
            _cachedTargets = null;
        }

        private void OnDisable() => DropActivePointer();

        private void OnApplicationPause(bool paused)
        {
            // 按住时切后台：回来后系统可能把同一个 fingerId 分给新触点，残留的 id 会让那次点按被吞。
            if (paused) DropActivePointer();
        }

        private void DropActivePointer()
        {
            if (_activeFingerId < 0) return;
            _activeFingerId = -1;
            if (appRoot != null) Cancel();
        }

        private void Update()
        {
            if (appRoot == null) return;

            if (Time.time >= _nextRefreshTime) RefreshHitTargets();

#if UNITY_ANDROID && !UNITY_EDITOR
            HandleTouches();
#else
            HandleMouse();
#endif
        }

        private void HandleMouse()
        {
            // 两个独立判断：极快的点击可能在同一帧里同时按下又抬起，用 else 会丢掉抬起。
            if (Input.GetMouseButtonDown(0)) Begin(Input.mousePosition);
            if (Input.GetMouseButtonUp(0)) End(Input.mousePosition);
        }

        private void HandleTouches()
        {
            if (Input.touchCount == 0)
            {
                // 触点列表被清空（系统在 Ended 之外直接收走触点）：记住的 fingerId 已经不存在，
                // 按"取消"处理，不当作一次抬起。
                if (_activeFingerId >= 0)
                {
                    _activeFingerId = -1;
                    Cancel();
                }

                return;
            }

            if (_activeFingerId < 0)
            {
                // 没有活动触点：只接本帧第一个 Began 并记住它的 fingerId，其他手指一律忽略。
                if (TryFindBegan(out var began))
                {
                    _activeFingerId = began.fingerId;
                    Begin(began.position);
                }

                return;
            }

            // 按记住的 fingerId 查找这个触点（不是固定 GetTouch(0)）。
            if (!TryFindTouch(_activeFingerId, out var active))
            {
                // fingerId 消失了：当作取消，不结算。
                _activeFingerId = -1;
                Cancel();
                return;
            }

            switch (active.phase)
            {
                case TouchPhase.Ended:
                    _activeFingerId = -1;
                    End(active.position);
                    break;
                case TouchPhase.Canceled:
                    // 系统取消（来电、手势返回等）不等于"用户抬起"：只丢弃本次按下，不结算。
                    _activeFingerId = -1;
                    Cancel();
                    break;
                default:
                    return;
            }

            // 被跟踪的手指本帧结束时，另一根手指可能恰好同帧按下：它的 Began 只出现这一帧，
            // 这里不接就再也接不到，那次点按会整个丢掉。
            if (TryFindBegan(out var next))
            {
                _activeFingerId = next.fingerId;
                Begin(next.position);
            }
        }

        /// <summary>按 fingerId 查找当前帧里那个触点。找不到返回 <c>false</c>（触点已经消失）。</summary>
        private static bool TryFindTouch(int fingerId, out Touch touch)
        {
            for (var i = 0; i < Input.touchCount; i++)
            {
                var candidate = Input.GetTouch(i);
                if (candidate.fingerId != fingerId) continue;
                touch = candidate;
                return true;
            }

            touch = default;
            return false;
        }

        /// <summary>找本帧第一个处于 Began 阶段的触点。</summary>
        private static bool TryFindBegan(out Touch touch)
        {
            for (var i = 0; i < Input.touchCount; i++)
            {
                var candidate = Input.GetTouch(i);
                if (candidate.phase != TouchPhase.Began) continue;
                touch = candidate;
                return true;
            }

            touch = default;
            return false;
        }

        private void Begin(Vector2 screenPosition)
        {
            var overUi = IsOverUi();
            var hitPetId = overUi ? null : HitPetIdAt(screenPosition);
            appRoot.OnTouchPointerDown(screenPosition, overUi, hitPetId);
        }

        private void End(Vector2 screenPosition)
        {
            LastHitPetId = HitPetIdAt(screenPosition);
            appRoot.OnTouchPointerUp(screenPosition, LastHitPetId);
        }

        private void Cancel()
        {
            LastHitPetId = null;
            appRoot.OnTouchPointerCanceled();
        }

        /// <summary>
        /// 界面输入不得穿透到角色：任何挂了 <see cref="UiInputBlocker"/> 且当前生效的面板
        /// 都优先于角色命中判定。<c>EventSystem.IsPointerOverGameObject</c> 只在有 EventSystem
        /// 时才查询，场景里没有 UI（如模拟场景）时直接视为未覆盖。
        /// </summary>
        private bool IsOverUi()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

#if UNITY_ANDROID && !UNITY_EDITOR
            // 用记住的 fingerId 查询（不是 GetTouch(0)）：多指时触点顺序会变。
            if (_activeFingerId >= 0)
            {
                return eventSystem.IsPointerOverGameObject(_activeFingerId);
            }
#endif
            return eventSystem.IsPointerOverGameObject();
        }

        /// <summary>
        /// 是否命中了任意一只宠物。保留布尔入口是为了兼容旧调用点与测试。
        /// </summary>
        public bool HitsPet(Vector2 screenPosition) => HitPetIdAt(screenPosition) != null;

        /// <summary>
        /// 命中判定的纯逻辑部分：返回沿射线最近的启用目标的 petId，没命中返回 <c>null</c>。
        /// 公开出来是为了能在 EditMode 测试里直接构造场景验证，不需要真的产生一次 <c>Input</c> 事件。
        /// </summary>
        public string HitPetIdAt(Vector2 screenPosition)
        {
            var cam = raycastCamera != null ? raycastCamera : Camera.main;
            if (cam == null) return null;

            if (_cachedTargets == null) RefreshHitTargets();

            var ray = cam.ScreenPointToRay(screenPosition);
            PetHitTarget best = null;
            var bestDistance = float.PositiveInfinity;

            for (var i = 0; i < _cachedTargets.Length; i++)
            {
                var target = _cachedTargets[i];
                // 只考虑启用中的目标：被禁用的组件 / 隐藏的角色不应还能被点中。
                if (target == null || !target.isActiveAndEnabled) continue;
                if (string.IsNullOrEmpty(target.PetId)) continue;

                var collider = target.HitCollider;
                float distance;

                if (collider != null)
                {
                    if (!collider.enabled) continue;
                    if (!collider.Raycast(ray, out var hit, maxRayDistance)) continue;
                    distance = hit.distance;
                }
                else
                {
                    // 没有碰撞体时，用"脚底到头顶的竖直胶囊"近似命中区域，
                    // 避免要求每个宠物 Prefab 都必须先做碰撞体才能测试触屏逗弄。
                    target.GetCapsule(out var bottom, out var top, out var radius);
                    if (!TryRayCapsule(ray, bottom, top, radius, maxRayDistance, out distance)) continue;
                }

                // 多只宠物重叠时以沿射线最近者为准，远处的宠物不能被"穿过"前面的角色点到。
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = target;
            }

            return best != null ? best.PetId : null;
        }

        /// <summary>
        /// 射线与胶囊（线段 <paramref name="bottom"/>–<paramref name="top"/> 加半径）是否相交。
        /// 用射线与中轴线段的最近点判定：最近距离 ≤ 半径即命中，<paramref name="distance"/>
        /// 返回该最近点沿射线的距离，只用于多只宠物重叠时排序，不是精确的表面交点。
        /// 最近点在摄像机背后或超出 <paramref name="maxDistance"/> 时不算命中。
        /// </summary>
        internal static bool TryRayCapsule(Ray ray, Vector3 bottom, Vector3 top, float radius, float maxDistance,
            out float distance)
        {
            distance = 0f;
            var d = ray.direction;
            var axis = top - bottom;
            var w = ray.origin - bottom;

            var a = Vector3.Dot(axis, axis);
            var b = Vector3.Dot(d, axis);
            var c = Vector3.Dot(w, axis);
            var e = Vector3.Dot(d, w);

            // s：中轴上的参数（0～1），t：射线上的距离。a≈0 时线段退化成一个点（球）。
            float s;
            if (a < 1e-10f)
            {
                s = 0f;
            }
            else
            {
                var denominator = a - b * b;   // |d| = 1，所以 a·1 − b²
                // 射线与中轴平行（正上方俯拍）：任一点到轴的距离相同，取离摄像机近的一端用于排序。
                s = denominator > 1e-6f * a ? (c - b * e) / denominator : (b > 0f ? 0f : 1f);
                s = Mathf.Clamp01(s);
            }

            // 中轴参数夹到线段内后，射线上的最近点就是该轴点在射线上的投影。
            var pointOnAxis = bottom + axis * s;
            var t = Vector3.Dot(pointOnAxis - ray.origin, d);
            if (t < 0f || t > maxDistance) return false;

            var pointOnRay = ray.origin + d * t;
            if ((pointOnRay - pointOnAxis).sqrMagnitude > radius * radius) return false;

            distance = t;
            return true;
        }

        /// <summary>
        /// 重新收集命中目标。序列化数组非空时以它为准（允许手工精确指定），
        /// 否则查找场景中所有 <see cref="PetHitTarget"/>（不包含未激活对象）。
        /// </summary>
        private void RefreshHitTargets()
        {
            _nextRefreshTime = Time.time + Mathf.Max(0.25f, hitTargetRefreshSeconds);

            if (hitTargets != null && hitTargets.Length > 0)
            {
                _cachedTargets = hitTargets;
                return;
            }

            _cachedTargets = FindObjectsByType<PetHitTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }
    }
}
