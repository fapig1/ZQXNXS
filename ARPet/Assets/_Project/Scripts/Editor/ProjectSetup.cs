using System.Collections.Generic;
using System.IO;
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
    /// <item><c>02_Main</c>：预留 Vuforia 摄像头与 ImageTarget 的挂点，导入 SDK 后再补。</item>
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
        private const string AnimatorControllerPath = AnimationsFolder + "/Pet01Controller.controller";
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
                      "下一步：把 Vuforia 的 ImageTarget 拖进 02_Main，并核对 AppRoot 上的跟踪源字段。");
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
                // presenter 挂在 PetRoot 上（锚点），用它作触屏命中判定的球心近似原点。
                AttachTouchInputBridge(appRoot.gameObject, appRoot, mainCamera, presenter.transform);
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

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[ProjectSetup] Android 构建设置已按 Vuforia 官方支持表配置：" +
                          "IL2CPP、仅 ARM64、minSdk=API 29、targetSdk=API 36。" +
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
                      "其余行为的动画尚未制作，暂不在 Controller 中出现。");

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

            // ── 模拟跟踪：小窝卡代理 + 食物卡代理 ──
            var trackingGo = new GameObject("TrackingSource (Stub)");
            var denProxy = new GameObject("DenCardProxy").transform;
            denProxy.SetParent(trackingGo.transform, worldPositionStays: false);
            denProxy.localPosition = Vector3.zero;

            var foodProxy = new GameObject("FoodCardProxy").transform;
            foodProxy.SetParent(trackingGo.transform, worldPositionStays: false);
            // 默认放在判定区外，避免一按 Play 就自动投喂一次。
            foodProxy.localPosition = new Vector3(0.3f, 0f, 0f);

            var stub = trackingGo.AddComponent<StubTrackingSource>();
            var stubSo = new SerializedObject(stub);
            stubSo.FindProperty("denProxy").objectReferenceValue = denProxy;
            stubSo.FindProperty("foodProxy").objectReferenceValue = foodProxy;
            stubSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 角色挂点 ──
            var petRoot = new GameObject("PetRoot (AnchoredToDen)");
            petRoot.transform.SetParent(denProxy, worldPositionStays: false);

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
            so.FindProperty("presenter").objectReferenceValue = presenter;
            so.FindProperty("anchoredPetRoot").objectReferenceValue = petRoot.transform;
            so.FindProperty("autoCreateStubSource").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            AttachTouchInputBridge(appGo, appRoot, camera, petRoot.transform);

            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[ProjectSetup] 已生成 01_TrackingSmoke.unity（使用模拟跟踪源）。");
        }

        /// <summary>
        /// 挂一个 <see cref="ZQXNXS.ARPet.App.TouchInputBridge"/> 并接好摄像机与命中判定锚点。
        /// 已存在时不重复添加，便于反复运行本工具。
        /// </summary>
        private static void AttachTouchInputBridge(GameObject appGo, ZQXNXS.ARPet.App.AppRoot appRoot,
            Camera camera, Transform hitRadiusOrigin)
        {
            var bridge = appGo.GetComponent<ZQXNXS.ARPet.App.TouchInputBridge>();
            if (bridge == null) bridge = appGo.AddComponent<ZQXNXS.ARPet.App.TouchInputBridge>();

            var bridgeSo = new SerializedObject(bridge);
            bridgeSo.FindProperty("appRoot").objectReferenceValue = appRoot;
            bridgeSo.FindProperty("raycastCamera").objectReferenceValue = camera;
            bridgeSo.FindProperty("hitRadiusOrigin").objectReferenceValue = hitRadiusOrigin;
            bridgeSo.ApplyModifiedPropertiesWithoutUndo();
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

            // 跟踪源与摄像机留空：它们必须由 Vuforia 的 ARCamera / ImageTarget 提供，
            // 在 SDK 尚未导入时不能凭空生成一个假的 Vuforia 场景。
            var hint = new GameObject("【待配置】Vuforia ARCamera 与两张 ImageTarget 请放在这里");
            hint.transform.SetParent(appGo.transform, worldPositionStays: false);

            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[ProjectSetup] 已生成 02_Main.unity。导入 Vuforia 后需要手工补：\n" +
                      "  1) ARCamera（Vuforia 预制体）\n" +
                      "  2) 小窝卡 ImageTarget → AppRoot.trackingSourceBehaviour 指向 VuforiaTrackingSource\n" +
                      "  3) 食物卡 ImageTarget → VuforiaTrackingSource.foodTargetObject\n" +
                      "  4) 两张卡的尺寸填**印刷图像的有效尺寸**，不是纸张外框");
        }

        /// <summary>
        /// Target Manager 数据库名与两张占位图的目标名。见 <c>Assets/StreamingAssets/Vuforia/ZQXS.xml</c>
        /// 与 AGENTS.md 2026-09-16 记录：两目标均为 0.1 m、den_card_placeholder / food_card_placeholder。
        /// 正式卡设计定稿后，把这三个常量与下面的尺寸改成实际值即可，接线逻辑不用变。
        /// </summary>
        private const string VuforiaDatabaseName = "ZQXS";
        // ImageTargetBehaviour stores the path relative to StreamingAssets, including
        // the Vuforia directory and the XML extension. A bare database name makes
        // the Android runtime look in the wrong asset path and causes
        // DATABASE_LOAD_ERROR during observer creation.
        private const string VuforiaDataSetPath = "Vuforia/" + VuforiaDatabaseName + ".xml";
        private const string DenTargetName = "den_card_placeholder";
        private const string FoodTargetName = "food_card_placeholder";
        private const float PlaceholderTargetSizeMeters = 0.1f;

        /// <summary>
        /// 把 02_Main 场景接成可用的 Vuforia AR 场景：
        /// ARCamera + 两张 ImageTarget（绑定到 ZQXS 数据库的两个占位目标）+
        /// VuforiaTrackingSource（引用两张卡）+ AppRoot（引用该跟踪源）。
        ///
        /// <b>只在真实导入 Vuforia Engine 后才会生效</b>（<c>VUFORIA_ENGINE</c> 宏点亮时）；
        /// 否则场景里连 Vuforia 的 GameObject 菜单项都不存在，此方法直接报错退出。
        ///
        /// 幂等：已经接好的场景重复运行不会产生第二套 ARCamera / ImageTarget。
        /// </summary>
        [MenuItem("桌上有龙/5. 接入 02_Main 的 Vuforia ARCamera 与 ImageTarget", false, 4)]
        public static void WireUpMainSceneVuforia()
        {
#if !VUFORIA_ENGINE
            Debug.LogError("[ProjectSetup] 未检测到 Vuforia Engine（VUFORIA_ENGINE 未定义），无法接线。" +
                           "请先按 SETUP.md 第 5 节导入 Vuforia。");
            return;
#else
            var scenePath = SceneFolder + "/02_Main.unity";
            if (!File.Exists(scenePath))
            {
                Debug.LogWarning($"[ProjectSetup] 找不到 {scenePath}，请先运行“1. 生成默认配置与场景”。");
                return;
            }

            var openScenePath = EditorSceneManager.GetActiveScene().path;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(scenePath);

            var appRoot = Object.FindFirstObjectByType<ZQXNXS.ARPet.App.AppRoot>(FindObjectsInactive.Include);
            if (appRoot == null)
            {
                Debug.LogWarning("[ProjectSetup] 02_Main 里没有找到 AppRoot，跳过接线。");
                return;
            }

            EnsureAtLeastTwoSimultaneousImageTargets();

            // Unity 的 “GameObject/...” 菜单项默认把新对象创建成当前选中物体的子物体。
            // 每次调用前清空选中，否则 ARCamera → DenCardTarget → FoodCardTarget 会
            // 逐级嵌套成父子关系；两张卡是各自独立的物理卡片，绝不应该互为父子——
            // 2026-09-17 实测嵌套还会连带触发 Vuforia 把子级 ImageTarget 的宽高
            // 悄悄改回默认的 0.2 m，与占位数据库的 0.1 m 不一致。
            Selection.activeGameObject = null;
            var arCamera = Object.FindFirstObjectByType<VuforiaBehaviour>(FindObjectsInactive.Include);
            if (arCamera == null)
            {
                EditorApplication.ExecuteMenuItem("GameObject/Vuforia Engine/AR Camera");
                arCamera = Object.FindFirstObjectByType<VuforiaBehaviour>(FindObjectsInactive.Include);
            }
            if (arCamera == null)
            {
                Debug.LogError("[ProjectSetup] 执行 “GameObject/Vuforia Engine/AR Camera” 后仍未找到 VuforiaBehaviour，接线中止。");
                return;
            }
            Selection.activeGameObject = null;

            var denTarget = FindOrCreateImageTarget("DenCardTarget", DenTargetName);
            Selection.activeGameObject = null;
            var foodTarget = FindOrCreateImageTarget("FoodCardTarget", FoodTargetName);
            Selection.activeGameObject = null;

            // 防御性纠正：万一场景里已经存在旧的嵌套层级（例如上一次运行遗留），
            // 显式把两张卡都摆到场景根，保证互不为父子，再重新写一次尺寸兜底纠正
            // Vuforia 在重新挂父级时可能悄悄改回默认宽高的问题（2026-09-17 实测复现）。
            denTarget.transform.SetParent(null, worldPositionStays: false);
            foodTarget.transform.SetParent(null, worldPositionStays: false);
            ApplyImageTargetProperties(denTarget, DenTargetName);
            ApplyImageTargetProperties(foodTarget, FoodTargetName);

            // ── 跟踪源：挂在 App 物体上，与 01_TrackingSmoke 的 StubTrackingSource 同级 ──
            var trackingSource = appRoot.GetComponent<VuforiaTrackingSource>();
            if (trackingSource == null) trackingSource = appRoot.gameObject.AddComponent<VuforiaTrackingSource>();

            var trackingSo = new SerializedObject(trackingSource);
            trackingSo.FindProperty("denTargetObject").objectReferenceValue = denTarget.gameObject;
            trackingSo.FindProperty("foodTargetObject").objectReferenceValue = foodTarget.gameObject;
            // 两张卡当前都是占位图，实际尺寸见 Assets/StreamingAssets/Vuforia/ZQXS.xml；
            // 正式卡设计定稿后必须同步改这两行，否则角色与真实卡片的比例会算错。
            trackingSo.FindProperty("denTargetSize").vector3Value =
                new Vector3(PlaceholderTargetSizeMeters, 0f, PlaceholderTargetSizeMeters);
            trackingSo.FindProperty("foodTargetSize").vector3Value =
                new Vector3(PlaceholderTargetSizeMeters, 0f, PlaceholderTargetSizeMeters);
            trackingSo.ApplyModifiedPropertiesWithoutUndo();

            var appSo = new SerializedObject(appRoot);
            appSo.FindProperty("trackingSourceBehaviour").objectReferenceValue = trackingSource;
            appSo.FindProperty("markerRegistryBehaviour").objectReferenceValue = trackingSource;
            appSo.ApplyModifiedPropertiesWithoutUndo();

            // 待配置提示物体已经不再需要：跟踪源与摄像机都已经真正接好。
            var hint = GameObject.Find("【待配置】Vuforia ARCamera 与两张 ImageTarget 请放在这里");
            if (hint != null) Object.DestroyImmediate(hint);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!string.IsNullOrEmpty(openScenePath) && openScenePath != scenePath && File.Exists(openScenePath))
            {
                EditorSceneManager.OpenScene(openScenePath);
            }

            Debug.Log("[ProjectSetup] 02_Main 已接好 ARCamera、两张 ImageTarget（绑定数据库 " +
                      $"“{VuforiaDatabaseName}”，目标 “{DenTargetName}” / “{FoodTargetName}”，尺寸 {PlaceholderTargetSizeMeters} m）" +
                      "与 VuforiaTrackingSource。这两个目标目前是占位图案，正式卡定稿后需要重新导入数据库、" +
                      "改这里的目标名与尺寸；真实识别效果仍待打印卡片与真机验证。");
#endif
        }

        /// <summary>
        /// 完成首轮真机测试所需的剩余场景装配：Pet01、材质、Animator、触屏输入、
        /// 跟踪/状态面板、EventSystem，以及 Android 构建场景列表。
        /// </summary>
        [MenuItem("桌上有龙/6. 准备 02_Main 真机基础测试", false, 5)]
        public static void PrepareMainSceneForPhoneTest()
        {
            if (!File.Exists(MainScenePath))
            {
                Debug.LogError($"[ProjectSetup] 找不到 {MainScenePath}，请先生成并接好 02_Main。");
                return;
            }

            EnsureMaterialsAndAnimator();

            var openScenePath = EditorSceneManager.GetActiveScene().path;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath);
            var appRoot = Object.FindFirstObjectByType<ZQXNXS.ARPet.App.AppRoot>(FindObjectsInactive.Include);
            var denTarget = GameObject.Find("DenCardTarget");
            var arCamera = Camera.main;

            if (appRoot == null || denTarget == null || arCamera == null)
            {
                Debug.LogError("[ProjectSetup] 02_Main 必须先包含 AppRoot、DenCardTarget 与 MainCamera。" +
                               "请先运行菜单 5 接入 Vuforia，再运行本步骤。");
                return;
            }

#if VUFORIA_ENGINE
            // 构建入口必须重新写入两张卡的完整运行时序列化状态。
            // 仅写数据库/目标名/尺寸不够：Vuforia 11 的 ObserverBehaviour 在
            // OnVuforiaStarted 中会跳过 mInitializedInEditor == false 的目标，
            // 导致该目标永远不会从数据库创建观察者。这里作为构建前自愈，
            // 也覆盖旧场景曾由编辑器菜单生成但未完成初始化的情况。
            var foodTarget = GameObject.Find("FoodCardTarget");
            var denImageTarget = denTarget.GetComponent<ImageTargetBehaviour>();
            var foodImageTarget = foodTarget != null
                ? foodTarget.GetComponent<ImageTargetBehaviour>()
                : null;
            if (foodImageTarget == null || denImageTarget == null)
            {
                Debug.LogError("[ProjectSetup] 02_Main 必须包含带 ImageTargetBehaviour 的 DenCardTarget 与 FoodCardTarget。" +
                               "请先运行菜单 5 接入 Vuforia，再运行本步骤。");
                return;
            }

            ApplyImageTargetProperties(denImageTarget, DenTargetName);
            ApplyImageTargetProperties(foodImageTarget, FoodTargetName);
#endif

            var petRoot = denTarget.transform.Find("PetRoot (AnchoredToDen)");
            if (petRoot == null)
            {
                petRoot = new GameObject("PetRoot (AnchoredToDen)").transform;
                petRoot.SetParent(denTarget.transform, worldPositionStays: false);
            }
            petRoot.localPosition = Vector3.zero;
            petRoot.localRotation = Quaternion.identity;
            petRoot.localScale = Vector3.one;

            var presenter = petRoot.GetComponent<PetPresenter>();
            if (presenter == null) presenter = petRoot.gameObject.AddComponent<PetPresenter>();

            var renderer = petRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            GameObject modelRoot;
            if (renderer == null)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(PetModelPath);
                if (model == null)
                {
                    Debug.LogError($"[ProjectSetup] 找不到角色模型 {PetModelPath}，无法准备真机测试。");
                    return;
                }

                modelRoot = (GameObject)PrefabUtility.InstantiatePrefab(model, petRoot);
                modelRoot.name = "Pet01";
                modelRoot.transform.localPosition = Vector3.zero;
                // Blender 导出约定：+Y 向上、+Z 向前；ImageTarget 局部坐标下不附加补偿旋转。
                modelRoot.transform.localRotation = Quaternion.identity;
                modelRoot.transform.localScale = Vector3.one;
            }
            else
            {
                modelRoot = renderer.transform.parent != null ? renderer.transform.parent.gameObject : renderer.gameObject;
            }

            AttachMaterialsAndAnimator(modelRoot, presenter);

            var appSo = new SerializedObject(appRoot);
            appSo.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<PetConfig>(ConfigPath);
            appSo.FindProperty("presenter").objectReferenceValue = presenter;
            appSo.FindProperty("anchoredPetRoot").objectReferenceValue = petRoot;
            appSo.FindProperty("autoCreateStubSource").boolValue = false;
            appSo.ApplyModifiedPropertiesWithoutUndo();

            AttachTouchInputBridge(appRoot.gameObject, appRoot, arCamera, petRoot);
            EnsureArLighting();
            EnsurePhoneTestUi(appRoot);
            EnsureMainSceneInBuildSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            if (!Application.isBatchMode && !string.IsNullOrEmpty(openScenePath) &&
                openScenePath != MainScenePath && File.Exists(openScenePath))
            {
                EditorSceneManager.OpenScene(openScenePath);
            }

            Debug.Log("[ProjectSetup] 02_Main 真机基础测试已就绪：Pet01 跟随 DenCardTarget，" +
                      "材质/Animator/触屏/UI 已接入，autoCreateStubSource 保持关闭，场景已加入构建列表。");
        }

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
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.offsetMin = new Vector2(24f, -520f);
            panelRect.offsetMax = new Vector2(-24f, -24f);

            var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
            if (panelImage == null) panelImage = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
            panelImage.color = new Color(0.035f, 0.055f, 0.07f, 0.82f);
            panelImage.raycastTarget = true;

            var tracking = EnsureText(panel, "TrackingText", 64, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRect(tracking.rectTransform, new Vector2(0f, 0.68f), Vector2.one,
                new Vector2(28f, 8f), new Vector2(-28f, -8f));

            var instruction = EnsureText(panel, "InstructionText", 32, TextAnchor.MiddleCenter, FontStyle.Normal);
            instruction.color = new Color(1f, 1f, 1f, 0.92f);
            SetRect(instruction.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 0.68f),
                new Vector2(28f, 4f), new Vector2(-28f, -4f));

            var behavior = EnsureText(panel, "BehaviorText", 30, TextAnchor.MiddleLeft, FontStyle.Normal);
            SetRect(behavior.rectTransform, Vector2.zero, new Vector2(0.25f, 0.38f),
                new Vector2(28f, 8f), new Vector2(-4f, -8f));
            var hunger = EnsureText(panel, "HungerText", 30, TextAnchor.MiddleCenter, FontStyle.Normal);
            SetRect(hunger.rectTransform, new Vector2(0.25f, 0f), new Vector2(0.5f, 0.38f),
                new Vector2(4f, 8f), new Vector2(-4f, -8f));
            var happiness = EnsureText(panel, "HappinessText", 30, TextAnchor.MiddleCenter, FontStyle.Normal);
            SetRect(happiness.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.75f, 0.38f),
                new Vector2(4f, 8f), new Vector2(-4f, -8f));
            var energy = EnsureText(panel, "EnergyText", 30, TextAnchor.MiddleRight, FontStyle.Normal);
            SetRect(energy.rectTransform, new Vector2(0.75f, 0f), new Vector2(1f, 0.38f),
                new Vector2(4f, 8f), new Vector2(-28f, -8f));

            var statusPanel = panel.GetComponent<PetStatusPanel>();
            if (statusPanel == null) statusPanel = panel.gameObject.AddComponent<PetStatusPanel>();
            var statusSo = new SerializedObject(statusPanel);
            statusSo.FindProperty("stateSourceBehaviour").objectReferenceValue = appRoot;
            statusSo.FindProperty("trackingText").objectReferenceValue = tracking;
            statusSo.FindProperty("instructionText").objectReferenceValue = instruction;
            statusSo.FindProperty("behaviorText").objectReferenceValue = behavior;
            statusSo.FindProperty("hungerText").objectReferenceValue = hunger;
            statusSo.FindProperty("happinessText").objectReferenceValue = happiness;
            statusSo.FindProperty("energyText").objectReferenceValue = energy;
            statusSo.ApplyModifiedPropertiesWithoutUndo();

            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
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
            PrepareMainSceneForPhoneTest();

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
        private static ImageTargetBehaviour FindOrCreateImageTarget(string goName, string trackableName)
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
        private static void ApplyImageTargetProperties(ImageTargetBehaviour behaviour, string trackableName)
        {
            var so = new SerializedObject(behaviour);
            so.FindProperty("mDataSetPath").stringValue = VuforiaDataSetPath;
            so.FindProperty("mTrackableName").stringValue = trackableName;
            // Vuforia 11 uses this editor-persisted flag to decide whether
            // ObserverBehaviour.OnVuforiaStarted may create the serialized target.
            // A false value is not a harmless editor hint: it makes the target
            // permanently skip CreateFromSerializedTarget() at runtime.
            so.FindProperty("mInitializedInEditor").boolValue = true;
            so.FindProperty("mWidth").floatValue = PlaceholderTargetSizeMeters;
            so.FindProperty("mHeight").floatValue = PlaceholderTargetSizeMeters;
            // Clear the migration marker left by older/partially initialized
            // ImageTarget editor data. Keep both targets on the same current path.
            so.FindProperty("mTrackingOptimizationNeedsUpgrade").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 项目需要同时跟踪小窝卡与食物卡两个目标，但 VuforiaConfiguration 默认
        /// <c>maxSimultaneousImageTargets = 1</c>（2026-09-16 生成时的默认值）。
        /// 只在低于 2 时才改，避免覆盖用户后续手工调高的值。
        /// </summary>
        private static void EnsureAtLeastTwoSimultaneousImageTargets()
        {
            var config = Resources.Load<VuforiaConfiguration>("VuforiaConfiguration");
            if (config == null)
            {
                Debug.LogWarning("[ProjectSetup] 找不到 Resources/VuforiaConfiguration，跳过 maxSimultaneousImageTargets 检查。");
                return;
            }

            if (config.Vuforia.MaxSimultaneousImageTargets >= 2) return;

            config.Vuforia.MaxSimultaneousImageTargets = 2;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] VuforiaConfiguration.MaxSimultaneousImageTargets 已从 1 调整为 2" +
                      "（小窝卡与食物卡需要同时跟踪）。");
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
