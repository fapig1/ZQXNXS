using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZQXNXS.ARPet.Pet;
using ZQXNXS.ARPet.Platform;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// 语音口令的资源齐套检查：四段回应音频能被 Resources 加载、KWS 模型文件齐全、关键词文件
    /// 覆盖配置里的全部口令、两个平台的原生库都在。这些缺任何一个，真机上语音功能都会静默停用。
    /// </summary>
    public sealed class VoiceAssetsTests
    {
        private const string ConfigPath = "Assets/_Project/Configs/PetBehaviorConfig.asset";

        [Test]
        public void EveryCommandSfx_LoadsFromResources()
        {
            var config = AssetDatabase.LoadAssetAtPath<PetConfig>(ConfigPath);
            Assert.IsNotNull(config);
            foreach (var command in config.VoiceCommands)
            {
                var clip = Resources.Load<AudioClip>("Audio/" + command.SfxId);
                Assert.IsNotNull(clip, $"缺少音效 Resources/Audio/{command.SfxId}（口令 {command.Phrase}）");
                Assert.Greater(clip.length, 0.2f);
            }

            Assert.IsNotNull(Resources.Load<AudioClip>("Audio/" + config.SocialSfxId), "缺少共同大笑音效");
        }

        [Test]
        public void KeywordModel_IsComplete_AndCoversEveryCommand()
        {
            var dir = Path.Combine(Application.streamingAssetsPath, VoiceKeywordListener.ModelFolder);
            foreach (var file in VoiceKeywordListener.ModelFiles)
            {
                var path = Path.Combine(dir, file);
                Assert.IsTrue(File.Exists(path), "缺少语音模型文件 " + path);
                Assert.Greater(new FileInfo(path).Length, 10, file + " 是空文件");
            }

            var keywords = File.ReadAllText(Path.Combine(dir, "keywords.txt"));
            var config = AssetDatabase.LoadAssetAtPath<PetConfig>(ConfigPath);
            foreach (var command in config.VoiceCommands)
            {
                Assert.IsTrue(keywords.Contains("@" + command.Phrase),
                    $"keywords.txt 没有口令“{command.Phrase}”：改口令后要重新生成关键词文件");
            }
        }

        /// <summary>
        /// 端到端：在编辑器里加载真实的 sherpa-onnx 原生库，把一段"雷霆大笑"（Windows TTS 合成，
        /// 16kHz 单声道）喂进去，必须识别出这句；一段无关句子（"我们去吃饭吧"）不能误触发。
        /// 这同时验证了 P/Invoke 结构体布局与 C API v2.0.1 一致。
        /// </summary>
        [Test]
        public void NativeSpotter_RecognisesCommand_AndIgnoresUnrelatedSpeech()
        {
            var dir = Path.Combine(Application.streamingAssetsPath, VoiceKeywordListener.ModelFolder);
            Assert.AreEqual("雷霆大笑", VoiceSelfTest.Recognise(dir, ReadWav("voice_leiting_tts.wav")));
            Assert.IsNull(VoiceSelfTest.Recognise(dir, ReadWav("voice_negative_tts.wav")), "无关句子不得触发口令");
            Assert.IsNull(VoiceSelfTest.Recognise(dir, new float[16000 * 2]), "静音不得触发口令");
        }

        private static float[] ReadWav(string name)
        {
            var bytes = File.ReadAllBytes("Assets/_Project/Scripts/Tests/EditMode/Fixtures/" + name);
            // 标准 44 字节 PCM 头，16-bit 单声道 16kHz（由 Tools 脚本生成，格式固定）。
            var count = (bytes.Length - 44) / 2;
            var samples = new float[count];
            for (var i = 0; i < count; i++) samples[i] = System.BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f;
            return samples;
        }

        [Test]
        public void NativeLibraries_ArePresentForAndroidAndEditor()
        {
            foreach (var path in new[]
                     {
                         "Assets/Plugins/Android/sherpa-onnx/arm64-v8a/libsherpa-onnx-c-api.so",
                         "Assets/Plugins/Android/sherpa-onnx/arm64-v8a/libonnxruntime.so",
                         "Assets/Plugins/x86_64/sherpa-onnx/sherpa-onnx-c-api.dll",
                         "Assets/Plugins/x86_64/sherpa-onnx/onnxruntime.dll",
                     })
            {
                Assert.IsTrue(File.Exists(path), "缺少原生库 " + path);
                var importer = AssetImporter.GetAtPath(path) as PluginImporter;
                Assert.IsNotNull(importer, path + " 没有被识别为插件");
            }

            var android = (PluginImporter)AssetImporter.GetAtPath("Assets/Plugins/Android/sherpa-onnx/arm64-v8a/libsherpa-onnx-c-api.so");
            Assert.IsTrue(android.GetCompatibleWithPlatform(BuildTarget.Android), "Android 库必须打进 APK");
            Assert.IsFalse(android.GetCompatibleWithEditor(), "Android 库不能在 Windows 编辑器里加载");
        }
    }
}
