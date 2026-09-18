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
    /// <item>恢复<b>不</b>重置交互判定里的"已结算"状态，否则同一张食物卡会被重新计一次投喂；</item>
    /// <item>暂停（切后台、权限被拒）与丢失分开，界面提示文案不同。</item>
    /// </list>
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

        /// <summary>丢失次数统计，供测试与调试观察。</summary>
        public int LostCount { get; private set; }

        /// <summary>恢复次数统计。</summary>
        public int RecoveredCount { get; private set; }

        /// <summary>
        /// 每帧推进。小窝卡是唯一的定位卡，因此这里只看小窝卡的状态；
        /// 食物卡的有效性由投喂判定单独处理。
        /// </summary>
        public TrackingUiState Tick(in PoseSample den, float now,
            float staleSeconds = PoseFreshness.DefaultStaleSeconds)
        {
            var valid = PoseFreshness.IsSettleable(den, now, staleSeconds);
            CanInteract = valid;

            if (den.Status == TrackingStatus.Paused)
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
