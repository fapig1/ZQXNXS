using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Interaction
{
    /// <summary>
    /// 单指单击 / 双击识别。输入是<b>一次已经合格的点按</b>（哪只宠物、什么时候抬起、点在哪），
    /// 输出是 <see cref="InteractionEventKind.Pet"/> 事件，手势种类填 <see cref="PetGesture"/>。
    ///
    /// 为什么单独一层：<c>TouchPokeRule</c> 只回答"这次按下/抬起算不算合格点按"，
    /// 手势类型需要跨多次点按的时间窗口才能确定，而事件 Id 必须<b>每宠严格递增</b>
    /// （行为状态机按 (Kind, PetId) 去重，Id 回退会让合法事件被当成重复丢掉）。
    /// 这里只识别、不结算任何数值：加分与满值判定都在 <c>BehaviorStateMachine</c> 一处完成。
    ///
    /// 三条规则：
    /// <list type="number">
    /// <item>第一次合格抬起只开一个待判窗口，<b>立刻不加分</b>；窗口到期才发一条单击。</item>
    /// <item>窗口内同一只宠物第二次合格抬起 → 立刻发一条双击，取消待发单击；双击只代表一次事件。</item>
    /// <item>每只宠物各自一个窗口，两次点在不同宠物上绝不合成双击；窗口到期时该宠不可交互则丢弃，不补发。</item>
    /// </list>
    /// </summary>
    public sealed class TapGestureRecognizer
    {
        private readonly Dictionary<string, PendingTap> _pending = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _nextEventIds = new(StringComparer.Ordinal);

        // Tick 里先收集到期宠物再删除，避免遍历字典时改动字典。
        private readonly List<string> _expired = new();

        public TapGestureRecognizer(TapGestureSettings settings = null, Func<string, bool> canInteract = null)
        {
            Settings = settings ?? new TapGestureSettings();
            CanInteract = canInteract;
        }

        /// <summary>可调参数（双击窗口等）。</summary>
        public TapGestureSettings Settings { get; }

        /// <summary>
        /// "该宠此刻还能不能交互"的判断入口。单击在窗口到期时才会问它；
        /// 为空表示始终可交互（只用于离线测试，运行期必须注入）。
        /// </summary>
        public Func<string, bool> CanInteract { get; set; }

        /// <summary>当前仍在等待双击窗口的宠物数量。</summary>
        public int PendingCount => _pending.Count;

        /// <summary>累计发出的事件数（单击 + 双击），供调试与测试观察。</summary>
        public int EmittedCount { get; private set; }

        /// <summary>该宠是否有待判的单击。</summary>
        public bool HasPendingTap(string petId) =>
            !string.IsNullOrEmpty(petId) && _pending.ContainsKey(petId);

        /// <summary>取该宠待判单击的抬起时刻；没有待判时为 <c>false</c>。</summary>
        public bool TryGetPendingTapTime(string petId, out float tapTime)
        {
            tapTime = 0f;
            if (string.IsNullOrEmpty(petId) || !_pending.TryGetValue(petId, out var pending)) return false;
            tapTime = pending.Time;
            return true;
        }

        /// <summary>
        /// 收到一次合格点按（按下与抬起命中同一只、位移与按住时长都在规则内）。
        /// 返回是否被识别器接收为合格点按。
        ///
        /// 三种结果：
        /// <list type="bullet">
        /// <item>该宠没有待判窗口 → 开窗口，本次不产生事件；</item>
        /// <item>窗口内（间隔严格小于 <see cref="TapGestureSettings.DoubleTapWindowSeconds"/>）→
        ///       立刻产生一条双击，取消待发单击；</item>
        /// <item>已超出窗口但还没被 <see cref="Tick"/> 结算 → 先补发上一次单击，再为本次开新窗口。</item>
        /// </list>
        /// 第二条意味着"第二次抬起的时刻"决定归属：0.279 s 算双击，0.281 s 算两次单击。
        /// </summary>
        /// <param name="petId">归属宠物稳定 ID。</param>
        /// <param name="point">点按位置（世界米制；没有可用位姿时传零向量）。</param>
        /// <param name="now">本次抬起的时刻。</param>
        /// <param name="produced">立刻产生的事件（只有双击会立刻产生）；可传 <c>null</c>。</param>
        public bool Tap(string petId, Vector3 point, float now, ICollection<InteractionEvent> produced)
        {
            if (string.IsNullOrEmpty(petId)) return false;
            // 非法时钟不建立窗口：否则窗口可能永不结算，单击被永久挂起。
            if (float.IsNaN(now) || float.IsInfinity(now)) return false;

            if (_pending.TryGetValue(petId, out var first))
            {
                var gap = now - first.Time;
                if (gap >= 0f && gap < Settings.DoubleTapWindowSeconds)
                {
                    // 双击：取消待发单击，只发这一条。Id 与单击共用同一序列。
                    _pending.Remove(petId);
                    Emit(petId, PetGesture.DoubleTap, now, point, produced);
                    return true;
                }

                // 超出窗口：上一次点按已经只可能是单击，先补发（该宠仍可交互才发），再开新窗口。
                _pending.Remove(petId);
                FlushSingle(petId, first, produced);
            }

            _pending[petId] = new PendingTap(now, point);
            return true;
        }

        /// <summary>
        /// 推进一步：把已经过期（当前时刻 − 抬起时刻 ≥ 窗口）的待判单击发出去。
        /// 发出前会问 <see cref="CanInteract"/>，不可交互则直接丢弃，不补发。
        /// 返回本次发出的单击条数。
        /// </summary>
        public int Tick(float now, ICollection<InteractionEvent> produced)
        {
            if (_pending.Count == 0) return 0;
            if (float.IsNaN(now) || float.IsInfinity(now)) return 0;

            var window = Settings.DoubleTapWindowSeconds;

            _expired.Clear();
            foreach (var pair in _pending)
            {
                if (now - pair.Value.Time < window) continue;
                _expired.Add(pair.Key);
            }

            var emitted = 0;
            for (var i = 0; i < _expired.Count; i++)
            {
                var petId = _expired[i];
                if (!_pending.TryGetValue(petId, out var pending)) continue;
                _pending.Remove(petId);
                if (FlushSingle(petId, pending, produced)) emitted++;
            }

            _expired.Clear();
            return emitted;
        }

        /// <summary>丢弃该宠的待判单击，不补发（跟踪丢失、该宠被取消时用）。</summary>
        public bool Cancel(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return false;
            return _pending.Remove(petId);
        }

        /// <summary>丢弃全部待判单击，不补发（切后台、全局暂停）。</summary>
        public void CancelAll() => _pending.Clear();

        /// <summary>
        /// 丢弃所有"此刻已不可交互"的待判单击。返回被丢弃的数量。
        /// 逐宠跟踪丢失时由应用入口每帧调用一次，保证恢复后不会补发旧单击。
        /// </summary>
        public int CancelUnavailable()
        {
            if (_pending.Count == 0 || CanInteract == null) return 0;

            _expired.Clear();
            foreach (var pair in _pending)
            {
                if (CanInteract(pair.Key)) continue;
                _expired.Add(pair.Key);
            }

            for (var i = 0; i < _expired.Count; i++) _pending.Remove(_expired[i]);

            var removed = _expired.Count;
            _expired.Clear();
            return removed;
        }

        private bool FlushSingle(string petId, in PendingTap pending, ICollection<InteractionEvent> produced)
        {
            // 窗口到期这一刻该宠已经不可交互（跟踪丢失 / 位姿过期 / 切后台）→ 丢弃，不补发。
            if (CanInteract != null && !CanInteract(petId)) return false;

            Emit(petId, PetGesture.SingleTap, pending.Time, pending.Point, produced);
            return true;
        }

        private void Emit(string petId, PetGesture gesture, float eventTime, Vector3 point,
            ICollection<InteractionEvent> produced)
        {
            var created = InteractionEvent.Create(NextEventId(petId), InteractionEventKind.Pet, petId,
                eventTime, point, 0f, gesture);
            EmittedCount++;
            produced?.Add(created);
        }

        /// <summary>
        /// 每宠独立单调递增的事件序号。单击与双击共用同一序列：
        /// 行为状态机的去重键是 (Kind, PetId)，若两套序列各自从 1 开始，后发的双击会被当成重复事件丢掉。
        /// </summary>
        private int NextEventId(string petId)
        {
            _nextEventIds.TryGetValue(petId, out var next);
            next = next <= 0 ? 1 : next;
            _nextEventIds[petId] = next + 1;
            return next;
        }

        private readonly struct PendingTap
        {
            public PendingTap(float time, Vector3 point)
            {
                Time = time;
                Point = point;
            }

            /// <summary>第一次合格抬起的时刻，也是待发单击的事件时刻。</summary>
            public float Time { get; }

            /// <summary>第一次合格抬起的位置，也是待发单击的位置。</summary>
            public Vector3 Point { get; }
        }

        /// <summary>双击识别参数。</summary>
        public sealed class TapGestureSettings
        {
            /// <summary>
            /// 双击窗口（秒）：第二次抬起与第一次抬起的间隔<b>严格小于</b>该值才算双击。
            /// 0.28 是按移动端手感取的设计初值，进设置类便于真机调。
            /// </summary>
            public float DoubleTapWindowSeconds = 0.28f;
        }
    }
}
