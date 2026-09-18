using UnityEngine;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 跟踪层对外唯一出口。上层模块只通过这个接口拿位姿与状态，
    /// 因此业务代码不依赖 Vuforia 的具体类型，换 SDK 或做模拟源都不需要改行为层。
    /// </summary>
    public interface ITrackingSource
    {
        /// <summary>整个跟踪系统是否已启动（已初始化且摄像头可用）。</summary>
        bool IsRunning { get; }

        /// <summary>跟踪暂停的原因：权限被拒、切后台、用户关闭等。用于界面提示。</summary>
        string PauseReason { get; }

        /// <summary>读取某个槽位的最新采样。<b>不保证位姿新鲜</b>，请用 <see cref="PoseFreshness"/> 判断。</summary>
        PoseSample GetSample(MarkerSlot marker);

        /// <summary>当前所有槽位的采样快照。</summary>
        PoseSample[] AllMarkers { get; }

        /// <summary>启动 / 恢复跟踪。</summary>
        void StartTracking();

        /// <summary>暂停跟踪（切后台、权限被拒等）。暂停不产生"丢失"事件，也不结算交互。</summary>
        void PauseTracking(string reason);
    }

    /// <summary>
    /// 标记注册表：把"槽位枚举"与"跟踪器里的目标对象"绑定起来。
    /// 由 AR 层实现（Vuforia 版或模拟版），交互层只按槽位查询。
    /// </summary>
    public interface IMarkerRegistry
    {
        /// <summary>该槽位是否已经配置了跟踪目标。</summary>
        bool IsRegistered(MarkerSlot marker);

        /// <summary>目标在物理世界中的有效尺寸（米）。必须对应实际印刷尺寸，不能填纸张外框。</summary>
        Vector3 GetTargetSize(MarkerSlot marker);

        /// <summary>该槽位位姿是否有效且未过期。</summary>
        bool IsPoseValid(MarkerSlot marker, float now);

        /// <summary>把世界坐标转换到小窝卡的局部坐标系（米制）。</summary>
        Vector3 ToDenLocal(MarkerSlot marker, Vector3 worldPosition, bool useRotation = true);

        /// <summary>小窝卡当前的世界位姿。未跟踪时返回 <c>false</c>。</summary>
        bool TryGetDenPose(out Vector3 worldPosition, out Quaternion worldRotation);
    }
}
