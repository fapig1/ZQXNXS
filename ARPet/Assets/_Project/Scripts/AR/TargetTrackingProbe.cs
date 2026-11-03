using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

#if VUFORIA_ENGINE
using Vuforia;
#endif

namespace ZQXNXS.ARPet.AR
{
    /// <summary>
    /// 四卡早期实测用的诊断探针（开发顺序第2步），不参与业务结算。
    ///
    /// 每个目标只在 Vuforia 报告 TRACKED 时显示其角色子树；EXTENDED_TRACKED / LIMITED / NO_POSE
    /// 一律隐藏，且不关闭 Observer。状态跳变、同时跟踪数与帧率写入
    /// <c>Application.persistentDataPath/TrackingProbe/*.csv</c>，屏幕左上角显示实时状态，
    /// 供真机记录识别距离、六组配对错认与四卡同框稳定性。
    /// </summary>
    public sealed class TargetTrackingProbe : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("ImageTarget 物体（带 ObserverBehaviour）。")]
            public GameObject target;

            [Tooltip("该卡对应的角色子树；只在 TRACKED 时激活。")]
            public GameObject content;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        [SerializeField] private bool showOverlay = true;

        private const float FpsWindowSeconds = 1f;

        private string[] _names = Array.Empty<string>();
        private string[] _status = Array.Empty<string>();
        private bool[] _tracked = Array.Empty<bool>();
        private int[] _acquisitions = Array.Empty<int>();
        private double[] _trackedSeconds = Array.Empty<double>();
        private readonly StringBuilder _log = new StringBuilder();
        private string _logPath;
        private int _maxSimultaneous;
        private int _currentTracked;
        private float _fpsWindowStart;
        private int _fpsFrames;
        private float _fps;
        private float _minFps = float.PositiveInfinity;
        private GUIStyle _style;

        public IReadOnlyList<Entry> Entries => entries;

        private void Awake()
        {
            var n = entries.Length;
            _names = new string[n];
            _status = new string[n];
            _tracked = new bool[n];
            _acquisitions = new int[n];
            _trackedSeconds = new double[n];
            for (var i = 0; i < n; i++)
            {
                _names[i] = entries[i].target != null ? entries[i].target.name : "missing";
                _status[i] = "INIT";
                if (entries[i].content != null) entries[i].content.SetActive(false);
            }

            var dir = Path.Combine(Application.persistentDataPath, "TrackingProbe");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "four_card_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
            _log.AppendLine("time_s,target,from,to,status_info,tracked_count,fps");
            _fpsWindowStart = Time.unscaledTime;
        }

        private void Update()
        {
            _fpsFrames++;
            var now = Time.unscaledTime;
            if (now - _fpsWindowStart >= FpsWindowSeconds)
            {
                _fps = _fpsFrames / (now - _fpsWindowStart);
                // 只有至少一张卡在跟踪时才计入最低帧率，避免启动黑屏段干扰。
                if (_currentTracked > 0 && _fps < _minFps) _minFps = _fps;
                _fpsFrames = 0;
                _fpsWindowStart = now;
            }

            var tracked = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                ReadStatus(entries[i].target, out var status, out var info, out var isTracked);
                if (isTracked)
                {
                    tracked++;
                    _trackedSeconds[i] += Time.unscaledDeltaTime;
                }

                if (status != _status[i])
                {
                    if (isTracked && !_tracked[i]) _acquisitions[i]++;
                    _log.Append(now.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(_names[i]).Append(',').Append(_status[i]).Append(',').Append(status).Append(',')
                        .Append(info).Append(',').Append(CountAfter(i, isTracked)).Append(',')
                        .Append(_fps.ToString("F1", CultureInfo.InvariantCulture)).AppendLine();
                    _status[i] = status;
                }

                if (_tracked[i] != isTracked && entries[i].content != null) entries[i].content.SetActive(isTracked);
                _tracked[i] = isTracked;
            }

            _currentTracked = tracked;
            if (tracked > _maxSimultaneous) _maxSimultaneous = tracked;
        }

        private int CountAfter(int changed, bool changedTracked)
        {
            var count = 0;
            for (var i = 0; i < _tracked.Length; i++) count += (i == changed ? changedTracked : _tracked[i]) ? 1 : 0;
            return count;
        }

        private static void ReadStatus(GameObject target, out string status, out string info, out bool isTracked)
        {
#if VUFORIA_ENGINE
            var observer = target != null ? target.GetComponent<ObserverBehaviour>() : null;
            if (observer == null || !observer.isActiveAndEnabled)
            {
                status = "INACTIVE"; info = "-"; isTracked = false;
                return;
            }
            status = observer.TargetStatus.Status.ToString();
            info = observer.TargetStatus.StatusInfo.ToString();
            isTracked = observer.TargetStatus.Status == Status.TRACKED;
#else
            status = "NO_VUFORIA"; info = "-"; isTracked = false;
#endif
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        private void OnDestroy() => Flush();

        private void Flush()
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            var summary = new StringBuilder();
            summary.AppendLine("# summary written_at=" + Time.unscaledTime.ToString("F1", CultureInfo.InvariantCulture) +
                               "s max_simultaneous=" + _maxSimultaneous +
                               " min_fps_while_tracking=" + (float.IsInfinity(_minFps) ? "n/a" : _minFps.ToString("F1", CultureInfo.InvariantCulture)) +
                               " device=" + SystemInfo.deviceModel + " os=" + SystemInfo.operatingSystem);
            for (var i = 0; i < _names.Length; i++)
            {
                summary.AppendLine("# " + _names[i] + " acquisitions=" + _acquisitions[i] + " tracked_s=" +
                                   _trackedSeconds[i].ToString("F1", CultureInfo.InvariantCulture));
            }
            try
            {
                File.WriteAllText(_logPath, _log + summary.ToString());
            }
            catch (IOException e)
            {
                Debug.LogWarning("[TargetTrackingProbe] 写日志失败：" + e.Message);
            }
        }

        private void OnGUI()
        {
            if (!showOverlay) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(24, Screen.height / 45) };
            var line = _style.fontSize * 1.35f;
            var area = Screen.safeArea;
            var y = Screen.height - area.yMax + 12f;
            GUI.Label(new Rect(area.x + 16f, y, area.width, line),
                $"FPS {_fps:F0}  tracked {_currentTracked}/{entries.Length}  max {_maxSimultaneous}", _style);
            for (var i = 0; i < entries.Length; i++)
            {
                y += line;
                GUI.Label(new Rect(area.x + 16f, y, area.width, line),
                    $"{_names[i]}  {_status[i]}  x{_acquisitions[i]}  {_trackedSeconds[i]:F0}s", _style);
            }
        }
    }
}
