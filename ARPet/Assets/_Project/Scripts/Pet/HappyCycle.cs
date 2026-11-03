using System;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Pet
{
    /// <summary>
    /// 一次点按在满值周期里的决策结果。<b>纯数据、无副作用</b>：
    /// 决策由 <see cref="HappyCycle.DecideTap"/> 算出，真正改数值 / 改周期状态由行为状态机提交，
    /// 这样"接受还是拒绝"可以先判断，失败时不会留下半截状态。
    /// </summary>
    public readonly struct TapDecision
    {
        public TapDecision(float gain, bool startExclusive, bool setPendingAuto)
        {
            Gain = gain;
            StartExclusive = startExclusive;
            SetPendingAuto = setPendingAuto;
        }

        /// <summary>本次要结算的开心值增量（0 表示不加分）。</summary>
        public float Gain { get; }

        /// <summary>本次点按直接开始满值专属演出（跨越满值且无冷却，或玩家确认）。</summary>
        public bool StartExclusive { get; }

        /// <summary>本次点按把周期从 Armed 挂起到 PendingAuto（冷却结束后自动开始）。</summary>
        public bool SetPendingAuto { get; }

        public bool AddsGain => Gain > 0f;
    }

    /// <summary>
    /// 开心值满值周期。四色共用同一套流程，差异全部来自 <see cref="PetConfig"/> 的变体调参表。
    ///
    /// 状态迁移（<see cref="HappyCycleState"/>）：
    /// <code>
    /// Armed ──跨越满值且无冷却（或已满值时点按）──→ 开始专属 ──→ Released ──(回落值 ≤ 阈值)──→ Armed
    /// Armed ──跨越满值但冷却未完──────────────→ PendingAuto ──冷却到点且回到默认行为──→ 开始专属 → Released → Armed
    /// 读档时已满值（Armed / PendingAuto）──────→ PendingConfirm ──玩家点一下确认且无冷却──→ 开始专属 → Released → Armed
    /// </code>
    ///
    /// 三条纪律：
    /// <list type="bullet">
    /// <item>本类<b>不</b>直接改开心值：加分与回落都由行为状态机在同一个调用里结算一次。</item>
    /// <item>冷却只随行为时钟递减：跟踪丢失时调用方不推进行为时钟，冷却随之冻结。</item>
    /// <item>读档不自动开始：已满值只变成"待确认"，不会在读档瞬间花掉一次专属演出。</item>
    /// </list>
    /// </summary>
    public sealed class HappyCycle
    {
        /// <summary>开心值满值判定阈值。跨越判定用"加分前 &lt; 1、加分后 ≥ 1"，不做每帧浮点相等检测。</summary>
        public const float FullHappiness = 1f;

        public HappyCycle(PetConfig config, PetVariantId variant)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            Variant = variant;
            // 未知变体或表里缺条目时，TryGetVariantTuning 会填好 classic 的数值并只警告一次。
            config.TryGetVariantTuning(variant, out var tuning);
            Tuning = Sanitise(tuning, variant);
            State = HappyCycleState.Armed;
        }

        /// <summary>该实例所属的形象变体。</summary>
        public PetVariantId Variant { get; }

        /// <summary>已校验的调参条目（回落值不会高于重新武装阈值）。</summary>
        public VariantTuning Tuning { get; }

        /// <summary>当前满值周期状态。</summary>
        public HappyCycleState State { get; private set; }

        /// <summary>专属演出冷却剩余秒数（只随行为时钟递减）。</summary>
        public float CooldownRemaining { get; private set; }

        /// <summary>"开始过"专属演出的次数。</summary>
        public int Starts { get; private set; }

        /// <summary>"完整看完"专属演出的次数（被打断不计）。</summary>
        public int Completions { get; private set; }

        /// <summary>满值演出播完后的回落值。</summary>
        public float FallbackHappiness => Tuning.FallbackHappiness;

        /// <summary>重新武装阈值。</summary>
        public float RearmThreshold => Tuning.RearmThreshold;

        /// <summary>满值冷却总长（秒）。</summary>
        public float CooldownSeconds => Tuning.ExclusiveCooldownSeconds;

        /// <summary>该变体的专属演出 Animator 状态名。</summary>
        public string ExclusiveStateName => Tuning.ExclusiveStateName;

        /// <summary>该变体的专属演出时长（秒）。</summary>
        public float ExclusiveDurationSeconds => Tuning.ExclusiveDurationSeconds;

        /// <summary>冷却已结束且确实有一次待自动开始的演出。</summary>
        public bool IsAutoStartReady => State == HappyCycleState.PendingAuto && CooldownRemaining <= 0f;

        /// <summary>按手势取该变体的加分值。双击只有这一份，不会叠加单击。</summary>
        public float GainFor(PetGesture gesture) =>
            gesture == PetGesture.DoubleTap ? Tuning.DoubleTapGain : Tuning.SingleTapGain;

        /// <summary>
        /// 计算一次被接受的触屏事件要做什么，<b>不修改任何状态</b>。
        /// </summary>
        /// <param name="gesture">单击 / 双击。</param>
        /// <param name="happiness">加分前的开心值。</param>
        /// <param name="exclusiveReady">本变体的专属演出是否可以播（片段缺失时整条周期停用）。</param>
        public TapDecision DecideTap(PetGesture gesture, float happiness, bool exclusiveReady)
        {
            var before = PetState.Clamp01(happiness);

            // ① 待确认：这一次点按只负责"确认"，不加分。
            if (State == HappyCycleState.PendingConfirm)
            {
                if (exclusiveReady && CooldownRemaining <= 0f)
                    return new TapDecision(0f, startExclusive: true, setPendingAuto: false);

                // 冷却没走完，或专属片段不可用：只播触屏反应，周期仍停在待确认。
                return new TapDecision(0f, startExclusive: false, setPendingAuto: false);
            }

            var gain = GainFor(gesture);
            var after = PetState.Clamp01(before + gain);

            // ② 只有 Armed 才在"跨越满值"时消费周期；已满值时继续点按不算跨越。
            if (State == HappyCycleState.Armed && exclusiveReady &&
                before < FullHappiness && after >= FullHappiness)
            {
                if (CooldownRemaining <= 0f) return new TapDecision(gain, startExclusive: true, setPendingAuto: false);
                return new TapDecision(gain, startExclusive: false, setPendingAuto: true);
            }

            // ③ 其他情况：正常加分并播触屏反应，最多挂起一条自动开始请求。
            return new TapDecision(gain, startExclusive: false, setPendingAuto: false);
        }

        /// <summary>周期从 Armed 挂起到 PendingAuto。只应在 Armed 时调用。</summary>
        public void ArmPendingAuto()
        {
            if (State != HappyCycleState.Armed) return;
            State = HappyCycleState.PendingAuto;
        }

        /// <summary>
        /// 开始一次专属演出（原子操作）：资格转为已消费、冷却重新计时、开始次数加一；
        /// 回落值已满足重新武装条件时立即转回 <see cref="HappyCycleState.Armed"/>。
        /// 数值回落与进入行为由调用方在同一处完成。
        /// </summary>
        public void BeginExclusive()
        {
            State = HappyCycleState.Released;
            CooldownRemaining = Mathf.Max(0f, Tuning.ExclusiveCooldownSeconds);
            Starts++;
            RefreshRearm();
        }

        /// <summary>专属演出正常播完：看完次数加一。被打断时<b>不</b>调用。</summary>
        public void MarkExclusiveCompleted() => Completions++;

        /// <summary>按行为时钟推进冷却。返回冷却值是否发生变化（每帧变化不需要打存档脏标记）。</summary>
        public bool AdvanceCooldown(float deltaSeconds)
        {
            if (CooldownRemaining <= 0f || deltaSeconds <= 0f || float.IsNaN(deltaSeconds)) return false;

            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - deltaSeconds);
            return true;
        }

        /// <summary>
        /// 读档恢复。三种处理：
        /// <list type="number">
        /// <item>不因 100% 自动开始：已满值且周期为 Armed / PendingAuto 时改为待确认；</item>
        /// <item>冷却清洗：NaN / 负数归零，上限为该变体的冷却秒数；</item>
        /// <item>计数清洗：负数归零。</item>
        /// </list>
        /// </summary>
        /// <param name="saved">存档里读到的状态。</param>
        /// <param name="happiness">清洗后的开心值（以 <see cref="IPetStateProvider"/> 的现值为准）。</param>
        public void RestoreFrom(PetState saved, float happiness)
        {
            State = SanitiseState(saved.HappyCycle);
            CooldownRemaining = SanitiseCooldown(saved.ExclusiveCooldownRemaining);
            Starts = Math.Max(0, saved.ExclusiveStartCount);
            Completions = Math.Max(0, saved.ExclusiveWatchedCount);

            var value = PetState.Clamp01(happiness);
            if (value >= FullHappiness && (State == HappyCycleState.Armed || State == HappyCycleState.PendingAuto))
                State = HappyCycleState.PendingConfirm;

            RefreshRearm();
        }

        /// <summary>回落值已满足重新武装条件时，Released 立即转回 Armed（冷却仍单独计时）。</summary>
        private void RefreshRearm()
        {
            if (State == HappyCycleState.Released && Tuning.FallbackHappiness <= Tuning.RearmThreshold)
                State = HappyCycleState.Armed;
        }

        private float SanitiseCooldown(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return Mathf.Min(value, Mathf.Max(0f, Tuning.ExclusiveCooldownSeconds));
        }

        private static HappyCycleState SanitiseState(HappyCycleState state) => state switch
        {
            HappyCycleState.Armed => HappyCycleState.Armed,
            HappyCycleState.PendingAuto => HappyCycleState.PendingAuto,
            HappyCycleState.PendingConfirm => HappyCycleState.PendingConfirm,
            HappyCycleState.Released => HappyCycleState.Released,
            _ => HappyCycleState.Armed,   // 未知枚举值：按"尚未满值"处理
        };

        /// <summary>
        /// 调参条目的范围校验。关键一条：回落值高于重新武装阈值时，宠物永远无法再次攒满，
        /// 这里记一条警告并按阈值处理，避免手改配置把满值演出彻底卡死。
        /// </summary>
        private static VariantTuning Sanitise(VariantTuning tuning, PetVariantId variant)
        {
            tuning.Variant = variant;
            tuning.SingleTapGain = Mathf.Clamp01(SanitiseNumber(tuning.SingleTapGain));
            tuning.DoubleTapGain = Mathf.Clamp01(SanitiseNumber(tuning.DoubleTapGain));
            tuning.FallbackHappiness = Mathf.Clamp01(SanitiseNumber(tuning.FallbackHappiness));
            tuning.RearmThreshold = Mathf.Clamp01(SanitiseNumber(tuning.RearmThreshold));
            tuning.ExclusiveCooldownSeconds = Mathf.Max(0f, SanitiseNumber(tuning.ExclusiveCooldownSeconds));
            tuning.ExclusiveDurationSeconds = Mathf.Max(0f, SanitiseNumber(tuning.ExclusiveDurationSeconds));
            tuning.ExclusiveStateName = string.IsNullOrWhiteSpace(tuning.ExclusiveStateName)
                ? string.Empty
                : tuning.ExclusiveStateName.Trim();

            if (tuning.FallbackHappiness > tuning.RearmThreshold)
            {
                Debug.LogWarning($"[HappyCycle] {variant} 的满值回落值 {tuning.FallbackHappiness:F2} 高于重新武装阈值 " +
                                 $"{tuning.RearmThreshold:F2}，会导致永远无法重新武装；已按阈值处理。");
                tuning.FallbackHappiness = tuning.RearmThreshold;
            }

            return tuning;
        }

        private static float SanitiseNumber(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }
}
