using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Persistence;
using ZQXNXS.ARPet.Pet;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 存档层（schema 2，多宠）的测试。使用独立临时目录进行真实文件读写，绝不触碰玩家的默认存档路径。
    /// 覆盖三类规则：
    /// <list type="bullet">
    /// <item>通用性质：原子写、.tmp / .bak 恢复、损坏回退、写盘节流、Flush；</item>
    /// <item>迁移：只有旧 v1 时一次性迁移到 v2，v1 原文件不删不改；</item>
    /// <item>多宠：按 PetId 逐只恢复/写回，非法与重复条目清洗，未知条目保留，未来版本只读保护。</item>
    /// </list>
    /// </summary>
    public sealed class PersistenceTests
    {
        private string _testRoot;
        private string _directory;
        private string _path;
        private string _legacyPath;
        private PetConfig _config;

        /// <summary>本用例创建过的宠物运行时（TearDown 只清引用，它们不是场景对象）。</summary>
        private readonly List<PetRuntime> _pets = new();

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ARPetSaveTests"));
            _directory = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, SaveService.FileName);
            _legacyPath = SaveService.LegacyPathFor(_path);
            _config = ScriptableObject.CreateInstance<PetConfig>();
            _pets.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _pets.Clear();
            UnityEngine.Object.DestroyImmediate(_config);
            var fullPath = Path.GetFullPath(_directory);
            if (!fullPath.StartsWith(_testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("测试清理路径越界");
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        }

        // ── 通用性质 ────────────────────────────────────────────────────────────

        [Test]
        public void MissingSave_IsExplicitlyReportedAsNew()
        {
            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsFalse(restored);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(SaveFile.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(0, loaded.Pets.Length);
            Assert.IsFalse(loaded.MigratedFromV1);
            Assert.IsFalse(SaveService.Exists(_path));
            Assert.IsFalse(File.Exists(_legacyPath), "没有存档时不得凭空创建 v1 文件");
        }

        [Test]
        public void CorruptSaveWithoutRecovery_IsReportedAsNewAndArchived()
        {
            File.WriteAllText(_path, "{broken json");
            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsFalse(restored);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, Directory.GetFiles(_directory, "*.corrupt_*").Length);
        }

        [Test]
        public void SaveRestoreAndNextSession_PreserveValues()
        {
            Assert.IsTrue(SaveService.Save(Saved(0.25f, 1000), 1000, _path));
            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);

            var classic = Runtime("pet_classic");
            Assert.AreEqual(1, SaveCoordinator.ApplyTo(loaded, _pets, loaded.SavedAtUnixSeconds, 0f));
            Assert.AreEqual(0.25f, classic.Needs.Snapshot.Happiness, 0.0001f);

            classic.Needs.IncrementCount(InteractionEventKind.Pet);
            var coordinator = new SaveCoordinator(_pets, loaded,
                (file, utc) => SaveService.Save(file, utc, _path));
            Assert.IsTrue(coordinator.Tick(1f, 1f));

            var reread = SaveService.Load(out restored, _path);
            Assert.IsTrue(restored);
            Assert.AreEqual(8, reread.Pets[0].PetCount);
            Assert.AreEqual(6, reread.Pets[0].PlayCount);
            Assert.AreEqual(5, reread.Pets[0].PhotoCount);
            Assert.AreEqual(101f, reread.TotalPlaySeconds, 0.0001f);
            CollectionAssert.AreEqual(new[] { "happy_pet" }, reread.UnlockedReactions);
        }

        [Test]
        public void SecondSave_KeepsPreviousCommittedFileAsBackup()
        {
            Assert.IsTrue(SaveService.Save(Saved(0.25f, 1000), 1000, _path));
            Assert.IsTrue(SaveService.Save(Saved(0.5f, 2000), 2000, _path));
            Assert.AreEqual(0.5f, SaveService.Load(out _, _path).Pets[0].Happiness, 0.0001f);

            var backup = JsonUtility.FromJson<SaveFile>(File.ReadAllText(_path + ".bak"));
            Assert.AreEqual(0.25f, backup.Pets[0].Happiness, 0.0001f);
            Assert.IsFalse(File.Exists(_path + ".tmp"));
        }

        [Test]
        public void MissingPrimary_RecoversCompleteTemporaryFileBeforeBackup()
        {
            Assert.IsTrue(SaveService.Save(Saved(0.25f, 1000), 1000, _path));
            File.Copy(_path, _path + ".bak");
            File.WriteAllText(_path + ".tmp", JsonUtility.ToJson(Saved(0.75f, 2000)));
            File.Delete(_path); // 模拟主档替换期间中断

            var recovered = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.AreEqual(0.75f, recovered.Pets[0].Happiness, 0.0001f);
            Assert.IsTrue(File.Exists(_path));
            Assert.AreEqual(0.75f, SaveService.Load(out _, _path).Pets[0].Happiness, 0.0001f);
        }

        [Test]
        public void ValidPrimary_WinsOverUncommittedTemporaryFile()
        {
            Assert.IsTrue(SaveService.Save(Saved(0.25f, 1000), 1000, _path));
            File.WriteAllText(_path + ".tmp", JsonUtility.ToJson(Saved(0.75f, 2000)));

            Assert.AreEqual(0.25f, SaveService.Load(out var restored, _path).Pets[0].Happiness, 0.0001f);
            Assert.IsTrue(restored);
        }

        [Test]
        public void BrokenPrimaryAndTemporaryFile_RecoverBackup()
        {
            Assert.IsTrue(SaveService.Save(Saved(0.25f, 1000), 1000, _path));
            Assert.IsTrue(SaveService.Save(Saved(0.5f, 2000), 2000, _path));
            File.WriteAllText(_path, "{");
            File.WriteAllText(_path + ".tmp", "{");

            var recovered = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.AreEqual(0.25f, recovered.Pets[0].Happiness, 0.0001f);
            Assert.AreEqual(2, Directory.GetFiles(_directory, "*.corrupt_*").Length);
            Assert.AreEqual(0.25f, SaveService.Load(out _, _path).Pets[0].Happiness, 0.0001f);
        }

        [Test]
        public void FailedWrite_PreservesOldFileAndLastSuccessfulTimestamp()
        {
            var file = Saved(0.25f, 1000);
            Assert.IsTrue(SaveService.Save(file, 1000, _path));

            Directory.CreateDirectory(_path + ".tmp"); // 确定性地阻止创建临时文件
            file.Pets[0].Happiness = 0.99f;
            LogAssert.Expect(LogType.Error, new Regex("\\[SaveService\\] 写入失败："));
            Assert.IsFalse(SaveService.Save(file, 2000, _path));

            Assert.AreEqual(1000L, file.SavedAtUnixSeconds);
            Assert.AreEqual(0.25f, SaveService.Load(out var restored, _path).Pets[0].Happiness, 0.0001f);
            Assert.IsTrue(restored);
        }

        [Test]
        public void ExplicitDeletion_RemovesAllRecoveryCandidates()
        {
            Assert.IsTrue(SaveService.Save(Saved(0.25f, 1000), 1000, _path));
            Assert.IsTrue(SaveService.Save(Saved(0.5f, 2000), 2000, _path));
            File.WriteAllText(_path + ".tmp", JsonUtility.ToJson(Saved(0.75f, 3000)));
            File.WriteAllText(_legacyPath, JsonUtility.ToJson(LegacyV1(0.1f, 1, 1000)));

            Assert.IsTrue(SaveService.Delete(_path));
            Assert.IsFalse(SaveService.Exists(_path));
            Assert.IsFalse(File.Exists(_legacyPath), "显式删除必须连旧 v1 一起清掉，否则下次启动会把它迁回来");

            SaveService.Load(out var restored, _path);
            Assert.IsFalse(restored);
        }

        [Test]
        public void FailedAutomaticSaves_AreThrottledAndRetriedWithoutClearingDirty()
        {
            var classic = Runtime("pet_classic");
            classic.Needs.Apply(PetNeed.Happiness, 0.1f, 0f);

            var canWrite = false;
            var attempts = 0;
            var coordinator = new SaveCoordinator(_pets, Saved(0.25f, 1000), (file, utc) =>
            {
                attempts++;
                return canWrite;
            });

            for (var frame = 0; frame < 120; frame++) coordinator.Tick(1f / 60f, frame / 60f);
            Assert.AreEqual(1, attempts, "两秒内不能每帧重试失败写盘");
            Assert.IsTrue(classic.Needs.Dirty);

            Assert.IsFalse(coordinator.Tick(0f, 5.1f));
            Assert.AreEqual(2, attempts);

            canWrite = true;
            Assert.IsFalse(coordinator.Tick(0f, 6f));
            Assert.IsTrue(coordinator.Tick(0f, 10.2f));
            Assert.AreEqual(3, attempts);
            Assert.AreEqual(1, coordinator.SaveCount);
            Assert.IsFalse(classic.Needs.Dirty);
        }

        [Test]
        public void PlayTimeChange_IsSavedEvenWhenNeedsDoNotChange()
        {
            var classic = Runtime("pet_classic");
            var savedSeconds = 0f;
            var coordinator = new SaveCoordinator(_pets, Saved(0.25f, 1000), (file, utc) =>
            {
                savedSeconds = file.TotalPlaySeconds;
                return true;
            });

            Assert.IsFalse(classic.Needs.Dirty);
            Assert.IsTrue(coordinator.Tick(1f, 1f));
            Assert.AreEqual(101f, savedSeconds, 0.0001f);
        }

        [Test]
        public void ClockRollback_DoesNotApplyNegativeOfflineTime()
        {
            Assert.AreEqual(0f, SaveService.OfflineSeconds(Saved(0.25f, 1000), 900), 0.0001f);
        }

        // ── 迁移：只有 v1 ───────────────────────────────────────────────────────

        [Test]
        public void LegacyOnlySave_IsMigratedToV2_AndV1FileIsUntouched()
        {
            File.WriteAllText(_legacyPath, JsonUtility.ToJson(LegacyV1(0.42f, 7, 1700000000), true));
            var legacyBefore = File.ReadAllBytes(_legacyPath);

            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.IsTrue(loaded.MigratedFromV1, "必须标记为来自 v1 迁移，调用方据此立刻写出 v2");
            Assert.AreEqual(SaveFile.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.IsFalse(File.Exists(_path), "迁移只在内存里完成，读档不能顺手写 v2");

            Assert.AreEqual(1, loaded.Pets.Length);
            Assert.AreEqual(SaveService.LegacyClassicPetId, loaded.Pets[0].PetId);
            Assert.AreEqual(0.42f, loaded.Pets[0].Happiness, 0.0001f);
            Assert.AreEqual(7, loaded.Pets[0].PetCount);
            Assert.AreEqual(6, loaded.Pets[0].PlayCount);
            Assert.AreEqual(5, loaded.Pets[0].PhotoCount);
            Assert.AreEqual(100f, loaded.TotalPlaySeconds, 0.0001f);
            Assert.AreEqual(1700000000L, loaded.SavedAtUnixSeconds, "旧时间戳要继承，离线补算才按玩家真正离开的时刻算");

            var classic = Runtime("pet_classic");
            var calm = Runtime("pet_calm");
            Assert.AreEqual(1, SaveCoordinator.ApplyTo(loaded, _pets, loaded.SavedAtUnixSeconds, 0f));
            Assert.AreEqual(0.42f, classic.Needs.Snapshot.Happiness, 0.0001f);
            Assert.AreEqual(7, classic.Needs.Snapshot.PetCount);
            Assert.AreEqual(0.6f, calm.Needs.Snapshot.Happiness, 0.0001f, "存档里没有的宠物必须保持 PetConfig 初始值");

            var coordinator = new SaveCoordinator(_pets, loaded,
                (file, utc) => SaveService.Save(file, utc, _path));
            Assert.IsTrue(coordinator.Flush(0f));

            Assert.IsTrue(File.Exists(_path), "迁移后必须写出 v2 主档");
            var reread = SaveService.Load(out var restoredAgain, _path);
            Assert.IsTrue(restoredAgain);
            Assert.IsFalse(reread.MigratedFromV1);
            Assert.AreEqual(2, reread.Pets.Length, "写回时要把本局两只宠物都补进存档");
            CollectionAssert.AreEqual(legacyBefore, File.ReadAllBytes(_legacyPath));
        }

        [Test]
        public void LegacyValues_AreSanitisedAndLegacyFieldsAreNotCarriedIntoV2()
        {
            var brokenLegacy = new SaveFileV1
            {
                SavedAtUnixSeconds = 1000,
                TotalPlaySeconds = -9f,
                UnlockedReactions = new[] { "first_feed", "happy_pet" },
                State = new PetState
                {
                    Happiness = -0.5f,
                    PetCount = -3,
                    PlayCount = -1,
                    PhotoCount = -2,
                    FeedCount = 12,
                    Hunger = 0.9f,
                    Energy = 0.1f,
                },
            };
            File.WriteAllText(_legacyPath, JsonUtility.ToJson(brokenLegacy, true));

            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.IsTrue(loaded.MigratedFromV1);
            Assert.AreEqual(0f, loaded.Pets[0].Happiness, 0.0001f, "负数开心值清洗到 0");
            Assert.AreEqual(0, loaded.Pets[0].PetCount);
            Assert.AreEqual(0, loaded.Pets[0].PlayCount);
            Assert.AreEqual(0, loaded.Pets[0].PhotoCount);
            Assert.AreEqual(0f, loaded.TotalPlaySeconds, 0.0001f, "负数累计时长归零");
            Assert.AreEqual(0, loaded.UnlockedReactions.Length, "旧玩法的反应 ID 不在 KnownReactionIds 里，不迁移");

            // NaN / ±∞ 无法写进 JSON，直接验证迁移用的清洗函数与新档迁移结果。
            Assert.AreEqual(0f, SaveService.SanitiseHappiness(float.NaN), 0f);
            Assert.AreEqual(0f, SaveService.SanitiseHappiness(float.PositiveInfinity), 0f);
            Assert.AreEqual(1f, SaveService.SanitiseHappiness(5f), 0.0001f);
            var nanMigrated = SaveService.MigrateV1(new SaveFileV1
            {
                State = new PetState { Happiness = float.NaN, PetCount = -5 },
            }, 1000);
            Assert.AreEqual(0f, nanMigrated.Pets[0].Happiness, 0.0001f);
            Assert.AreEqual(0, nanMigrated.Pets[0].PetCount);

            Runtime("pet_classic");
            var coordinator = new SaveCoordinator(_pets, loaded,
                (file, utc) => SaveService.Save(file, utc, _path));
            Assert.IsTrue(coordinator.Flush(0f));

            var json = File.ReadAllText(_path);
            Assert.IsFalse(json.Contains("FeedCount"), "v2 不再写入投喂次数");
            Assert.IsFalse(json.Contains("Hunger"), "v2 不再写入饥饿");
            Assert.IsFalse(json.Contains("Energy"), "v2 不再写入精力");
            Assert.IsFalse(json.Contains("\"State\""), "v2 不再有单份 State 字段");
        }

        [Test]
        public void BothVersionsPresent_V2WinsAndLegacyIsIgnored()
        {
            File.WriteAllText(_legacyPath, JsonUtility.ToJson(LegacyV1(0.1f, 1, 1000), true));
            Assert.IsTrue(SaveService.Save(Saved(0.9f, 2000), 2000, _path));

            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.IsFalse(loaded.MigratedFromV1, "有 v2 时不得再迁 v1");
            Assert.AreEqual(1, loaded.Pets.Length);
            Assert.AreEqual(0.9f, loaded.Pets[0].Happiness, 0.0001f);
        }

        // ── v2 校验 ────────────────────────────────────────────────────────────

        [Test]
        public void DuplicateAndIllegalPetIds_AreDroppedOnLoad()
        {
            var file = SaveFile.CreateNew(1000);
            file.Pets = new[]
            {
                new PetSaveEntry { PetId = "pet_classic", Happiness = 0.11f },
                new PetSaveEntry { PetId = "pet_classic", Happiness = 0.99f },
                new PetSaveEntry { PetId = string.Empty, Happiness = 0.5f },
                new PetSaveEntry { PetId = "pet calm!", Happiness = 0.3f },
            };
            Assert.IsTrue(SaveService.Save(file, 1000, _path));

            var loaded = SaveService.Load(out _, _path);
            Assert.AreEqual(1, loaded.Pets.Length, "重复与非法的 PetId 都必须被丢弃");
            Assert.AreEqual("pet_classic", loaded.Pets[0].PetId);
            Assert.AreEqual(0.11f, loaded.Pets[0].Happiness, 0.0001f, "重复 PetId 保留第一条");
            StringAssert.Contains("重复", SaveService.LastError);
            StringAssert.Contains("非法", SaveService.LastError);
        }

        [Test]
        public void MissingPetInSave_GetsConfigDefaults_AndIsAppendedOnWrite()
        {
            Assert.IsTrue(SaveService.Save(Saved(0.25f, 1000), 1000, _path));
            var loaded = SaveService.Load(out _, _path);

            var classic = Runtime("pet_classic");
            var calm = Runtime("pet_calm");
            Assert.AreEqual(0.6f, calm.Needs.Snapshot.Happiness, 0.0001f);

            Assert.AreEqual(1, SaveCoordinator.ApplyTo(loaded, _pets, loaded.SavedAtUnixSeconds, 0f));
            Assert.AreEqual(0.25f, classic.Needs.Snapshot.Happiness, 0.0001f);
            Assert.AreEqual(0.6f, calm.Needs.Snapshot.Happiness, 0.0001f, "缺少的宠物不能被改成 0 分");

            var coordinator = new SaveCoordinator(_pets, loaded,
                (file, utc) => SaveService.Save(file, utc, _path));
            Assert.IsTrue(coordinator.Flush(0f));

            var reread = SaveService.Load(out _, _path);
            Assert.AreEqual(2, reread.Pets.Length, "写回时必须给缺失的宠物补出条目");
            Assert.IsTrue(reread.TryGetPet("pet_calm", out var calmEntry));
            Assert.AreEqual(0.6f, calmEntry.Happiness, 0.0001f);
        }

        [Test]
        public void UnknownPetEntry_IsPreservedOnWriteBack()
        {
            var file = SaveFile.CreateNew(1000);
            file.Pets = new[]
            {
                new PetSaveEntry { PetId = "pet_classic", Happiness = 0.25f },
                new PetSaveEntry { PetId = "pet_future", Happiness = 0.75f, PetCount = 4 },
            };
            Assert.IsTrue(SaveService.Save(file, 1000, _path));

            var loaded = SaveService.Load(out _, _path);
            Assert.AreEqual(2, loaded.Pets.Length, "未知 PetId 运行时不使用，但读取时不能丢");

            Runtime("pet_classic");
            var coordinator = new SaveCoordinator(_pets, loaded,
                (writerFile, utc) => SaveService.Save(writerFile, utc, _path));
            Assert.IsTrue(coordinator.Flush(0f));

            var reread = SaveService.Load(out _, _path);
            Assert.AreEqual(2, reread.Pets.Length);
            Assert.IsTrue(reread.TryGetPet("pet_future", out var future), "写回后未知条目必须原样保留");
            Assert.AreEqual(0.75f, future.Happiness, 0.0001f);
            Assert.AreEqual(4, future.PetCount);
        }

        [Test]
        public void FutureSchemaSave_IsReadOnlyAndNeverOverwritten()
        {
            const string futureJson =
                "{ \"SchemaVersion\": 99, \"SavedAtUnixSeconds\": 1000, \"Pets\": [ { \"PetId\": \"pet_classic\", \"Happiness\": 1.0 } ] }";
            File.WriteAllText(_path, futureJson);
            var before = File.ReadAllBytes(_path);

            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsFalse(restored, "未来版本的数据不可信，运行数据必须退回默认值");
            Assert.IsTrue(loaded.WriteBlocked);
            Assert.AreEqual(99, loaded.BlockedSchemaVersion);
            Assert.AreEqual(0, loaded.Pets.Length);
            Assert.AreEqual(0, Directory.GetFiles(_directory, "*.corrupt_*").Length,
                "未来版本存档不能被当成损坏档归档");

            var classic = Runtime("pet_classic");
            Assert.AreEqual(0.6f, classic.Needs.Snapshot.Happiness, 0.0001f);

            var writes = 0;
            var coordinator = new SaveCoordinator(_pets, loaded, (file, utc) =>
            {
                writes++;
                return true;
            });
            Assert.IsTrue(coordinator.WriteBlocked);
            Assert.IsFalse(coordinator.Tick(1f, 10f));
            Assert.IsFalse(coordinator.Flush(10f));
            Assert.AreEqual(0, writes, "被只读保护时一律不得写盘");
            Assert.IsFalse(SaveService.Save(loaded, 2000, _path), "即使直接调用 SaveService 也不许覆盖新版本存档");

            CollectionAssert.AreEqual(before, File.ReadAllBytes(_path));
            Assert.IsFalse(File.Exists(_path + ".tmp"));
            Assert.IsFalse(File.Exists(_path + ".bak"));
        }

        // ── 多宠写盘 ───────────────────────────────────────────────────────────

        [Test]
        public void TwoDirtyPets_WriteOnceWithinThrottle_WithBothEntries()
        {
            var file = SaveFile.CreateNew(1000);
            var classic = Runtime("pet_classic");
            var calm = Runtime("pet_calm");

            var writes = 0;
            var coordinator = new SaveCoordinator(_pets, file, (writerFile, utc) =>
            {
                writes++;
                return true;
            });

            classic.Needs.Apply(PetNeed.Happiness, 0.1f, 0f);  // 0.6 → 0.7
            calm.Needs.Apply(PetNeed.Happiness, -0.2f, 0f);    // 0.6 → 0.4

            Assert.IsTrue(coordinator.Tick(0f, 0f));
            Assert.AreEqual(1, writes, "两只宠物同时脏也只能写一次");
            Assert.IsFalse(coordinator.Tick(1f, 1f), "5 秒节流内不得再次写盘");
            Assert.AreEqual(1, writes);

            Assert.AreEqual(2, file.Pets.Length);
            Assert.IsTrue(file.TryGetPet("pet_classic", out var classicEntry));
            Assert.IsTrue(file.TryGetPet("pet_calm", out var calmEntry));
            Assert.AreEqual(0.7f, classicEntry.Happiness, 0.0001f);
            Assert.AreEqual(0.4f, calmEntry.Happiness, 0.0001f);
            Assert.IsFalse(classic.Needs.Dirty);
            Assert.IsFalse(calm.Needs.Dirty);

            classic.Needs.Apply(PetNeed.Happiness, 0.1f, 1f);  // 0.8
            calm.Needs.Apply(PetNeed.Happiness, 0.1f, 1f);     // 0.5
            Assert.IsTrue(coordinator.Tick(0f, 5.1f));
            Assert.AreEqual(2, writes);

            // 写盘会重建条目对象，断言要用重建后的引用。
            Assert.IsTrue(file.TryGetPet("pet_classic", out classicEntry));
            Assert.IsTrue(file.TryGetPet("pet_calm", out calmEntry));
            Assert.AreEqual(0.8f, classicEntry.Happiness, 0.0001f);
            Assert.AreEqual(0.5f, calmEntry.Happiness, 0.0001f);
        }

        // ── 满值周期（P3 新增字段） ─────────────────────────────────────────────

        [Test]
        public void LegacyFullHappiness_IsMigratedAsPendingConfirm()
        {
            File.WriteAllText(_legacyPath, JsonUtility.ToJson(LegacyV1(1f, 7, 1000), true));

            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.IsTrue(loaded.MigratedFromV1);
            Assert.AreEqual(1f, loaded.Pets[0].Happiness, 0.0001f);
            Assert.AreEqual((int)HappyCycleState.PendingConfirm, loaded.Pets[0].HappyCycle,
                "旧档满值迁移后是“待确认”：不会在读档瞬间自动播一次专属演出");
            Assert.AreEqual(0, loaded.Pets[0].ExclusiveStartCount);
            Assert.AreEqual(0, loaded.Pets[0].ExclusiveWatchedCount);

            var classic = Runtime("pet_classic");
            SaveCoordinator.ApplyTo(loaded, _pets, loaded.SavedAtUnixSeconds, 0f);
            Assert.AreEqual(HappyCycleState.PendingConfirm, classic.Snapshot.HappyCycle);
            Assert.AreEqual(0, classic.Snapshot.ExclusiveStartCount);
        }

        [Test]
        public void V2EntryWithoutCycleFields_LoadsAsArmed()
        {
            // 旧 v2 存档没有 P3 的四个字段：缺字段必须读成 Armed / 0 / 0 / 0，不能读崩。
            const string oldJson =
                "{ \"SchemaVersion\": 2, \"SavedAtUnixSeconds\": 1000, \"TotalPlaySeconds\": 10, " +
                "\"Pets\": [ { \"PetId\": \"pet_classic\", \"Happiness\": 0.42, \"PetCount\": 3, " +
                "\"PlayCount\": 2, \"PhotoCount\": 1 } ] }";
            File.WriteAllText(_path, oldJson);

            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.AreEqual(0, loaded.Pets[0].HappyCycle, "缺字段读成 Armed（0）");
            Assert.AreEqual(0f, loaded.Pets[0].ExclusiveCooldownRemaining, 0.0001f);
            Assert.AreEqual(0, loaded.Pets[0].ExclusiveStartCount);
            Assert.AreEqual(0, loaded.Pets[0].ExclusiveWatchedCount);

            var classic = Runtime("pet_classic");
            SaveCoordinator.ApplyTo(loaded, _pets, loaded.SavedAtUnixSeconds, 0f);
            Assert.AreEqual(HappyCycleState.Armed, classic.Snapshot.HappyCycle);
            Assert.AreEqual(0.42f, classic.Snapshot.Happiness, 0.0001f);
        }

        [Test]
        public void CycleFields_RoundTripThroughSaveFile()
        {
            var classic = Runtime("pet_classic");
            classic.RestoreFrom(new PetState
            {
                Happiness = 0.9f,
                HappyCycle = HappyCycleState.PendingAuto,
                ExclusiveCooldownRemaining = 3.5f,
                ExclusiveStartCount = 2,
                ExclusiveWatchedCount = 1,
            });
            Assert.AreEqual(HappyCycleState.PendingAuto, classic.Snapshot.HappyCycle,
                "未满值的 PendingAuto 照存档恢复");

            var coordinator = new SaveCoordinator(_pets, SaveFile.CreateNew(1000),
                (file, utc) => SaveService.Save(file, utc, _path));
            Assert.IsTrue(coordinator.Flush(0f));

            var reread = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.IsTrue(reread.TryGetPet("pet_classic", out var entry));
            Assert.AreEqual((int)HappyCycleState.PendingAuto, entry.HappyCycle);
            Assert.AreEqual(3.5f, entry.ExclusiveCooldownRemaining, 0.0001f);
            Assert.AreEqual(2, entry.ExclusiveStartCount);
            Assert.AreEqual(1, entry.ExclusiveWatchedCount);

            // 读回到一只新的运行时，四个字段必须逐一还原。
            var revived = new PetRuntime(IdentityOf("pet_classic"), _config, NullPresenter.Instance);
            revived.Start();
            SaveCoordinator.ApplyTo(reread, new[] { revived }, reread.SavedAtUnixSeconds, 0f);

            Assert.AreEqual(HappyCycleState.PendingAuto, revived.Snapshot.HappyCycle);
            Assert.AreEqual(3.5f, revived.Snapshot.ExclusiveCooldownRemaining, 0.0001f);
            Assert.AreEqual(2, revived.Snapshot.ExclusiveStartCount);
            Assert.AreEqual(1, revived.Snapshot.ExclusiveWatchedCount);
            Assert.AreEqual(0.9f, revived.Snapshot.Happiness, 0.0001f);
        }

        // ── 辅助 ────────────────────────────────────────────────────────────────

        /// <summary>一只宠物在 v2 里的基准快照：PetCount 7 / PlayCount 6 / PhotoCount 5。</summary>
        private static SaveFile Saved(float happiness, long time)
        {
            var file = SaveFile.CreateNew(time);
            file.TotalPlaySeconds = 100f;
            file.UnlockedReactions = new[] { "happy_pet" };
            file.Pets = new[]
            {
                new PetSaveEntry
                {
                    PetId = SaveService.LegacyClassicPetId,
                    Happiness = happiness,
                    PetCount = 7,
                    PlayCount = 6,
                    PhotoCount = 5,
                },
            };
            return file;
        }

        /// <summary>旧版 v1 存档。旧玩法的 Hunger / Energy / FeedCount 一起带上，验证它们不会被迁进 v2。</summary>
        private static SaveFileV1 LegacyV1(float happiness, int petCount, long savedAt) => new SaveFileV1
        {
            SchemaVersion = SaveFileV1.SchemaVersionValue,
            SavedAtUnixSeconds = savedAt,
            TotalPlaySeconds = 100f,
            UnlockedReactions = new[] { "first_feed", "happy_pet" },
            State = new PetState
            {
                Happiness = happiness,
                PetCount = petCount,
                PlayCount = 6,
                PhotoCount = 5,
                FeedCount = 9,
                Hunger = 0.4f,
                Energy = 0.7f,
            },
        };

        /// <summary>创建纯 C# 的宠物运行时并登记，TearDown 无需销毁场景对象。</summary>
        private PetRuntime Runtime(string petId)
        {
            var runtime = new PetRuntime(IdentityOf(petId), _config, NullPresenter.Instance);
            runtime.Start();
            _pets.Add(runtime);
            return runtime;
        }

        private static PetIdentity IdentityOf(string petId)
        {
            foreach (var identity in PetIdentityCatalog.Default)
            {
                if (identity.PetId == petId) return identity;
            }

            throw new InvalidOperationException("身份目录里没有 " + petId);
        }

        /// <summary>空表现层：存档测试不关心表现，只要满足 PetRuntime 的构造契约。</summary>
        private sealed class NullPresenter : IPetPresenter
        {
            public static readonly NullPresenter Instance = new NullPresenter();

            public void PlayBehavior(PetBehaviorId behavior, string animationName, bool loop, float crossFadeSeconds) { }

            public void SetExpression(PetExpression expression, float intensity) { }

            public void PlaySfx(string sfxId) { }

            public void PlayFx(string fxId) { }

            public void SetPlaybackSpeed(float speed) { }

            public void SetInteractable(bool interactable) { }

            public void SetTrackingVisibility(bool visible) { }
        }
    }
}
