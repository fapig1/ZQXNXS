using System;
using UnityEngine;

namespace ZQXNXS.ARPet.Core
{
    /// <summary>
    /// 交互事件的种类。所有事件都由交互判定层产生一次，行为层只负责决定接不接受。
    /// </summary>
    public enum InteractionEventKind
    {
        /// <summary>实体投喂：小窝卡与食物卡同时有效，且满足距离、高度、停留与冷却条件。</summary>
        Feed = 0,

        /// <summary>触屏逗弄：点击或滑动角色可交互区域。<b>这是触屏输入，不是现实手部接触识别。</b></summary>
        Pet = 1,

        /// <summary>追球：小球投入小窝定义的虚拟平面区域内。</summary>
        Play = 2,

        /// <summary>拍照留念。</summary>
        Photo = 3,
    }

    /// <summary>
    /// 一次已经过判定、可以交给行为层的交互事件。
    /// 每个事件由 (Kind, Source, Id) 唯一标识，行为层据此保证同一次进入只结算一次。
    /// </summary>
    [Serializable]
    public struct InteractionEvent
    {
        /// <summary>在同一个 (Kind, Source) 事件流中递增的序号，用于去重与日志追踪。</summary>
        public int Id;

        public InteractionEventKind Kind;
        public MarkerSlot Source;

        /// <summary>事件发生时刻（<c>Time.time</c> 或跟踪源的时钟，二者保持一致）。</summary>
        public float Time;

        /// <summary>小窝局部坐标下的位置，米制。触屏事件可填点击射线与虚拟平面的交点。</summary>
        public Vector3 LocalPoint;

        /// <summary>投喂时的判定距离（米），其他事件为 0。</summary>
        public float Distance;

        public static InteractionEvent Create(int id, InteractionEventKind kind, MarkerSlot source, float time,
            Vector3 localPoint = default, float distance = 0f) => new InteractionEvent
        {
            Id = id,
            Kind = kind,
            Source = source,
            Time = time,
            LocalPoint = localPoint,
            Distance = distance,
        };

        public override string ToString() =>
            $"#{Id} {Kind} from {Source} @t={Time:F2} d={Distance:F3}m";
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

        /// <summary>当前动画的播放速度。调用方只在待机时放慢，单次进食维持 1 倍速。</summary>
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
    }
}
