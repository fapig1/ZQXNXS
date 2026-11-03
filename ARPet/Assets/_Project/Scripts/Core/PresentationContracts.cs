using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 满值专属演出的招牌表情。具体眼/嘴由表现层按形象决定
        /// （classic 眯眼微笑、calm 闭眼舒展、rage 怒眼怒吼、divine 白眼舒展），行为层不需要知道形象。
        /// </summary>
        Signature = 7,

        /// <summary>怒吼：怒眼 + 怒吼嘴（嘴角不上扬）。四色统一，用于"欧拉"语音回应等场合。</summary>
        Roar = 8,
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
    /// 单只宠物的界面快照。多宠同屏时界面按 <see cref="IAppUiState.Pets"/> 逐行展示，
    /// 只暴露稳定 ID 与只读数值，不暴露任何场景对象或运行时引用。
    /// </summary>
    [Serializable]
    public struct PetUiSnapshot
    {
        /// <summary>宠物稳定 ID，如 <c>pet_classic</c>。</summary>
        public string PetId;

        /// <summary>形象变体（暖黄 / 湖蓝 / 朱红 / 紫）。</summary>
        public PetVariantId Variant;

        /// <summary>该宠独立的跟踪界面状态。</summary>
        public TrackingUiState Tracking;

        /// <summary>该宠的数值快照（新版只有开心值参与运行）。</summary>
        public PetState State;

        /// <summary>该宠当前行为。</summary>
        public PetBehaviorId Behavior;
    }

    /// <summary>
    /// UI 可读取的应用状态。接口放在 Core，避免 UI 反向依赖 App 装配层。
    /// 实现方只暴露快照，不允许界面直接修改业务状态。
    ///
    /// 多宠语义：<see cref="Pets"/> 是全部可用宠物的逐行快照；
    /// <see cref="TrackingState"/> / <see cref="PetSnapshot"/> / <see cref="CurrentBehavior"/>
    /// 三个旧属性保留，但含义收窄为"<b>选中宠物</b>"的对应值，
    /// 这样原来的单宠界面不需要同时改三处调用点。
    /// </summary>
    public interface IAppUiState
    {
        /// <summary>选中宠物的跟踪状态。</summary>
        TrackingUiState TrackingState { get; }

        /// <summary>选中宠物的数值快照。</summary>
        PetState PetSnapshot { get; }

        /// <summary>选中宠物的当前行为。</summary>
        PetBehaviorId CurrentBehavior { get; }

        /// <summary>全部可用宠物的逐行快照，顺序稳定。实现方应复用列表，避免每帧分配。</summary>
        IReadOnlyList<PetUiSnapshot> Pets { get; }

        /// <summary>当前选中的宠物稳定 ID。没有可用宠物时为空字符串。</summary>
        string SelectedPetId { get; }

        bool IsReady { get; }

        string TrackingPauseReason { get; }
    }

    /// <summary>语音识别的状态（界面按它显示麦克风按钮的提示）。</summary>
    public enum VoiceListenState
    {
        /// <summary>识别不可用：模型/原生库缺失、平台不支持或初始化失败。</summary>
        Unavailable = 0,

        /// <summary>已就绪，等待按住麦克风。</summary>
        Idle = 1,

        /// <summary>正在请求麦克风权限。</summary>
        RequestingPermission = 2,

        /// <summary>麦克风权限被拒绝。</summary>
        PermissionDenied = 3,

        /// <summary>按住中，正在聆听。</summary>
        Listening = 4,
    }

    /// <summary>
    /// 离线关键词识别的抽象（Platform 实现，App 装配）。只负责"听到了哪句口令"，
    /// 不知道宠物、不结算任何数值：命中结果由 App 转成每只在场宠物的语音事件。
    /// </summary>
    public interface IVoiceCommandSource
    {
        VoiceListenState State { get; }

        /// <summary>不可用或被拒时的原因（界面直接显示）。</summary>
        string StatusText { get; }

        /// <summary>开始聆听（按下麦克风按钮）。权限未获准时先请求。</summary>
        void BeginListening();

        /// <summary>
        /// 停止聆听（松开按钮）。会再给识别器约 0.3 秒尾音，命中结果仍可能在本帧之后到达。
        /// </summary>
        void EndListening();

        /// <summary>
        /// 取出自上次调用以来识别到的口令原文（如"安迪"），没有则返回 false。
        /// 识别在后台线程进行，结果在主线程由调用方每帧拉取。
        /// </summary>
        bool TryDequeue(out string phrase);
    }

    /// <summary>帧率来源（Platform 的性能记录实现），界面只读不写。</summary>
    public interface IFrameRateSource
    {
        /// <summary>最近一个完整统计窗口的平均帧率；还没有完整窗口时为 0。</summary>
        float CurrentFps { get; }
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
