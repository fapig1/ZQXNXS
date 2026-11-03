using System;

namespace ZQXNXS.ARPet.Platform
{
    /// <summary>
    /// 帧时间统计窗口（纯逻辑，可在 EditMode 直接测）。
    ///
    /// 按固定时长（默认 5 秒）累计帧间隔，窗口结束时给出一行汇总：平均 FPS、最慢帧、
    /// 低于目标帧率（默认 30FPS，即帧间隔 &gt; 1/30 s）的帧占比。只统计前台帧，
    /// 切后台等长间隔由调用方用 <see cref="Reset"/> 丢弃，不计入卡顿。
    /// </summary>
    public sealed class FrameStats
    {
        /// <summary>一个统计窗口的汇总。</summary>
        public readonly struct Window
        {
            public readonly float StartTime;
            public readonly float Duration;
            public readonly int Frames;
            public readonly float AverageFps;
            public readonly float WorstFrameMs;
            public readonly float SlowFrameFraction;

            public Window(float startTime, float duration, int frames, float worstFrameMs, int slowFrames)
            {
                StartTime = startTime;
                Duration = duration;
                Frames = frames;
                AverageFps = duration > 0f ? frames / duration : 0f;
                WorstFrameMs = worstFrameMs;
                SlowFrameFraction = frames > 0 ? (float)slowFrames / frames : 0f;
            }
        }

        private readonly float _windowSeconds;
        private readonly float _slowFrameSeconds;
        private readonly float _discardAboveSeconds;

        private float _windowStart = float.NaN;
        private float _accumulated;
        private int _frames;
        private int _slowFrames;
        private float _worst;

        /// <param name="windowSeconds">每个汇总窗口的时长。</param>
        /// <param name="targetFps">低于这个帧率的帧记为"慢帧"。</param>
        /// <param name="discardAboveSeconds">超过这个间隔的帧视为暂停/切后台，直接丢弃并重开窗口。</param>
        public FrameStats(float windowSeconds = 5f, float targetFps = 30f, float discardAboveSeconds = 1f)
        {
            if (windowSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            if (targetFps <= 0f) throw new ArgumentOutOfRangeException(nameof(targetFps));
            _windowSeconds = windowSeconds;
            _slowFrameSeconds = 1f / targetFps;
            _discardAboveSeconds = discardAboveSeconds;
        }

        /// <summary>当前窗口里已累计的帧数。</summary>
        public int PendingFrames => _frames;

        /// <summary>
        /// 记一帧。<paramref name="now"/> 为本帧结束时刻（unscaled），<paramref name="frameSeconds"/> 为本帧间隔。
        /// 窗口满时返回 true 并给出汇总，随后自动开新窗口。
        /// </summary>
        public bool Add(float now, float frameSeconds, out Window window)
        {
            window = default;
            if (float.IsNaN(frameSeconds) || frameSeconds <= 0f) return false;
            if (frameSeconds > _discardAboveSeconds)
            {
                Reset();
                return false;
            }

            if (float.IsNaN(_windowStart)) _windowStart = now - frameSeconds;

            _accumulated += frameSeconds;
            _frames++;
            if (frameSeconds > _slowFrameSeconds) _slowFrames++;
            if (frameSeconds > _worst) _worst = frameSeconds;

            if (_accumulated < _windowSeconds) return false;

            window = new Window(_windowStart, _accumulated, _frames, _worst * 1000f, _slowFrames);
            _windowStart = now;
            _accumulated = 0f;
            _frames = 0;
            _slowFrames = 0;
            _worst = 0f;
            return true;
        }

        /// <summary>丢弃当前窗口（切后台、暂停跟踪时调用），下一帧重新开窗。</summary>
        public void Reset()
        {
            _windowStart = float.NaN;
            _accumulated = 0f;
            _frames = 0;
            _slowFrames = 0;
            _worst = 0f;
        }
    }
}
