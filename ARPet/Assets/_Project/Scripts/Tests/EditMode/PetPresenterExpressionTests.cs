using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Pet;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.Tests
{
    /// <summary>
    /// P5-B 二维嘴眼表情：选格规则、按渲染器隔离（四宠共用材质不串表情）、遮盖材质换槽、
    /// 与跟踪隐藏的配合。贴片几何与画面效果由 Blender 预览和视觉审查负责，这里只测逻辑。
    /// </summary>
    public sealed class PetPresenterExpressionTests
    {
        private const string LayoutJson =
            "{\"viewOffsetMeters\":0.01,\"eyesPatch\":\"Pet01_FaceEyes\",\"mouthPatch\":\"Pet01_FaceMouth\",\"headBone\":\"Head\"," +
            "\"eyeCells\":[{\"name\":\"squint\",\"st\":[0.5,0.1875,0,0.8125]},{\"name\":\"closed\",\"st\":[0.5,0.1875,0.5,0.8125]}," +
            "{\"name\":\"divine\",\"st\":[0.5,0.1875,0,0.625]},{\"name\":\"rage\",\"st\":[0.5,0.1875,0.5,0.625]}]," +
            "\"mouthCells\":[{\"name\":\"smile\",\"st\":[0.5,0.25,0,0.75]},{\"name\":\"laugh\",\"st\":[0.5,0.25,0.5,0.75]}," +
            "{\"name\":\"roar\",\"st\":[0.5,0.25,0,0.5]},{\"name\":\"o\",\"st\":[0.5,0.25,0.5,0.5]}," +
            "{\"name\":\"serene\",\"st\":[0.5,0.25,0,0.25]},{\"name\":\"flat\",\"st\":[0.5,0.25,0.5,0.25]}]," +
            "\"covers\":[{\"variant\":\"classic\",\"eyesUv\":[0.25,0.9],\"mouthUv\":[0.25,0.85]}]}";

        private sealed class Rig
        {
            public GameObject Root;
            public PetPresenter Presenter;
            public MeshRenderer Eyes;
            public MeshRenderer Mouth;
            public SkinnedMeshRenderer Body;
            public Material[] Original;
            public Material EyesCover;
            public Material MouthCover;
        }

        private readonly System.Collections.Generic.List<Object> _created = new();
        private Material _sharedEyes;
        private Material _sharedMouth;
        private TextAsset _layout;

        [SetUp]
        public void SetUp()
        {
            _sharedEyes = Track(new Material(Shader.Find("Standard")) { name = "SharedEyes" });
            _sharedMouth = Track(new Material(Shader.Find("Standard")) { name = "SharedMouth" });
            _layout = Track(new TextAsset(LayoutJson) { name = "layout" });
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void Table_SignatureFollowsVariant_AndRoarNeverUsesSmile()
        {
            Assert.AreEqual(new FaceExpressionTable.Cells("squint", "smile"),
                FaceExpressionTable.Resolve(PetVariantId.Classic, PetExpression.Signature));
            Assert.AreEqual(new FaceExpressionTable.Cells("closed", "serene"),
                FaceExpressionTable.Resolve(PetVariantId.Calm, PetExpression.Signature));
            Assert.AreEqual(new FaceExpressionTable.Cells("rage", "roar"),
                FaceExpressionTable.Resolve(PetVariantId.Rage, PetExpression.Signature),
                "rage 专属必须是怒眼+怒吼嘴，不能用上扬的笑嘴");
            Assert.AreEqual(new FaceExpressionTable.Cells("divine", "serene"),
                FaceExpressionTable.Resolve(PetVariantId.Divine, PetExpression.Signature),
                "divine 是唯一换成纯白发光眼的专属");
        }

        [Test]
        public void Table_SharedExpressions_AreTheSameForEveryVariant()
        {
            foreach (PetVariantId variant in System.Enum.GetValues(typeof(PetVariantId)))
            {
                Assert.AreEqual(new FaceExpressionTable.Cells("squint", "laugh"),
                    FaceExpressionTable.Resolve(variant, PetExpression.VeryHappy), variant + " 共同大笑");
                Assert.AreEqual(new FaceExpressionTable.Cells(null, "o"),
                    FaceExpressionTable.Resolve(variant, PetExpression.Surprised), variant + " 被点");
                Assert.IsFalse(FaceExpressionTable.Resolve(variant, PetExpression.Neutral).ShowsAnything,
                    variant + " 中性表情必须露出原模型的眼和嘴");
            }
        }

        [Test]
        public void BehaviorMapping_ExclusiveUsesSignature_SocialUsesVeryHappy()
        {
            Assert.AreEqual(PetExpression.Signature, BehaviorStateMachine.ExpressionFor(PetBehaviorId.Exclusive));
            Assert.AreEqual(PetExpression.VeryHappy, BehaviorStateMachine.ExpressionFor(PetBehaviorId.Social));
            Assert.AreEqual(PetExpression.Surprised, BehaviorStateMachine.ExpressionFor(PetBehaviorId.Petted));
            Assert.AreEqual(PetExpression.Neutral, BehaviorStateMachine.ExpressionFor(PetBehaviorId.Idle));
        }

        [Test]
        public void SetExpression_SelectsCellViaPropertyBlock_AndShowsOnlyUsedPatches()
        {
            var rig = Build(PetVariantId.Rage);

            rig.Presenter.SetExpression(PetExpression.Signature, 1f);

            Assert.AreEqual(new FaceExpressionTable.Cells("rage", "roar"), rig.Presenter.CurrentCells);
            Assert.IsTrue(rig.Eyes.enabled);
            Assert.IsTrue(rig.Mouth.enabled);
            Assert.AreEqual(new Vector4(0.5f, 0.1875f, 0.5f, 0.625f), ReadSt(rig.Eyes));
            Assert.AreEqual(new Vector4(0.5f, 0.25f, 0f, 0.5f), ReadSt(rig.Mouth));

            rig.Presenter.SetExpression(PetExpression.Surprised, 1f);
            Assert.IsFalse(rig.Eyes.enabled, "惊讶只换嘴，眼贴片必须关掉");
            Assert.IsTrue(rig.Mouth.enabled);
            Assert.AreEqual(new Vector4(0.5f, 0.25f, 0.5f, 0.5f), ReadSt(rig.Mouth));
        }

        [Test]
        public void FourPets_SharingOneMaterial_KeepTheirOwnExpression()
        {
            var rigs = new[]
            {
                Build(PetVariantId.Classic), Build(PetVariantId.Calm), Build(PetVariantId.Rage), Build(PetVariantId.Divine),
            };

            for (var i = 0; i < rigs.Length; i++) rigs[i].Presenter.SetExpression(PetExpression.Signature, 1f);
            // 再改其中一只：其他三只不能跟着变。
            rigs[1].Presenter.SetExpression(PetExpression.Surprised, 1f);

            Assert.AreEqual(new Vector4(0.5f, 0.1875f, 0f, 0.8125f), ReadSt(rigs[0].Eyes), "classic 眯眼");
            Assert.IsFalse(rigs[1].Eyes.enabled, "calm 改成惊讶后眼贴片隐藏");
            Assert.AreEqual(new Vector4(0.5f, 0.25f, 0.5f, 0.5f), ReadSt(rigs[1].Mouth), "calm o 嘴");
            Assert.AreEqual(new Vector4(0.5f, 0.1875f, 0.5f, 0.625f), ReadSt(rigs[2].Eyes), "rage 怒眼");
            Assert.AreEqual(new Vector4(0.5f, 0.1875f, 0f, 0.625f), ReadSt(rigs[3].Eyes), "divine 白眼");
            for (var i = 0; i < rigs.Length; i++)
            {
                Assert.AreSame(_sharedEyes, rigs[i].Eyes.sharedMaterial, "选格不能通过复制材质实现");
                Assert.AreSame(_sharedMouth, rigs[i].Mouth.sharedMaterial);
            }
        }

        [Test]
        public void CoverMaterials_SwapInOnlyWhileTheirPatchIsShown()
        {
            var rig = Build(PetVariantId.Classic);

            rig.Presenter.SetExpression(PetExpression.VeryHappy, 1f);
            Assert.AreSame(rig.Original[0], rig.Body.sharedMaterials[0], "Skin 槽不变");
            Assert.AreSame(rig.MouthCover, rig.Body.sharedMaterials[1], "嘴贴片显示时 Oral 槽换成遮盖");
            Assert.AreSame(rig.EyesCover, rig.Body.sharedMaterials[2], "眼贴片显示时 Eyes 槽换成遮盖");

            rig.Presenter.SetExpression(PetExpression.Surprised, 1f);
            Assert.AreSame(rig.MouthCover, rig.Body.sharedMaterials[1]);
            Assert.AreSame(rig.Original[2], rig.Body.sharedMaterials[2], "眼贴片隐藏时恢复原眼");

            rig.Presenter.SetExpression(PetExpression.Neutral, 1f);
            Assert.AreSame(rig.Original[1], rig.Body.sharedMaterials[1]);
            Assert.AreSame(rig.Original[2], rig.Body.sharedMaterials[2]);
        }

        [Test]
        public void TrackingHidden_KeepsPatchesOff_AndRecoveryRestoresOnlyActiveOnes()
        {
            var rig = Build(PetVariantId.Calm);
            rig.Presenter.SetExpression(PetExpression.Surprised, 1f);

            rig.Presenter.SetTrackingVisibility(false);
            Assert.IsFalse(rig.Mouth.enabled);
            Assert.IsFalse(rig.Body.enabled);

            rig.Presenter.SetExpression(PetExpression.Signature, 1f);
            Assert.IsFalse(rig.Eyes.enabled, "跟踪丢失期间换表情也不能把贴片点亮");
            Assert.IsFalse(rig.Mouth.enabled);

            rig.Presenter.SetTrackingVisibility(true);
            Assert.IsTrue(rig.Body.enabled);
            Assert.IsTrue(rig.Eyes.enabled, "恢复后按当前格子显示眼贴片");
            Assert.IsTrue(rig.Mouth.enabled);

            rig.Presenter.SetExpression(PetExpression.Neutral, 1f);
            rig.Presenter.SetTrackingVisibility(false);
            rig.Presenter.SetTrackingVisibility(true);
            Assert.IsFalse(rig.Eyes.enabled, "中性表情恢复跟踪后贴片保持隐藏");
            Assert.IsFalse(rig.Mouth.enabled);
        }

        [Test]
        public void MissingLayout_IsSafe_AndShowsNothing()
        {
            var rig = Build(PetVariantId.Classic, withLayout: false);

            Assert.DoesNotThrow(() => rig.Presenter.SetExpression(PetExpression.Signature, 1f));
            Assert.IsFalse(rig.Eyes.enabled);
            Assert.IsFalse(rig.Mouth.enabled);
            Assert.AreSame(rig.Original[2], rig.Body.sharedMaterials[2], "没有布局就不能遮掉原眼");
        }

        private Rig Build(PetVariantId variant, bool withLayout = true)
        {
            var rig = new Rig { Root = Track(new GameObject("Pet_" + variant)) };
            rig.Presenter = rig.Root.AddComponent<PetPresenter>();

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(rig.Root.transform, false);
            rig.Body = bodyGo.AddComponent<SkinnedMeshRenderer>();
            rig.Original = new[]
            {
                Track(new Material(Shader.Find("Standard")) { name = "Skin" }),
                Track(new Material(Shader.Find("Standard")) { name = "Oral" }),
                Track(new Material(Shader.Find("Standard")) { name = "Eyes" }),
            };
            rig.Body.sharedMaterials = (Material[])rig.Original.Clone();
            rig.EyesCover = Track(new Material(Shader.Find("Standard")) { name = "EyesCover" });
            rig.MouthCover = Track(new Material(Shader.Find("Standard")) { name = "MouthCover" });

            rig.Eyes = Patch(bodyGo.transform, "Pet01_FaceEyes", _sharedEyes);
            rig.Mouth = Patch(bodyGo.transform, "Pet01_FaceMouth", _sharedMouth);

            var so = new SerializedObject(rig.Presenter);
            so.FindProperty("variant").intValue = (int)variant;
            so.FindProperty("eyesDecal").objectReferenceValue = rig.Eyes;
            so.FindProperty("mouthDecal").objectReferenceValue = rig.Mouth;
            so.FindProperty("bodyRenderer").objectReferenceValue = rig.Body;
            var materials = so.FindProperty("bodyMaterials");
            materials.arraySize = rig.Original.Length;
            for (var i = 0; i < rig.Original.Length; i++)
                materials.GetArrayElementAtIndex(i).objectReferenceValue = rig.Original[i];
            so.FindProperty("eyesCoverMaterial").objectReferenceValue = rig.EyesCover;
            so.FindProperty("mouthCoverMaterial").objectReferenceValue = rig.MouthCover;
            so.FindProperty("faceLayout").objectReferenceValue = withLayout ? _layout : null;
            so.FindProperty("visualRoot").objectReferenceValue = rig.Root;
            so.ApplyModifiedPropertiesWithoutUndo();
            return rig;
        }

        private static MeshRenderer Patch(Transform parent, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false;
            return renderer;
        }

        private static Vector4 ReadSt(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetVector("_MainTex_ST");
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }
    }
}
