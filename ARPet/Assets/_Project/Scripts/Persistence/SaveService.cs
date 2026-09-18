using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ZQXNXS.ARPet.Persistence
{
    /// <summary>
    /// 本地版本化 JSON 存档。优先原子替换并保留 .bak；平台不支持替换时，
    /// 先持久化备份再换主档。读取按主档、完整临时档、备份依次恢复。
    /// 可选 path 供隔离的文件系统测试使用，运行时默认 persistentDataPath。
    /// </summary>
    public static class SaveService
    {
        public const string FileName = "zqxnxs_save_v1.json";

        public static string LastError { get; private set; } = string.Empty;

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists(string path = null)
        {
            path ??= SavePath;
            return File.Exists(path) || File.Exists(path + ".tmp") || File.Exists(path + ".bak");
        }

        public static SaveFile Load() => Load(out _);

        /// <summary>
        /// restoredFromDisk 仅在确实读到有效存档（含恢复档）时为 true。
        /// 新建或全部损坏时返回 false，让调用方保留 PetConfig 的初始数值。
        /// </summary>
        public static SaveFile Load(out bool restoredFromDisk, string path = null)
        {
            path ??= SavePath;
            LastError = string.Empty;
            restoredFromDisk = false;
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var failures = new List<string>();

            foreach (var candidate in new[] { path, path + ".tmp", path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                if (!TryRead(candidate, out var loaded, out var reason))
                {
                    failures.Add(Path.GetFileName(candidate) + ": " + reason);
                    ArchiveInvalid(candidate, nowUnix);
                    continue;
                }

                restoredFromDisk = true;
                loaded.UnlockedReactions ??= Array.Empty<string>();
                Migrate(loaded);

                if (candidate != path)
                {
                    LastError = "已从 " + Path.GetFileName(candidate) + " 恢复存档";
                    // 保留恢复源；修复主档途中失败或掉电，下一次仍可读恢复源。
                    try { CopyDurably(candidate, path); }
                    catch (Exception e) { LastError += $"；主档修复失败：{e.GetType().Name}"; }
                    Debug.LogWarning("[SaveService] " + LastError);
                }
                return loaded;
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
                var primaryValid = TryRead(path, out _, out _);

                // 上次可能只剩完整 .tmp，先备份它再复用临时路径，避免重试损坏唯一恢复源。
                if (!primaryValid && TryRead(tempPath, out _, out _))
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
                    try
                    {
                        File.Replace(tempPath, path, backup);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // 可恢复替换：删除主档前，必须已经有完整、已刷盘的备份。
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

        /// <summary>显式删除存档时同时删除恢复档，避免下次启动又把已删除数据恢复回来。</summary>
        public static bool Delete(string path = null)
        {
            path ??= SavePath;
            try
            {
                foreach (var candidate in new[] { path, path + ".tmp", path + ".bak" })
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

        private static bool TryRead(string path, out SaveFile file, out string reason)
        {
            file = null;
            reason = string.Empty;
            try
            {
                if (!File.Exists(path)) return false;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    reason = "存档为空";
                    return false;
                }

                file = JsonUtility.FromJson<SaveFile>(json);
                if (file == null || !file.IsStructurallyValid())
                {
                    reason = "存档结构或 schema 版本无效";
                    file = null;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                reason = "读取异常：" + e.GetType().Name;
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

        private static void Migrate(SaveFile file)
        {
            // 版本 1 暂无迁移步骤；以后按 SchemaVersion 依次补齐。
            if (file.SchemaVersion < SaveFile.CurrentSchemaVersion)
                file.SchemaVersion = SaveFile.CurrentSchemaVersion;
        }

        private static void ArchiveInvalid(string path, long nowUnix)
        {
            try { File.Copy(path, path + $".corrupt_{nowUnix}_{Guid.NewGuid():N}"); }
            catch { /* 归档失败不能阻断后续 .tmp / .bak 恢复。 */ }
        }
    }
}
