using System.Collections.Generic;
using UnityEngine;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 跟踪层对外唯一出口。上层模块只通过这个接口拿位姿与状态，
    /// 因此业务代码不依赖 Vuforia 的具体类型，换 SDK 或做模拟源都不需要改行为层。
    ///
    /// 目标用稳定字符串 ID（<see cref="TargetId"/>）索引：每个角色卡对应一只奶蛙，
    /// 身份到目标的映射见 <see cref="PetIdentityCatalog"/>。
    /// </summary>
    public interface ITrackingSource
    {
        /// <summary>整个跟踪系统是否已启动（已初始化且摄像头可用）。</summary>
        bool IsRunning { get; }

        /// <summary>跟踪暂停的原因：权限被拒、切后台、用户关闭等。用于界面提示。</summary>
        string PauseReason { get; }

        /// <summary>已注册的目标 ID，顺序稳定。未注册的 ID 不会出现在这里。</summary>
        IReadOnlyList<string> TargetIds { get; }

        /// <summary>
        /// 读取某个目标的最新采样。<b>不保证位姿新鲜</b>，请用 <see cref="PoseFreshness"/> 判断。
        /// 未注册的目标返回 <see cref="TrackingStatus.Lost"/> 的空采样，不抛异常。
        /// </summary>
        PoseSample GetSample(string targetId);

        /// <summary>所有已注册目标的采样快照，按 <see cref="TargetIds"/> 顺序返回。</summary>
        PoseSample[] AllSamples { get; }

        /// <summary>启动 / 恢复跟踪。</summary>
        void StartTracking();

        /// <summary>暂停跟踪（切后台、权限被拒等）。暂停不产生"丢失"事件，也不结算交互。</summary>
        void PauseTracking(string reason);
    }

    /// <summary>
    /// 标记注册表：把稳定的目标 ID 与跟踪器里的目标对象绑定起来。
    /// 由 AR 层实现（Vuforia 版或模拟版），交互层只按目标 ID 查询。
    /// 所有目标共用同一世界米制坐标系，接近判定直接用世界位姿。
    /// </summary>
    public interface IMarkerRegistry
    {
        /// <summary>该目标 ID 是否已经配置了跟踪目标。</summary>
        bool IsRegistered(string targetId);

        /// <summary>目标在物理世界中的有效尺寸（米，x=宽、z=高）。必须对应实际印刷尺寸，不能填纸张外框。</summary>
        Vector3 GetTargetSize(string targetId);

        /// <summary>该目标位姿是否有效且未过期。</summary>
        bool IsPoseValid(string targetId, float now);

        /// <summary>目标当前的世界位姿（米制）。未跟踪、未注册或已过期时返回 <c>false</c>。</summary>
        bool TryGetTargetPose(string targetId, out Vector3 worldPosition, out Quaternion worldRotation);
    }
}
