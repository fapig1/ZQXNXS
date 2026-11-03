using NUnit.Framework;
using ZQXNXS.ARPet.Platform;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>P6 帧率记录的统计窗口：平均帧率、慢帧占比与切后台丢弃。</summary>
    public sealed class FrameStatsTests
    {
        [Test]
        public void SteadySixtyFps_ReportsSixty_AndNoSlowFrames()
        {
            var stats = new FrameStats(windowSeconds: 1f, targetFps: 30f);
            FrameStats.Window window = default;
            var now = 0f;
            var done = false;
            for (var i = 0; i < 61 && !done; i++)
            {
                now += 1f / 60f;
                done = stats.Add(now, 1f / 60f, out window);
            }

            Assert.IsTrue(done, "累计满 1 秒必须出一个窗口");
            Assert.AreEqual(60f, window.AverageFps, 1f);
            Assert.AreEqual(0f, window.SlowFrameFraction, 1e-6f);
            Assert.AreEqual(1000f / 60f, window.WorstFrameMs, 0.1f);
            Assert.AreEqual(0, stats.PendingFrames, "出窗口后重新计数");
        }

        [Test]
        public void FramesSlowerThanTarget_AreCountedAsSlow()
        {
            var stats = new FrameStats(windowSeconds: 1f, targetFps: 30f);
            FrameStats.Window window = default;
            var now = 0f;
            var done = false;
            // 交替 20ms（快）与 50ms（慢）：14 对 = 0.98s，再补一帧慢帧跨过 1s。
            for (var i = 0; i < 40 && !done; i++)
            {
                var dt = i % 2 == 0 ? 0.02f : 0.05f;
                now += dt;
                done = stats.Add(now, dt, out window);
            }

            Assert.IsTrue(done);
            Assert.Greater(window.SlowFrameFraction, 0.45f);
            Assert.Less(window.SlowFrameFraction, 0.55f);
            Assert.AreEqual(50f, window.WorstFrameMs, 0.1f);
        }

        [Test]
        public void LongGap_FromPause_IsDiscardedNotCountedAsStutter()
        {
            var stats = new FrameStats(windowSeconds: 1f, targetFps: 30f, discardAboveSeconds: 1f);
            Assert.IsFalse(stats.Add(0.1f, 0.1f, out _));
            Assert.AreEqual(1, stats.PendingFrames);

            Assert.IsFalse(stats.Add(5f, 4.9f, out _), "切后台回来的长间隔直接丢弃");
            Assert.AreEqual(0, stats.PendingFrames, "并清空当前窗口");

            Assert.IsFalse(stats.Add(5.01f, 0f, out _), "零间隔帧不计");
            Assert.IsFalse(stats.Add(5.02f, float.NaN, out _), "NaN 帧不计");
            Assert.AreEqual(0, stats.PendingFrames);
        }
    }
}
