using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.EditorTools
{
    public static class ExclusiveAnimationSetup
    {
        private const string Art = "Assets/_Project/Art/Characters/Pet01/";
        private const string ControllerPath = "Assets/_Project/Animations/Pet01Controller.controller";
        private const string CatalogPath = "Assets/_Project/Configs/PetExclusiveAnimations.asset";
        public static readonly string[] StateNames = { "classic_proud", "calm_zen", "rage_roar", "divine_awaken" };
        public const string SharedLaughState = "social_laugh";
        public const string TouchReactState = "TouchReact";

        [MenuItem("桌上有龙/动画/接入四色专属动作")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            var activeQuality = QualitySettings.GetQualityLevel();
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.skinWeights = SkinWeights.Unlimited;
            }
            QualitySettings.SetQualityLevel(activeQuality, false);
            foreach (var settings in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset"))
                EditorUtility.SetDirty(settings);
            // Revised mesh needs the same skinning influences used in Blender validation.
            foreach (var path in Directory.GetFiles(Art + "Models", "*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.skinWeights = ModelImporterSkinWeights.Custom;
                importer.maxBonesPerVertex = 8;
                importer.minBoneWeight = 0.00001f;
                importer.optimizeGameObjects = false;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new InvalidOperationException("Pet01Controller missing");
            // Re-exported old takes must retain their state names and loop flags.
            foreach (var name in new[] { "Idle", "Eat" })
            {
                var oldPath = Art + "Models/Pet01_" + name + ".fbx";
                var oldImporter = (ModelImporter)AssetImporter.GetAtPath(oldPath);
                var oldClips = oldImporter.defaultClipAnimations;
                oldClips[0].name = name;
                oldClips[0].loopTime = name == "Idle";
                oldImporter.clipAnimations = oldClips;
                oldImporter.SaveAndReimport();
                var oldState = controller.layers[0].stateMachine.states.Single(s => s.state.name == name).state;
                oldState.motion = AssetDatabase.LoadAllAssetsAtPath(oldPath).OfType<AnimationClip>().Single(c => c.name == name);
            }
            var catalog = AssetDatabase.LoadAssetAtPath<PetExclusiveAnimations>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PetExclusiveAnimations>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.Entries = new PetExclusiveAnimations.Entry[4];
            for (var i = 0; i < StateNames.Length; i++)
            {
                var stateName = StateNames[i];
                var path = Art + "Models/Pet01_" + stateName + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var clips = importer.defaultClipAnimations;
                if (clips.Length != 1) throw new InvalidOperationException(path + ": expected one take");
                clips[0].name = stateName;
                clips[0].loopTime = false;
                clips[0].loopPose = false;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Single(c => c.name == stateName);
                var machine = controller.layers[0].stateMachine;
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName)
                    ?? machine.AddState(stateName);
                state.motion = clip;
                catalog.Entries[i] = new PetExclusiveAnimations.Entry
                    { Variant = (PetVariantId)i, StateName = stateName, Clip = clip };
                var variant = ((PetVariantId)i).ToString().ToLowerInvariant();
                var texturePath = Art + "Textures/Pet01_BaseColor_" + variant + ".png";
                if (!File.Exists(texturePath))
                {
                    var sourcePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Docs/AssetReview/20260919_卡面基准形象/work/tex_variant/Pet01_BaseColor_" + variant + ".png"));
                    if (!File.Exists(sourcePath)) throw new FileNotFoundException("四色贴图缺失", sourcePath);
                    File.Copy(sourcePath, Path.GetFullPath(Path.Combine(Application.dataPath, "_Project/Art/Characters/Pet01/Textures/Pet01_BaseColor_" + variant + ".png")));
                    AssetDatabase.ImportAsset(texturePath);
                }
                foreach (var slot in new[] { "Skin", "Oral", "Eyes" })
                {
                    var materialPath = Art + "Materials/Pet01_" + slot + "_" + variant + ".mat";
                    if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null) continue;
                    var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(Art + "Materials/Pet01_" + slot + ".mat"));
                    material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
            }
            // Shared group laugh: one state for all four variants; the Pet coordinator decides when to play it.
            {
                var path = Art + "Models/Pet01_" + SharedLaughState + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer == null) throw new FileNotFoundException("共享大笑FBX缺失", path);
                var clips = importer.defaultClipAnimations;
                if (clips.Length != 1) throw new InvalidOperationException(path + ": expected one take");
                clips[0].name = SharedLaughState;
                clips[0].loopTime = false;
                clips[0].loopPose = false;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                var machine = controller.layers[0].stateMachine;
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == SharedLaughState)
                    ?? machine.AddState(SharedLaughState);
                state.motion = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Single(c => c.name == SharedLaughState);
            }
            // 触屏反应：单击 / 双击共用的 0.7 s 单次反馈，由 PetConfig 的 Petted 行为驱动。
            // 与 social_laugh 同样处理：取 FBX 里唯一 take、命名、关循环、幂等增删状态。
            {
                var path = Art + "Models/Pet01_" + TouchReactState + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer == null) throw new FileNotFoundException("触屏反应FBX缺失", path);
                var clips = importer.defaultClipAnimations;
                if (clips.Length != 1) throw new InvalidOperationException(path + ": expected one take");
                clips[0].name = TouchReactState;
                clips[0].loopTime = false;
                clips[0].loopPose = false;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                var machine = controller.layers[0].stateMachine;
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == TouchReactState)
                    ?? machine.AddState(TouchReactState);
                state.motion = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Single(c => c.name == TouchReactState);
            }
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[ExclusiveAnimationSetup] Four exclusive clips, shared social_laugh and TouchReact installed.");
        }

        [MenuItem("桌上有龙/动画/创建四色专属预览场景")]
        public static void CreatePreview()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Install();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, .24f, .85f);
            camera.transform.LookAt(new Vector3(0, .10f, 0));
            camera.orthographic = true; camera.orthographicSize = .25f;
            camera.nearClipPlane = .01f;
            var light = new GameObject("KeyLight").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45, 155, 0);
            RenderSettings.ambientLight = new Color(.55f, .55f, .55f);
            for (var i = 0; i < 4; i++)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Models/Pet01.fbx"));
                model.name = ((PetVariantId)i).ToString();
                model.transform.position = new Vector3((i - 1.5f) * .16f, 0, 0);
                var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
                renderer.quality = SkinQuality.Auto;
                renderer.sharedMaterials = new[] { "Skin", "Oral", "Eyes" }.Select(n =>
                    AssetDatabase.LoadAssetAtPath<Material>(Art + "Materials/Pet01_" + n + "_" + ((PetVariantId)i).ToString().ToLowerInvariant() + ".mat")).ToArray();
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                animator.applyRootMotion = false;
                var preview = model.AddComponent<ExclusiveAnimationPreview>();
                preview.Animator = animator;
                preview.Catalog = AssetDatabase.LoadAssetAtPath<PetExclusiveAnimations>(CatalogPath);
                preview.Variant = (PetVariantId)i;
            }
            EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/03_ExclusiveAnimationPreview.unity");
        }
    }
}
