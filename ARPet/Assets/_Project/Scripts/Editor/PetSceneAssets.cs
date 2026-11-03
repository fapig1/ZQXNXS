using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.EditorTools
{
    /// <summary>
    /// 四色宠物场景装配共用的命名规则与资产查找。
    ///
    /// <c>02_Main</c>（多宠主场景）与 <c>04_FourCardTracking</c>（识别实测场景）都按同一套规则
    /// 生成"目标卡 → 宠物根 → Pet01 实例"的层级，目标物体名、宠物根名与材质变体后缀
    /// 只在这里定义一份，避免两个场景各写一套后漂移。
    /// </summary>
    internal static class PetSceneAssets
    {
        public const string ArtRoot = "Assets/_Project/Art/Characters/Pet01/";
        public const string ModelPath = ArtRoot + "Models/Pet01.fbx";

        /// <summary>四张角色卡同屏所需的最大同时跟踪数。</summary>
        public const int RequiredSimultaneousTargets = 4;

        /// <summary>角色显示比例。模型高 0.2m，66×94mm 小卡上偏大；早期识别实测先保持 1（02_Main 与 04 一致）。</summary>
        public const float PetScale = 1f;

        /// <summary>
        /// 材质槽在 <c>SkinnedMeshRenderer.sharedMaterials</c> 里的实际顺序。
        /// 2026-09-16 用编辑器菜单核对过 Pet01.fbx 的子网格顺序；四色变体沿用同一顺序。
        /// </summary>
        public static readonly string[] MaterialSlots = { "Skin", "Oral", "Eyes" };

        /// <summary>ImageTarget 物体名，由稳定 TargetId 派生（如 Target_card_classic）。</summary>
        public static string TargetObjectName(string targetId) => "Target_" + targetId;

        /// <summary>一张卡下的宠物根名（如 PetRoot_pet_classic），PetPresenter / PetHitTarget 挂在这里。</summary>
        public static string PetRootName(string petId) => "PetRoot_" + petId;

        /// <summary>Pet01 实例名与材质变体后缀：PetVariantId → classic / calm / rage / divine。</summary>
        public static string VariantKey(PetVariantId variant) => variant.ToString().ToLowerInvariant();

        public static AnimatorController LoadController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ProjectSetup.AnimatorControllerPath);
            if (controller == null)
                throw new FileNotFoundException("缺少 Animator Controller，请先运行“动画/接入四色专属动作”",
                    ProjectSetup.AnimatorControllerPath);
            return controller;
        }

        public static GameObject LoadModel()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new FileNotFoundException("缺少 Pet01 模型", ModelPath);
            return model;
        }

        public static Material LoadMaterial(PetVariantId variant, string slot)
        {
            var name = "Pet01_" + slot + "_" + VariantKey(variant);
            var path = ArtRoot + "Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                throw new FileNotFoundException("缺少四色材质，请先运行“动画/接入四色专属动作”", name);
            return material;
        }

        /// <summary>按渲染器槽位顺序返回一只宠物应有的三个材质。</summary>
        public static Material[] LoadMaterials(PetVariantId variant) =>
            MaterialSlots.Select(slot => LoadMaterial(variant, slot)).ToArray();
    }
}
