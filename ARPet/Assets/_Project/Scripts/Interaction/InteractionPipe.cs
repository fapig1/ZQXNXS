using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Interaction
{
    /// <summary>
    /// 交互判定层的总闸门：收集"谁想提交事件"，统一做可用性检查，再交给行为层。
    ///
    /// 它承担两条项目纪律：
    /// <list type="number">
    /// <item><b>跟踪暂停期间不产生新事件</b>：<see cref="ArAvailable"/> 为 false 时直接丢弃，
    ///       而不是排队等恢复后补发（否则会出现"重新对准后宠物凭空吃了一口"）。</item>
    /// <item><b>一次进入只结算一次</b>：事件自带唯一 Id，行为状态机按 Id 去重。</item>
    /// </list>
    /// </summary>
    public sealed class InteractionPipe
    {
        private readonly IInteractionEventSink _sink;

        /// <summary>最近被丢弃的事件数量，用于调试。</summary>
        private int _droppedWhileUnavailable;

        public InteractionPipe(IInteractionEventSink sink)
        {
            _sink = sink;
        }

        /// <summary>AR 空间交互是否可用。跟踪丢失 / 暂停 / 摄像头权限被拒时为 false。</summary>
        public bool ArAvailable { get; set; } = true;

        /// <summary>被丢弃的事件数（跟踪不可用期间）。</summary>
        public int DroppedWhileUnavailable => _droppedWhileUnavailable;

        /// <summary>提交一个事件。返回行为层是否接受。</summary>
        public bool Submit(in InteractionEvent interactionEvent)
        {
            if (_sink == null) return false;

            if (!ArAvailable)
            {
                // 触屏事件同样受影响：角色不在场时点击不应该被记录成一次互动。
                _droppedWhileUnavailable++;
                return false;
            }

            return _sink.TrySubmit(interactionEvent);
        }
    }

    /// <summary>
    /// 只做记录、不做决策的事件接收器。
    /// 用于"表现与托管"阶段先观察事件流，或在没有行为层时保持界面可用。
    /// </summary>
    public sealed class RecordingEventSink : IInteractionEventSink
    {
        private readonly List<InteractionEvent> _events = new();

        public IReadOnlyList<InteractionEvent> Events => _events;

        public bool TrySubmit(in InteractionEvent interactionEvent)
        {
            _events.Add(interactionEvent);
            return true;
        }

        public void Clear() => _events.Clear();
    }
}
