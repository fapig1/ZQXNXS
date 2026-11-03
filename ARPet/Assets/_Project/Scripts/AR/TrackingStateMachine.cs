using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.AR
{
    /// <summary>
    /// 把底层的 <see cref="TrackingStatus"/> 归并成界面的四态：
    /// 识别中 / 有效 / 丢失 / 恢复。
    ///
    /// 关键行为：
    /// <list type="bullet">
    /// <item>进入 <see cref="TrackingUiState.Recovered"/> 只保持很短一段时间用于提示，随后自动回到 Tracking；</item>
    /// <item>恢复<b>不</b>重置交互判定里的"已结算"状态，否则同一次进入会被重新结算一次；</item>
    /// <item>暂停（切后台、权限被拒）与丢失分开，界面提示文案不同。</item>
    /// </list>
    /// 本切片只跟踪一只宠物绑定的目标卡（<paramref name="target"/>）；多宠切片会对每只宠物各持一个实例。
    /// </summary>
    public sealed class TrackingStateMachine
    {
        private readonly float _recoverHoldSeconds;
        private readonly float _lostDebounceSeconds;

        private float _lastValidTime = float.NegativeInfinity;
        private float _recoveredAt = float.NegativeInfinity;

        public TrackingStateMachine(float recoverHoldSeconds = 1.2f, float lostDebounceSeconds = 0.25f)
        {
            _recoverHoldSeconds = recoverHoldSeconds;
            _lostDebounceSeconds = lostDebounceSeconds;
        }

        /// <summary>当前界面状态。</summary>
        public TrackingUiState State { get; private set; } = TrackingUiState.Searching;

        /// <summary>状态最近一次发生变化的时刻。</summary>
        public float StateChangedAt { get; private set; }

        /// <summary>是否曾经成功跟踪过（用于区分"首次识别中"与"丢失后恢复"）。</summary>
        public bool EverTracked { get; private set; }

        /// <summary>本帧能否交互。无效采样立即关闭，不等待界面丢失提示的去抖。</summary>
        public bool CanInteract { get; private set; }

        /// <summary>
        /// 本帧角色能否显示。比 <see cref="CanInteract"/> 宽一档：<see cref="TrackingStatus.Limited"/>
        /// （卡仍在画面里，只是倾斜 / 偏远 / 模糊）且位姿新鲜时照常显示，但不开放交互；
        /// 丢失、延伸跟踪（卡已不可见）、暂停或过期位姿一律隐藏。
        /// 这样跟踪质量在 TRACKED / LIMITED 间抖动时角色不会闪烁。
        /// </summary>
        public bool CanShow { get; private set; }

        /// <summary>丢失次数统计，供测试与调试观察。</summary>
        public int LostCount { get; private set; }

        /// <summary>恢复次数统计。</summary>
        public int RecoveredCount { get; private set; }

        /// <summary>
        /// 每帧推进。只看这只宠物绑定的目标卡状态；
        /// 其他宠物的目标由各自的实例分别处理。
        /// </summary>
        public TrackingUiState Tick(in PoseSample target, float now,
            float staleSeconds = PoseFreshness.DefaultStaleSeconds)
        {
            var valid = PoseFreshness.IsSettleable(target, now, staleSeconds);
            CanInteract = valid;
            CanShow = valid || (target.Status == TrackingStatus.Limited && !PoseFreshness.IsStale(target, now, staleSeconds));

            if (target.Status == TrackingStatus.Paused)
            {
                return Transition(TrackingUiState.Lost, now);
            }

            if (valid)
            {
                _lastValidTime = now;

                if (State == TrackingUiState.Lost || State == TrackingUiState.Searching)
                {
                    if (EverTracked)
                    {
                        RecoveredCount++;
                        _recoveredAt = now;
                        return Transition(TrackingUiState.Recovered, now);
                    }

                    EverTracked = true;
                    return Transition(TrackingUiState.Tracking, now);
                }

                if (State == TrackingUiState.Recovered && now - _recoveredAt > _recoverHoldSeconds)
                {
                    return Transition(TrackingUiState.Tracking, now);
                }

                return State;
            }

            // 无效：做一点去抖，避免单帧抖动就闪一下"丢失"提示。
            // 从最后一次有效采样计时，不能每帧移动去抖起点。
            // 从未识别成功时保持 Searching，不把初始化误报为丢失。
            if (EverTracked && now - _lastValidTime >= _lostDebounceSeconds)
            {
                return Transition(TrackingUiState.Lost, now);
            }

            return State;
        }

        /// <summary>应用启动或场景重载时归零。</summary>
        public void Reset()
        {
            State = TrackingUiState.Searching;
            StateChangedAt = 0f;
            EverTracked = false;
            CanInteract = false;
            CanShow = false;
            LostCount = 0;
            RecoveredCount = 0;
            _lastValidTime = float.NegativeInfinity;
            _recoveredAt = float.NegativeInfinity;
        }

        private TrackingUiState Transition(TrackingUiState next, float now)
        {
            if (next == State) return State;
            if (next == TrackingUiState.Lost && EverTracked) LostCount++;
            State = next;
            StateChangedAt = now;
            return State;
        }
    }
}
