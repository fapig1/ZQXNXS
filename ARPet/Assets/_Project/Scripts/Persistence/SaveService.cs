using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Persistence
{
    /// <summary>
    /// 本地版本化 JSON 存档（schema 2，多宠）。优先原子替换并保留 .bak；平台不支持替换时，
    /// 先持久化备份再换主档。读取按主档、完整临时档、备份依次恢复。
    /// 可选 path 供隔离的文件系统测试使用，运行时默认 persistentDataPath。
    ///
    /// 版本策略：
    /// <list type="bullet">
    /// <item>有 v2 → 读 v2 并逐条校验；</item>
    /// <item>没有 v2 但有 v1 → 解析旧结构迁移成 v2，<b>v1 原文件不删不改</b>，由调用方随后正常写盘；</item>
    /// <item>读到比当前代码更高的版本 → 不归档、不覆盖，返回默认数据并置 <see cref="SaveFile.WriteBlocked"/>；</item>
    /// <item>版本非法或 JSON 损坏 → 走既有 .tmp / .bak 恢复，仍失败则新建。</item>
    /// </list>
    /// </summary>
    public static class SaveService
    {
        /// <summary>当前写出的存档文件名（schema 2，多宠）。</summary>
        public const string FileName = "zqxnxs_save_v2.json";

        /// <summary>旧版单宠存档文件名。<b>只读</b>：迁移后原样保留，作为历史备份。</summary>
        public const string LegacyV1FileName = "zqxnxs_save_v1.json";

        /// <summary>v1 的单份数值迁移到哪只宠物。旧存档没有宠物概念，暖黄是默认承接者。</summary>
        public const string LegacyClassicPetId = "pet_classic";

        /// <summary>
        /// 新版认识的"已解锁反应"稳定 ID。目前新版还没有接入任何反应，所以这里为空：
        /// v1 存档里的旧反应 ID（食物、旧玩法）一律不迁移。以后接入反应时登记在这里，
        /// 迁移按它过滤，未知 ID 不会污染新档。
        /// </summary>
        public static readonly string[] KnownReactionIds = Array.Empty<string>();

        public static string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// Android 的 persistentDataPath 在 FUSE 上，<c>File.Replace</c> 内部的 <c>link()</c> 被 SELinux 拒绝，
        /// 每次都抛 IOException 并留一条 avc denied（2026-10-30 真机 BUG-03：每 5 秒一条，挤掉 logcat 业务日志）。
        /// Android 上直接走兜底；其他平台第一次失败后也记住，不再每次写盘都抛一次异常。
        /// </summary>
#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool _replaceUnavailable = true;
#else
        private static bool _replaceUnavailable;
#endif

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>旧版 v1 存档在运行时的默认路径。</summary>
        public static string LegacyV1SavePath => Path.Combine(Application.persistentDataPath, LegacyV1FileName);

        /// <summary>用 v2 路径推导同目录下的 v1 路径（测试用隔离目录时同样适用）。</summary>
        public static string LegacyPathFor(string path)
        {
            if (string.IsNullOrEmpty(path)) return LegacyV1FileName;
            var directory = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(directory) ? LegacyV1FileName : Path.Combine(directory, LegacyV1FileName);
        }

        public static bool Exists(string path = null)
        {
            path ??= SavePath;
            return File.Exists(path) || File.Exists(path + ".tmp") || File.Exists(path + ".bak");
        }

        public static SaveFile Load() => Load(out _);

        /// <summary>
        /// restoredFromDisk 仅在确实读到有效存档（含恢复档与 v1 迁移）时为 true。
        /// 新建、全部损坏或遇到未来版本时为 false，让调用方保留 PetConfig 的初始数值。
        /// </summary>
        public static SaveFile Load(out bool restoredFromDisk, string path = null)
        {
            path ??= SavePath;
            var legacyPath = LegacyPathFor(path);
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            LastError = string.Empty;
            restoredFromDisk = false;

            var failures = new List<string>();

            // ── ① 当前格式（v2）：主档 → 完整临时档 → 备份 ──
            var candidates = new[] { path, path + ".tmp", path + ".bak" };
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (!File.Exists(candidate)) continue;

                var outcome = TryReadV2(candidate, out var loaded, out var version, out var reason);
                if (outcome == V2ReadOutcome.FutureVersion)
                {
                    // 更高版本的存档：既不归档也不覆盖，运行数据退回默认值，写盘由标志位拦下。
                    var blocked = SaveFile.CreateNew(nowUnix);
                    blocked.WriteBlocked = true;
                    blocked.BlockedSchemaVersion = version;
                    LastError = $"{Path.GetFileName(candidate)} 是 schema {version} 的存档，高于当前支持的 " +
                                $"{SaveFile.CurrentSchemaVersion}；已按只读处理并阻止写盘，避免覆盖新版本数据";
                    Debug.LogWarning("[SaveService] " + LastError);
                    return blocked;
                }

                if (outcome != V2ReadOutcome.Ok)
                {
                    failures.Add(Path.GetFileName(candidate) + "：" + reason);
                    ArchiveInvalid(candidate, nowUnix);
                    continue;
                }

                restoredFromDisk = true;
                loaded.MigratedFromV1 = false;
                loaded.WriteBlocked = false;
                loaded.BlockedSchemaVersion = 0;

                var notes = new List<string>();
                ValidateV2(loaded, notes);
                if (candidate != path)
                {
                    notes.Insert(0, "已从 " + Path.GetFileName(candidate) + " 恢复存档");
                    // 保留恢复源；修复主档途中失败或掉电，下一次仍可读恢复源。
                    try { CopyDurably(candidate, path); }
                    catch (Exception e) { notes.Add($"主档修复失败：{e.GetType().Name}"); }
                }

                Report(failures, notes);
                return loaded;
            }

            // ── ② 旧格式（v1）：只读迁移。任何情况下都不写 v1 主档 / 临时档 / 备份。 ──
            var legacyCandidates = new[] { legacyPath, legacyPath + ".tmp", legacyPath + ".bak" };
            for (var i = 0; i < legacyCandidates.Length; i++)
            {
                var candidate = legacyCandidates[i];
                if (!File.Exists(candidate)) continue;
                if (!TryReadV1(candidate, out var legacy, out var reason))
                {
                    // 旧档已损坏：不归档、不改写，保留原文件供人工排查。
                    failures.Add(Path.GetFileName(candidate) + "：" + reason);
                    continue;
                }

                restoredFromDisk = true;
                var migrated = MigrateV1(legacy, nowUnix);

                var notes = new List<string>
                {
                    $"已从旧版 {Path.GetFileName(candidate)} 迁移到 v2（v1 原文件保留）",
                };
                ValidateV2(migrated, notes);

                Report(failures, notes);
                return migrated;
            }

            if (failures.Count > 0)
            {
                LastError = string.Join("；", failures);
                Debug.LogWarning($"[SaveService] 没有可恢复的存档，已创建新档：{LastError}");
            }
            return SaveFile.CreateNew(nowUnix);
        }

        /// <summary>写入完整临时档后替换；失败时保留旧主档或可恢复备份。</summary>
        public static bool Save(SaveFile file, long nowUnix, string path = null)
        {
            if (file == null)
            {
                LastError = "存档对象为空";
                return false;
            }

            if (file.WriteBlocked)
            {
                // 兜底：即使调用方绕过了 SaveCoordinator，也不许覆盖比自己新的存档。
                LastError = $"存档来自 schema {file.BlockedSchemaVersion} 的更高版本，已拒绝写盘";
                return false;
            }

            path ??= SavePath;
            var previousVersion = file.SchemaVersion;
            var previousTimestamp = file.SavedAtUnixSeconds;
            file.SchemaVersion = SaveFile.CurrentSchemaVersion;
            file.SavedAtUnixSeconds = nowUnix;

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var tempPath = path + ".tmp";
                var backupPath = path + ".bak";
                var primaryValid = TryReadV2(path, out _, out _, out _) == V2ReadOutcome.Ok;

                // 上次可能只剩完整 .tmp，先备份它再复用临时路径，避免重试损坏唯一恢复源。
                if (!primaryValid && TryReadV2(tempPath, out _, out _, out _) == V2ReadOutcome.Ok)
                    CopyDurably(tempPath, backupPath);

                var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(file, true));
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    // 坏主档不能覆盖好备份。它已由 Load 另存为 .corrupt_*。
                    var backup = primaryValid ? backupPath : null;
                    var replaced = false;
                    if (!_replaceUnavailable)
                    {
                        try
                        {
                            File.Replace(tempPath, path, backup);
                            replaced = true;
                        }
                        catch (Exception replaceError) when (replaceError is PlatformNotSupportedException ||
                                                             replaceError is IOException)
                        {
                            // 失败一次就记住，本次运行不再尝试，避免每次写盘都抛异常。
                            _replaceUnavailable = true;
                            Debug.LogWarning("[SaveService] File.Replace 不可用（" + replaceError.GetType().Name +
                                             "），本次运行改用“备份 → 删除 → 移动”写盘。");
                        }
                    }

                    if (!replaced)
                    {
                        // 可恢复替换：删除主档前，必须已经有完整、已刷盘的备份；
                        // 若在删除与移动之间被杀，Load 会按 主档 → .tmp → .bak 的顺序恢复。
                        if (backup != null) CopyDurably(path, backup);
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }

                LastError = string.Empty;
                return true;
            }
            catch (Exception e)
            {
                file.SchemaVersion = previousVersion;
                file.SavedAtUnixSeconds = previousTimestamp;
                LastError = $"写入失败：{e.GetType().Name} {e.Message}";
                Debug.LogError("[SaveService] " + LastError);
                return false;
            }
        }

        /// <summary>
        /// 显式删除存档时同时删除恢复档与旧版 v1 备份，避免下次启动又把已删除的数据迁移回来。
        /// 只用于"玩家确认清空"这类明确动作；正常迁移路径绝不碰 v1 文件。
        /// </summary>
        public static bool Delete(string path = null)
        {
            path ??= SavePath;
            var legacyPath = LegacyPathFor(path);
            try
            {
                var candidates = new[]
                {
                    path, path + ".tmp", path + ".bak",
                    legacyPath, legacyPath + ".tmp", legacyPath + ".bak",
                };
                foreach (var candidate in candidates)
                    if (File.Exists(candidate)) File.Delete(candidate);
                LastError = string.Empty;
                return true;
            }
            catch (Exception e)
            {
                LastError = $"删除失败：{e.Message}";
                return false;
            }
        }

        public static float OfflineSeconds(SaveFile file, long nowUnix)
        {
            if (file == null || file.SavedAtUnixSeconds <= 0) return 0f;
            var delta = nowUnix - file.SavedAtUnixSeconds;
            return delta > 0 ? delta : 0f;
        }

        /// <summary>
        /// 把旧版 v1 存档迁移成 v2。用户已确认：新文件名 + 读旧档，v1 原文件保留不删。
        /// 只迁 <c>pet_classic</c> 的开心值与三种计数；饥饿 / 精力 / 投喂次数随旧玩法丢弃。
        /// 旧档开心值已满（迁移清洗后 = 1）时，周期显式写成"待确认"，与运行层读档规则一致：
        /// 迁移不会自动播一次专属演出，玩家点一下才播。
        /// </summary>
        public static SaveFile MigrateV1(SaveFileV1 legacy, long nowUnix)
        {
            if (legacy == null) throw new ArgumentNullException(nameof(legacy));

            var state = legacy.State;
            var happiness = SanitiseHappiness(state.Happiness);
            var file = SaveFile.CreateNew(nowUnix);
            // 继承旧时间戳：离线衰减要按玩家真正离开的时刻补算，而不是按迁移的这一刻。
            file.SavedAtUnixSeconds = legacy.SavedAtUnixSeconds;
            file.TotalPlaySeconds = SanitiseNonNegative(legacy.TotalPlaySeconds);
            file.UnlockedReactions = FilterKnownReactions(legacy.UnlockedReactions);
            file.Pets = new[]
            {
                new PetSaveEntry
                {
                    PetId = LegacyClassicPetId,
                    Happiness = happiness,
                    PetCount = Math.Max(0, state.PetCount),
                    PlayCount = Math.Max(0, state.PlayCount),
                    PhotoCount = Math.Max(0, state.PhotoCount),
                    HappyCycle = (int)(happiness >= 1f ? HappyCycleState.PendingConfirm : HappyCycleState.Armed),
                    ExclusiveCooldownRemaining = 0f,
                    ExclusiveStartCount = 0,
                    ExclusiveWatchedCount = 0,
                },
            };
            file.MigratedFromV1 = true;
            return file;
        }

        /// <summary>开心值清洗：NaN / ±∞ 按 0 处理（无法判断就不凭空给高分），其余夹到 [0,1]。</summary>
        public static float SanitiseHappiness(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        /// <summary>满值周期状态清洗：未知枚举值一律按 <see cref="HappyCycleState.Armed"/> 处理。</summary>
        public static HappyCycleState SanitiseHappyCycle(int value) => value switch
        {
            (int)HappyCycleState.Armed => HappyCycleState.Armed,
            (int)HappyCycleState.PendingAuto => HappyCycleState.PendingAuto,
            (int)HappyCycleState.PendingConfirm => HappyCycleState.PendingConfirm,
            (int)HappyCycleState.Released => HappyCycleState.Released,
            _ => HappyCycleState.Armed,
        };

        /// <summary>
        /// 专属冷却清洗：NaN / ±∞ / 负数一律归零。
        /// 上限由变体调参表在运行层（<c>HappyCycle.RestoreFrom</c>）收紧，存档层不知道变体。
        /// </summary>
        public static float SanitiseCooldown(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }

        /// <summary>只保留新版登记的已知反应 ID；未知 ID 属于旧玩法，迁移时丢弃。</summary>
        public static string[] FilterKnownReactions(string[] ids)
        {
            if (ids == null || ids.Length == 0 || KnownReactionIds.Length == 0) return Array.Empty<string>();

            var kept = new List<string>();
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (string.IsNullOrEmpty(id)) continue;
                if (Array.IndexOf(KnownReactionIds, id) < 0) continue;
                if (kept.Contains(id)) continue;
                kept.Add(id);
            }

            return kept.ToArray();
        }

        /// <summary>
        /// v2 校验：空数组兜底、非法 / 重复 PetId 丢弃、数值清洗。
        /// 未知 PetId（不在当前身份目录）保留在文件里原样写回，只是运行时不使用 —— 不丢数据。
        /// </summary>
        private static void ValidateV2(SaveFile file, List<string> notes)
        {
            if (file.UnlockedReactions == null) file.UnlockedReactions = Array.Empty<string>();

            var total = SanitiseNonNegative(file.TotalPlaySeconds);
            if (!total.Equals(file.TotalPlaySeconds))
            {
                notes.Add("累计时长非法，已清零");
                file.TotalPlaySeconds = total;
            }

            if (file.Pets == null || file.Pets.Length == 0)
            {
                file.Pets = Array.Empty<PetSaveEntry>();
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<PetSaveEntry>(file.Pets.Length);
            for (var i = 0; i < file.Pets.Length; i++)
            {
                var entry = file.Pets[i];
                if (entry == null)
                {
                    notes.Add($"第 {i} 条宠物记录为空，已丢弃");
                    continue;
                }

                if (!PetIdentity.IsValidStableId(entry.PetId))
                {
                    notes.Add($"第 {i} 条宠物记录的 PetId “{entry.PetId ?? "<null>"}” 非法，已丢弃");
                    continue;
                }

                if (!seen.Add(entry.PetId))
                {
                    notes.Add($"PetId “{entry.PetId}” 重复，保留第一条，丢弃后续");
                    continue;
                }

                var happiness = SanitiseHappiness(entry.Happiness);
                if (!happiness.Equals(entry.Happiness))
                {
                    notes.Add($"宠物 “{entry.PetId}” 的开心值 {entry.Happiness} 非法或越界，已清洗为 {happiness}");
                    entry.Happiness = happiness;
                }

                entry.PetCount = SanitiseCount(entry.PetCount, entry.PetId, "逗弄次数", notes);
                entry.PlayCount = SanitiseCount(entry.PlayCount, entry.PetId, "追球次数", notes);
                entry.PhotoCount = SanitiseCount(entry.PhotoCount, entry.PetId, "拍照次数", notes);

                // 满值周期（P3 新增字段）：旧档缺字段时是 0 / 0 / 0 / 0，正好等于"重新武装、无冷却"。
                var cycle = SanitiseHappyCycle(entry.HappyCycle);
                if ((int)cycle != entry.HappyCycle)
                {
                    notes.Add($"宠物 “{entry.PetId}” 的满值周期状态 {entry.HappyCycle} 未知，已按 Armed 处理");
                    entry.HappyCycle = (int)cycle;
                }

                var cooldown = SanitiseCooldown(entry.ExclusiveCooldownRemaining);
                if (!cooldown.Equals(entry.ExclusiveCooldownRemaining))
                {
                    notes.Add($"宠物 “{entry.PetId}” 的专属冷却 {entry.ExclusiveCooldownRemaining} 非法，已清洗为 {cooldown}");
                    entry.ExclusiveCooldownRemaining = cooldown;
                }

                entry.ExclusiveStartCount = SanitiseCount(entry.ExclusiveStartCount, entry.PetId, "专属开始次数", notes);
                entry.ExclusiveWatchedCount = SanitiseCount(entry.ExclusiveWatchedCount, entry.PetId, "专属看完次数", notes);
                // 共同大笑（P4 新增字段）：旧档缺字段时是 0，负数归零。
                entry.SocialWatchedCount = SanitiseCount(entry.SocialWatchedCount, entry.PetId, "共同大笑看完次数", notes);
                kept.Add(entry);
            }

            file.Pets = kept.ToArray();
        }

        private static int SanitiseCount(int value, string petId, string label, List<string> notes)
        {
            if (value >= 0) return value;
            notes.Add($"宠物 “{petId}” 的{label}为负，已归零");
            return 0;
        }

        private static float SanitiseNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }

        private static void Report(List<string> failures, List<string> notes)
        {
            if (failures.Count == 0 && notes.Count == 0)
            {
                LastError = string.Empty;
                return;
            }

            var all = new List<string>(failures.Count + notes.Count);
            all.AddRange(failures);
            all.AddRange(notes);
            LastError = string.Join("；", all);
            Debug.LogWarning("[SaveService] " + LastError);
        }

        private static V2ReadOutcome TryReadV2(string path, out SaveFile file, out int version, out string reason)
        {
            file = null;
            version = 0;
            reason = string.Empty;

            if (!TryReadJson(path, out var json, out reason)) return V2ReadOutcome.Invalid;
            if (!TryParseJson(json, out SchemaProbe probe)) return V2ReadOutcome.Invalid;

            version = probe.SchemaVersion;
            if (version > SaveFile.CurrentSchemaVersion) return V2ReadOutcome.FutureVersion;
            if (version <= 0)
            {
                reason = $"schema 版本 {version} 非法";
                return V2ReadOutcome.Invalid;
            }

            if (version < SaveFile.CurrentSchemaVersion)
            {
                reason = $"schema 版本 {version} 不是当前格式";
                return V2ReadOutcome.Invalid;
            }

            if (!TryParseJson(json, out file) || file == null)
            {
                reason = "存档结构无法解析";
                return V2ReadOutcome.Invalid;
            }

            file.SchemaVersion = version;
            return V2ReadOutcome.Ok;
        }

        private static bool TryReadV1(string path, out SaveFileV1 file, out string reason)
        {
            file = null;
            if (!TryReadJson(path, out var json, out reason)) return false;
            if (!TryParseJson(json, out SchemaProbe probe))
            {
                reason = "存档不是合法 JSON";
                return false;
            }

            if (probe.SchemaVersion >= SaveFile.CurrentSchemaVersion)
            {
                reason = $"schema 版本 {probe.SchemaVersion} 不是旧版格式";
                return false;
            }

            if (probe.SchemaVersion <= 0)
            {
                reason = $"schema 版本 {probe.SchemaVersion} 非法";
                return false;
            }

            if (!TryParseJson(json, out file) || file == null)
            {
                reason = "旧版存档结构无法解析";
                return false;
            }

            file.SchemaVersion = probe.SchemaVersion;
            return true;
        }

        private static bool TryReadJson(string path, out string json, out string reason)
        {
            json = null;
            reason = string.Empty;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                reason = "读取异常：" + e.GetType().Name;
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "存档为空";
                return false;
            }

            return true;
        }

        private static bool TryParseJson<T>(string json, out T value) where T : class
        {
            try
            {
                value = JsonUtility.FromJson<T>(json);
                return value != null;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private static void CopyDurably(string source, string destination)
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
        }

        private static void ArchiveInvalid(string path, long nowUnix)
        {
            try { File.Copy(path, path + $".corrupt_{nowUnix}_{Guid.NewGuid():N}"); }
            catch { /* 归档失败不能阻断后续 .tmp / .bak 恢复。 */ }
        }

        /// <summary>读版本号用的小结构：先确认 schema 再决定按哪套结构解析。</summary>
        [Serializable]
        private sealed class SchemaProbe
        {
            public int SchemaVersion;
        }

        private enum V2ReadOutcome
        {
            Ok = 0,
            Invalid = 1,
            FutureVersion = 2,
        }
    }
}
