using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZQXNXS.ARPet.EditorTools
{
    /// <summary>
    /// 诊断工具，不进入构建流程、不写任何资产。
    ///
    /// 用工程里真实导入的 Pet01.fbx、classic 材质、Directional Light 和 RenderTexture，
    /// 从多个方向近距离渲染左右肩，判断真机上右肩（UpperArm.R）肩球与躯干交界
    /// “最高点偏前”的深色楔形缝是否来自 Unity 端渲染（导入、法线、光照、MSAA）。
    /// 输出写到 ArtSource/.../Logs/ShoulderCheck_20261026/unity/，同时记录 manifest.txt。
    /// 需要图形设备，命令行调用不要加 -nographics。
    /// </summary>
    public static class ShoulderCloseupCapture
    {
        private const string Art = "Assets/_Project/Art/Characters/Pet01/";
        private const string ModelPath = Art + "Models/Pet01.fbx";
        private const string IdlePath = Art + "Models/Pet01_Idle.fbx";
        private const string OutputRelativePath = "../../ArtSource/Characters/Pet01/Logs/ShoulderCheck_20261026/unity/";

        private const string PoseRest = "rest";
        private const string PoseIdle0 = "idle0";
        private const string DistNear = "near";
        private const string DistFar = "far";
        private const string LightA = "lightA";
        private const string LightB = "lightB";

        /// <summary>两侧肩部共用的 RenderTexture 边长。</summary>
        private const int ShotSize = 1600;
        private const float NearDistance = 0.09f;
        private const float FarDistance = 0.15f;
        private const float TargetLift = 0.004f;

        /// <summary>forward 与 right 的点积超过此值就认为不正交，改用叉积重建 forward。</summary>
        private const float MaxForwardRightDot = 0.3f;

        /// <summary>场景里的 AmbientSkyColor，天空盒不可用时退回 Trilight 使用。</summary>
        private static readonly Color FallbackAmbientColor = new Color(0.212f, 0.227f, 0.259f);

        private static readonly string[] PoseNames = { PoseRest, PoseIdle0 };
        private static readonly string[] SideNames = { "R", "L" };
        private static readonly string[] DistanceNames = { DistNear, DistFar };
        private static readonly string[] LightNames = { LightA, LightB };
        private static readonly int[] AntiAliasingSteps = { 1, 8 };
        private static readonly string[] ViewNames =
            { "front", "front_outer", "outer", "top_front", "top_front_outer", "back_outer", "device_like" };

        private static int s_shotIndex;
        private static int s_shotTotal;

        [MenuItem("桌上有龙/诊断/肩部近景截图")]
        public static void Capture()
        {
            // 当前场景不保存，但用户可能还有未保存改动，先按标准流程询问。
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var directory = OutputDirectory();
            Directory.CreateDirectory(directory);

            var previousQuality = QualitySettings.GetQualityLevel();
            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbientLight = RenderSettings.ambientLight;
            var previousAmbientSky = RenderSettings.ambientSkyColor;
            var previousAmbientEquator = RenderSettings.ambientEquatorColor;
            var previousAmbientGround = RenderSettings.ambientGroundColor;
            var previousSkybox = RenderSettings.skybox;

            var manifest = new StringBuilder();
            GameObject cameraObject = null;
            GameObject lightObject = null;
            var manifestWritten = false;
            s_shotIndex = 0;
            s_shotTotal = ExpectedShotCount();

            try
            {
                QualitySettings.SetQualityLevel(AndroidQualityLevel(), false);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ApplyAmbient();

                lightObject = new GameObject("ShoulderCloseupLight");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.color = new Color(1f, 0.96f, 0.9f);
                light.shadows = LightShadows.None;

                cameraObject = new GameObject("ShoulderCloseupCamera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = false;
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 20f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.45f, 0.45f, 0.45f);
                camera.aspect = 1f;
                camera.allowHDR = false;

                AppendHeader(manifest);

                // Idle 片段先加载，缺资源时在渲染前就失败，不留半套图。
                var idleClip = LoadIdleClip();

                // 先渲染 rest：此时没有任何动画采样，模型停在导入的静止姿态。
                var restPet = InstantiatePet();
                int restShots;
                try
                {
                    restShots = CapturePose(PoseRest, restPet, camera, light, directory, manifest);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(restPet);
                }

                // 再新建一个实例并采样第 0 帧，避免 rest 组受采样结果影响。
                var idlePet = InstantiatePet();
                int idleShots;
                try
                {
                    idleClip.SampleAnimation(idlePet, 0f);
                    idleShots = CapturePose(PoseIdle0, idlePet, camera, light, directory, manifest);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(idlePet);
                }

                File.WriteAllText(Path.Combine(directory, "manifest.txt"), manifest.ToString(), new UTF8Encoding(false));
                manifestWritten = true;
                Debug.Log($"[ShoulderCloseupCapture] 已输出 {restShots + idleShots} 张肩部近景（rest {restShots} / idle0 {idleShots}）：{directory}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!manifestWritten && manifest.Length > 0) TryWriteManifest(directory, manifest);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.ambientSkyColor = previousAmbientSky;
                RenderSettings.ambientEquatorColor = previousAmbientEquator;
                RenderSettings.ambientGroundColor = previousAmbientGround;
                RenderSettings.skybox = previousSkybox;
                QualitySettings.SetQualityLevel(previousQuality, false);
            }
        }

        /// <summary>命令行入口：-executeMethod ZQXNXS.ARPet.EditorTools.ShoulderCloseupCapture.CaptureBatch</summary>
        public static void CaptureBatch()
        {
            try
            {
                Capture();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static string OutputDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, OutputRelativePath));
        }

        /// <summary>Android 默认等级是第 3 档 "Medium"，按名字找，找不到退回索引 2。</summary>
        private static int AndroidQualityLevel()
        {
            var names = QualitySettings.names;
            for (var i = 0; i < names.Length; i++)
            {
                if (names[i] == "Medium") return i;
            }
            Debug.LogWarning("[ShoulderCloseupCapture] QualitySettings 里没有 Medium，退回等级 2。");
            return 2;
        }

        private static void ApplyAmbient()
        {
            var skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (skybox == null)
            {
                ApplyFallbackAmbient("内置 Default-Skybox.mat 不可用");
                return;
            }
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            try
            {
                DynamicGI.UpdateEnvironment();
            }
            catch (Exception exception)
            {
                ApplyFallbackAmbient("DynamicGI.UpdateEnvironment 失败：" + exception.Message);
            }
        }

        private static void ApplyFallbackAmbient(string reason)
        {
            Debug.LogWarning($"[ShoulderCloseupCapture] {reason}，退回到 Trilight 环境光。");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = FallbackAmbientColor;
            RenderSettings.ambientEquatorColor = FallbackAmbientColor;
            RenderSettings.ambientGroundColor = FallbackAmbientColor;
        }

        private static void TryWriteManifest(string directory, StringBuilder manifest)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "manifest.txt"), manifest.ToString(), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ShoulderCloseupCapture] 中途失败，manifest 写入也失败：" + exception.Message);
            }
        }

        private static GameObject InstantiatePet()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("缺少 " + ModelPath + "，无法做肩部近景截图。");
            var pet = (GameObject)PrefabUtility.InstantiatePrefab(model);
            pet.transform.position = Vector3.zero;
            pet.transform.rotation = Quaternion.identity;
            pet.transform.localScale = Vector3.one;
            var renderer = pet.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null) throw new InvalidOperationException("实例化的 Pet01 里找不到 SkinnedMeshRenderer。");
            // 与 FourCardTrackingSetup 一致：槽位顺序 Skin、Oral、Eyes。
            renderer.sharedMaterials = new[] { "Skin", "Oral", "Eyes" }
                .Select(slot => LoadMaterial("Pet01_" + slot + "_classic")).ToArray();
            // 近景相机离骨骼只有 0.09m，必须关掉视锥剔除，否则肩部可能整块消失。
            renderer.updateWhenOffscreen = true;
            return pet;
        }

        private static Material LoadMaterial(string name)
        {
            var path = Art + "Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) throw new InvalidOperationException("缺少材质 " + path + "。");
            return material;
        }

        private static AnimationClip LoadIdleClip()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(IdlePath);
            if (assets == null || assets.Length == 0)
                throw new InvalidOperationException("加载不到 " + IdlePath + " 里的任何资源。");
            var clip = assets.OfType<AnimationClip>()
                .FirstOrDefault(c => c != null && !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null)
                throw new InvalidOperationException(IdlePath + " 里找不到可用的 AnimationClip（只看到 __preview__ 或空集合）。");
            return clip;
        }

        /// <summary>渲染一个姿态的全部视角，返回实际写出的图片数。</summary>
        private static int CapturePose(string pose, GameObject pet, Camera camera, Light light, string directory, StringBuilder manifest)
        {
            var frame = ResolveFrame(pet);
            AppendFrame(manifest, pose, frame);
            var shots = 0;

            foreach (var side in SideNames)
            {
                var sideDirection = side == "R" ? frame.Right : -frame.Right;
                var bonePosition = side == "R" ? frame.UpperArmR : frame.UpperArmL;
                var target = bonePosition + frame.Up * TargetLift;
                foreach (var view in BuildViews(frame, sideDirection))
                {
                    var direction = view.Direction.normalized;
                    foreach (var distanceName in DistanceNames)
                    {
                        var distance = DistanceOf(distanceName);
                        foreach (var lightName in LightNames)
                        {
                            foreach (var antiAliasing in AntiAliasingSteps)
                            {
                                if (!IsWantedShot(pose, view.Name, distanceName, lightName, antiAliasing)) continue;

                                light.transform.rotation = LightRotation(lightName);
                                var cameraPosition = target + direction * distance;
                                var fileName = $"{pose}_{side}_{view.Name}_{distanceName}_{lightName}_aa{antiAliasing}.png";
                                EditorUtility.DisplayProgressBar("肩部近景截图",
                                    $"{s_shotIndex + 1}/{s_shotTotal} {fileName}",
                                    s_shotTotal > 0 ? (float)s_shotIndex / s_shotTotal : 0f);
                                RenderShot(camera, cameraPosition, target, antiAliasing, Path.Combine(directory, fileName));
                                AppendShot(manifest, fileName, cameraPosition, target, direction, distance, lightName, antiAliasing);
                                s_shotIndex++;
                                shots++;
                            }
                        }
                    }
                }
            }

            return shots;
        }

        /// <summary>
        /// 出图配额：rest 只取 lightA + near，aa 1/8 全给；
        /// idle0 全视角 × 两档距离 × 两盏灯，但只出 aa8，另外补 device_like 与 front_outer 的 near/lightA/aa1。
        /// </summary>
        private static bool IsWantedShot(string pose, string view, string distanceName, string lightName, int antiAliasing)
        {
            if (pose == PoseRest) return lightName == LightA && distanceName == DistNear;
            if (lightName == LightA && distanceName == DistNear && antiAliasing == 1)
                return view == "device_like" || view == "front_outer";
            return antiAliasing == 8;
        }

        private static int ExpectedShotCount()
        {
            var count = 0;
            foreach (var pose in PoseNames)
            foreach (var view in ViewNames)
            foreach (var distanceName in DistanceNames)
            foreach (var lightName in LightNames)
            foreach (var antiAliasing in AntiAliasingSteps)
            {
                if (IsWantedShot(pose, view, distanceName, lightName, antiAliasing)) count += SideNames.Length;
            }
            return count;
        }

        private static float DistanceOf(string distanceName)
        {
            return distanceName == DistNear ? NearDistance : FarDistance;
        }

        private static Quaternion LightRotation(string lightName)
        {
            return lightName == LightA ? Quaternion.Euler(50f, -30f, 0f) : Quaternion.Euler(50f, 150f, 0f);
        }

        /// <summary>
        /// 不假设轴向：青蛙自己的右侧由两根 UpperArm 决定，前方由“肩中点 → 手中点”决定
        /// （手在肚子前面，所以先去掉 up 分量）。
        /// </summary>
        private static ShoulderFrame ResolveFrame(GameObject pet)
        {
            var upperArmR = RequireBone(pet.transform, "UpperArm.R");
            var upperArmL = RequireBone(pet.transform, "UpperArm.L");
            var handR = RequireBone(pet.transform, "Hand.R");
            var handL = RequireBone(pet.transform, "Hand.L");

            var up = Vector3.up;
            var right = upperArmR.position - upperArmL.position;
            if (right.sqrMagnitude < 1e-8f)
                throw new InvalidOperationException("UpperArm.R 与 UpperArm.L 位置几乎重合，无法推算青蛙自己的右侧方向。");
            right = right.normalized;

            var forward = (handR.position + handL.position) * 0.5f - (upperArmR.position + upperArmL.position) * 0.5f;
            forward -= Vector3.Project(forward, up);
            if (forward.sqrMagnitude < 1e-8f)
                throw new InvalidOperationException("肩中点到手中点的向量在水平面上几乎为零，无法推算前方方向。");
            var rawForward = forward.normalized;
            forward = rawForward;

            if (Mathf.Abs(Vector3.Dot(forward, right)) > MaxForwardRightDot)
            {
                // 两个叉积互为反方向，取与原 forward 点积为非负的那个叉积，保证朝向不翻转。
                var fromRight = Vector3.Cross(right, up);
                var fromUp = Vector3.Cross(up, right);
                forward = (Vector3.Dot(fromRight, rawForward) >= Vector3.Dot(fromUp, rawForward) ? fromRight : fromUp).normalized;
            }

            return new ShoulderFrame
            {
                Right = right,
                Up = up,
                Forward = forward,
                UpperArmR = upperArmR.position,
                UpperArmL = upperArmL.position,
            };
        }

        private static Transform RequireBone(Transform root, string boneName)
        {
            var bone = FindBone(root, boneName);
            if (bone == null)
                throw new InvalidOperationException($"实例化的 Pet01 里找不到骨骼 {boneName}（臂分离修订版应有 UpperArm.L/R、Hand.L/R）。");
            return bone;
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            if (root.name == boneName) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindBone(root.GetChild(i), boneName);
                if (found != null) return found;
            }
            return null;
        }

        private static ViewSpec[] BuildViews(ShoulderFrame frame, Vector3 sideDirection)
        {
            var up = frame.Up;
            var forward = frame.Forward;
            return new[]
            {
                new ViewSpec { Name = "front", Direction = forward },
                new ViewSpec { Name = "front_outer", Direction = forward + sideDirection },
                new ViewSpec { Name = "outer", Direction = sideDirection },
                new ViewSpec { Name = "top_front", Direction = forward + up * 1.2f },
                new ViewSpec { Name = "top_front_outer", Direction = forward + sideDirection + up * 1.2f },
                new ViewSpec { Name = "back_outer", Direction = -forward + sideDirection },
                // 模拟真机从前方略偏外侧、略偏上方拍。
                new ViewSpec { Name = "device_like", Direction = forward + sideDirection * 0.35f + up * 0.3f },
            };
        }

        private static void RenderShot(Camera camera, Vector3 cameraPosition, Vector3 target, int antiAliasing, string filePath)
        {
            camera.transform.position = cameraPosition;
            camera.transform.LookAt(target, Vector3.up);
            var renderTexture = new RenderTexture(ShotSize, ShotSize, 24) { antiAliasing = antiAliasing };
            var previousActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture = new Texture2D(ShotSize, ShotSize, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, ShotSize, ShotSize), 0, 0);
                texture.Apply();
                File.WriteAllBytes(filePath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static void AppendHeader(StringBuilder manifest)
        {
            manifest.AppendLine("Pet01 肩部近景截图（Unity 端诊断，不写资产）");
            manifest.AppendLine("date=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            manifest.AppendLine("unity=" + Application.unityVersion);
            manifest.AppendLine("model=" + ModelPath);
            manifest.AppendLine("quality=" + QualitySettings.GetQualityLevel() + " " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            manifest.AppendLine("ambientMode=" + RenderSettings.ambientMode);
            manifest.AppendLine("camera=fov 60, near 0.05, far 20, aspect 1, background 0.45");
            manifest.AppendLine("shot=" + ShotSize + "x" + ShotSize + " depth24, dist near " + Number(NearDistance) + " far " + Number(FarDistance));
            manifest.AppendLine("lightA=" + FormatRotation(LightRotation(LightA)));
            manifest.AppendLine("lightB=" + FormatRotation(LightRotation(LightB)));
            manifest.AppendLine();
        }

        private static void AppendFrame(StringBuilder manifest, string pose, ShoulderFrame frame)
        {
            manifest.AppendLine("[frame] pose=" + pose);
            manifest.AppendLine("upperArm.R=" + Format(frame.UpperArmR));
            manifest.AppendLine("upperArm.L=" + Format(frame.UpperArmL));
            manifest.AppendLine("right=" + Format(frame.Right));
            manifest.AppendLine("up=" + Format(frame.Up));
            manifest.AppendLine("forward=" + Format(frame.Forward));
            manifest.AppendLine();
        }

        private static void AppendShot(StringBuilder manifest, string fileName, Vector3 cameraPosition, Vector3 target,
            Vector3 direction, float distance, string lightName, int antiAliasing)
        {
            manifest.AppendLine("[image] " + fileName);
            manifest.AppendLine("camera=" + Format(cameraPosition));
            manifest.AppendLine("target=" + Format(target));
            manifest.AppendLine("direction=" + Format(direction));
            manifest.AppendLine("distance=" + Number(distance));
            manifest.AppendLine("light=" + lightName + " " + FormatRotation(LightRotation(lightName)));
            manifest.AppendLine("aa=" + antiAliasing.ToString(CultureInfo.InvariantCulture));
            manifest.AppendLine();
        }

        private static string Format(Vector3 value)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})",
                value.x.ToString("F6", CultureInfo.InvariantCulture),
                value.y.ToString("F6", CultureInfo.InvariantCulture),
                value.z.ToString("F6", CultureInfo.InvariantCulture));
        }

        private static string FormatRotation(Quaternion rotation)
        {
            return "Euler" + Format(rotation.eulerAngles);
        }

        private static string Number(float value)
        {
            return value.ToString("F6", CultureInfo.InvariantCulture);
        }

        private struct ShoulderFrame
        {
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Forward;
            public Vector3 UpperArmR;
            public Vector3 UpperArmL;
        }

        private struct ViewSpec
        {
            public string Name;
            public Vector3 Direction;
        }
    }
}
