using System;
using System.Collections.Generic;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Interaction
{
    /// <summary>
    /// 一帧里某只宠物的空间采样。由应用层填好再交进来：
    /// <see cref="Usable"/> 已经包含"本帧该宠可交互、采样为 Tracked 且位姿新鲜"三个条件，
    /// 接近规则自己不再读跟踪源、不判断新鲜度，因此可以脱离场景验证。
    /// </summary>
    public struct ProximityInput
    {
        /// <summary>宠物稳定 ID（如 <c>pet_classic</c>）。</summary>
        public string PetId;

        /// <summary>本帧是否可用于接近判定。丢失、位姿过期、全局暂停都是 <c>false</c>。</summary>
        public bool Usable;

        /// <summary>
        /// 卡是否仍在画面里（新鲜的 TRACKED 或 LIMITED）。比 <see cref="Usable"/> 宽：LIMITED 不能用来判距离，
        /// 但也不算"拿走"，不累计丢失计时。<see cref="Usable"/> 为 true 时本字段视为 true。
        /// </summary>
        public bool Present;

        /// <summary>按"可用即在场"合并后的在场判定。</summary>
        public bool IsPresent => Usable || Present;

        /// <summary>世界坐标下的目标位姿（米）。<b>不可用时不要读</b>，接近规则会忽略它。</summary>
        public Vector3 Position;

        /// <summary>世界朝向。Unity 里 ImageTarget 的局部 +Y 就是卡面法向。</summary>
        public Quaternion Rotation;
    }

    /// <summary>
    /// 一对宠物之间的几何量。全部使用同一世界米制坐标系、两卡对称的相对关系，
    /// 不依赖旧小窝（Den）任何局部参照。
    /// <list type="bullet">
    /// <item><see cref="Normal"/> 是两卡法向的平均方向（退化时取 A 的法向）；</item>
    /// <item><see cref="HeightDelta"/> 是双向投影高度差，所以交换 A/B 结果不变；</item>
    /// <item><see cref="HorizontalDistance"/> 是去掉平均法向分量后的"卡面内"距离。</item>
    /// </list>
    /// </summary>
    public struct ProximityMeasure
    {
        /// <summary>两卡中心的水平（卡面内）距离（米）。</summary>
        public float HorizontalDistance;

        /// <summary>沿平均法向的双向高度差（米）：两卡各自的投影取绝对值后取大者。</summary>
        public float HeightDelta;

        /// <summary>两卡法向夹角（度，0～180）。</summary>
        public float NormalAngleDegrees;

        /// <summary>平均法向（单位向量，已归一化）。</summary>
        public Vector3 Normal;

        /// <summary>B 相对 A 的位移（调试用；交换 A/B 会取反）。</summary>
        public Vector3 CenterDelta;

        /// <summary>是否满足全部进入条件（距离、高度、夹角）。</summary>
        public bool InEnterZone;

        /// <summary>是否满足离开条件（只按水平距离，见 <see cref="ProximityRule"/> 的说明）。</summary>
        public bool InExitZone;

        /// <summary>既不算进入也不算离开：迟滞带，两个停留计时都清零。</summary>
        public bool InHysteresis => !InEnterZone && !InExitZone;

        public override string ToString() =>
            $"水平 {HorizontalDistance:F3}m 高差 {HeightDelta:F3}m 夹角 {NormalAngleDegrees:F1}° " +
            $"{(InEnterZone ? "进入区" : InExitZone ? "退出区" : "迟滞带")}";
    }

    /// <summary>
    /// 接近规则：把一帧里全部宠物的可用位姿换算成"哪些关系对已经合格靠近"。
    /// 纯 C#，<b>只读位姿</b>——不碰行为、不改任何数值、不读 <c>Time</c>，时间步长由调用方给。
    ///
    /// <b>它只回答两件事：</b>
    /// <list type="number">
    /// <item>某个关系对是否连续满足了进入条件（<see cref="QualifiedPairs"/>）；</item>
    /// <item>某个关系对是否已经真实分开（<see cref="IsSeparated"/>），从而可以重新武装。</item>
    /// </list>
    /// 组划分、开始/取消仲裁、记账都在 <c>SocialCoordinator</c>（Pet 层），两层之间只传结论，
    /// 关系对用 Core 里的 <see cref="PetPair"/>，因此 Pet 不需要反向引用 Interaction。
    ///
    /// <b>四条状态纪律：</b>
    /// <list type="bullet">
    /// <item><b>迟滞</b>：进入要求水平距离 ≤ <see cref="SocialTuning.EnterDistance"/>，
    ///       离开要求水平距离 ≥ <see cref="SocialTuning.ExitDistance"/>；介于两者之间（或高度 / 夹角超限）
    ///       就是迟滞带，进入与离开计时都清零，既不合格也不解锁。</item>
    /// <item><b>离开只按水平距离</b>：高度或夹角超限只把关系对打回迟滞带，不解锁。
    ///       这样一次跟踪抖动（法向瞬间翻转、卡片被抬起）不会把关系对解锁成"可重播"，
    ///       宁可要求用户真的把两张卡水平拉开。</item>
    /// <item><b>短暂丢失不等于离开</b>：任一只不可用时两个计时都清零、资格作废，但锁保持不变；
    ///       恢复后仍按"同一次接近已经消费过"处理，跟踪抖动不会让同一对重演。</item>
    /// <item><b>拿走再换位放回才算离开</b>：任一只连续不在画面里（LIMITED 仍算在画面里）满
    ///       <see cref="SocialTuning.LostSeparationSeconds"/> 后，等它放回时与另一只的相对位置变化
    ///       ≥ <see cref="SocialTuning.RelocationDistance"/> 才解锁。竖屏视野装不下"两卡同时入镜拉开 0.23m"，
    ///       用户真实的分开动作通常是把一张卡拿走；而四卡同屏时边缘卡原地掉跟踪不能算拿走（2026-10-30 O-10）。</item>
    /// </list>
    ///
    /// 不再有"启动先上锁"（2026-10-30 真机 BUG-04）：先放一张、再把第二张放到旁边时，
    /// 第二张进入 TRACKED 的那一帧两卡就已贴近，旧规则会把这次最自然的摆法判成"启动即贴住"并永久锁死。
    /// 现在第一次同时可见就已贴近的一对照常累计停留后开演；同一次接近只演一次仍由开演时的锁保证。
    ///
    /// 每对的状态只在内存里（不存档）。四只最多六对，按 PetId 的 Ordinal 顺序遍历，
    /// 同一输入快照总是得到同一个输出顺序。
    /// </summary>
    public sealed class ProximityRule
    {
        /// <summary>一对宠物的停留计时与锁。键是不可变的 <see cref="PetPair"/>。</summary>
        private sealed class PairState
        {
            public float EnterDwell;
            public float ExitDwell;
            public bool Qualified;
            public bool Separated;
            public bool Locked;

            /// <summary>任一只连续不在画面里的时长（秒）。LIMITED 不累计。</summary>
            public float LostDwell;

            /// <summary>
            /// 已锁的一对里有一只拿出画面满 <see cref="SocialTuning.LostSeparationSeconds"/>：
            /// 等它放回时相对位置变化 ≥ <see cref="SocialTuning.RelocationDistance"/> 才解锁。
            /// </summary>
            public bool AwaitRelocation;

            /// <summary>最近一次两只都可用时，B 相对 A 的位移（A 的局部坐标，与相机和世界原点无关）。</summary>
            public Vector3 LastOffset;

            public bool HasLastOffset;

            /// <summary>进入"等待换位"时记下的参照位移。</summary>
            public Vector3 ReferenceOffset;

            public bool HasMeasure;
            public ProximityMeasure Measure;
        }

        private readonly Dictionary<PetPair, PairState> _states = new();
        private readonly HashSet<PetPair> _touched = new();

        // 每帧重建，供协调器当帧消费；调用方不要跨帧保存引用。
        private readonly List<PetPair> _qualified = new();
        private readonly List<ProximityInput> _ordered = new();

        public ProximityRule(SocialTuning tuning)
        {
            // 配置再校验一次：调用方应传 config.ResolveSocial()，这里兜住手写临时结构的情况。
            Tuning = tuning.Sanitised();
        }

        /// <summary>本规则实际生效的参数（已校验）。</summary>
        public SocialTuning Tuning { get; }

        /// <summary>
        /// 本帧"已连续满足进入条件"的关系对，每帧重建、顺序按 (A,B) 的 Ordinal 稳定。
        /// <b>包含已锁的关系对</b>——协调器自己按 <see cref="IsLocked"/> 过滤，
        /// 这样"锁着但已经够近"不会被误当成"从未靠近"。
        /// </summary>
        public IReadOnlyCollection<PetPair> QualifiedPairs => _qualified;

        /// <summary>
        /// 推进一帧。<paramref name="inputs"/> 应覆盖本局全部宠物（不可用的也要传，用
        /// <see cref="ProximityInput.Usable"/> 标 false）；重复或空 PetId 会被忽略。
        /// 非法的 <paramref name="deltaTime"/>（NaN / ±∞ / 负数）按 0 处理：只清瞬态，不推进计时。
        /// </summary>
        public void Tick(float deltaTime, IReadOnlyList<ProximityInput> inputs)
        {
            _qualified.Clear();
            _touched.Clear();

            BuildOrderedInputs(inputs);

            var dt = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f ? 0f : deltaTime;

            for (var i = 0; i < _ordered.Count; i++)
            {
                for (var j = i + 1; j < _ordered.Count; j++)
                {
                    var left = _ordered[i];
                    var right = _ordered[j];
                    var pair = new PetPair(left.PetId, right.PetId);

                    if (!_states.TryGetValue(pair, out var state))
                    {
                        state = new PairState();
                        _states.Add(pair, state);
                    }

                    _touched.Add(pair);
                    Evaluate(pair, state, left, right, dt);
                }
            }

            ClearUntouchedTransients();
        }

        /// <summary>该关系对是否已被一次以前的会话锁住（同一次接近只触发一次演出）。</summary>
        public bool IsLocked(PetPair pair) => _states.TryGetValue(pair, out var state) && state.Locked;

        /// <summary>该关系对是否已确认真实分开（可以重新武装）。</summary>
        public bool IsSeparated(PetPair pair) => _states.TryGetValue(pair, out var state) && state.Separated;

        /// <summary>调试用：读该关系对最近一次（两只都可用时）的几何量。</summary>
        public bool TryGetMeasure(PetPair pair, out ProximityMeasure measure)
        {
            if (_states.TryGetValue(pair, out var state) && state.HasMeasure)
            {
                measure = state.Measure;
                return true;
            }

            measure = default;
            return false;
        }

        /// <summary>
        /// 锁住一个组内部<b>所有</b>成员两两之间的关系对（不只是连通树上的边）：
        /// 否则 ABC 演完一轮后 AB / BC 会立刻各自重开一次。成员不足两只时不做事。
        /// </summary>
        public void LockGroup(IReadOnlyList<string> members)
        {
            if (members == null || members.Count < 2) return;

            for (var i = 0; i < members.Count; i++)
            {
                if (string.IsNullOrEmpty(members[i])) continue;
                for (var j = i + 1; j < members.Count; j++)
                {
                    if (string.IsNullOrEmpty(members[j])) continue;
                    if (string.Equals(members[i], members[j], StringComparison.Ordinal)) continue;

                    var pair = new PetPair(members[i], members[j]);
                    if (!_states.TryGetValue(pair, out var state))
                    {
                        state = new PairState();
                        _states.Add(pair, state);
                    }

                    if (!state.Locked) Debug.Log($"[ProximityRule] {pair} 上锁：共同大笑开始，同一次接近只演一次。");
                    state.Locked = true;
                    state.Separated = false;
                    state.AwaitRelocation = false;
                }
            }
        }

        /// <summary>
        /// 把该关系对的进入计时清零并撤回资格，让它重新累计
        /// <see cref="SocialTuning.EnterDwellSeconds"/>。协调器丢弃超时候选时调用：
        /// 候选没开始时不能无期限占位，也不能立刻重试。
        /// 锁与退出计时不动（这次接近是否已经消费由锁表达）。
        /// 本帧已经发布出去的资格也一并撤回，改由下一帧重新累计，避免协调器当帧又拿到同一个对。
        /// </summary>
        public void ResetDwell(PetPair pair)
        {
            if (!_states.TryGetValue(pair, out var state)) return;
            state.EnterDwell = 0f;
            state.Qualified = false;
            _qualified.Remove(pair);
        }

        /// <summary>
        /// 全局暂停 / 切后台：清零所有停留计时（含丢失计时）与"合格 / 已分开"，<b>锁保持不变</b>
        /// （暂停不是离开：已上锁的对回来后仍要真实分开再靠近才能重演；未上锁的对重新累计停留时间）。
        /// 全局跟踪未运行期间应调用本方法而不是 <see cref="Tick"/>，否则暂停时长会被算作"拿出画面"。
        /// </summary>
        public void ResetTransient()
        {
            _qualified.Clear();

            foreach (var entry in _states)
            {
                var state = entry.Value;
                state.EnterDwell = 0f;
                state.ExitDwell = 0f;
                state.LostDwell = 0f;
                state.Qualified = false;
                state.Separated = false;
                state.HasMeasure = false;
            }
        }

        /// <summary>
        /// 两卡对称的几何量。<paramref name="tuning"/> 视为已校验（<see cref="SocialTuning.Sanitised"/>）。
        ///
        /// nA = RotA·up，nB = RotB·up（ImageTarget 的局部 +Y 就是卡面法向）；
        /// d = PosB − PosA；平均法向 n = normalize(nA + nB)（退化时用 nA）；
        /// 水平距离 = |d − (d·n)·n|，双向高度差 = max(|d·nA|, |d·nB|)，夹角 = Angle(nA, nB)。
        /// 交换 A/B 只会让 <see cref="ProximityMeasure.CenterDelta"/> 取反，其余量不变。
        /// </summary>
        public static ProximityMeasure Measure(Vector3 positionA, Quaternion rotationA,
            Vector3 positionB, Quaternion rotationB, SocialTuning tuning)
        {
            var normalA = NormalOf(rotationA);
            var normalB = NormalOf(rotationB);

            var sum = normalA + normalB;
            var normal = sum.sqrMagnitude > 1e-8f ? sum.normalized : normalA;

            var delta = positionB - positionA;
            var alongAverage = Vector3.Dot(delta, normal);

            var measure = new ProximityMeasure
            {
                Normal = normal,
                CenterDelta = delta,
                HorizontalDistance = (delta - alongAverage * normal).magnitude,
                HeightDelta = Mathf.Max(Mathf.Abs(Vector3.Dot(delta, normalA)), Mathf.Abs(Vector3.Dot(delta, normalB))),
                NormalAngleDegrees = Vector3.Angle(normalA, normalB),
            };

            measure.InEnterZone =
                measure.HorizontalDistance <= tuning.EnterDistance &&
                measure.HeightDelta <= tuning.MaxHeightDelta &&
                measure.NormalAngleDegrees <= tuning.MaxNormalAngleDegrees;

            // 离开只看水平距离：高度 / 夹角异常只代表"没对齐"，不代表真的搬走了。
            measure.InExitZone = measure.HorizontalDistance >= tuning.ExitDistance;
            return measure;
        }

        // ── 内部 ────────────────────────────────────────────────────────────────

        private void Evaluate(PetPair pair, PairState state, in ProximityInput left, in ProximityInput right, float dt)
        {
            // ① 任一只不可用：计时与资格作废，"已分开"也作废，锁保持不变。
            //    只有真的不在画面里（LIMITED 不算）才累计丢失计时；满 LostSeparationSeconds 后
            //    进入"等待换位"，放回时位置确实变了才解锁（2026-10-30 真机 O-10：原地抖动不能反复重演）。
            if (!left.Usable || !right.Usable)
            {
                state.EnterDwell = 0f;
                state.ExitDwell = 0f;
                state.Qualified = false;
                state.Separated = false;
                state.HasMeasure = false;

                if (left.IsPresent && right.IsPresent)
                {
                    state.LostDwell = 0f;
                    return;
                }

                state.LostDwell += dt;
                if (state.Locked && !state.AwaitRelocation && state.LostDwell >= Tuning.LostSeparationSeconds)
                {
                    if (state.HasLastOffset)
                    {
                        state.AwaitRelocation = true;
                        state.ReferenceOffset = state.LastOffset;
                        Debug.Log($"[ProximityRule] {pair} 有一张卡离开画面满 {Tuning.LostSeparationSeconds:F1}s，" +
                                  $"放回时位置变化 ≥{Tuning.RelocationDistance:F3}m 才重新武装。");
                    }
                    else
                    {
                        // 没有任何参照位姿（理论上锁住时一定测过）：退化为直接解锁。
                        Unlock(pair, state, "离开画面且无参照位姿");
                    }
                }

                return;
            }

            state.LostDwell = 0f;

            var measure = Measure(left.Position, left.Rotation, right.Position, right.Rotation, Tuning);
            state.Measure = measure;
            state.HasMeasure = true;

            var offset = LocalOffset(left, right);
            if (state.AwaitRelocation)
            {
                var moved = (offset - state.ReferenceOffset).magnitude;
                if (!state.Locked) state.AwaitRelocation = false;
                else if (moved >= Tuning.RelocationDistance) Unlock(pair, state, $"放回后位置变化 {moved:F3}m");
            }

            state.LastOffset = offset;
            state.HasLastOffset = true;

            if (measure.InEnterZone)
            {
                state.ExitDwell = 0f;
                state.Separated = false;
                state.EnterDwell += dt;
                if (state.EnterDwell >= Tuning.EnterDwellSeconds) state.Qualified = true;
            }
            else if (measure.InExitZone)
            {
                state.EnterDwell = 0f;
                state.Qualified = false;
                state.ExitDwell += dt;
                if (state.ExitDwell >= Tuning.ExitDwellSeconds)
                {
                    state.Separated = true;
                    if (state.Locked) Unlock(pair, state, $"两卡拉开到 {measure.HorizontalDistance:F3}m");
                }
            }
            else
            {
                // ③ 迟滞带：两个计时都清零，既不合格也不算分开。
                state.EnterDwell = 0f;
                state.ExitDwell = 0f;
                state.Qualified = false;
                state.Separated = false;
            }

            if (state.Qualified) _qualified.Add(pair);
        }

        private static void Unlock(PetPair pair, PairState state, string reason)
        {
            state.Locked = false;
            state.AwaitRelocation = false;
            Debug.Log($"[ProximityRule] {pair} 解锁：{reason}。");
        }

        /// <summary>B 相对 A 的位移，表达在 A 的局部坐标里：相机移动、世界原点漂移都不改变它。</summary>
        private static Vector3 LocalOffset(in ProximityInput left, in ProximityInput right)
        {
            var rotation = left.Rotation;
            var valid = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z +
                        rotation.w * rotation.w > 1e-8f;
            var delta = right.Position - left.Position;
            return valid ? Quaternion.Inverse(rotation) * delta : delta;
        }

        /// <summary>本帧没被评估到的关系对（成员已不在输入里）：清瞬态、保留锁。</summary>
        private void ClearUntouchedTransients()
        {
            if (_touched.Count == _states.Count) return;

            foreach (var entry in _states)
            {
                if (_touched.Contains(entry.Key)) continue;

                var state = entry.Value;
                state.EnterDwell = 0f;
                state.ExitDwell = 0f;
                state.LostDwell = 0f;
                state.Qualified = false;
                state.Separated = false;
                state.HasMeasure = false;
            }
        }

        /// <summary>按 PetId 的 Ordinal 排序去重，保证同一输入快照得到同一输出顺序。</summary>
        private void BuildOrderedInputs(IReadOnlyList<ProximityInput> inputs)
        {
            _ordered.Clear();
            if (inputs == null) return;

            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                if (string.IsNullOrWhiteSpace(input.PetId)) continue;
                if (ContainsId(input.PetId)) continue;
                _ordered.Add(input);
            }

            _ordered.Sort(CompareByPetId);
        }

        private bool ContainsId(string petId)
        {
            for (var i = 0; i < _ordered.Count; i++)
            {
                if (string.Equals(_ordered[i].PetId, petId, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static int CompareByPetId(ProximityInput left, ProximityInput right) =>
            string.CompareOrdinal(left.PetId, right.PetId);

        /// <summary>卡面法向 = 旋转后的局部 +Y；非法旋转退化成世界 +Y（"朝向未知"按平放处理）。</summary>
        private static Vector3 NormalOf(Quaternion rotation)
        {
            var normal = rotation * Vector3.up;
            return normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
        }
    }
}
