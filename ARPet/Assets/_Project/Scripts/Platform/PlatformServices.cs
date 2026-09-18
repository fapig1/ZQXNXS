using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ZQXNXS.ARPet.Platform
{
    /// <summary>
    /// 摄像头权限。AR 应用最常见的"打开就是黑屏"原因就是权限被拒后没有引导。
    ///
    /// Android 使用运行时 Permission API 与回调；编辑器直接放行。
    /// 普通拒绝与不再询问均返回 false，同一次请求只完成一次。
    /// </summary>
    public static class CameraPermission
    {
        /// <summary>是否已获得摄像头权限。编辑器里直接视为已获得。</summary>
        public static bool HasPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
                return true;
#endif
            }
        }

        /// <summary>请求权限。结果通过回调返回，避免调用方写协程。</summary>
        public static void Request(System.Action<bool> onResult)
        {
            if (HasPermission)
            {
                onResult?.Invoke(true);
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            _pendingResults += onResult;
            if (_callbacks != null) return;

            _callbacks = new PermissionCallbacks();
            _callbacks.PermissionGranted += Granted;
            _callbacks.PermissionDenied += Denied;
            _callbacks.PermissionDeniedAndDontAskAgain += Denied;
            try
            {
                Permission.RequestUserPermission(Permission.Camera, _callbacks);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CameraPermission] 权限请求失败：{e.Message}");
                Complete(false);
            }
#else
            onResult?.Invoke(true);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static PermissionCallbacks _callbacks;
        private static System.Action<bool> _pendingResults;

        private static void Granted(string permission)
        {
            if (permission == Permission.Camera) Complete(true);
        }

        private static void Denied(string permission)
        {
            if (permission == Permission.Camera) Complete(false);
        }

        private static void Complete(bool granted)
        {
            if (_callbacks == null) return;
            _callbacks.PermissionGranted -= Granted;
            _callbacks.PermissionDenied -= Denied;
            _callbacks.PermissionDeniedAndDontAskAgain -= Denied;
            _callbacks = null;

            var results = _pendingResults;
            _pendingResults = null;
            if (!granted)
                Debug.LogWarning("[CameraPermission] 摄像头权限未获准，AR 已暂停。可在系统设置中开启权限。");
            results?.Invoke(granted);
        }
#endif
    }

    /// <summary>
    /// 应用生命周期：切后台时暂停跟踪并落盘，回到前台时重新请求跟踪。
    ///
    /// 为什么必须做：安卓上切后台后摄像头会被系统回收，回到前台时跟踪器状态已经失效。
    /// 如果不处理，玩家会看到角色停在过期位姿上，甚至依据旧位置继续计时。
    /// </summary>
    public sealed class AppLifecycle : MonoBehaviour
    {
        /// <summary>切后台时触发。参数为"需要落盘"的提示，由应用入口处理。</summary>
        public event System.Action<bool> FocusChanged;

        /// <summary>切后台的累计次数，供测试与报告记录。</summary>
        public int BackgroundCount { get; private set; }

        /// <summary>当前是否在前台。</summary>
        public bool IsFocused { get; private set; } = true;

        /// <summary>应用是否被系统要求退出（用于强制保存）。</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            IsFocused = hasFocus;
            if (!hasFocus) BackgroundCount++;

            // 拿到焦点说明回到前台，需要重新开始跟踪；失去焦点要暂停并保存。
            FocusChanged?.Invoke(hasFocus);
            Debug.Log($"[AppLifecycle] focus={(hasFocus ? "前台" : "后台")}，累计切后台 {BackgroundCount} 次");
        }

        private void OnApplicationPause(bool paused)
        {
            // 安卓上 Pause(true) 与 Focus(false) 都会到达，二者语义重叠，
            // 因此这里只用于保存，不重复改变跟踪状态。
            if (paused) FocusChanged?.Invoke(false);
        }

        private void OnApplicationQuit()
        {
            // 退出前给应用入口最后一次落盘机会。
            FocusChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// 拍照留念的平台侧出口。
    ///
    /// 当前状态：<b>尚未实现</b>。合成截图（现实背景 + 角色）与写入系统相册
    /// 需要真机验证 Scoped Storage 行为，因此这里先只保留接口与失败提示，
    /// 不假装已经能保存到相册。
    /// </summary>
    public static class PhotoCapture
    {
        /// <summary>最近一次失败原因。</summary>
        public static string LastError { get; private set; } = "尚未实现";

        /// <summary>拍照结果是否已经写入系统相册。</summary>
        public static bool CanWriteToGallery => false;

        /// <summary>
        /// 截取当前画面并保存。调用方应把结果如实告知用户（包括"仅保存在应用目录"）。
        /// </summary>
        public static bool TryCapture(string fileName, out string savedPath)
        {
            savedPath = string.Empty;

            // 合成截图需要同时包含摄像头背景与 AR 内容，这一步依赖具体跟踪 SDK 的
            // 背景渲染方式（Vuforia 的摄像头背景不在普通相机渲染流程里），
            // 必须等 SDK 接入并在真机上验证后再实现。
            LastError = "拍照功能待接入：需要跟踪 SDK + 真机验证相册写入";
            return false;
        }

        /// <summary>触发一次系统分享。首版不做，保留占位以免上层散落平台判断。</summary>
        public static void Share(string path)
        {
            LastError = "分享功能不属于首版范围";
        }
    }
}
