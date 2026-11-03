using System.IO;
using UnityEditor;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.EditorTools
{
    /// <summary>
    /// P5-B：把 Blender 生成的二维嘴眼贴片接到每只宠物上（幂等）。
    ///
    /// <list type="bullet">
    /// <item><c>Pet01_FaceOverlay.fbx</c> 只取两片网格（眼区、嘴区），不导入动画与材质；
    ///       在宠物实例里作为 <c>Head</c> 骨骼的子物体放置，随头部动作一起动。贴片顶点本来就是
    ///       Blender 世界坐标，与 Pet01.fbx 同一套导出参数，所以挂到 Pet01 实例根下后零变换即对位，
    ///       再以 worldPositionStays 挂到 Head 下。</item>
    /// <item>两张图集共用一个 <c>ZQXNXS/FaceDecal</c> 材质各一份；选格由 PetPresenter 的
    ///       per-Renderer MaterialPropertyBlock 完成，四只宠物不会串表情。</item>
    /// <item>遮盖材质：每色 Skin 材质副本，<c>_MainTex_ST=(0,0,u,v)</c> 把整片 UV 钉在眼周/嘴周皮肤纹素上，
    ///       颜色与光照都与皮肤一致；贴片显示时由 PetPresenter 换进 Eyes / Oral 槽。</item>
    /// </list>
    /// </summary>
    internal static class FaceOverlaySetup
    {
        public const string OverlayModelPath = PetSceneAssets.ArtRoot + "Models/Pet01_FaceOverlay.fbx";
        public const string LayoutPath = PetSceneAssets.ArtRoot + "face_overlay_layout.json";
        public const string ShaderName = "ZQXNXS/FaceDecal";
        public const string EyesAtlasPath = PetSceneAssets.ArtRoot + "Textures/Pet01_FaceEyes.png";
        public const string MouthAtlasPath = PetSceneAssets.ArtRoot + "Textures/Pet01_FaceMouth.png";
        public const string EyesDecalMaterialPath = PetSceneAssets.ArtRoot + "Materials/Pet01_FaceEyes_Decal.mat";
        public const string MouthDecalMaterialPath = PetSceneAssets.ArtRoot + "Materials/Pet01_FaceMouth_Decal.mat";

        public static string CoverMaterialPath(PetVariantId variant, string part) =>
            PetSceneAssets.ArtRoot + "Materials/Pet01_" + part + "Cover_" + PetSceneAssets.VariantKey(variant) + ".mat";

        /// <summary>资产就绪检查：缺 FBX / 布局 / 着色器时整段跳过，不让场景装配失败。</summary>
        public static bool AssetsPresent(out string missing)
        {
            if (!File.Exists(OverlayModelPath)) { missing = OverlayModelPath; return false; }
            if (!File.Exists(LayoutPath)) { missing = LayoutPath; return false; }
            if (!File.Exists(EyesAtlasPath)) { missing = EyesAtlasPath; return false; }
            if (!File.Exists(MouthAtlasPath)) { missing = MouthAtlasPath; return false; }
            missing = null;
            return true;
        }

        /// <summary>导入设置：FBX 不要动画/材质，图集保留 alpha、双线性、夹边。</summary>
        public static void ConfigureImporters()
        {
            if (AssetImporter.GetAtPath(OverlayModelPath) is ModelImporter model)
            {
                var changed = model.importAnimation || model.materialImportMode != ModelImporterMaterialImportMode.None;
                model.importAnimation = false;
                model.materialImportMode = ModelImporterMaterialImportMode.None;
                if (changed) model.SaveAndReimport();
            }

            foreach (var path in new[] { EyesAtlasPath, MouthAtlasPath })
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter texture)) continue;
                var changed = !texture.alphaIsTransparency || texture.wrapMode != TextureWrapMode.Clamp ||
                              texture.filterMode != FilterMode.Bilinear || texture.mipmapEnabled;
                texture.alphaIsTransparency = true;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                // 关掉 mip：图集格子紧挨着，低级 mip 会把相邻格子混进来。
                texture.mipmapEnabled = false;
                if (changed) texture.SaveAndReimport();
            }
        }

        public static FaceOverlayLayout LoadLayout()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
            if (asset == null) throw new FileNotFoundException("缺少表情布局", LayoutPath);
            return FaceOverlayLayout.Parse(asset.text);
        }

        public static Material EnsureDecalMaterial(string path, string atlasPath, float viewOffset)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new FileNotFoundException("找不到着色器 " + ShaderName);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_MainTex", atlas);
            material.SetFloat("_ViewOffset", viewOffset);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 脸的正前方（贴片网格物体空间）= 贴片法线平均。Blender 里所有贴片法线都朝脸外（报告
        /// normals_all_positive_y），导入后的轴向由 FBX 节点决定，所以不手写方向，按网格算一次。
        /// 四只宠物用同一份网格，物体空间方向相同，写在共享材质上即可。
        /// </summary>
        public static void SetFaceForward(Material material, Mesh mesh)
        {
            var normals = mesh.normals;
            var sum = Vector3.zero;
            for (var i = 0; i < normals.Length; i++) sum += normals[i];
            if (sum.sqrMagnitude < 1e-12f) throw new System.InvalidOperationException(mesh.name + " 没有可用法线");
            var forward = sum.normalized;
            material.SetVector("_FaceForward", new Vector4(forward.x, forward.y, forward.z, 0f));
            EditorUtility.SetDirty(material);
        }

        public static Material EnsureCoverMaterial(PetVariantId variant, string part, float[] uv)
        {
            var path = CoverMaterialPath(variant, part);
            var skin = PetSceneAssets.LoadMaterial(variant, "Skin");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(skin) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            material.CopyPropertiesFromMaterial(skin);
            // scale 0 把整片 UV 压成一个点，offset 指向皮肤纹素：遮盖色与皮肤完全一致。
            material.SetTextureScale("_MainTex", Vector2.zero);
            material.SetTextureOffset("_MainTex", new Vector2(uv[0], uv[1]));
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 在一只宠物实例下补齐两片贴片并写进 PetPresenter（幂等）。
        /// <paramref name="petInstance"/> 是 Pet01.fbx 的实例根，<paramref name="body"/> 是其 SkinnedMeshRenderer。
        /// </summary>
        public static void Attach(PetPresenter presenter, GameObject petInstance, SkinnedMeshRenderer body,
            PetVariantId variant)
        {
            var layout = LoadLayout();
            var head = FindDeep(petInstance.transform, string.IsNullOrEmpty(layout.headBone) ? "Head" : layout.headBone);
            if (head == null) throw new System.InvalidOperationException("Pet01 实例里找不到 Head 骨骼");

            var eyesMaterial = EnsureDecalMaterial(EyesDecalMaterialPath, EyesAtlasPath, layout.viewOffsetMeters);
            var mouthMaterial = EnsureDecalMaterial(MouthDecalMaterialPath, MouthAtlasPath, layout.viewOffsetMeters);
            var eyes = EnsurePatch(petInstance.transform, head, layout.eyesPatch, eyesMaterial);
            var mouth = EnsurePatch(petInstance.transform, head, layout.mouthPatch, mouthMaterial);

            var cover = layout.CoverFor(variant);
            if (cover == null) throw new System.InvalidOperationException("表情布局缺少遮盖 UV：" + variant);
            var eyesCover = EnsureCoverMaterial(variant, "Eyes", cover.eyesUv);
            var mouthCover = EnsureCoverMaterial(variant, "Mouth", cover.mouthUv);

            var so = new SerializedObject(presenter);
            so.FindProperty("variant").intValue = (int)variant;
            so.FindProperty("eyesDecal").objectReferenceValue = eyes;
            so.FindProperty("mouthDecal").objectReferenceValue = mouth;
            so.FindProperty("bodyRenderer").objectReferenceValue = body;
            var materials = so.FindProperty("bodyMaterials");
            var original = PetSceneAssets.LoadMaterials(variant);
            materials.arraySize = original.Length;
            for (var i = 0; i < original.Length; i++)
                materials.GetArrayElementAtIndex(i).objectReferenceValue = original[i];
            so.FindProperty("eyesCoverMaterial").objectReferenceValue = eyesCover;
            so.FindProperty("mouthCoverMaterial").objectReferenceValue = mouthCover;
            so.FindProperty("faceLayout").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
            so.FindProperty("oralSlot").intValue = System.Array.IndexOf(PetSceneAssets.MaterialSlots, "Oral");
            so.FindProperty("eyesSlot").intValue = System.Array.IndexOf(PetSceneAssets.MaterialSlots, "Eyes");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static MeshRenderer EnsurePatch(Transform petInstance, Transform head, string patchName, Material material)
        {
            var existing = FindDeep(head, patchName);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                // 整个 FBX 克隆到实例根下零变换：导入器给网格节点加的轴向转换与 Pet01.fbx 相同，
                // 于是贴片与绑定姿态的脸对齐；取出目标节点、保持世界位姿挂到 Head，再丢掉克隆根。
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayModelPath);
                if (model == null) throw new FileNotFoundException("缺少表情贴片模型", OverlayModelPath);
                var clone = Object.Instantiate(model, petInstance, false);
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale = Vector3.one;
                var node = FindDeep(clone.transform, patchName);
                if (node == null)
                {
                    Object.DestroyImmediate(clone);
                    throw new FileNotFoundException($"{OverlayModelPath} 里没有节点 {patchName}");
                }

                node.SetParent(head, worldPositionStays: true);
                node.name = patchName;
                Object.DestroyImmediate(clone);
                go = node.gameObject;
                if (go.GetComponent<MeshFilter>() == null) go.AddComponent<MeshFilter>().sharedMesh = LoadMesh(patchName);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            SetFaceForward(material, go.GetComponent<MeshFilter>().sharedMesh);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // 默认隐藏，由 PetPresenter.SetExpression 按格子打开。
            renderer.enabled = false;
            return renderer;
        }

        private static Mesh LoadMesh(string meshName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(OverlayModelPath))
            {
                if (asset is Mesh mesh && mesh.name == meshName) return mesh;
            }

            throw new FileNotFoundException($"{OverlayModelPath} 里没有网格 {meshName}");
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }
    }
}
