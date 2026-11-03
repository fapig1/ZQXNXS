using System;
using UnityEngine;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 交互事件的种类。所有事件都由交互判定层产生一次，行为层只负责决定接不接受。
    /// </summary>
    public enum InteractionEventKind
    {
        // 旧值 Feed = 0（食物卡投喂）已随食物玩法删除。以下保留原数值，旧存档按数值读取时不会错位。

        /// <summary>触屏逗弄：点击或滑动角色可交互区域。<b>这是触屏输入，不是现实手部接触识别。</b></summary>
        Pet = 1,

        /// <summary>追球：小球投入小窝定义的虚拟平面区域内。</summary>
        Play = 2,

        /// <summary>拍照留念。</summary>
        Photo = 3,

        /// <summary>
        /// 语音口令：用户按住麦克风说出一句口令，被离线关键词识别命中。
        /// 每只在场宠物各收到一个事件（各自的 Id 流），口令 ID 放在 <see cref="InteractionEvent.CommandId"/>。
        /// </summary>
        Voice = 4,
    }

    /// <summary>
    /// 触屏手势种类。决定单击 / 双击各自结算多少开心值（数值在 PetConfig 的变体调参表里）。
    /// <b>默认值必须是 <see cref="SingleTap"/> = 0</b>：旧代码与旧测试构造的事件没有该字段，
    /// 读成单击才符合"一次点按"的原意。
    /// </summary>
    public enum PetGesture
    {
        SingleTap = 0,
        DoubleTap = 1,
    }

    /// <summary>
    /// 一次已经过判定、可以交给行为层的交互事件。
    /// 每个事件由 (PetId, Kind, Id) 唯一标识，行为层据此保证同一次进入只结算一次；
    /// 不同宠物的同名事件流互不吞并。
    /// </summary>
    [Serializable]
    public struct InteractionEvent
    {
        /// <summary>在同一个 (PetId, Kind) 事件流中递增的序号，用于去重与日志追踪。</summary>
        public int Id;

        public InteractionEventKind Kind;

        /// <summary>事件归属的宠物稳定 ID（如 <c>pet_classic</c>）。空值属于错误配置，不应产生事件。</summary>
        public string PetId;

        /// <summary>事件发生时刻（<c>Time.time</c> 或跟踪源的时钟，二者保持一致）。</summary>
        public float Time;

        /// <summary>事件发生位置，世界米制坐标。触屏事件可填点击射线与角色交互区域的交点。</summary>
        public Vector3 LocalPoint;

        /// <summary>判定距离（米），仅用于调试追踪；不再有投喂判定，普通事件为 0。</summary>
        public float Distance;

        /// <summary>
        /// 触屏手势种类（单击 / 双击）。非触屏事件忽略该字段；
        /// 缺省值是单击，保证旧代码构造的事件不会被当成双击多结算一次。
        /// </summary>
        public PetGesture Gesture;

        /// <summary>语音口令的稳定 ID（如 <c>andi</c>），只对 <see cref="InteractionEventKind.Voice"/> 有意义。</summary>
        public string CommandId;

        public static InteractionEvent Create(int id, InteractionEventKind kind, string petId, float time,
            Vector3 localPoint = default, float distance = 0f, PetGesture gesture = PetGesture.SingleTap) => new InteractionEvent
        {
            Id = id,
            Kind = kind,
            PetId = petId,
            Time = time,
            LocalPoint = localPoint,
            Distance = distance,
            Gesture = gesture,
        };

        /// <summary>构造一个语音口令事件。</summary>
        public static InteractionEvent CreateVoice(int id, string petId, float time, string commandId) => new InteractionEvent
        {
            Id = id,
            Kind = InteractionEventKind.Voice,
            PetId = petId,
            Time = time,
            CommandId = commandId,
        };

        public override string ToString() => Kind == InteractionEventKind.Voice
            ? $"#{Id} Voice[{CommandId}] for {PetId} @t={Time:F2}"
            : $"#{Id} {Kind} for {PetId} @t={Time:F2} d={Distance:F3}m g={Gesture}";
    }

    /// <summary>
    /// 事件接收方。行为状态机实现它；测试与调试面板也可以实现它以做记录。
    /// 返回值表示行为层是否**接受**该事件，调用方据此决定是否播放"拒绝"反馈。
    /// </summary>
    public interface IInteractionEventSink
    {
        bool TrySubmit(in InteractionEvent interactionEvent);
    }

    /// <summary>
    /// 表现（动画 / 表情 / 音效 / 特效）的抽象。
    /// 行为层只发出指令，不关心 Animator、贴图或 AudioSource 的具体实现。
    /// </summary>
    public interface IPetPresenter
    {
        /// <summary>切换行为状态。animationName 已由行为配置解析，表现层无需依赖 PetConfig。</summary>
        void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds);

        /// <summary>表情等级。对应二维表情与骨骼表情的组合。</summary>
        void SetExpression(PetExpression expression, float intensity);

        /// <summary>播放一个短音效事件。音效属于后续表现增强项，未接入时应安全忽略。</summary>
        void PlaySfx(string sfxId);

        /// <summary>播放一次性特效。</summary>
        void PlayFx(string fxId);

        /// <summary>当前动画的播放速度。新版运行固定 1 倍速，不再按困倦放慢。</summary>
        void SetPlaybackSpeed(float speed);

        /// <summary>是否允许显示可交互高亮。</summary>
        void SetInteractable(bool interactable);

        /// <summary>跟踪丢失时的显示策略：保留最后姿态、隐藏，或显示提示。</summary>
        void SetTrackingVisibility(bool visible);
    }

    /// <summary>
    /// 行为状态机需要知道的状态数值接口。
    /// 由 <c>PetNeeds</c> 实现：状态机读数值做决策，数值变化通过 <see cref="Apply"/> 回流。
    /// </summary>
    public interface IPetStateProvider
    {
        float Get(PetNeed need);
        PetState Snapshot { get; }

        /// <summary>应用一次结算的数值增量。返回是否真的发生了变化。</summary>
        bool Apply(PetNeed need, float delta, float now);

        /// <summary>
        /// 写入开心值满值周期的可保存字段（周期状态、专属冷却剩余、开始 / 看完次数）。
        /// 由行为状态机在周期迁移与专属计时变化时调用，保证存档快照只有一份来源。
        /// </summary>
        /// <param name="markDirty">
        /// 周期迁移与计数变化必须为 <c>true</c>（要落盘）；
        /// 冷却每帧递减本身不必每次打脏标记，传 <c>false</c>，值仍会随下一次写盘一起保存。
        /// </param>
        void StoreHappyCycle(HappyCycleState state, float cooldownRemaining, int startCount, int watchedCount,
            bool markDirty);
    }
}
