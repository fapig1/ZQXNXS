using UnityEngine;
using UnityEngine.EventSystems;

namespace ZQXNXS.ARPet.App
{
    /// <summary>
    /// 触屏输入入口：把 Unity 的触屏 / 鼠标事件转换成 <see cref="AppRoot"/> 需要的
    /// 屏幕坐标与"是否命中角色"，别的地方都不直接读 <c>Input</c>。
    ///
    /// 命名纪律与 <c>TouchPokeRule</c> 一致：这是<b>触屏输入</b>，不是现实手部接触识别，
    /// 界面文案与报告不能称为"识别到真实手部接触"。
    ///
    /// 命中判定用一条从摄像机出发的射线与角色可交互区域求交，而不是 UI 事件系统的
    /// 屏幕矩形碰撞，因为角色是 3D AR 内容、不是 Canvas 元素。是否落在 UI 上则反过来
    /// 交给 <see cref="EventSystem.IsPointerOverGameObject"/> 判断，两者不会互相依赖。
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
        [Tooltip("角色可交互区域的碰撞体。留空时命中判定按“角色锚点周围的球形半径”近似（见 hitRadiusMeters）。")]
        [SerializeField] private Collider petHitCollider;

        [Tooltip("没有挂碰撞体时，以此变换为球心的近似命中半径（米）。默认约等于奶蛙的身高，覆盖整只角色。")]
        [SerializeField] private Transform hitRadiusOrigin;

        [SerializeField] private float hitRadiusMeters = 0.12f;

        [Tooltip("射线检测的最远距离（米）。远大于桌面 AR 的常见使用距离即可。")]
        [SerializeField] private float maxRayDistance = 5f;

        /// <summary>是否命中角色可交互区域，用于测试与调试面板观察。</summary>
        public bool LastHitPet { get; private set; }

        private void Reset()
        {
            appRoot = FindFirstObjectByType<AppRoot>(FindObjectsInactive.Include);
        }

        private void Update()
        {
            if (appRoot == null) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            HandleTouches();
#else
            HandleMouse();
#endif
        }

        private void HandleMouse()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Begin(Input.mousePosition);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                End(Input.mousePosition);
            }
        }

        private void HandleTouches()
        {
            if (Input.touchCount == 0) return;
            var touch = Input.GetTouch(0);

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    Begin(touch.position);
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    End(touch.position);
                    break;
            }
        }

        private void Begin(Vector2 screenPosition)
        {
            var overUi = IsOverUi();
            appRoot.FeedTouchPointerDown(screenPosition, overUi);
        }

        private void End(Vector2 screenPosition)
        {
            LastHitPet = HitsPet(screenPosition);
            appRoot.FeedTouchPointerUp(screenPosition, LastHitPet);
        }

        /// <summary>
        /// 界面输入不得穿透到角色：任何挂了 <see cref="UiInputBlocker"/> 且当前生效的面板
        /// 都优先于角色命中判定。<c>EventSystem.IsPointerOverGameObject</c> 只在有 EventSystem
        /// 时才查询，场景里没有 UI（如模拟场景）时直接视为未覆盖。
        /// </summary>
        private static bool IsOverUi()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (Input.touchCount > 0)
            {
                return eventSystem.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            }
#endif
            return eventSystem.IsPointerOverGameObject();
        }

        /// <summary>
        /// 命中判定的纯逻辑部分，公开出来是为了能在 EditMode 测试里直接构造场景验证，
        /// 不需要真的产生一次 <c>Input</c> 事件。
        /// </summary>
        public bool HitsPet(Vector2 screenPosition)
        {
            var cam = raycastCamera != null ? raycastCamera : Camera.main;
            if (cam == null) return false;

            var ray = cam.ScreenPointToRay(screenPosition);

            if (petHitCollider != null)
            {
                return petHitCollider.Raycast(ray, out _, maxRayDistance);
            }

            if (hitRadiusOrigin == null) return false;

            // 没有碰撞体时，用射线到锚点的最近距离近似命中区域，
            // 避免要求每个宠物 Prefab 都必须先做碰撞体才能测试触屏逗弄。
            var toOrigin = hitRadiusOrigin.position - ray.origin;
            var projection = Vector3.Dot(toOrigin, ray.direction);
            if (projection < 0f || projection > maxRayDistance) return false;

            var closestPoint = ray.origin + ray.direction * projection;
            return Vector3.Distance(closestPoint, hitRadiusOrigin.position) <= hitRadiusMeters;
        }
    }
}
