using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;

namespace ZQXNXS.ARPet.EditorTools
{
    /// <summary>
    /// 读取 StreamingAssets 里的 Vuforia 设备数据库 XML，作为目标名与印刷尺寸的唯一来源。
    /// 编辑器工具不再手写目标尺寸，避免构建准备把新卡片写回占位尺寸。
    /// </summary>
    internal static class VuforiaTargetDatabase
    {
        public const string DatabaseName = "ZQXS";

        // ImageTargetBehaviour stores the path relative to StreamingAssets, including
        // the Vuforia directory and the XML extension. A bare database name makes
        // the Android runtime look in the wrong asset path and causes
        // DATABASE_LOAD_ERROR during observer creation.
        public const string DataSetPath = "Vuforia/" + DatabaseName + ".xml";

        public static string XmlFullPath => Path.Combine(Application.streamingAssetsPath, DataSetPath);

        /// <summary>目标名 → 宽高（米）。按 XML 中出现的顺序返回。</summary>
        public static IReadOnlyDictionary<string, Vector2> LoadSizes()
        {
            var document = new XmlDocument();
            document.Load(XmlFullPath);
            var result = new Dictionary<string, Vector2>();
            var nodes = document.SelectNodes("//ImageTarget");
            if (nodes == null) return result;
            foreach (XmlElement node in nodes)
            {
                var parts = node.GetAttribute("size").Split(' ');
                if (parts.Length != 2) throw new InvalidDataException($"{DataSetPath}: {node.GetAttribute("name")} 的 size 格式无效");
                result[node.GetAttribute("name")] = new Vector2(
                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture));
            }
            return result;
        }

        public static Vector2 GetSize(string targetName)
        {
            if (LoadSizes().TryGetValue(targetName, out var size)) return size;
            throw new KeyNotFoundException($"{DataSetPath} 中没有目标 {targetName}");
        }
    }
}
