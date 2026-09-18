using System;
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
    /// <summary>使用独立临时目录进行真实文件读写，绝不触碰玩家的默认存档路径。</summary>
    public sealed class PersistenceTests
    {
        private string _testRoot;
        private string _directory;
        private string _path;
        private PetConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.GetFullPath(Path.Combine(Application.temporaryCachePath, "ARPetSaveTests"));
            _directory = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, SaveService.FileName);
            _config = ScriptableObject.CreateInstance<PetConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
            var fullPath = Path.GetFullPath(_directory);
            if (!fullPath.StartsWith(_testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("测试清理路径越界");
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        }

        private static SaveFile Saved(int feedCount, long time)
        {
            var file = SaveFile.CreateNew(time);
            file.State.FeedCount = feedCount;
            file.State.PetCount = 7;
            file.State.PlayCount = 6;
            file.State.PhotoCount = 5;
            file.TotalPlaySeconds = 100f;
            file.UnlockedReactions = new[] { "first_feed", "happy_pet" };
            return file;
        }

        [Test]
        public void MissingSave_IsExplicitlyReportedAsNew()
        {
            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsFalse(restored);
            Assert.IsNotNull(loaded);
            Assert.IsFalse(SaveService.Exists(_path));
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
        public void SaveRestoreAndNextSession_PreserveCountsUnlocksAndTotalTime()
        {
            Assert.IsTrue(SaveService.Save(Saved(8, 1000), 1000, _path));
            var loaded = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            var needs = new PetNeeds(_config);
            needs.RestoreFrom(loaded.State);
            needs.IncrementCount(InteractionEventKind.Feed);
            var coordinator = new SaveCoordinator(needs, loaded,
                (file, utc) => SaveService.Save(file, utc, _path));
            Assert.IsTrue(coordinator.Tick(1f, 1f));

            var reread = SaveService.Load(out restored, _path);
            Assert.IsTrue(restored);
            Assert.AreEqual(9, reread.State.FeedCount);
            Assert.AreEqual(7, reread.State.PetCount);
            Assert.AreEqual(6, reread.State.PlayCount);
            Assert.AreEqual(5, reread.State.PhotoCount);
            Assert.AreEqual(101f, reread.TotalPlaySeconds, 0.0001f);
            CollectionAssert.AreEqual(new[] { "first_feed", "happy_pet" }, reread.UnlockedReactions);
        }

        [Test]
        public void SecondSave_KeepsPreviousCommittedFileAsBackup()
        {
            Assert.IsTrue(SaveService.Save(Saved(1, 1000), 1000, _path));
            Assert.IsTrue(SaveService.Save(Saved(2, 2000), 2000, _path));
            Assert.AreEqual(2, SaveService.Load(out _, _path).State.FeedCount);
            var backup = JsonUtility.FromJson<SaveFile>(File.ReadAllText(_path + ".bak"));
            Assert.AreEqual(1, backup.State.FeedCount);
            Assert.IsFalse(File.Exists(_path + ".tmp"));
        }

        [Test]
        public void MissingPrimary_RecoversCompleteTemporaryFileBeforeBackup()
        {
            Assert.IsTrue(SaveService.Save(Saved(1, 1000), 1000, _path));
            File.Copy(_path, _path + ".bak");
            File.WriteAllText(_path + ".tmp", JsonUtility.ToJson(Saved(2, 2000)));
            File.Delete(_path); // 模拟主档替换期间中断
            var recovered = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.AreEqual(2, recovered.State.FeedCount);
            Assert.IsTrue(File.Exists(_path));
            Assert.AreEqual(2, SaveService.Load(out _, _path).State.FeedCount);
        }

        [Test]
        public void ValidPrimary_WinsOverUncommittedTemporaryFile()
        {
            Assert.IsTrue(SaveService.Save(Saved(1, 1000), 1000, _path));
            File.WriteAllText(_path + ".tmp", JsonUtility.ToJson(Saved(2, 2000)));
            Assert.AreEqual(1, SaveService.Load(out var restored, _path).State.FeedCount);
            Assert.IsTrue(restored);
        }

        [Test]
        public void BrokenPrimaryAndTemporaryFile_RecoverBackup()
        {
            Assert.IsTrue(SaveService.Save(Saved(1, 1000), 1000, _path));
            Assert.IsTrue(SaveService.Save(Saved(2, 2000), 2000, _path));
            File.WriteAllText(_path, "{");
            File.WriteAllText(_path + ".tmp", "{");
            var recovered = SaveService.Load(out var restored, _path);
            Assert.IsTrue(restored);
            Assert.AreEqual(1, recovered.State.FeedCount);
            Assert.AreEqual(2, Directory.GetFiles(_directory, "*.corrupt_*").Length);
            Assert.AreEqual(1, SaveService.Load(out _, _path).State.FeedCount);
        }

        [Test]
        public void FailedWrite_PreservesOldFileAndLastSuccessfulTimestamp()
        {
            var file = Saved(1, 1000);
            Assert.IsTrue(SaveService.Save(file, 1000, _path));
            Directory.CreateDirectory(_path + ".tmp"); // 确定性地阻止创建临时文件
            file.State.FeedCount = 99;
            LogAssert.Expect(LogType.Error, new Regex("\\[SaveService\\] 写入失败："));
            Assert.IsFalse(SaveService.Save(file, 2000, _path));
            Assert.AreEqual(1000L, file.SavedAtUnixSeconds);
            Assert.AreEqual(1, SaveService.Load(out var restored, _path).State.FeedCount);
            Assert.IsTrue(restored);
        }

        [Test]
        public void ExplicitDeletion_RemovesAllRecoveryCandidates()
        {
            Assert.IsTrue(SaveService.Save(Saved(1, 1000), 1000, _path));
            Assert.IsTrue(SaveService.Save(Saved(2, 2000), 2000, _path));
            File.WriteAllText(_path + ".tmp", JsonUtility.ToJson(Saved(3, 3000)));
            Assert.IsTrue(SaveService.Delete(_path));
            Assert.IsFalse(SaveService.Exists(_path));
            SaveService.Load(out var restored, _path);
            Assert.IsFalse(restored);
        }

        [Test]
        public void FailedAutomaticSaves_AreThrottledAndRetriedWithoutClearingDirty()
        {
            var needs = new PetNeeds(_config);
            needs.Apply(PetNeed.Hunger, 0.1f, 0f);
            var canWrite = false;
            var attempts = 0;
            var coordinator = new SaveCoordinator(needs, Saved(0, 1000), (file, utc) =>
            {
                attempts++;
                return canWrite;
            });
            for (var frame = 0; frame < 120; frame++) coordinator.Tick(1f / 60f, frame / 60f);
            Assert.AreEqual(1, attempts, "两秒内不能每帧重试失败写盘");
            Assert.IsTrue(needs.Dirty);
            Assert.IsFalse(coordinator.Tick(0f, 5.1f));
            Assert.AreEqual(2, attempts);
            canWrite = true;
            Assert.IsFalse(coordinator.Tick(0f, 6f));
            Assert.IsTrue(coordinator.Tick(0f, 10.2f));
            Assert.AreEqual(3, attempts);
            Assert.AreEqual(1, coordinator.SaveCount);
            Assert.IsFalse(needs.Dirty);
        }

        [Test]
        public void PlayTimeChange_IsSavedEvenWhenNeedsDoNotChange()
        {
            var needs = new PetNeeds(_config);
            var savedSeconds = 0f;
            var coordinator = new SaveCoordinator(needs, Saved(0, 1000), (file, utc) =>
            {
                savedSeconds = file.TotalPlaySeconds;
                return true;
            });
            Assert.IsFalse(needs.Dirty);
            Assert.IsTrue(coordinator.Tick(1f, 1f));
            Assert.AreEqual(101f, savedSeconds, 0.0001f);
        }

        [Test]
        public void ClockRollback_DoesNotApplyNegativeOfflineTime()
        {
            Assert.AreEqual(0f, SaveService.OfflineSeconds(Saved(0, 1000), 900), 0.0001f);
        }
    }
}
