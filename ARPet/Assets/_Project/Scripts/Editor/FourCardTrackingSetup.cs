using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;

#if VUFORIA_ENGINE
using Vuforia;
#endif

namespace ZQXNXS.ARPet.EditorTools
{
    /// <summary>
    /// 开发顺序第2步：四张 card_* 目标的早期实测场景与 APK。
    ///
    /// 场景 <c>04_FourCardTracking</c> 独立于旧单宠 02_Main：ARCamera + 四个平级 ImageTarget
    /// （名称、印刷尺寸取自 ZQXS.xml，映射取自 <see cref="PetIdentityCatalog"/>），每张卡下挂对应颜色的
    /// Pet01 做 Idle，<see cref="TargetTrackingProbe"/> 只在 TRACKED 时显示并记录日志。
    /// 不接 AppRoot、不结算开心值；业务多宠化属于第3步。
    /// </summary>
    public static class FourCardTrackingSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/04_FourCardTracking.unity";
        public const int RequiredSimultaneousTargets = PetSceneAssets.RequiredSimultaneousTargets;
        private const string ApkRelativePath = "../../Builds/Android/ARPet-four-card-test.apk";

        /// <summary>角色显示比例。模型高 0.2m，66×94mm 小卡上偏大；早期识别实测先保持 1，与 02_Main 一致。</summary>
        public const float PetScale = PetSceneAssets.PetScale;

        public static string TargetObjectName(string targetId) => PetSceneAssets.TargetObjectName(targetId);

        [MenuItem("桌上有龙/8. 生成四卡跟踪实测场景 04_FourCardTracking", false, 7)]
        public static void CreateScene()
        {
#if !VUFORIA_ENGINE
            Debug.LogError("[FourCardTrackingSetup] 未检测到 Vuforia Engine，无法生成四卡场景。");
#else
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var sizes = VuforiaTargetDatabase.LoadSizes();
            var identities = PetIdentityCatalog.Default;
            if (!PetIdentityCatalog.TryValidate(identities, out var error))
                throw new System.InvalidOperationException(error);
            foreach (var identity in identities)
            {
                if (!sizes.ContainsKey(identity.TargetId))
                    throw new System.InvalidOperationException($"{VuforiaTargetDatabase.DataSetPath} 缺少目标 {identity.TargetId}");
            }

            EnsureSimultaneousImageTargets(RequiredSimultaneousTargets);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Selection.activeGameObject = null;
            EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/AR Camera");
            if (Object.FindFirstObjectByType<VuforiaBehaviour>(FindObjectsInactive.Include) == null)
                throw new System.InvalidOperationException("执行 GameObject/Vuforia Engine/AR Camera 后未找到 VuforiaBehaviour");

            var light = new GameObject("AR Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var controller = PetSceneAssets.LoadController();
            var model = PetSceneAssets.LoadModel();

            var entries = new TargetTrackingProbe.Entry[identities.Count];
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                // 清空选中：Vuforia 菜单会把新目标建成选中物体的子级，嵌套还会把宽高改回默认值。
                Selection.activeGameObject = null;
                var target = ProjectSetup.FindOrCreateImageTarget(TargetObjectName(identity.TargetId), identity.TargetId);
                if (target == null) throw new System.InvalidOperationException("创建 ImageTarget 失败：" + identity.TargetId);
                Selection.activeGameObject = null;
                target.transform.SetParent(null, worldPositionStays: false);
                // 仅用于编辑器内并排查看；运行时位姿由 Vuforia 写入。
                target.transform.position = new Vector3((i - 1.5f) * 0.12f, 0f, 0f);
                target.transform.rotation = Quaternion.identity;
                ProjectSetup.ApplyImageTargetProperties(target, identity.TargetId);

                // 显隐只由探针按 TRACKED 决定，去掉 Vuforia 默认的渲染器开关，避免延伸跟踪时显示旧位姿。
                // DefaultObserverEventHandler 在 Vuforia 示例程序集里，本程序集不引用它，按类型名匹配。
                foreach (var handler in target.GetComponents<MonoBehaviour>()
                             .Where(c => c != null && c.GetType().Name == "DefaultObserverEventHandler").ToArray())
                    Object.DestroyImmediate(handler);

                var variant = PetSceneAssets.VariantKey(identity.Variant);
                var content = new GameObject(PetSceneAssets.PetRootName(identity.PetId));
                content.transform.SetParent(target.transform, worldPositionStays: false);
                var pet = (GameObject)PrefabUtility.InstantiatePrefab(model, content.transform);
                pet.name = "Pet01_" + variant;
                pet.transform.localPosition = Vector3.zero;
                pet.transform.localRotation = Quaternion.identity;
                pet.transform.localScale = Vector3.one * PetScale;
                var renderer = pet.GetComponentInChildren<SkinnedMeshRenderer>(true);
                renderer.sharedMaterials = PetSceneAssets.LoadMaterials(identity.Variant);
                var animator = pet.GetComponent<Animator>();
                if (animator == null) animator = pet.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                entries[i] = new TargetTrackingProbe.Entry { target = target.gameObject, content = content };
            }

            var probeGo = new GameObject("FourCardTrackingProbe");
            var probe = probeGo.AddComponent<TargetTrackingProbe>();
            var so = new SerializedObject(probe);
            var list = so.FindProperty("entries");
            list.arraySize = entries.Length;
            for (var i = 0; i < entries.Length; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("target").objectReferenceValue = entries[i].target;
                element.FindPropertyRelative("content").objectReferenceValue = entries[i].content;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FourCardTrackingSetup] 已生成 {ScenePath}：四个平级 ImageTarget，" +
                      $"尺寸取自 {VuforiaTargetDatabase.DataSetPath}，最大同时跟踪数 ≥{RequiredSimultaneousTargets}。");
#endif
        }

        /// <summary>
        /// 重新写入四个目标的数据库/尺寸/初始化状态后构建四卡测试 APK。
        /// 只把 04 场景放进本次构建，不改 EditorBuildSettings（02_Main 仍是默认构建场景）。
        /// </summary>
        [MenuItem("桌上有龙/9. 构建四卡跟踪实测 APK", false, 8)]
        public static void BuildFourCardTestApk()
        {
            ProjectSetup.ConfigureAndroidBuildSettings();
            if (!File.Exists(ScenePath)) CreateScene();
#if VUFORIA_ENGINE
            else RefreshTargets();
#endif
            var outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, ApkRelativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                var message = $"四卡 APK 构建失败：{report.summary.result}，错误 {report.summary.totalErrors} 个。";
                Debug.LogError("[FourCardTrackingSetup] " + message);
                if (Application.isBatchMode) { EditorApplication.Exit(1); return; }
                throw new System.InvalidOperationException(message);
            }
            Debug.Log($"[FourCardTrackingSetup] 四卡测试 APK：{outputPath} ({report.summary.totalSize / (1024f * 1024f):F1} MiB)");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

#if VUFORIA_ENGINE
        /// <summary>构建前自愈：按 XML 重写四个目标的序列化状态（与菜单6对 02_Main 的做法一致）。</summary>
        private static void RefreshTargets()
        {
            EnsureSimultaneousImageTargets(RequiredSimultaneousTargets);
            var scene = EditorSceneManager.OpenScene(ScenePath);
            foreach (var identity in PetIdentityCatalog.Default)
            {
                var go = GameObject.Find(TargetObjectName(identity.TargetId));
                var target = go != null ? go.GetComponent<ImageTargetBehaviour>() : null;
                if (target == null)
                    throw new System.InvalidOperationException($"{ScenePath} 缺少 {TargetObjectName(identity.TargetId)}，请重新运行菜单 8");
                ProjectSetup.ApplyImageTargetProperties(target, identity.TargetId);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>只上调、不下调，保留手工设置的更大值。配置资产缺失时直接失败（04 场景必须能同时跟踪四张卡）。</summary>
        private static void EnsureSimultaneousImageTargets(int required)
        {
            if (ProjectSetup.EnsureSimultaneousImageTargets(required)) return;
            throw new System.InvalidOperationException("找不到 Resources/VuforiaConfiguration");
        }
#endif
    }
}
