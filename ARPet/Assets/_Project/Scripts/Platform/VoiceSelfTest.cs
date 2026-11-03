using System.IO;
using UnityEngine;

namespace ZQXNXS.ARPet.Platform
{
    /// <summary>
    /// 不开麦克风的识别自检：把一段 16kHz 单声道音频直接喂给真实的原生识别器，返回命中的口令。
    /// 编辑器测试用它确认原生库能加载、结构体布局正确、模型与关键词文件可用。
    /// </summary>
    public static class VoiceSelfTest
    {
        /// <summary>
        /// <paramref name="modelDir"/> 是含 <see cref="VoiceKeywordListener.ModelFiles"/> 的目录。
        /// 识别器创建失败时抛异常（由调用方报告）；没命中返回 null。
        /// </summary>
        public static string Recognise(string modelDir, float[] samples16k, float threshold = 0.25f, float score = 1.5f)
        {
            using var spotter = new SherpaKeywordSpotter(
                Path.Combine(modelDir, "encoder.int8.onnx"), Path.Combine(modelDir, "decoder.onnx"),
                Path.Combine(modelDir, "joiner.int8.onnx"), Path.Combine(modelDir, "tokens.txt"),
                Path.Combine(modelDir, "keywords.txt"), threshold, score);

            var chunk = new float[1600];
            string hit = null;
            var padded = new float[samples16k.Length + 16000 * 9 / 10];
            System.Array.Copy(samples16k, 0, padded, 4800, samples16k.Length);
            for (var i = 0; i < padded.Length && hit == null; i += chunk.Length)
            {
                var n = Mathf.Min(chunk.Length, padded.Length - i);
                System.Array.Copy(padded, i, chunk, 0, n);
                hit = spotter.Feed(chunk, n);
            }

            return hit;
        }
    }
}
