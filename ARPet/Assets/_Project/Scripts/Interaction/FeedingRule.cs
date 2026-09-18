using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Interaction
{
    /// <summary>
    /// 实体投喂判定。**这是整个项目规则最密集的一块，也是唯一需要重点测试的纯逻辑。**
    ///
    /// 一次合法投喂必须同时满足：
    /// <list type="number">
    /// <item>小窝卡与食物卡都处于"可用且未过期"状态；</item>
    /// <item>食物卡在小窝局部坐标系下，与小窝中心的水平距离不超过上限；</item>
    /// <item>高度差不超过上限（防止举在半空也算投喂）；</item>
    /// <item>上述条件连续保持到规定的停留时长；</item>
    /// <item>不在冷却期内；</item>
    /// <item>上一次结算之后，食物卡确实离开过判定区（"离开后再次进入"）。</item>
    /// </list>
    ///
    /// 通过后<b>只产生一个事件</b>，并进入冷却。跟踪恢复不会自动补发事件。
    /// </summary>
    public sealed class FeedingRule
    {
        /// <summary>单个槽位的判定状态。用数组按 <see cref="MarkerSlot"/> 索引。</summary>
        private struct SlotState
        {
            /// <summary>已连续满足条件的时间。</summary>
            public float DwellSeconds;

            /// <summary>冷却结束时刻。</summary>
            public float CooldownUntil;

            /// <summary>是否处于"已结算但还没离开判定区"的锁定状态。</summary>
            public bool LockedUntilExit;

            /// <summary>锁定期间是否已经离开过判定区。</summary>
            public bool LeftZone;
        }

        private readonly PetConfigEatSettings _settings;
        private readonly SlotState[] _slots = new SlotState[2];
        private int _nextEventId = 1;

        /// <summary>最近一次判定结果，供调试面板与测试观察。</summary>
        public string LastReason { get; private set; } = "尚未判定";

        public FeedingRule(PetConfigEatSettings settings)
        {
            _settings = settings;
        }

        /// <summary>已产生的事件序号，测试可据此断言事件数量。</summary>
        public int IssuedEventCount => _nextEventId - 1;

        /// <summary>
        /// 每帧判定一次。返回 <c>true</c> 表示本帧产生了一个投喂事件。
        /// </summary>
        /// <param name="den">小窝卡采样。</param>
        /// <param name="food">食物卡采样。</param>
        /// <param name="now">当前时刻（秒）。</param>
        /// <param name="deltaTime">本帧时长（秒）。由调用方传入而不是读 <c>Time.deltaTime</c>，便于测试用假时间步进。</param>
        /// <param name="foodLocalToDen">食物卡在小窝局部坐标系下的位置（米）。</param>
        /// <param name="staleSeconds">位姿过期阈值。</param>
        /// <param name="produced">产生的事件。</param>
        public bool Evaluate(in PoseSample den, in PoseSample food, float now, float deltaTime,
            Vector3 foodLocalToDen, float staleSeconds, out InteractionEvent produced)
        {
            produced = default;

            // ① 双目标的有效性：任一不可用或位姿过期，直接清零停留计时。
            //    注意这里"清零"而不是"暂停"——否则卡片短暂丢失期间会累计出虚假的停留时间。
            if (!PoseFreshness.IsSettleable(den, now, staleSeconds) ||
                !PoseFreshness.IsSettleable(food, now, staleSeconds))
            {
                ResetDwell(MarkerSlot.Food);
                // 无效位姿不能证明卡片已经移开，必须保留结算后的离开锁。
                LastReason = "双目标未同时有效，暂停投喂判定";
                return false;
            }

            var slot = _slots[(int)MarkerSlot.Food];

            // ② 几何条件。
            var horizontal = new Vector2(foodLocalToDen.x, foodLocalToDen.z);
            var height = foodLocalToDen.y;

            var inRange = horizontal.magnitude <= _settings.MaxHorizontalDistance &&
                          Mathf.Abs(height) <= _settings.MaxHeightDelta;

            if (!inRange)
            {
                ResetDwell(MarkerSlot.Food);
                MarkExitIfLocked(MarkerSlot.Food, far: horizontal.magnitude > _settings.ReArmDistance);
                LastReason = "食物卡不在判定区内";
                return false;
            }

            // ③ 冷却。
            if (now < slot.CooldownUntil)
            {
                LastReason = $"冷却中，剩余 {slot.CooldownUntil - now:F1}s";
                return false;
            }

            // ④ 离开后再次进入：上一次结算后必须真的离开过，否则同一次摆放会反复投喂。
            if (slot.LockedUntilExit && !slot.LeftZone)
            {
                LastReason = "需先移开食物卡再重新放入";
                return false;
            }

            // ⑤ 停留时长。
            slot.DwellSeconds += deltaTime > 0f ? deltaTime : 0f;
            _slots[(int)MarkerSlot.Food] = slot;

            if (slot.DwellSeconds < _settings.RequiredDwellSeconds)
            {
                LastReason = $"停留中 {slot.DwellSeconds:F2}/{_settings.RequiredDwellSeconds:F2}s";
                return false;
            }

            // ⑥ 结算一次，然后锁定到"离开判定区"为止。
            slot.DwellSeconds = 0f;
            slot.CooldownUntil = now + _settings.CooldownSeconds;
            slot.LockedUntilExit = true;
            slot.LeftZone = false;
            _slots[(int)MarkerSlot.Food] = slot;

            produced = InteractionEvent.Create(
                _nextEventId++,
                InteractionEventKind.Feed,
                MarkerSlot.Food,
                now,
                foodLocalToDen,
                horizontal.magnitude);

            LastReason = "投喂事件已产生";
            return true;
        }

        /// <summary>在测试中手动推进停留计时，避免依赖真实帧步长。</summary>
        public void AdvanceDwell(float seconds)
        {
            var slot = _slots[(int)MarkerSlot.Food];
            slot.DwellSeconds += Mathf.Max(0f, seconds);
            _slots[(int)MarkerSlot.Food] = slot;
        }

        /// <summary>跟踪暂停时只清理连续停留，保留冷却与离开锁。</summary>
        public void SuspendDwell()
        {
            ResetDwell(MarkerSlot.Food);
            LastReason = "跟踪暂停，需重新连续停留";
        }

        /// <summary>仅在新会话或场景重载时重置；跟踪暂停必须调用 SuspendDwell。</summary>
        public void Reset()
        {
            for (var i = 0; i < _slots.Length; i++) _slots[i] = default;
            LastReason = "已重置";
        }

        private void ResetDwell(MarkerSlot slot)
        {
            _slots[(int)slot].DwellSeconds = 0f;
        }

        private void MarkExitIfLocked(MarkerSlot slot, bool far)
        {
            var s = _slots[(int)slot];
            if (s.LockedUntilExit && far)
            {
                s.LeftZone = true;
                s.LockedUntilExit = false;
            }
            _slots[(int)slot] = s;
        }
    }

    /// <summary>
    /// 投喂判定参数的轻量载体，避免业务逻辑直接依赖 ScriptableObject，
    /// 以便在 EditMode 测试里用普通对象构造。
    /// </summary>
    public sealed class PetConfigEatSettings
    {
        public float MaxHorizontalDistance = 0.10f;
        public float MaxHeightDelta = 0.06f;
        public float RequiredDwellSeconds = 0.6f;
        public float CooldownSeconds = 4f;
        public float ReArmDistance = 0.16f;
        public float StaleSeconds = PoseFreshness.DefaultStaleSeconds;

        public static PetConfigEatSettings From(PetConfig config) => new PetConfigEatSettings
        {
            MaxHorizontalDistance = config.Eat.MaxHorizontalDistance,
            MaxHeightDelta = config.Eat.MaxHeightDelta,
            RequiredDwellSeconds = config.Eat.RequiredDwellSeconds,
            CooldownSeconds = config.Eat.CooldownSeconds,
            ReArmDistance = config.Eat.ReArmDistance,
        };
    }
}
