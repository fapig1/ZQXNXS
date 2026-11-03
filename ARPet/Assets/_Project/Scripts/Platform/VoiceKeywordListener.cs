using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using ZQXNXS.ARPet.Core;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ZQXNXS.ARPet.Platform
{
    /// <summary>
    /// 按住说话的离线关键词识别（sherpa-onnx KWS，zipformer wenetspeech 3.3M int8）。
    ///
    /// 流程：<see cref="BeginListening"/> 开麦（首次先要麦克风权限）→ 每帧把麦克风新增样本降到 16kHz
    /// 放进队列 → 识别线程取样本喂给识别器 → 命中的口令原文放进结果队列 → App 每帧 <see cref="TryDequeue"/>。
    /// <see cref="EndListening"/> 后再喂 0.3 秒静音让尾字解码完，然后关麦。
    ///
    /// 模型放在 <c>StreamingAssets/Voice/kws/</c>。Android 上 StreamingAssets 在 APK 里不能按路径读，
    /// 首次启动用 UnityWebRequest 解到 <c>persistentDataPath/kws/</c>（按清单校验大小，已解过就跳过）。
    /// 缺模型、缺原生库或平台不支持时进入 <see cref="VoiceListenState.Unavailable"/>，其余功能不受影响。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceKeywordListener : MonoBehaviour, IVoiceCommandSource
    {
        public const string ModelFolder = "Voice/kws";
        public static readonly string[] ModelFiles =
        {
            "encoder.int8.onnx", "decoder.onnx", "joiner.int8.onnx", "tokens.txt", "keywords.txt",
        };

        [Tooltip("触发阈值（0～1），越低越容易触发也越容易误触发。2026-10-29 用 TTS 与自录音频离线验证过 0.25。")]
        [SerializeField] private float keywordsThreshold = 0.25f;

        [Tooltip("关键词加权分，越高越容易命中。")]
        [SerializeField] private float keywordsScore = 1.5f;

        [Tooltip("单次按住最长录音秒数，超时自动结束，防止一直开麦。")]
        [SerializeField] private float maxListenSeconds = 6f;

        public VoiceListenState State { get; private set; } = VoiceListenState.Unavailable;
        public string StatusText { get; private set; } = "语音识别初始化中…";

        private const int TargetRate = 16000;
        private const float TailSeconds = 0.3f;

        private readonly Queue<float[]> _audio = new();
        private readonly Queue<string> _hits = new();
        private readonly object _gate = new();
        private readonly AutoResetEvent _signal = new(false);

        private Thread _worker;
        private volatile bool _stop;
        private volatile bool _resetRequested;
        private string _device;
        private AudioClip _clip;
        private int _micRate;
        private int _readPosition;
        private float _listenStarted;
        private float _endRequestedAt = -1f;
        private bool _permissionPending;
        private float[] _readBuffer = Array.Empty<float>();
        private float _resamplePhase;

        /// <summary>
        /// 识别器本身是否不可用（缺模型 / 缺原生库）。这是唯一的永久失败；
        /// "没有麦克风"和"没有权限"都可以在授权或回到前台后恢复。
        /// </summary>
        private bool _engineFailed;

        // 权限回调可能不在 Unity 主线程上触发，只记结果，由 Update 在主线程处理。
        private const int PermissionNone = 0, PermissionGranted = 1, PermissionRefused = 2;
        private volatile int _permissionResult;

        private void Start() => StartCoroutine(Initialise());

        /// <summary>
        /// 先准备识别器（与麦克风无关），再找麦克风设备。
        /// Android 未授予 RECORD_AUDIO 时 <c>Microphone.devices</c> 是空数组，这时不能判成"没有麦克风"
        /// 并停用：保持 Idle，等首次按住按钮时请求权限，授权后再取设备（2026-10-30 真机 BUG-01）。
        /// </summary>
        private IEnumerator Initialise()
        {
            var dir = Path.Combine(Application.persistentDataPath, "kws");
            string error = null;
            yield return ExtractModel(dir, e => error = e);
            if (error != null)
            {
                SetUnavailable("缺少语音模型：" + error);
                yield break;
            }

            SherpaKeywordSpotter spotter;
            try
            {
                spotter = new SherpaKeywordSpotter(
                    Path.Combine(dir, "encoder.int8.onnx"), Path.Combine(dir, "decoder.onnx"),
                    Path.Combine(dir, "joiner.int8.onnx"), Path.Combine(dir, "tokens.txt"),
                    Path.Combine(dir, "keywords.txt"), keywordsThreshold, keywordsScore);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException ||
                                      e is InvalidOperationException || e is BadImageFormatException)
            {
                SetUnavailable("语音识别库加载失败：" + e.Message);
                yield break;
            }

            _stop = false;
            _worker = new Thread(() => RunWorker(spotter)) { IsBackground = true, Name = "VoiceKWS" };
            _worker.Start();

            if (!HasMicPermission())
            {
                SetIdle("按住说话");
                Debug.Log("[VoiceKeywordListener] 离线关键词识别已就绪；尚未获得麦克风权限，首次按住按钮时请求。");
            }
            else if (TryResolveDevice())
            {
                SetIdle("按住说话");
                Debug.Log("[VoiceKeywordListener] 离线关键词识别已就绪，麦克风：" + _device);
            }
            else
            {
                SetNoDevice();
            }
        }

        public void BeginListening()
        {
            if (_engineFailed || _worker == null || State == VoiceListenState.Listening) return;

            if (!HasMicPermission())
            {
                RequestMicPermission();
                return;
            }

            // 有权限但还没取到设备（刚授权、或之前确实没插麦克风）：每次按下都重新找一次。
            if (_device == null && !TryResolveDevice())
            {
                SetNoDevice();
                return;
            }

            _clip = Microphone.Start(_device, true, 10, 44100);
            if (_clip == null)
            {
                StatusText = "麦克风打开失败";
                return;
            }

            _micRate = _clip.frequency;
            _readPosition = 0;
            _resamplePhase = 0f;
            _resetRequested = true;
            _endRequestedAt = -1f;
            _listenStarted = Time.unscaledTime;
            State = VoiceListenState.Listening;
            StatusText = "正在听…";
        }

        public void EndListening()
        {
            if (State != VoiceListenState.Listening || _endRequestedAt >= 0f) return;
            _endRequestedAt = Time.unscaledTime;
        }

        public bool TryDequeue(out string phrase)
        {
            lock (_gate)
            {
                if (_hits.Count > 0)
                {
                    phrase = _hits.Dequeue();
                    return true;
                }
            }

            phrase = null;
            return false;
        }

        private void Update()
        {
            HandlePermissionResult();
            if (State != VoiceListenState.Listening || _clip == null) return;

            PumpMicrophone();

            var now = Time.unscaledTime;
            if (_endRequestedAt < 0f && now - _listenStarted > maxListenSeconds) _endRequestedAt = now;
            if (_endRequestedAt >= 0f && now - _endRequestedAt >= TailSeconds) StopMicrophone();
        }

        private void PumpMicrophone()
        {
            var position = Microphone.GetPosition(_device);
            if (position < 0 || position == _readPosition) return;

            var total = _clip.samples;
            var available = position > _readPosition ? position - _readPosition : total - _readPosition + position;
            if (available <= 0) return;

            var channels = Mathf.Max(1, _clip.channels);
            var needed = available * channels;
            if (_readBuffer.Length < needed) _readBuffer = new float[needed];

            if (position > _readPosition)
            {
                ReadClip(_readPosition, available, channels, 0);
            }
            else
            {
                var first = total - _readPosition;
                ReadClip(_readPosition, first, channels, 0);
                ReadClip(0, position, channels, first * channels);
            }

            _readPosition = position;
            Enqueue(Resample(_readBuffer, available, channels));
        }

        private void ReadClip(int offset, int frames, int channels, int destination)
        {
            if (frames <= 0) return;
            // GetData 按目标数组长度读取，所以分段读需要各自大小的临时数组；只在聆听期间每帧发生。
            var temp = new float[frames * channels];
            _clip.GetData(temp, offset);
            Array.Copy(temp, 0, _readBuffer, destination, temp.Length);
        }

        /// <summary>立体声取平均，线性插值降采样到 16kHz。相位跨块保留，块边界不产生咔哒声。</summary>
        private float[] Resample(float[] source, int frames, int channels)
        {
            var step = (float)_micRate / TargetRate;
            var count = Mathf.Max(0, Mathf.FloorToInt((frames - _resamplePhase) / step));
            var output = new float[count];
            for (var i = 0; i < count; i++)
            {
                var position = _resamplePhase + i * step;
                var index = (int)position;
                var next = Mathf.Min(index + 1, frames - 1);
                var t = position - index;
                var a = Mono(source, index, channels);
                output[i] = a + (Mono(source, next, channels) - a) * t;
            }

            _resamplePhase = _resamplePhase + count * step - frames;
            if (_resamplePhase < 0f) _resamplePhase = 0f;
            return output;
        }

        private static float Mono(float[] source, int frame, int channels)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++) sum += source[frame * channels + c];
            return sum / channels;
        }

        private void Enqueue(float[] samples)
        {
            if (samples.Length == 0) return;
            lock (_gate) _audio.Enqueue(samples);
            _signal.Set();
        }

        private void StopMicrophone()
        {
            PumpMicrophone();
            Microphone.End(_device);
            _clip = null;
            // 尾音：补一段静音，让最后一个字也能被解码出来。
            Enqueue(new float[(int)(TargetRate * TailSeconds)]);
            State = VoiceListenState.Idle;
            StatusText = "按住说话";
        }

        private void RunWorker(SherpaKeywordSpotter spotter)
        {
            try
            {
                while (!_stop)
                {
                    _signal.WaitOne(200);
                    if (_resetRequested)
                    {
                        _resetRequested = false;
                        spotter.Reset();
                    }

                    while (true)
                    {
                        float[] chunk;
                        lock (_gate)
                        {
                            if (_audio.Count == 0) break;
                            chunk = _audio.Dequeue();
                        }

                        var hit = spotter.Feed(chunk, chunk.Length);
                        if (string.IsNullOrEmpty(hit)) continue;
                        lock (_gate) _hits.Enqueue(hit);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VoiceKeywordListener] 识别线程异常退出：" + e.Message);
            }
            finally
            {
                spotter.Dispose();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && State == VoiceListenState.Listening) StopMicrophone();
        }

        /// <summary>从系统设置里手动开了麦克风再切回来：不必重启应用，回前台时重新检查一次。</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || _engineFailed || _worker == null || _permissionPending) return;
            if (State != VoiceListenState.PermissionDenied && State != VoiceListenState.Unavailable) return;
            if (!HasMicPermission()) return;

            if (TryResolveDevice()) SetIdle("已允许麦克风，按住说话");
            else SetNoDevice();
        }

        private void HandlePermissionResult()
        {
            var result = _permissionResult;
            if (result == PermissionNone) return;
            _permissionResult = PermissionNone;
            _permissionPending = false;

            if (result == PermissionRefused)
            {
                State = VoiceListenState.PermissionDenied;
                StatusText = "麦克风权限被拒绝：请在系统设置中允许麦克风";
                return;
            }

            // 授权后 Microphone.devices 才有内容：必须在这里重新取设备，否则识别永远开不了麦。
            if (TryResolveDevice())
            {
                SetIdle("已允许麦克风，按住说话");
                Debug.Log("[VoiceKeywordListener] 麦克风权限已授予，麦克风：" + _device);
            }
            else
            {
                SetNoDevice();
            }
        }

        private bool TryResolveDevice()
        {
            var devices = Microphone.devices;
            _device = devices != null && devices.Length > 0 ? devices[0] : null;
            return _device != null;
        }

        private void SetIdle(string text)
        {
            State = VoiceListenState.Idle;
            StatusText = text;
        }

        /// <summary>有权限但找不到设备。可恢复：下次按下或回前台会重新找。</summary>
        private void SetNoDevice()
        {
            State = VoiceListenState.Unavailable;
            StatusText = "没有可用的麦克风";
            Debug.LogWarning("[VoiceKeywordListener] 已有麦克风权限但没有找到麦克风设备（按下按钮或回到前台时会重试）。");
        }

        private void OnDestroy()
        {
            if (_clip != null) Microphone.End(_device);
            _stop = true;
            _signal.Set();
            _worker?.Join(500);
        }

        private void SetUnavailable(string reason)
        {
            _engineFailed = true;
            State = VoiceListenState.Unavailable;
            StatusText = reason;
            Debug.LogWarning("[VoiceKeywordListener] " + reason + "（语音功能停用，其他功能不受影响）");
        }

        private static bool HasMicPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }

        private void RequestMicPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_permissionPending) return;
            _permissionPending = true;
            State = VoiceListenState.RequestingPermission;
            StatusText = "请允许使用麦克风";
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => _permissionResult = PermissionGranted;
            callbacks.PermissionDenied += _ => _permissionResult = PermissionRefused;
            callbacks.PermissionDeniedAndDontAskAgain += _ => _permissionResult = PermissionRefused;
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
#endif
        }

        /// <summary>把 StreamingAssets 里的模型复制到可按路径读取的目录。已存在且大小一致就跳过。</summary>
        private static IEnumerator ExtractModel(string targetDir, Action<string> fail)
        {
            Directory.CreateDirectory(targetDir);
            for (var i = 0; i < ModelFiles.Length; i++)
            {
                var name = ModelFiles[i];
                var source = Path.Combine(Application.streamingAssetsPath, ModelFolder, name);
                var target = Path.Combine(targetDir, name);
#if UNITY_ANDROID && !UNITY_EDITOR
                using var request = UnityWebRequest.Get(source);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    fail(name + "（" + request.error + "）");
                    yield break;
                }

                var bytes = request.downloadHandler.data;
                if (!File.Exists(target) || new FileInfo(target).Length != bytes.Length) File.WriteAllBytes(target, bytes);
#else
                if (!File.Exists(source))
                {
                    fail(name);
                    yield break;
                }

                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                    File.Copy(source, target, overwrite: true);
                yield return null;
#endif
            }
        }
    }
}
