using System;
using System.Collections.Generic;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Interaction
{
    /// <summary>
    /// 语音口令的交互判定（纯逻辑）。把"识别器听到了一句口令"变成"每只在场宠物各一个语音事件"。
    ///
    /// 三条纪律：
    /// <list type="bullet">
    /// <item>只给<b>此刻可交互</b>（跟踪有效、位姿新鲜）的宠物发事件：卡不在画面里的宠物听不到。</item>
    /// <item>每只宠物有自己递增的事件 Id，与触屏事件流互不影响；去重仍由行为层按 (PetId, Kind, Id) 做。</item>
    /// <item>本类不加分、不判冷却、不判是否在忙：那些都是行为层的结算规则。</item>
    /// </list>
    /// </summary>
    public sealed class VoiceCommandRule
    {
        private readonly Dictionary<string, int> _nextIds = new(StringComparer.Ordinal);

        /// <summary>
        /// 为一句口令生成事件。<paramref name="petIds"/> 是全部宠物，<paramref name="canInteract"/>
        /// 判断哪些在场；结果追加到 <paramref name="output"/>（调用方复用列表，避免分配）。
        /// 返回生成的事件数。口令为空时不生成。
        /// </summary>
        public int Build(string commandId, float now, IReadOnlyList<string> petIds, Func<string, bool> canInteract,
            List<InteractionEvent> output)
        {
            if (string.IsNullOrWhiteSpace(commandId) || petIds == null || output == null) return 0;

            var count = 0;
            for (var i = 0; i < petIds.Count; i++)
            {
                var petId = petIds[i];
                if (string.IsNullOrEmpty(petId)) continue;
                if (canInteract != null && !canInteract(petId)) continue;

                _nextIds.TryGetValue(petId, out var last);
                var id = last + 1;
                _nextIds[petId] = id;
                output.Add(InteractionEvent.CreateVoice(id, petId, now, commandId));
                count++;
            }

            return count;
        }
    }
}
