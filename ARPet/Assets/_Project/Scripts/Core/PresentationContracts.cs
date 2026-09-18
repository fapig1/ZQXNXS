using System;
using UnityEngine;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 表情等级。用于二维表情图与骨骼表情（眼睑 / 嘴）的组合控制。
    /// 名称按"游戏内可观察状态"命名，不绑定具体贴图文件名。
    /// </summary>
    public enum PetExpression
    {
        Neutral = 0,
        Happy = 1,
        VeryHappy = 2,
        Sleepy = 3,
        Hungry = 4,
        Surprised = 5,
        Refuse = 6,
    }

    /// <summary>
    /// 追踪状态在业务层的四态提示：识别中 / 有效 / 丢失 / 恢复。
    /// 由 <c>TrackingStateMachine</c> 从 <see cref="TrackingStatus"/> 归并而来，
    /// 界面只按这四态给提示，不直接看底层枚举。
    /// </summary>
    public enum TrackingUiState
    {
        /// <summary>尚未建立跟踪。</summary>
        Searching = 0,

        /// <summary>跟踪有效。</summary>
        Tracking = 1,

        /// <summary>跟踪丢失，暂停新交互并引导重新对准。</summary>
        Lost = 2,

        /// <summary>刚刚从丢失中恢复。需要提示并做一次"恢复而不重置交互"的处理。</summary>
        Recovered = 3,
    }

    /// <summary>
    /// UI 可读取的应用状态。接口放在 Core，避免 UI 反向依赖 App 装配层。
    /// 实现方只暴露快照，不允许界面直接修改业务状态。
    /// </summary>
    public interface IAppUiState
    {
        TrackingUiState TrackingState { get; }
        PetState PetSnapshot { get; }
        PetBehaviorId CurrentBehavior { get; }
        bool IsReady { get; }
        string TrackingPauseReason { get; }
    }

    /// <summary>
    /// 小窝局部坐标系下的虚拟平面定义。
    /// 追球等玩法只在这个平面内进行，<b>不代表系统扫描了真实桌面</b>。
    /// </summary>
    [Serializable]
    public struct PlayPlane
    {
        /// <summary>平面相对小窝卡原点的局部位移（米）。</summary>
        public Vector3 LocalOriginOffset;

        /// <summary>玩法区域半径（米）。超出范围的目标不参与追球。</summary>
        public float Radius;

        public static PlayPlane Default => new PlayPlane
        {
            LocalOriginOffset = new Vector3(0f, 0.02f, 0f),
            Radius = 0.15f,
        };

        public bool Contains(Vector3 localPoint) =>
            Vector3.Distance(localPoint, LocalOriginOffset) <= Radius;
    }
}
