using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ZQXNXS.ARPet.Platform
{
    /// <summary>
    /// sherpa-onnx 关键词识别（KWS）C API 的最小封装：建识别器、喂音频、取命中口令。
    ///
    /// 原生库：Android 为 <c>Plugins/Android/sherpa-onnx/arm64-v8a/libsherpa-onnx-c-api.so</c>（依赖同目录
    /// <c>libonnxruntime.so</c>），Windows 编辑器为 <c>Plugins/x86_64/sherpa-onnx-c-api.dll</c>。版本 v2.0.1，
    /// 结构体布局逐字段对照 <c>sherpa-onnx/c-api/c-api.h</c>，改版本时必须重新核对。
    ///
    /// 线程：本类不加锁，只能在一个线程里使用（<see cref="VoiceKeywordListener"/> 的识别线程）。
    /// </summary>
    internal sealed class SherpaKeywordSpotter : IDisposable
    {
        private const string Lib = "sherpa-onnx-c-api";

        [StructLayout(LayoutKind.Sequential)]
        private struct FeatureConfig
        {
            public int SampleRate;
            public int FeatureDim;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TransducerConfig
        {
            public IntPtr Encoder;
            public IntPtr Decoder;
            public IntPtr Joiner;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TwoPaths
        {
            public IntPtr A;
            public IntPtr B;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OnePath
        {
            public IntPtr Model;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OnlineModelConfig
        {
            public TransducerConfig Transducer;
            public TwoPaths Paraformer;
            public OnePath Zipformer2Ctc;
            public IntPtr Tokens;
            public int NumThreads;
            public IntPtr Provider;
            public int Debug;
            public IntPtr ModelType;
            public IntPtr ModelingUnit;
            public IntPtr BpeVocab;
            public IntPtr TokensBuf;
            public int TokensBufSize;
            public OnePath NemoCtc;
            public OnePath ToneCtc;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeywordSpotterConfig
        {
            public FeatureConfig Feat;
            public OnlineModelConfig Model;
            public int MaxActivePaths;
            public int NumTrailingBlanks;
            public float KeywordsScore;
            public float KeywordsThreshold;
            public IntPtr KeywordsFile;
            public IntPtr KeywordsBuf;
            public int KeywordsBufSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeywordResult
        {
            public IntPtr Keyword;
            public IntPtr Tokens;
            public IntPtr TokensArr;
            public int Count;
            public IntPtr Timestamps;
            public float StartTime;
            public IntPtr Json;
        }

        [DllImport(Lib, EntryPoint = "SherpaOnnxCreateKeywordSpotter")]
        private static extern IntPtr CreateSpotter(ref KeywordSpotterConfig config);

        [DllImport(Lib, EntryPoint = "SherpaOnnxDestroyKeywordSpotter")]
        private static extern void DestroySpotter(IntPtr spotter);

        [DllImport(Lib, EntryPoint = "SherpaOnnxCreateKeywordStream")]
        private static extern IntPtr CreateStream(IntPtr spotter);

        [DllImport(Lib, EntryPoint = "SherpaOnnxDestroyOnlineStream")]
        private static extern void DestroyStream(IntPtr stream);

        [DllImport(Lib, EntryPoint = "SherpaOnnxOnlineStreamAcceptWaveform")]
        private static extern void AcceptWaveform(IntPtr stream, int sampleRate, float[] samples, int n);

        [DllImport(Lib, EntryPoint = "SherpaOnnxIsKeywordStreamReady")]
        private static extern int IsReady(IntPtr spotter, IntPtr stream);

        [DllImport(Lib, EntryPoint = "SherpaOnnxDecodeKeywordStream")]
        private static extern void Decode(IntPtr spotter, IntPtr stream);

        [DllImport(Lib, EntryPoint = "SherpaOnnxResetKeywordStream")]
        private static extern void ResetStream(IntPtr spotter, IntPtr stream);

        [DllImport(Lib, EntryPoint = "SherpaOnnxGetKeywordResult")]
        private static extern IntPtr GetResult(IntPtr spotter, IntPtr stream);

        [DllImport(Lib, EntryPoint = "SherpaOnnxDestroyKeywordResult")]
        private static extern void DestroyResult(IntPtr result);

        private IntPtr _spotter;
        private IntPtr _stream;

        /// <summary>模型文件的绝对路径（已从 StreamingAssets 解出到可读目录）。</summary>
        public SherpaKeywordSpotter(string encoder, string decoder, string joiner, string tokens, string keywordsFile,
            float threshold, float score)
        {
            var strings = new[] { encoder, decoder, joiner, tokens, "cpu", keywordsFile };
            var handles = new IntPtr[strings.Length];
            try
            {
                for (var i = 0; i < strings.Length; i++) handles[i] = Utf8(strings[i]);

                var config = new KeywordSpotterConfig
                {
                    Feat = new FeatureConfig { SampleRate = 16000, FeatureDim = 80 },
                    Model = new OnlineModelConfig
                    {
                        Transducer = new TransducerConfig { Encoder = handles[0], Decoder = handles[1], Joiner = handles[2] },
                        Tokens = handles[3],
                        NumThreads = 1,
                        Provider = handles[4],
                    },
                    MaxActivePaths = 4,
                    NumTrailingBlanks = 1,
                    KeywordsScore = score,
                    KeywordsThreshold = threshold,
                    KeywordsFile = handles[5],
                };

                _spotter = CreateSpotter(ref config);
                if (_spotter == IntPtr.Zero) throw new InvalidOperationException("SherpaOnnxCreateKeywordSpotter 返回空（模型或关键词文件不可读）");
                _stream = CreateStream(_spotter);
                if (_stream == IntPtr.Zero) throw new InvalidOperationException("SherpaOnnxCreateKeywordStream 返回空");
            }
            finally
            {
                for (var i = 0; i < handles.Length; i++)
                    if (handles[i] != IntPtr.Zero) Marshal.FreeHGlobal(handles[i]);
            }
        }

        /// <summary>
        /// 喂一段 16kHz 单声道 float 音频，并把能解码的都解码掉。命中时返回口令原文并重置流，否则返回 null。
        /// </summary>
        public string Feed(float[] samples, int count)
        {
            if (_spotter == IntPtr.Zero || count <= 0) return null;
            AcceptWaveform(_stream, 16000, samples, count);

            string hit = null;
            while (IsReady(_spotter, _stream) != 0)
            {
                Decode(_spotter, _stream);
                var result = GetResult(_spotter, _stream);
                if (result == IntPtr.Zero) continue;
                try
                {
                    var r = Marshal.PtrToStructure<KeywordResult>(result);
                    var keyword = FromUtf8(r.Keyword);
                    if (!string.IsNullOrEmpty(keyword))
                    {
                        hit = keyword;
                        ResetStream(_spotter, _stream);
                    }
                }
                finally
                {
                    DestroyResult(result);
                }
            }

            return hit;
        }

        /// <summary>丢掉流里尚未解码的状态（每次按下麦克风前调用，避免上一次的尾音串进来）。</summary>
        public void Reset()
        {
            if (_spotter != IntPtr.Zero && _stream != IntPtr.Zero) ResetStream(_spotter, _stream);
        }

        public void Dispose()
        {
            if (_stream != IntPtr.Zero) DestroyStream(_stream);
            if (_spotter != IntPtr.Zero) DestroySpotter(_spotter);
            _stream = IntPtr.Zero;
            _spotter = IntPtr.Zero;
        }

        private static IntPtr Utf8(string value)
        {
            var bytes = Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
            var ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
        }

        private static string FromUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            var length = 0;
            while (Marshal.ReadByte(ptr, length) != 0) length++;
            if (length == 0) return string.Empty;
            var bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
