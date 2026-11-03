#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace ZQXNXS.ARPet.EditorTools
{
    /// <summary>
    /// 语音口令需要 <c>RECORD_AUDIO</c>。Unity 通常会因为检测到 <c>Microphone</c> 调用自动加上，
    /// 这里在 Gradle 工程生成后再显式确认一次，避免裁剪或检测失误导致真机永远拿不到麦克风权限。
    /// 不覆盖整个 AndroidManifest，只在 unityLibrary 的清单里补一条（已有就跳过）。
    /// </summary>
    internal sealed class AndroidMicrophonePermission : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string Permission = "android.permission.RECORD_AUDIO";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;

            var doc = new XmlDocument();
            doc.Load(manifest);
            var root = doc.DocumentElement;
            if (root == null) return;

            foreach (XmlNode node in root.SelectNodes("uses-permission"))
            {
                var name = node.Attributes?["name", AndroidNs]?.Value;
                if (name == Permission) return;
            }

            var element = doc.CreateElement("uses-permission");
            element.SetAttribute("name", AndroidNs, Permission);
            root.PrependChild(element);
            doc.Save(manifest);
        }
    }
}
#endif
