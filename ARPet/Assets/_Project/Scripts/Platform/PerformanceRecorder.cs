using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ZQXNXS.ARPet.Platform
{
    /// <summary>
    /// 真机性能记录：每 5 秒把一行帧率汇总追加到 <c>persistentDataPath/perf/perf_&lt;启动时间&gt;.csv</c>。
    ///
    /// 用途是 P6 验收的"实际帧率记录"，测试结束后用
    /// <c>adb pull /sdcard/Android/data/&lt;包名&gt;/files/perf</c> 取回；Android 11+ 部分 ROM 拒绝该路径时，
    /// 用 <c>adb exec-out run-as &lt;包名&gt; tar c files/perf &gt; perf.tar</c>（测试包为 Development 构建，可 run-as）。
    /// 启动后第一个窗口与每次回前台后的第一个窗口不记（初始化/相机重启的一次性卡顿）。温升没有跨机型可靠的 Unity API，
    /// 仍按测试记录表人工读取；电量写进每一行（<see cref="SystemInfo.batteryLevel"/>，不支持时为 -1）。
    ///
    /// 只读帧时间，不碰业务；写盘失败只警告一次后停用，不影响运行。
    /// <see cref="CurrentFps"/> 供诊断界面显示最近一个窗口的平均帧率。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceRecorder : MonoBehaviour, ZQXNXS.ARPet.Core.IFrameRateSource
    {
        [Tooltip("每个汇总窗口的秒数。")]
        [SerializeField] private float windowSeconds = 5f;

        [Tooltip("低于该帧率的帧记为慢帧（课程目标 30FPS）。")]
        [SerializeField] private float targetFps = 30f;

        [Tooltip("关闭时只在内存里统计（诊断界面仍可显示），不写 CSV。")]
        [SerializeField] private bool writeCsv = true;

        /// <summary>最近一个完整窗口的平均帧率；还没有完整窗口时为 0。</summary>
        public float CurrentFps { get; private set; }

        /// <summary>最近一个完整窗口的慢帧占比（0～1）。</summary>
        public float SlowFrameFraction { get; private set; }

        /// <summary>本次运行的 CSV 路径；未写盘时为空。</summary>
        public string CsvPath { get; private set; } = string.Empty;

        private FrameStats _stats;
        private bool _writeFailed;
        private readonly StringBuilder _line = new();

        private void Awake()
        {
            _stats = new FrameStats(windowSeconds, targetFps);
        }

        // 启动后第一个窗口含 Vuforia 初始化、着色器预热等一次性卡顿，丢掉不记；回前台后同理。
        private bool _skipNextWindow = true;

        private void Update()
        {
            if (_stats == null) return;
            if (!_stats.Add(Time.unscaledTime, Time.unscaledDeltaTime, out var window)) return;

            if (_skipNextWindow)
            {
                _skipNextWindow = false;
                return;
            }

            CurrentFps = window.AverageFps;
            SlowFrameFraction = window.SlowFrameFraction;
            if (writeCsv && !_writeFailed) Append(window);
        }

        private void OnApplicationPause(bool paused)
        {
            // 切后台的长间隔不是卡顿：丢掉当前窗口；回到前台后相机重启那一窗也不计。
            if (!paused) return;
            _stats?.Reset();
            _skipNextWindow = true;
        }

        private void Append(FrameStats.Window window)
        {
            try
            {
                if (string.IsNullOrEmpty(CsvPath))
                {
                    var dir = Path.Combine(Application.persistentDataPath, "perf");
                    Directory.CreateDirectory(dir);
                    CsvPath = Path.Combine(dir, "perf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
                    File.WriteAllText(CsvPath,
                        "time_s,duration_s,frames,avg_fps,worst_ms,slow_fraction,battery,device,os\n");
                }

                var c = CultureInfo.InvariantCulture;
                _line.Clear();
                _line.Append(window.StartTime.ToString("F1", c)).Append(',')
                    .Append(window.Duration.ToString("F2", c)).Append(',')
                    .Append(window.Frames).Append(',')
                    .Append(window.AverageFps.ToString("F1", c)).Append(',')
                    .Append(window.WorstFrameMs.ToString("F1", c)).Append(',')
                    .Append(window.SlowFrameFraction.ToString("F3", c)).Append(',')
                    .Append(SystemInfo.batteryLevel.ToString("F2", c)).Append(',')
                    .Append(Csv(SystemInfo.deviceModel)).Append(',')
                    .Append(Csv(SystemInfo.operatingSystem)).Append('\n');
                File.AppendAllText(CsvPath, _line.ToString());
            }
            catch (Exception exception)
            {
                _writeFailed = true;
                Debug.LogWarning("[PerformanceRecorder] 性能记录写盘失败，本次运行停止写 CSV：" + exception.Message);
            }
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "'") + "\"";
    }
}
