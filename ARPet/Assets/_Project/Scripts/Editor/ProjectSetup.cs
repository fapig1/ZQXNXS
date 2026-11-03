using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Persistence;
using ZQXNXS.ARPet.Pet;
using ZQXNXS.ARPet.Presentation;
using ZQXNXS.ARPet.UI;

#if VUFORIA_ENGINE
using Vuforia;
#endif

// 不能写 using System;：本文件大量使用 UnityEngine.Object，System.Object 会造成 CS0104 二义性。
using InvalidOperationException = System.InvalidOperationException;

namespace ZQXNXS.ARPet.EditorTools
{
    /// <summary>
    /// 一键生成工程缺失的配置资产与两个场景。
    ///
    /// 为什么需要它：本机的 Unity 6 LTS 与 Vuforia 尚未安装，
    /// 因此场景文件、材质实例这类<b>必须由编辑器序列化产生</b>的二进制 / YAML 资产
    /// 不能在仓库里凭空写出来。装上编辑器后跑一次这个工具，就能得到可打开、可运行的最小场景。
    ///
    /// 它生成的是<b>骨架</b>，不是完成品：
    /// <list type="bullet">
    /// <item><c>01_TrackingSmoke</c>：用模拟跟踪源验证脚本编译、状态机与界面提示。</item>
    /// <item><c>02_Main</c>：多宠主场景。菜单 5 接 Vuforia 的 ARCamera 与四张角色卡 ImageTarget，
    /// 每张卡下挂一只对应颜色的宠物；菜单 6 补齐材质、Animator、触屏输入与诊断界面。</item>
    /// </list>
    /// </summary>
    public static class ProjectSetup
    {
        private const string ConfigFolder = "Assets/_Project/Configs";
        private const string SceneFolder = "Assets/_Project/Scenes";
        private const string ConfigPath = ConfigFolder + "/PetBehaviorConfig.asset";
        private const string MaterialFolder = "Assets/_Project/Art/Characters/Pet01/Materials";
        private const string PetModelPath = "Assets/_Project/Art/Characters/Pet01/Models/Pet01.fbx";
        private const string IdleFbxPath = "Assets/_Project/Art/Characters/Pet01/Models/Pet01_Idle.fbx";
        private const string EatFbxPath = "Assets/_Project/Art/Characters/Pet01/Models/Pet01_Eat.fbx";
        private const string TexturePath = "Assets/_Project/Art/Characters/Pet01/Textures/Pet01_BaseColor.png";
        private const string AnimationsFolder = "Assets/_Project/Animations";

        /// <summary>四色宠物共用的 Animator Controller；<see cref="PetSceneAssets"/> 也按这个路径加载。</summary>
        internal const string AnimatorControllerPath = AnimationsFolder + "/Pet01Controller.controller";
        private const string SmokeScenePath = SceneFolder + "/01_TrackingSmoke.unity";
        private const string MainScenePath = SceneFolder + "/02_Main.unity";

        /// <summary>
        /// 材质槽在 <c>SkinnedMeshRenderer.sharedMaterials</c> 里的实际顺序。
        /// 2026-09-16 用编辑器菜单核对过 Pet01.fbx 的子网格顺序，
        /// 与 <c>final_manifest.json</c> 里 <c>materials_planned</c> 的列出顺序不同，不能凭该文件顺序假设。
        /// </summary>
        private static readonly (string Name, float Smoothness)[] MaterialSlotOrder =
        {
            ("Pet01_Skin", 0.43f),
            ("Pet01_Oral", 0.57f),
            ("Pet01_Eyes", 0.64f),
        };

        [MenuItem("桌上有龙/1. 生成默认配置与场景", false, 0)]
        public static void GenerateAll()
        {
            var needsScenes = !File.Exists(SceneFolder + "/01_TrackingSmoke.unity") ||
                              !File.Exists(SceneFolder + "/02_Main.unity");
            if (needsScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(ConfigFolder);
            EnsureFolder(SceneFolder);
            EnsureFolder(MaterialFolder);

            var config = CreateOrLoadConfig();
            BuildSmokeScene(config);
            BuildMainScene(config);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AttachMaterialsAndAnimatorToSmokeScene();

            Debug.Log("[ProjectSetup] 默认配置与场景已检查，已有场景已保留，缺失场景已生成。\n" +
                      "下一步：运行菜单 5 把 02_Main 接成四色多宠 Vuforia 场景（ARCamera + 四张角色卡）。");
        }

        [MenuItem("桌上有龙/2. 只生成默认配置资产", false, 1)]
        public static void GenerateConfigOnly()
        {
            EnsureFolder(ConfigFolder);
            var config = CreateOrLoadConfig();
            Selection.activeObject = config;
            Debug.Log($"[ProjectSetup] 配置资产就绪：{ConfigPath}");
        }

        /// <summary>
        /// 创建 / 复用材质与 Animator Controller，并把它们接到 <c>01_TrackingSmoke</c> 场景里
        /// 已经存在的 Pet01 实例上。独立成菜单项是因为该场景大多数情况下已经存在（不会被
        /// <see cref="GenerateAll"/> 重新生成），材质与动画接入需要单独一次遍历已有场景。
        /// </summary>
        [MenuItem("桌上有龙/3. 接入角色材质、Animator 与触屏输入", false, 2)]
        public static void AttachMaterialsAndAnimatorToSmokeScene()
        {
            if (!File.Exists(SmokeScenePath))
            {
                Debug.LogWarning($"[ProjectSetup] 找不到 {SmokeScenePath}，请先运行“1. 生成默认配置与场景”。");
                return;
            }

            EnsureMaterialsAndAnimator();

            var openScenePath = EditorSceneManager.GetActiveScene().path;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(SmokeScenePath);

            var presenter = Object.FindFirstObjectByType<PetPresenter>(FindObjectsInactive.Include);
            if (presenter == null)
            {
                Debug.LogWarning("[ProjectSetup] 01_TrackingSmoke 里没有找到 PetPresenter，跳过材质与 Animator 接入。");
                return;
            }

            var renderer = presenter.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null)
            {
                Debug.LogWarning("[ProjectSetup] 场景中的 Pet01 实例没有 SkinnedMeshRenderer，可能仍是占位胶囊体。" +
                                 "请先确认 Pet01.fbx 已导入，再重新运行本工具。");
                return;
            }

            // Pet01.fbx 的根物体是 SkinnedMeshRenderer 的父级（Pet01_Mesh 与 Pet01Rig 的共同父节点），
            // Animator 应挂在这里，不是挂在更外层的 PetRoot（锚点）或更内层的 Mesh 节点上。
            var modelRoot = renderer.transform.parent != null ? renderer.transform.parent.gameObject : renderer.gameObject;
            AttachMaterialsAndAnimator(modelRoot, presenter);

            var appRoot = Object.FindFirstObjectByType<ZQXNXS.ARPet.App.AppRoot>(FindObjectsInactive.Include);
            var mainCamera = Camera.main;
            if (appRoot != null && mainCamera != null)
            {
                AttachTouchInputBridge(appRoot.gameObject, appRoot, mainCamera);
            }
            else
            {
                Debug.LogWarning("[ProjectSetup] 场景里缺少 AppRoot 或 MainCamera，跳过触屏输入链路接入。");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!string.IsNullOrEmpty(openScenePath) && openScenePath != SmokeScenePath && File.Exists(openScenePath))
            {
                EditorSceneManager.OpenScene(openScenePath);
            }

            Debug.Log("[ProjectSetup] 已把材质、Animator Controller 与触屏输入链路接到 01_TrackingSmoke 场景上。");
        }

        /// <summary>
        /// 切换构建目标到 Android，并按 Vuforia 11.4.4 官方支持表（
        /// Docs/环境缺失与下载清单.md 第 2.2 节）设置 IL2CPP / 仅 ARM64 / minSdk ≥29。
        /// 幂等：已经是目标值的项不会重复触发切换。
        /// </summary>
        [MenuItem("桌上有龙/4. 切换到 Android 并设置构建选项", false, 3)]
        public static void ConfigureAndroidBuildSettings()
        {
            var changed = false;

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[ProjectSetup] 正在切换构建目标到 Android（可能需要重新导入部分资源，稍等）……");
                var ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                if (!ok)
                {
                    Debug.LogError("[ProjectSetup] 切换构建目标失败，请检查 Console 中的详细报错。");
                    return;
                }
                changed = true;
            }

            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                changed = true;
            }

            // Vuforia 11.2.4 起已移除 armv7 支持，官方支持表标注 "ARM 64-bit only"。
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                changed = true;
            }

            // Vuforia 支持表要求 Android 10.0+（API 29）。Unity 默认值低于此，必须手工上调。
            if (PlayerSettings.Android.minSdkVersion != AndroidSdkVersions.AndroidApiLevel29)
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
                changed = true;
            }

            // 固定到本项目已验证安装的 API 36。Auto 会在每次批处理构建时运行
            // sdkmanager --list 并访问远程仓库，网络受限时可能阻塞数十分钟。
            if (PlayerSettings.Android.targetSdkVersion != AndroidSdkVersions.AndroidApiLevel36)
            {
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
                changed = true;
            }

            // 2026-10-30：横竖屏都支持（竖屏横向视野装不下两卡拉开，横屏才放得下）。倒置竖屏不开放。
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation ||
                !PlayerSettings.allowedAutorotateToPortrait ||
                PlayerSettings.allowedAutorotateToPortraitUpsideDown ||
                !PlayerSettings.allowedAutorotateToLandscapeLeft ||
                !PlayerSettings.allowedAutorotateToLandscapeRight)
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                PlayerSettings.allowedAutorotateToPortrait = true;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[ProjectSetup] Android 构建设置已按 Vuforia 官方支持表配置：" +
                          "IL2CPP、仅 ARM64、minSdk=API 29、targetSdk=API 36、竖屏 + 左右横屏自动旋转。" +
                          "仍需人工确认：Player Settings 里的包名、版本号、图标等发布信息。");
            }
            else
            {
                Debug.Log("[ProjectSetup] Android 构建设置已经是目标值，无需改动。");
            }
        }

        [MenuItem("桌上有龙/帮助/检查工程设置", false, 20)]
        public static void CheckProjectSettings()
        {
            var problems = new List<string>();

            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
            {
                problems.Add("Android 脚本后端不是 IL2CPP（Vuforia 在 Android 上需要 IL2CPP）。");
            }

            var architectures = PlayerSettings.Android.targetArchitectures;
            if ((architectures & AndroidArchitecture.ARM64) == 0)
            {
                problems.Add("Android 目标架构未勾选 ARM64。");
            }

            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel29)
            {
                problems.Add($"Android 最低 API 级别为 {PlayerSettings.Android.minSdkVersion}，" +
                             "低于 Vuforia 要求的 API 29（Android 10.0）。");
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                problems.Add($"当前构建目标为 {EditorUserBuildSettings.activeBuildTarget}，切换到 Android 后再打包。");
            }

            if (!File.Exists(PetModelPath))
            {
                problems.Add($"未找到角色模型：{PetModelPath}");
            }

#if !VUFORIA_ENGINE
            problems.Add("未检测到 Vuforia Engine（VUFORIA_ENGINE 未定义）。" +
                         "AR 相关代码处于条件编译关闭状态，当前只能用模拟跟踪源。");
#endif

            if (problems.Count == 0)
            {
                Debug.Log("[ProjectSetup] 工程设置检查通过。");
                return;
            }

            foreach (var problem in problems) Debug.LogWarning("[ProjectSetup] " + problem);
        }

        private static PetConfig CreateOrLoadConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PetConfig>(ConfigPath);
            if (existing != null) return existing;

            var config = ScriptableObject.CreateInstance<PetConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            Debug.Log($"[ProjectSetup] 已创建配置资产：{ConfigPath}");

            ReimportPetModel();
            AssignClipsToConfig(config);
            EditorUtility.SetDirty(config);

            return config;
        }

        /// <summary>
        /// 创建（或复用已有）三个材质与一个 Animator Controller，并把它们贴到 Pet01 的
        /// SkinnedMeshRenderer / Animator 上。幂等：已存在的资产不会被覆盖或重新赋参数，
        /// 避免抹掉已经手工调过的效果。
        /// </summary>
        private static void EnsureMaterialsAndAnimator()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(AnimationsFolder);
            CreatePet01Materials();
            CreatePet01AnimatorController();
            if (File.Exists(PetModelPath.Replace("Pet01.fbx", "Pet01_classic_proud.fbx")))
                ExclusiveAnimationSetup.Install();
        }

        private static Material[] CreatePet01Materials()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null)
            {
                Debug.LogWarning($"[ProjectSetup] 未找到贴图：{TexturePath}，材质将不带 BaseColor。");
            }

            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("[ProjectSetup] 找不到内置 Standard 着色器，材质创建已中止。");
                return System.Array.Empty<Material>();
            }

            var materials = new Material[MaterialSlotOrder.Length];
            for (var i = 0; i < MaterialSlotOrder.Length; i++)
            {
                var (name, smoothness) = MaterialSlotOrder[i];
                var path = $"{MaterialFolder}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = name };
                    if (texture != null) material.SetTexture("_MainTex", texture);
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Glossiness", smoothness);
                    AssetDatabase.CreateAsset(material, path);
                    Debug.Log($"[ProjectSetup] 已创建材质：{path}（Metallic=0, Smoothness={smoothness}）。");
                }
                materials[i] = material;
            }

            return materials;
        }

        /// <summary>
        /// 生成 Idle / Eat 两个状态的 Animator Controller。
        ///
        /// <b>不在这里连自动过渡边。</b><see cref="PetPresenter.PlayBehavior"/> 是靠
        /// <c>Animator.CrossFadeInFixedTime(状态名哈希, ...)</c> 直接切状态，完全绕开
        /// Animator Controller 自身的过渡图；行为层也已经在 <c>BehaviorStateMachine</c> 里
        /// 用固定时长（3.2 s）结算并主动切回 Idle。如果在这里加一条没有 Exit Time / 条件的
        /// Idle→Eat 边，Unity 会在 Console 打印"transition will be ignored"的噪音警告
        /// （2026-09-16 实测），加了也不会被用到，所以两个状态保持独立、互不连接。
        /// </summary>
        private static AnimatorController CreatePet01AnimatorController()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
            if (existing != null) return existing;

            var idleClip = LoadClip(IdleFbxPath, "Idle");
            var eatClip = LoadClip(EatFbxPath, "Eat");

            var controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);
            var layer = controller.layers[0];
            var stateMachine = layer.stateMachine;

            var idleState = stateMachine.AddState("Idle");
            idleState.motion = idleClip;
            stateMachine.defaultState = idleState;

            var eatState = stateMachine.AddState("Eat");
            eatState.motion = eatClip;

            AssetDatabase.SaveAssets();
            Debug.Log($"[ProjectSetup] 已创建 Animator Controller：{AnimatorControllerPath}。" +
                      "Idle/Eat 两个状态已就位，切换由 PetPresenter 用 CrossFadeInFixedTime 按状态名直接驱动，" +
                      "四色专属片段通过‘动画/接入四色专属动作’菜单幂等接入。");

            return controller;
        }

        private static AnimationClip LoadClip(string fbxPath, string clipName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip && clip.name == clipName) return clip;
            }
            Debug.LogWarning($"[ProjectSetup] 在 {fbxPath} 里没有找到名为 “{clipName}” 的 AnimationClip。");
            return null;
        }

        /// <summary>把已创建的材质与 Animator Controller 装到场景里的 Pet01 实例上。</summary>
        private static void AttachMaterialsAndAnimator(GameObject petInstance, PetPresenter presenter)
        {
            if (petInstance == null) return;

            var renderer = petInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer != null)
            {
                var materials = new Material[MaterialSlotOrder.Length];
                for (var i = 0; i < MaterialSlotOrder.Length; i++)
                {
                    materials[i] = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{MaterialSlotOrder[i].Name}.mat");
                }
                renderer.sharedMaterials = materials;
            }

            // 注意：不能写成 GetComponent<Animator>() ?? AddComponent<Animator>()。
            // Unity 组件的“找不到”用的是伪 null（重载 == 才能识别），??  运算符只做真正的引用 null 检查，
            // 会把“找不到”的伪 null 包装对象当成非 null 直接返回，导致后面访问它时抛 MissingComponentException。
            var animator = petInstance.GetComponent<Animator>();
            if (animator == null) animator = petInstance.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
            animator.applyRootMotion = false;

            if (presenter != null)
            {
                var so = new SerializedObject(presenter);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// 按 Docs/奶蛙最终版资产说明.md 的导入步骤设置 FBX 导入参数。
        /// 这一步用代码做，避免手工勾错：Rig=Generic、关闭材质导入。
        /// </summary>
        private static void ReimportPetModel()
        {
            var importer = AssetImporter.GetAtPath(PetModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[ProjectSetup] 无法读取模型导入设置：{PetModelPath}（文件是否存在？）");
                return;
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = true;
            importer.optimizeGameObjects = false;   // 首次检查阶段关闭，便于在层级里看到骨骼
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 8;
            importer.minBoneWeight = 0.00001f;

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            Debug.Log("[ProjectSetup] 已把 Pet01.fbx 设为 Generic 骨架、关闭材质导入与动画压缩。");
        }

        private static void AssignClipsToConfig(PetConfig config)
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("Pet01")) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) continue;

                if (!clips.ContainsKey(clip.name)) clips.Add(clip.name, clip);
            }

            if (clips.Count == 0)
            {
                Debug.LogWarning("[ProjectSetup] 尚未找到 Pet01 的 AnimationClip。" +
                                 "请先确认 3 份 FBX 已被 Unity 导入完成，再重新运行本工具。");
                return;
            }

            foreach (var clip in clips.Keys)
            {
                Debug.Log($"[ProjectSetup] 发现动画片段：{clip}");
            }
        }

        private static void BuildSmokeScene(PetConfig config)
        {
            var scenePath = SceneFolder + "/01_TrackingSmoke.unity";
            if (File.Exists(scenePath))
            {
                Debug.Log($"[ProjectSetup] 保留已有场景：{scenePath}");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 灯光与相机（仅用于编辑器检查，不属于资产的一部分）。
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 0.18f, -0.45f);
            cameraGo.transform.rotation = Quaternion.Euler(12f, 0f, 0f);

            // ── 模拟跟踪：只用一张卡的代理（本切片仍为单宠）──
            var trackingGo = new GameObject("TrackingSource (Stub)");
            var cardProxy = new GameObject("ClassicCardProxy").transform;
            cardProxy.SetParent(trackingGo.transform, worldPositionStays: false);
            cardProxy.localPosition = Vector3.zero;

            var stub = trackingGo.AddComponent<StubTrackingSource>();
            var stubSo = new SerializedObject(stub);
            var stubTargets = stubSo.FindProperty("targets");
            stubTargets.arraySize = 1;
            var stubEntry = stubTargets.GetArrayElementAtIndex(0);
            stubEntry.FindPropertyRelative("targetId").stringValue = "card_classic";
            stubEntry.FindPropertyRelative("proxy").objectReferenceValue = cardProxy;
            stubEntry.FindPropertyRelative("size").vector3Value = new Vector3(0.08f, 0f, 0.08f);
            stubEntry.FindPropertyRelative("simulatedStatus").intValue = (int)TrackingStatus.Tracked;
            stubSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 角色挂点 ──
            var petRoot = new GameObject(PetSceneAssets.PetRootName(SmokeScenePetId));
            petRoot.transform.SetParent(cardProxy, worldPositionStays: false);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PetModelPath);
            GameObject instance = null;
            if (model != null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(model, petRoot.transform);
                if (instance != null)
                {
                    instance.name = "Pet01";
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                }
            }
            else
            {
                Debug.LogWarning($"[ProjectSetup] 未找到 {PetModelPath}，场景中先放一个占位胶囊体。");
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "Pet01_Placeholder";
                capsule.transform.SetParent(petRoot.transform, worldPositionStays: false);
                capsule.transform.localScale = new Vector3(0.08f, 0.1f, 0.08f);
            }

            var presenter = petRoot.AddComponent<PetPresenter>();

            // 材质与 Animator 由 AttachMaterialsAndAnimatorToSmokeScene 统一接入，
            // 这样新建场景与已存在的旧场景走同一条路径，不必在这里重复一份逻辑。

            // ── 应用组装点 ──
            var appGo = new GameObject("App");
            var appRoot = appGo.AddComponent<ZQXNXS.ARPet.App.AppRoot>();

            var so = new SerializedObject(appRoot);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("trackingSourceBehaviour").objectReferenceValue = stub;
            so.FindProperty("markerRegistryBehaviour").objectReferenceValue = stub;
            so.FindProperty("autoCreateStubSource").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssignSmokeScenePetBinding(appRoot, presenter, petRoot.transform);

            AttachTouchInputBridge(appGo, appRoot, camera);

            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[ProjectSetup] 已生成 01_TrackingSmoke.unity（使用模拟跟踪源）。");
        }

        /// <summary>
        /// 挂一个 <see cref="ZQXNXS.ARPet.App.TouchInputBridge"/> 并接好摄像机。
        /// 命中目标不在这里逐个手拖：桥接脚本会在运行时自动查找场景中的
        /// <see cref="ZQXNXS.ARPet.App.PetHitTarget"/>，新增一只宠物只需给它加一个标记组件。
        /// 已存在时不重复添加，便于反复运行本工具。
        /// </summary>
        private static void AttachTouchInputBridge(GameObject appGo, ZQXNXS.ARPet.App.AppRoot appRoot,
            Camera camera)
        {
            var bridge = appGo.GetComponent<ZQXNXS.ARPet.App.TouchInputBridge>();
            if (bridge == null) bridge = appGo.AddComponent<ZQXNXS.ARPet.App.TouchInputBridge>();

            var bridgeSo = new SerializedObject(bridge);
            bridgeSo.FindProperty("appRoot").objectReferenceValue = appRoot;
            bridgeSo.FindProperty("raycastCamera").objectReferenceValue = camera;
            bridgeSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 把 <c>01_TrackingSmoke</c> 的 AppRoot 写成单条宠物绑定（pet_classic + 该表现层），
        /// 并给宠物根挂一只 <see cref="ZQXNXS.ARPet.App.PetHitTarget"/>。
        /// 只有冒烟场景是单宠；<c>02_Main</c> 的四色装配走 <see cref="WireTrackingAndBindings"/>。
        /// </summary>
        private static void AssignSmokeScenePetBinding(ZQXNXS.ARPet.App.AppRoot appRoot, PetPresenter presenter,
            Transform petRoot)
        {
            if (appRoot == null) return;

            var appSo = new SerializedObject(appRoot);
            var pets = appSo.FindProperty("pets");
            pets.arraySize = 1;
            var entry = pets.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("petId").stringValue = SmokeScenePetId;
            entry.FindPropertyRelative("presenter").objectReferenceValue = presenter;
            appSo.ApplyModifiedPropertiesWithoutUndo();

            AttachPetHitTarget(petRoot, SmokeScenePetId);
        }

        /// <summary>给宠物根挂命中区域标记（已有则只刷新 petId），供触屏拾取路由到这只宠物。</summary>
        private static void AttachPetHitTarget(Transform petRoot, string petId)
        {
            if (petRoot == null) return;

            var hitTarget = petRoot.GetComponent<ZQXNXS.ARPet.App.PetHitTarget>();
            if (hitTarget == null) hitTarget = petRoot.gameObject.AddComponent<ZQXNXS.ARPet.App.PetHitTarget>();

            // 宠物根在卡面原点 = 角色脚底：近似命中区是脚底到头顶的竖直胶囊（奶蛙高 0.2m）。
            var so = new SerializedObject(hitTarget);
            so.FindProperty("petId").stringValue = petId;
            so.FindProperty("hitRadiusMeters").floatValue = 0.08f;
            so.FindProperty("hitHeightMeters").floatValue = 0.2f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildMainScene(PetConfig config)
        {
            var scenePath = SceneFolder + "/02_Main.unity";
            if (File.Exists(scenePath))
            {
                Debug.Log($"[ProjectSetup] 保留已有场景：{scenePath}");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var appGo = new GameObject("App");
            var appRoot = appGo.AddComponent<ZQXNXS.ARPet.App.AppRoot>();

            var so = new SerializedObject(appRoot);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("autoCreateStubSource").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            // ARCamera 与四张角色卡 ImageTarget 必须由 Vuforia 的菜单生成（SDK 未导入时不能凭空造），
            // 这里只放一个待配置提示，菜单 5 接线成功后会删掉它。
            var hint = new GameObject("【待配置】Vuforia ARCamera 与四张角色卡 ImageTarget 请运行菜单 5");
            hint.transform.SetParent(appGo.transform, worldPositionStays: false);

            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[ProjectSetup] 已生成 02_Main.unity（四色多宠主场景骨架）。导入 Vuforia 后运行菜单 5：\n" +
                      "  1) 复用已有 ARCamera（缺失才创建）\n" +
                      "  2) 为 card_classic / card_calm / card_rage / card_divine 各建一个平级 ImageTarget\n" +
                      "  3) 每张卡下挂 PetRoot_<petId> + 对应颜色 Pet01 + PetPresenter + PetHitTarget\n" +
                      "  4) 尺寸一律取自 Assets/StreamingAssets/Vuforia/ZQXS.xml 的印刷有效尺寸");
        }

        /// <summary>Vuforia 数据库名，02_Main 与 04_FourCardTracking 共用。</summary>
        private const string VuforiaDatabaseName = VuforiaTargetDatabase.DatabaseName;

        /// <summary>
        /// 冒烟场景的宠物 ID。<c>02_Main</c> 不再使用单条绑定，四色由身份目录逐条装配。
        /// </summary>
        private const string SmokeScenePetId = "pet_classic";

        /// <summary>
        /// 把 02_Main 接成四色多宠主场景（开发顺序第 5 步的场景侧前置）：
        /// ARCamera（复用已有）+ 四张平级 ImageTarget（目标名、尺寸取自 ZQXS.xml）+
        /// 每张卡下 PetRoot_&lt;petId&gt;（PetPresenter / PetHitTarget / 对应颜色 Pet01）+
        /// VuforiaTrackingSource（登记四条目标）+ AppRoot（四条 pets 绑定）。
        ///
        /// <b>只在真实导入 Vuforia Engine 后才会生效</b>（<c>VUFORIA_ENGINE</c> 宏点亮时）。
        /// 幂等：已经接好的场景重复运行不会产生第二套 ARCamera / ImageTarget / 宠物。
        ///
        /// 旧单宠的 <c>DenCardTarget</c> / <c>FoodCardTarget</c>（连同其下的旧 Pet01 实例与
        /// DefaultObserverEventHandler）会被删除：食物玩法已删除，小窝卡也不再是业务入口。
        /// </summary>
        [MenuItem("桌上有龙/5. 接入 02_Main 的四色多宠 Vuforia 接线", false, 4)]
        public static void WireUpMainSceneVuforia()
        {
#if !VUFORIA_ENGINE
            Debug.LogError("[ProjectSetup] 未检测到 Vuforia Engine（VUFORIA_ENGINE 未定义），无法接线。" +
                           "请先按 SETUP.md 第 5 节导入 Vuforia。");
#else
            try
            {
                WireUpMainSceneVuforiaCore();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[ProjectSetup] 02_Main 四色多宠接线失败：" + exception.Message);
                Debug.LogException(exception);
            }
#endif
        }

        /// <summary>
        /// 批处理入口：先按菜单 5 重建 02_Main 的四色多宠接线，再按菜单 6 补齐材质、Animator、
        /// 触屏输入与诊断界面，然后保存场景。抛出异常即 <c>EditorApplication.Exit(1)</c>，
        /// 成功即 <c>EditorApplication.Exit(0)</c>；batchmode 下不弹"是否保存场景"对话框。
        /// </summary>
        public static void RebuildMainSceneForBatch()
        {
#if !VUFORIA_ENGINE
            Debug.LogError("[ProjectSetup] 未检测到 Vuforia Engine（VUFORIA_ENGINE 未定义），无法重建 02_Main。");
            EditorApplication.Exit(1);
#else
            try
            {
                // 先确保材质/Controller/四色专属状态齐备：菜单 5 需要按变体挂材质。
                EnsureMaterialsAndAnimator();
                WireUpMainSceneVuforiaCore();
                PrepareMainSceneForPhoneTestCore();
                AssetDatabase.SaveAssets();
                Debug.Log("[ProjectSetup] 批处理重建 02_Main 完成：四张角色卡各挂一只对应颜色的宠物。");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
#endif
        }

#if VUFORIA_ENGINE
        /// <summary>
        /// 菜单 5 的实际实现。失败直接抛异常：菜单包装器把它转成日志，批处理入口把它转成退出码 1。
        /// </summary>
        private static void WireUpMainSceneVuforiaCore()
        {
            if (!File.Exists(MainScenePath))
                throw new FileNotFoundException($"找不到 {MainScenePath}，请先运行“1. 生成默认配置与场景”。", MainScenePath);

            // batchmode 下不能弹"是否保存场景"对话框：直接丢弃当前未保存的临时改动。
            var openScenePath = EditorSceneManager.GetActiveScene().path;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath);
            try
            {
                var appRoot = Object.FindFirstObjectByType<ZQXNXS.ARPet.App.AppRoot>(FindObjectsInactive.Include);
                if (appRoot == null) throw new InvalidOperationException("02_Main 里没有找到 AppRoot，无法接线。");

                EnsureSimultaneousImageTargets(PetSceneAssets.RequiredSimultaneousTargets);
                EnsureArCamera();

                // 旧单宠结构必须先清掉：Den/Food 两张占位卡连同其下旧 Pet01 与
                // DefaultObserverEventHandler 都不再参与业务（食物玩法已删除）。
                DestroyAllNamed("DenCardTarget");
                DestroyAllNamed("FoodCardTarget");

                var identities = RequireIdentities();
                var presenters = EnsureCardsAndPets(identities);
                var trackingSource = EnsureTrackingSource(appRoot);
                WireTrackingAndBindings(appRoot, trackingSource, identities, presenters);
                RemovePlaceholderHints();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();

                Debug.Log($"[ProjectSetup] 02_Main 已接成四色多宠场景：数据库 “{VuforiaDatabaseName}”，" +
                          "四个平级 ImageTarget（" +
                          string.Join("、", identities.Select(i => PetSceneAssets.TargetObjectName(i.TargetId))) +
                          "），每张卡下挂对应颜色的 Pet01 + PetPresenter + PetHitTarget；" +
                          "VuforiaTrackingSource 与 AppRoot.pets 各登记四条，尺寸取自 ZQXS.xml。" +
                          "真实识别效果仍待打印卡片与真机验证。");
            }
            finally
            {
                if (!Application.isBatchMode && !string.IsNullOrEmpty(openScenePath) &&
                    openScenePath != MainScenePath && File.Exists(openScenePath))
                {
                    EditorSceneManager.OpenScene(openScenePath);
                }
            }
        }

        private static IReadOnlyList<PetIdentity> RequireIdentities()
        {
            var identities = PetIdentityCatalog.Default;
            if (!PetIdentityCatalog.TryValidate(identities, out var error))
                throw new InvalidOperationException(error);

            var sizes = VuforiaTargetDatabase.LoadSizes();
            foreach (var identity in identities)
            {
                if (!sizes.ContainsKey(identity.TargetId))
                    throw new InvalidOperationException($"{VuforiaTargetDatabase.DataSetPath} 缺少目标 {identity.TargetId}");
            }

            return identities;
        }

        /// <summary>
        /// 复用场景里已有的 ARCamera。batchmode 下 Vuforia 的 GameObject 菜单可能不可用，
        /// 因此场景里已有 ARCamera 时一律跳过创建；缺失且创建失败要给出明确原因。
        /// </summary>
        private static void EnsureArCamera()
        {
            Selection.activeObject = null;
            if (Object.FindFirstObjectByType<VuforiaBehaviour>(FindObjectsInactive.Include) != null) return;

            EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/AR Camera");
            Selection.activeObject = null;
            if (Object.FindFirstObjectByType<VuforiaBehaviour>(FindObjectsInactive.Include) != null) return;

            throw new InvalidOperationException("02_Main 缺少 ARCamera，且 “GameObject/Vuforia Engine/AR Camera” 没有创建出 VuforiaBehaviour" +
                (Application.isBatchMode
                    ? "（batchmode 下 Vuforia 菜单可能不可用，请先在编辑器里运行菜单 5 生成一次场景）。"
                    : "。"));
        }

        /// <summary>
        /// 创建 / 复用一张卡对应的 ImageTarget（Target_&lt;targetId&gt;），并把它摆到场景根。
        /// 显隐由 PetPresenter 按逐宠跟踪状态控制，所以这里不保留 DefaultObserverEventHandler。
        /// </summary>
        private static ImageTargetBehaviour EnsureCardTarget(PetIdentity identity, int layoutIndex)
        {
            // Unity 的 “GameObject/...” 菜单会把新物体建成当前选中物体的子物体；
            // 嵌套还会让 Vuforia 把子级目标的宽高悄悄改回默认值（2026-09-17 实测复现），
            // 所以每次创建前清空选中，创建后显式挂回场景根并重新写一次尺寸。
            Selection.activeObject = null;
            var objectName = PetSceneAssets.TargetObjectName(identity.TargetId);
            var target = FindOrCreateImageTarget(objectName, identity.TargetId);
            if (target == null) throw new InvalidOperationException("创建 ImageTarget 失败：" + objectName);
            Selection.activeObject = null;

            target.transform.SetParent(null, worldPositionStays: false);
            // 只用于编辑器里并排查看；运行时位姿由 Vuforia 写入。
            target.transform.position = new Vector3(
                (layoutIndex - (PetSceneAssets.RequiredSimultaneousTargets - 1) * 0.5f) * 0.12f, 0f, 0f);
            target.transform.rotation = Quaternion.identity;
            ApplyImageTargetProperties(target, identity.TargetId);

            // DefaultObserverEventHandler 在 Vuforia 示例程序集里，本程序集不引用它，按类型名匹配。
            foreach (var handler in target.GetComponents<MonoBehaviour>()
                         .Where(c => c != null && c.GetType().Name == "DefaultObserverEventHandler").ToArray())
            {
                Object.DestroyImmediate(handler);
            }

            return target;
        }

        /// <summary>逐身份建立"卡 → PetRoot_&lt;petId&gt; → Pet01 实例"，返回 petId → PetPresenter。</summary>
        private static Dictionary<string, PetPresenter> EnsureCardsAndPets(IReadOnlyList<PetIdentity> identities)
        {
            var presenters = new Dictionary<string, PetPresenter>(System.StringComparer.Ordinal);
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                var card = EnsureCardTarget(identity, i);
                presenters[identity.PetId] = EnsurePetForIdentity(card.transform, identity);
            }

            return presenters;
        }

        /// <summary>
        /// 在一张卡下补齐宠物子树（幂等）。朝向沿用旧单宠 02_Main 的导出坐标约定：
        /// Blender 导出与 ImageTarget 局部坐标都使用 +Y 向上、+Z 向前，不附加补偿旋转。
        /// 材质按变体换成 Pet01_{Skin,Oral,Eyes}_&lt;variant&gt;。
        /// </summary>
        private static PetPresenter EnsurePetForIdentity(Transform card, PetIdentity identity)
        {
            if (card == null) throw new InvalidOperationException("卡物体为空：" + identity.TargetId);

            var petRoot = card.Find(PetSceneAssets.PetRootName(identity.PetId));
            if (petRoot == null)
            {
                petRoot = new GameObject(PetSceneAssets.PetRootName(identity.PetId)).transform;
                petRoot.SetParent(card, worldPositionStays: false);
            }

            petRoot.localPosition = Vector3.zero;
            petRoot.localRotation = Quaternion.identity;
            petRoot.localScale = Vector3.one;

            var presenter = petRoot.GetComponent<PetPresenter>();
            if (presenter == null) presenter = petRoot.gameObject.AddComponent<PetPresenter>();

            var renderer = petRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            GameObject petInstance;
            if (renderer == null)
            {
                var model = PetSceneAssets.LoadModel();
                petInstance = (GameObject)PrefabUtility.InstantiatePrefab(model, petRoot);
                if (petInstance == null)
                    throw new InvalidOperationException("实例化 Pet01.fbx 失败：" + PetSceneAssets.ModelPath);
                petInstance.name = "Pet01_" + PetSceneAssets.VariantKey(identity.Variant);
                petInstance.transform.localPosition = Vector3.zero;
                petInstance.transform.localRotation = Quaternion.identity;
                petInstance.transform.localScale = Vector3.one * PetSceneAssets.PetScale;
            }
            else
            {
                petInstance = renderer.transform.parent != null ? renderer.transform.parent.gameObject : renderer.gameObject;
            }

            renderer = petInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null)
                throw new InvalidOperationException("宠物实例缺少 SkinnedMeshRenderer：" + PetSceneAssets.ModelPath);
            renderer.sharedMaterials = PetSceneAssets.LoadMaterials(identity.Variant);

            // 注意：不能写成 GetComponent<Animator>() ?? AddComponent<Animator>()。
            // Unity 的“找不到组件”是伪 null，?? 只做真正的引用 null 检查，会把它当成非 null。
            var animator = petInstance.GetComponent<Animator>();
            if (animator == null) animator = petInstance.AddComponent<Animator>();
            animator.runtimeAnimatorController = PetSceneAssets.LoadController();
            animator.applyRootMotion = false;

            var presenterSo = new SerializedObject(presenter);
            presenterSo.FindProperty("animator").objectReferenceValue = animator;
            // 跟踪丢失时隐藏的只是渲染器，不影响卡/Animator 本身。
            presenterSo.FindProperty("visualRoot").objectReferenceValue = petInstance;
            presenterSo.ApplyModifiedPropertiesWithoutUndo();

            // P5-B：二维嘴眼贴片挂到 Head 下并接进 PetPresenter。资源未生成时只警告，不阻断装配。
            if (FaceOverlaySetup.AssetsPresent(out var missingFace))
            {
                FaceOverlaySetup.ConfigureImporters();
                FaceOverlaySetup.Attach(presenter, petInstance, renderer, identity.Variant);
            }
            else
            {
                Debug.LogWarning("[ProjectSetup] 缺少表情贴片资源 " + missingFace + "，" + identity.PetId + " 暂不接嘴眼表情。");
            }

            // 2026-10-29：每只宠物一个 2D AudioSource（不随距离衰减，AR 桌面距离很近），PlaySfx 用它出声。
            var audio = petRoot.GetComponent<AudioSource>();
            if (audio == null) audio = petRoot.gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.volume = 1f;
            var audioSo = new SerializedObject(presenter);
            audioSo.FindProperty("sfxSource").objectReferenceValue = audio;
            audioSo.ApplyModifiedPropertiesWithoutUndo();

            AttachPetHitTarget(petRoot, identity.PetId);
            return presenter;
        }

        /// <summary>跟踪源挂在 App 物体上，与 01_TrackingSmoke 的 StubTrackingSource 同级。</summary>
        private static VuforiaTrackingSource EnsureTrackingSource(ZQXNXS.ARPet.App.AppRoot appRoot)
        {
            var source = appRoot.GetComponent<VuforiaTrackingSource>();
            if (source == null) source = appRoot.gameObject.AddComponent<VuforiaTrackingSource>();
            return source;
        }

        /// <summary>
        /// 把跟踪源与 AppRoot 一起写成四条：每条 = 一个身份（targetId/targetObject/尺寸、petId/presenter）。
        /// 值一律取自身份目录与 XML，避免"App 里一份、场景里又一份"的漂移。
        /// </summary>
        private static void WireTrackingAndBindings(ZQXNXS.ARPet.App.AppRoot appRoot,
            VuforiaTrackingSource trackingSource, IReadOnlyList<PetIdentity> identities,
            IReadOnlyDictionary<string, PetPresenter> presenters)
        {
            var trackingSo = new SerializedObject(trackingSource);
            var targets = trackingSo.FindProperty("targets");
            targets.arraySize = identities.Count;
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                var objectName = PetSceneAssets.TargetObjectName(identity.TargetId);
                var card = GameObject.Find(objectName);
                if (card == null) throw new InvalidOperationException("场景里缺少卡物体 " + objectName);

                var size = VuforiaTargetDatabase.GetSize(identity.TargetId);
                var entry = targets.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("targetId").stringValue = identity.TargetId;
                entry.FindPropertyRelative("targetObject").objectReferenceValue = card;
                // 印刷图像有效尺寸（米）：x=宽、z=高，不是纸张外框。
                entry.FindPropertyRelative("size").vector3Value = new Vector3(size.x, 0f, size.y);
            }

            trackingSo.ApplyModifiedPropertiesWithoutUndo();

            var appSo = new SerializedObject(appRoot);
            var pets = appSo.FindProperty("pets");
            pets.arraySize = identities.Count;
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (!presenters.TryGetValue(identity.PetId, out var presenter) || presenter == null)
                    throw new InvalidOperationException($"宠物 {identity.PetId} 缺少 PetPresenter，无法写入 AppRoot.pets");
                var entry = pets.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("petId").stringValue = identity.PetId;
                entry.FindPropertyRelative("presenter").objectReferenceValue = presenter;
            }

            appSo.FindProperty("trackingSourceBehaviour").objectReferenceValue = trackingSource;
            appSo.FindProperty("markerRegistryBehaviour").objectReferenceValue = trackingSource;
            appSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>按名字删除场景里的物体（含未激活的）。用于清理旧单宠的 Den/Food 双卡结构。</summary>
        private static void DestroyAllNamed(string objectName)
        {
            var found = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(go => go != null && go.name == objectName).ToArray();
            foreach (var go in found) Object.DestroyImmediate(go);
        }

        /// <summary>删除菜单 1 留下的"待配置"提示物体（已重命名多次，按前缀匹配）。</summary>
        private static void RemovePlaceholderHints()
        {
            var found = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(go => go != null && go.name.StartsWith("【待配置】")).ToArray();
            foreach (var go in found) Object.DestroyImmediate(go);
        }
#endif

        /// <summary>
        /// 完成首轮真机测试所需的剩余场景装配：四色宠物的材质/Animator、触屏输入、
        /// 跟踪/状态面板（含四宠列表）、EventSystem，以及 Android 构建场景列表。
        ///
        /// 要求菜单 5 已经把四张角色卡接好（<c>Target_card_*</c>）；这里只做幂等自愈与补齐。
        /// </summary>
        [MenuItem("桌上有龙/6. 准备 02_Main 真机基础测试", false, 5)]
        public static void PrepareMainSceneForPhoneTest()
        {
#if !VUFORIA_ENGINE
            Debug.LogError("[ProjectSetup] 未检测到 Vuforia Engine（VUFORIA_ENGINE 未定义），无法准备 02_Main。" +
                           "请先按 SETUP.md 第 5 节导入 Vuforia。");
#else
            try
            {
                PrepareMainSceneForPhoneTestCore();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[ProjectSetup] 02_Main 真机准备失败：" + exception.Message);
                Debug.LogException(exception);
            }
#endif
        }

#if VUFORIA_ENGINE
        /// <summary>菜单 6 的实际实现。失败直接抛异常，由菜单包装器/批处理入口决定如何收场。</summary>
        private static void PrepareMainSceneForPhoneTestCore()
        {
            if (!File.Exists(MainScenePath))
                throw new FileNotFoundException($"找不到 {MainScenePath}，请先生成并接好 02_Main。", MainScenePath);

            EnsureMaterialsAndAnimator();

            var openScenePath = EditorSceneManager.GetActiveScene().path;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath);
            try
            {
                var appRoot = Object.FindFirstObjectByType<ZQXNXS.ARPet.App.AppRoot>(FindObjectsInactive.Include);
                var arCamera = Camera.main;
                if (appRoot == null || arCamera == null)
                    throw new InvalidOperationException("02_Main 必须先包含 AppRoot 与 ARCamera（由 Vuforia AR Camera 提供 MainCamera）。" +
                                                        "请先运行菜单 5 接入四色多宠接线，再运行本步骤。");

                EnsureSimultaneousImageTargets(PetSceneAssets.RequiredSimultaneousTargets);
                var identities = RequireIdentities();

                // 构建前自愈：把四张卡的数据库/目标名/尺寸/初始化状态全部重写一遍。
                // 仅写数据库名和尺寸不够：Vuforia 11 的 ObserverBehaviour 在 OnVuforiaStarted 中会跳过
                // mInitializedInEditor == false 的目标，该目标就永远不会创建运行时观察者（2026-09-18 实测）。
                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    var objectName = PetSceneAssets.TargetObjectName(identity.TargetId);
                    var card = GameObject.Find(objectName);
                    var imageTarget = card != null ? card.GetComponent<ImageTargetBehaviour>() : null;
                    if (imageTarget == null)
                        throw new InvalidOperationException(
                            $"02_Main 缺少带 ImageTargetBehaviour 的 {objectName}，请先运行菜单 5 接入四色多宠接线。");
                    card.transform.SetParent(null, worldPositionStays: false);
                    ApplyImageTargetProperties(imageTarget, identity.TargetId);
                }

                var presenters = EnsureCardsAndPets(identities);
                var trackingSource = EnsureTrackingSource(appRoot);
                WireTrackingAndBindings(appRoot, trackingSource, identities, presenters);

                var appSo = new SerializedObject(appRoot);
                appSo.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<PetConfig>(ConfigPath);
                appSo.FindProperty("autoCreateStubSource").boolValue = false;
                appSo.ApplyModifiedPropertiesWithoutUndo();

                AttachTouchInputBridge(appRoot.gameObject, appRoot, arCamera);
                EnsureArLighting();
                EnsurePhoneTestUi(appRoot);
                EnsureMainSceneInBuildSettings();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();

                Debug.Log("[ProjectSetup] 02_Main 真机基础测试已就绪：四张角色卡各挂一只对应颜色的 Pet01，" +
                          "材质/Animator/触屏/四宠列表 UI 已接入，autoCreateStubSource 保持关闭，场景已加入构建列表。");
            }
            finally
            {
                if (!Application.isBatchMode && !string.IsNullOrEmpty(openScenePath) &&
                    openScenePath != MainScenePath && File.Exists(openScenePath))
                {
                    EditorSceneManager.OpenScene(openScenePath);
                }
            }
        }
#endif

        private static void EnsureArLighting()
        {
            var lightGo = GameObject.Find("AR Key Light");
            if (lightGo == null) lightGo = new GameObject("AR Key Light");
            var light = lightGo.GetComponent<Light>();
            if (light == null) light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.9f);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void EnsurePhoneTestUi(ZQXNXS.ARPet.App.AppRoot appRoot)
        {
            var canvasGo = GameObject.Find("PhoneTestCanvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("PhoneTestCanvas", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
            }

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 2400f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var safeArea = FindOrCreateUiChild(canvasGo.transform, "SafeArea");
            Stretch(safeArea);
            if (safeArea.GetComponent<SafeAreaFitter>() == null) safeArea.gameObject.AddComponent<SafeAreaFitter>();

            var panel = FindOrCreateUiChild(safeArea, "StatusPanel");
            var panelRect = (RectTransform)panel;
            panelRect.pivot = new Vector2(0.5f, 1f);
            // 竖屏铺满顶部宽度；横屏（参考画布约 2390×1080）只占左上 45%，内部比例与竖屏一致，
            // 不压住右侧相机画面。运行时由 OrientationAnchors 按屏幕宽高切换。
            var portraitPanel = new OrientationAnchors.Layout(new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -520f), new Vector2(-24f, -24f));
            var landscapePanel = new OrientationAnchors.Layout(new Vector2(0f, 1f), new Vector2(0.45f, 1f),
                new Vector2(24f, -520f), new Vector2(-24f, -24f));
            SetRect(panelRect, portraitPanel.AnchorMin, portraitPanel.AnchorMax, portraitPanel.OffsetMin,
                portraitPanel.OffsetMax);
            var panelOrientation = panel.GetComponent<OrientationAnchors>();
            if (panelOrientation == null) panelOrientation = panel.gameObject.AddComponent<OrientationAnchors>();
            panelOrientation.SetLayouts(portraitPanel, landscapePanel);
            EditorUtility.SetDirty(panelOrientation);

            var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
            if (panelImage == null) panelImage = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
            panelImage.color = new Color(0.035f, 0.055f, 0.07f, 0.82f);
            panelImage.raycastTarget = true;

            var tracking = EnsureText(panel, "TrackingText", 56, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRect(tracking.rectTransform, new Vector2(0f, 0.78f), Vector2.one,
                new Vector2(28f, 8f), new Vector2(-28f, -8f));

            var instruction = EnsureText(panel, "InstructionText", 30, TextAnchor.MiddleCenter, FontStyle.Normal);
            instruction.color = new Color(1f, 1f, 1f, 0.92f);
            SetRect(instruction.rectTransform, new Vector2(0f, 0.60f), new Vector2(1f, 0.78f),
                new Vector2(28f, 4f), new Vector2(-28f, -4f));

            // 食物玩法已删除：清掉旧场景遗留的饥饿/精力文本，避免面板上留下不再更新的死值。
            RemoveLegacyText(panel, "HungerText");
            RemoveLegacyText(panel, "EnergyText");

            var behavior = EnsureText(panel, "BehaviorText", 30, TextAnchor.MiddleLeft, FontStyle.Normal);
            SetRect(behavior.rectTransform, new Vector2(0f, 0.50f), new Vector2(0.4f, 0.60f),
                new Vector2(28f, 8f), new Vector2(-4f, -8f));
            var happiness = EnsureText(panel, "HappinessText", 30, TextAnchor.MiddleRight, FontStyle.Normal);
            // 行为与开心值同一行（右对齐），下面的 0.44～0.475 让给开心条。
            SetRect(happiness.rectTransform, new Vector2(0.4f, 0.50f), new Vector2(1f, 0.60f),
                new Vector2(4f, 8f), new Vector2(-28f, -8f));

            // 选中宠物的开心条：背景 + Filled 填充。fillAmount 由 PetStatusPanel 每帧按
            // "向下取整到 1%" 的开心值写入，0.999 不会提前填满。
            var barBackground = EnsureImage(panel, "HappinessBar", new Color(1f, 1f, 1f, 0.14f));
            SetRect(barBackground.rectTransform, new Vector2(0.4f, 0.44f), new Vector2(1f, 0.475f),
                new Vector2(4f, 0f), new Vector2(-28f, 0f));

            var happinessFill = EnsureImage(barBackground.transform, "Fill", new Color(1f, 0.78f, 0.36f, 1f));
            // Filled 类型必须带 sprite 才会真的按 fillAmount 裁切显示。
            happinessFill.type = UnityEngine.UI.Image.Type.Filled;
            happinessFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            happinessFill.fillAmount = 0f;
            Stretch(happinessFill.rectTransform);

            // 多宠：逐行列出四只宠物的变体与跟踪状态，选中项前加 "▶"。
            var petList = EnsureText(panel, "PetListText", 28, TextAnchor.MiddleLeft, FontStyle.Normal);
            petList.color = new Color(1f, 1f, 1f, 0.9f);
            SetRect(petList.rectTransform, Vector2.zero, new Vector2(1f, 0.42f),
                new Vector2(28f, 12f), new Vector2(-28f, -4f));

            EnsureVoiceUi(appRoot, safeArea);

            // P6：真机帧率记录（每 5 秒一行 CSV 到 persistentDataPath/perf），面板右下角显示最近窗口的 FPS。
            var recorder = appRoot.GetComponent<ZQXNXS.ARPet.Platform.PerformanceRecorder>();
            if (recorder == null) recorder = appRoot.gameObject.AddComponent<ZQXNXS.ARPet.Platform.PerformanceRecorder>();
            var fpsText = EnsureText(panel, "FpsText", 24, TextAnchor.LowerRight, FontStyle.Normal);
            fpsText.color = new Color(1f, 1f, 1f, 0.6f);
            SetRect(fpsText.rectTransform, new Vector2(0.6f, 0f), new Vector2(1f, 0.1f),
                new Vector2(4f, 4f), new Vector2(-28f, -2f));

            var statusPanel = panel.GetComponent<PetStatusPanel>();
            if (statusPanel == null) statusPanel = panel.gameObject.AddComponent<PetStatusPanel>();
            var statusSo = new SerializedObject(statusPanel);
            statusSo.FindProperty("stateSourceBehaviour").objectReferenceValue = appRoot;
            statusSo.FindProperty("trackingText").objectReferenceValue = tracking;
            statusSo.FindProperty("instructionText").objectReferenceValue = instruction;
            statusSo.FindProperty("behaviorText").objectReferenceValue = behavior;
            statusSo.FindProperty("happinessText").objectReferenceValue = happiness;
            statusSo.FindProperty("petListText").objectReferenceValue = petList;
            statusSo.FindProperty("happinessBar").objectReferenceValue = happinessFill;
            statusSo.FindProperty("fpsText").objectReferenceValue = fpsText;
            statusSo.FindProperty("fpsSourceBehaviour").objectReferenceValue = recorder;
            statusSo.ApplyModifiedPropertiesWithoutUndo();

            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }

        /// <summary>
        /// 语音口令（2026-10-29）：AppRoot 上挂一份 <see cref="ZQXNXS.ARPet.Platform.VoiceKeywordListener"/>，
        /// 屏幕底部中间放"按住说话"按钮。按钮是 UI（raycastTarget=true），按住它不会被当成点宠物。
        /// </summary>
        private static void EnsureVoiceUi(ZQXNXS.ARPet.App.AppRoot appRoot, RectTransform safeArea)
        {
            var listener = appRoot.GetComponent<ZQXNXS.ARPet.Platform.VoiceKeywordListener>();
            if (listener == null) listener = appRoot.gameObject.AddComponent<ZQXNXS.ARPet.Platform.VoiceKeywordListener>();

            var appSo = new SerializedObject(appRoot);
            appSo.FindProperty("voiceSourceBehaviour").objectReferenceValue = listener;
            appSo.ApplyModifiedPropertiesWithoutUndo();

            var buttonRect = FindOrCreateUiChild(safeArea, "VoiceButton");
            // 底部居中，固定宽 368（竖屏约屏宽 1/3）、高 140，离底边 72。固定宽度横竖屏都适用，
            // 不随横屏变成一条 800 宽的长条。
            buttonRect.pivot = new Vector2(0.5f, 0f);
            SetRect(buttonRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-184f, 72f), new Vector2(184f, 212f));

            var background = buttonRect.GetComponent<UnityEngine.UI.Image>();
            if (background == null) background = buttonRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            if (background.sprite == null) background.sprite = LoadBuiltinUiSprite();
            background.type = UnityEngine.UI.Image.Type.Sliced;
            background.color = new Color(0.16f, 0.55f, 0.95f, 0.92f);
            // 按钮必须接收射线：TouchInputBridge 据此判定"点在 UI 上"，不会穿透成点宠物。
            background.raycastTarget = true;

            var label = EnsureText(buttonRect, "Label", 40, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(label.rectTransform);
            label.text = "按住说话";

            var button = buttonRect.GetComponent<VoiceHoldButton>();
            if (button == null) button = buttonRect.gameObject.AddComponent<VoiceHoldButton>();
            var so = new SerializedObject(button);
            so.FindProperty("sourceBehaviour").objectReferenceValue = listener;
            so.FindProperty("background").objectReferenceValue = background;
            so.FindProperty("label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static RectTransform FindOrCreateUiChild(Transform parent, string name)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null) return existing;
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>删除旧版界面遗留的只读文本（已删除的饥饿 / 精力显示）。</summary>
        private static void RemoveLegacyText(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        /// <summary>
        /// 创建 / 复用一个纯色 Image。sprite 取内建 UISprite：Filled 类型必须带 sprite
        /// 才会按 fillAmount 裁切显示，否则 fillAmount 写了也看不出来。
        /// 一律 <c>raycastTarget = false</c>：状态面板只读，不能挡住角色点击。
        /// </summary>
        private static UnityEngine.UI.Image EnsureImage(Transform parent, string name, Color color)
        {
            var rect = FindOrCreateUiChild(parent, name);
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();

            if (image.sprite == null) image.sprite = LoadBuiltinUiSprite();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 内建白色 UI 图。两个候选名都试一遍：不同 Unity 版本里内建 skin 的命名略有差异，
        /// 拿不到 sprite 时 Filled 类型只会画成一整块，fillAmount 看起来像没生效。
        /// </summary>
        private static Sprite LoadBuiltinUiSprite()
        {
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite == null) sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            return sprite;
        }

        private static Text EnsureText(Transform parent, string name, int size, TextAnchor alignment, FontStyle style)
        {
            var rect = FindOrCreateUiChild(parent, name);
            var text = rect.GetComponent<Text>();
            if (text == null) text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void EnsureMainSceneInBuildSettings()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
        }

        /// <summary>生成可直接安装并采集日志的 Android Development 测试包。</summary>
        [MenuItem("桌上有龙/7. 构建 Android 基础测试 APK", false, 6)]
        public static void BuildPhoneTestApk()
        {
            ConfigureAndroidBuildSettings();
#if VUFORIA_ENGINE
            // 构建前必须确认四张 card_* 角色卡都在：菜单 6 会重写它们的数据库/尺寸/初始化状态，
            // 缺卡直接中止，避免打出只有 App 空壳的场景。
            try
            {
                PrepareMainSceneForPhoneTestCore();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[ProjectSetup] 构建前检查 02_Main 失败：" + exception.Message);
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }
                throw;
            }
#endif

            var outputPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../Builds/Android/ARPet-phone-test.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                var message = $"Android APK 构建失败：{report.summary.result}，错误 {report.summary.totalErrors} 个。";
                Debug.LogError("[ProjectSetup] " + message);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }
                throw new System.InvalidOperationException(message);
            }

            Debug.Log($"[ProjectSetup] Android 基础测试 APK 已生成：{outputPath} " +
                      $"({report.summary.totalSize / (1024f * 1024f):F1} MiB)");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

#if VUFORIA_ENGINE
        /// <summary>
        /// 找场景里名字匹配的 ImageTarget；不存在就用 Vuforia 官方菜单创建一个，
        /// 再把它的数据库、目标名、尺寸与运行时观察者初始化状态接到 ZQXS 数据库的
        /// 对应目标上。
        ///
        /// 数据库、目标名和尺寸字段名是 2026-09-17 通过反射 Vuforia.Unity.Engine.dll 与
        /// Vuforia.Unity.Editor.dll（编辑器用来画 Database/Target 下拉框的
        /// <c>VuforiaUtilities.DrawDatabaseTargetInspector</c>）核实的，官方没有公开文档，
        /// 版本升级后如果接线失效，先用同样的反射方法重新核对字段名是否还存在。
        /// </summary>
        internal static ImageTargetBehaviour FindOrCreateImageTarget(string goName, string trackableName)
        {
            var existing = GameObject.Find(goName);
            ImageTargetBehaviour behaviour;
            if (existing != null)
            {
                behaviour = existing.GetComponent<ImageTargetBehaviour>();
            }
            else
            {
                var before = Object.FindObjectsByType<ImageTargetBehaviour>(FindObjectsSortMode.None);
                EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/Image Target");
                var after = Object.FindObjectsByType<ImageTargetBehaviour>(FindObjectsSortMode.None);
                behaviour = System.Linq.Enumerable.FirstOrDefault(after, b => System.Array.IndexOf(before, b) < 0);
                if (behaviour == null && Application.isBatchMode)
                {
                    // batchmode 下 Vuforia 的 GameObject 菜单不保证执行（它依赖编辑器界面逻辑）。
                    // 退化为直接创建目标组件：数据库/目标名/宽高/初始化状态随后都由
                    // ApplyImageTargetProperties 写入，但这条路径没有经过 Vuforia 自己的菜单，
                    // 真机识别前应当用编辑器里的菜单 5 复核一次生成的场景。
                    Debug.LogWarning("[ProjectSetup] batchmode 下未能通过 Vuforia 菜单创建 ImageTarget，改为直接挂组件：" +
                                     goName + "。真机识别前请用编辑器菜单 5 复核。");
                    behaviour = new GameObject(goName).AddComponent<ImageTargetBehaviour>();
                }
                if (behaviour == null)
                {
                    Debug.LogError("[ProjectSetup] 执行 “GameObject/Vuforia Engine/Image Target” 后未能定位新对象。");
                    return null;
                }
                behaviour.gameObject.name = goName;
            }

            ApplyImageTargetProperties(behaviour, trackableName);
            return behaviour;
        }

        /// <summary>
        /// 写入数据库名、目标名、尺寸与运行时观察者初始化状态。独立成方法是因为 2026-09-17 实测：
        /// 如果新建的 ImageTarget 当时是另一个 ImageTarget 的子物体，Vuforia 的
        /// 编辑器逻辑会在后续刷新时把宽高悄悄改回默认的 0.2 m。调用方在确认对象
        /// 已经挂到场景根、且没有父子关系之后，必须重新调用本方法兜底纠正一次。
        /// </summary>
        internal static void ApplyImageTargetProperties(ImageTargetBehaviour behaviour, string trackableName)
        {
            var size = VuforiaTargetDatabase.GetSize(trackableName);
            var so = new SerializedObject(behaviour);
            so.FindProperty("mDataSetPath").stringValue = VuforiaTargetDatabase.DataSetPath;
            so.FindProperty("mTrackableName").stringValue = trackableName;
            // Vuforia 11 uses this editor-persisted flag to decide whether
            // ObserverBehaviour.OnVuforiaStarted may create the serialized target.
            // A false value is not a harmless editor hint: it makes the target
            // permanently skip CreateFromSerializedTarget() at runtime.
            so.FindProperty("mInitializedInEditor").boolValue = true;
            so.FindProperty("mWidth").floatValue = size.x;
            so.FindProperty("mHeight").floatValue = size.y;
            // Vuforia stores height/width; keep it consistent with the database entry.
            var aspect = so.FindProperty("mAspectRatio");
            if (aspect != null) aspect.floatValue = size.y / size.x;
            // Clear the migration marker left by older/partially initialized
            // ImageTarget editor data. Keep both targets on the same current path.
            so.FindProperty("mTrackingOptimizationNeedsUpgrade").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 把 <c>VuforiaConfiguration.MaxSimultaneousImageTargets</c> 提到至少 <paramref name="required"/>。
        /// 默认值是 1（2026-09-16 生成时的默认值），四卡同屏必须 ≥4。只上调、不下调，
        /// 保留用户手工调高的值。找不到配置资产时返回 false，由调用方决定是否报错。
        /// </summary>
        internal static bool EnsureSimultaneousImageTargets(int required)
        {
            var config = Resources.Load<VuforiaConfiguration>("VuforiaConfiguration");
            if (config == null)
            {
                Debug.LogWarning("[ProjectSetup] 找不到 Resources/VuforiaConfiguration，跳过 maxSimultaneousImageTargets 检查。");
                return false;
            }

            if (config.Vuforia.MaxSimultaneousImageTargets >= required) return true;

            var previous = config.Vuforia.MaxSimultaneousImageTargets;
            config.Vuforia.MaxSimultaneousImageTargets = required;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ProjectSetup] VuforiaConfiguration.MaxSimultaneousImageTargets {previous} → {required}" +
                      "（四张角色卡需要同时被跟踪）。");
            return true;
        }
#endif

        private static void EnsureFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath)) return;

            var parts = assetFolderPath.Split('/');
            var current = parts[0]; // "Assets"
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
