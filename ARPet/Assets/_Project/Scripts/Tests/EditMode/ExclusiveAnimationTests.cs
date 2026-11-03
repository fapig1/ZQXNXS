using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.Tests
{
    public sealed class ExclusiveAnimationTests
    {
        private const string Art = "Assets/_Project/Art/Characters/Pet01/";

        [TestCase(PetVariantId.Classic, "classic_proud", 2f)]
        [TestCase(PetVariantId.Calm, "calm_zen", 2.5f)]
        [TestCase(PetVariantId.Rage, "rage_roar", 2f)]
        [TestCase(PetVariantId.Divine, "divine_awaken", 2.5f)]
        public void RealModel_PlaysExclusiveThenReturnsToIdle(PetVariantId variant, string state, float duration)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PetExclusiveAnimations>("Assets/_Project/Configs/PetExclusiveAnimations.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryGet(variant, out var entry), Is.True);
            Assert.That(entry.StateName, Is.EqualTo(state));
            Assert.That(entry.Clip.length, Is.EqualTo(duration).Within(.04f));
            Assert.That(entry.Clip.isLooping, Is.False);
            Assert.That(entry.Clip.events, Is.Empty, "动画不结算开心值");
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Models/Pet01.fbx"));
            var baked = new Mesh();
            try
            {
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/_Project/Animations/Pet01Controller.controller");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                var head = model.GetComponentsInChildren<Transform>().Single(t => t.name == "Head");
                animator.Rebind();
                animator.Play(state, 0, 0f);
                animator.Update(0f);
                var startHead = head.localRotation;
                skin.BakeMesh(baked);
                var start = baked.vertices;
                animator.Update(duration * .48f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True);
                Assert.That(Quaternion.Angle(startHead, head.localRotation), Is.GreaterThan(.1f), "Clip 必须驱动实际骨骼");
                skin.BakeMesh(baked);
                Assert.That(baked.vertices.Where((v, i) => Vector3.Distance(v, start[i]) > .0001f).Any(), Is.True);
                Assert.That(baked.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), Is.True);
                animator.Play("Idle", 0, 0f);
                animator.Update(0f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void SharedLaugh_DrivesSpineAndHeadThenReturnsToIdle()
        {
            var path = Art + "Models/Pet01_social_laugh.fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => c.name == "social_laugh");
            Assert.That(clip.length, Is.EqualTo(2.4f).Within(.04f));
            Assert.That(clip.isLooping, Is.False);
            Assert.That(clip.events, Is.Empty, "动画不结算开心值");
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Models/Pet01.fbx"));
            var baked = new Mesh();
            try
            {
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/_Project/Animations/Pet01Controller.controller");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var bones = model.GetComponentsInChildren<Transform>();
                var head = bones.Single(t => t.name == "Head");
                var root = bones.Single(t => t.name == "Root");
                var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                animator.Rebind();
                animator.Play("social_laugh", 0, 0f);
                animator.Update(0f);
                var startHead = head.position;
                var startRoot = root.localPosition;
                skin.BakeMesh(baked);
                var start = baked.vertices;
                // Second wave peak (frame 56 of 73).
                animator.Update(55f / 30f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("social_laugh"), Is.True);
                Assert.That(Vector3.Distance(startHead, head.position), Is.GreaterThan(.03f), "深度后仰必须明显移动头部");
                Assert.That(Vector3.Distance(startRoot, root.localPosition), Is.LessThan(1e-5f), "不烘焙根位移");
                skin.BakeMesh(baked);
                var peak = baked.vertices;
                Assert.That(peak.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), Is.True);
                Assert.That(peak.Where((v, i) => Vector3.Distance(v, start[i]) > .0001f).Any(), Is.True);
                animator.Play("Idle", 0, 0f);
                animator.Update(0f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void RevisedMesh_PreservesMoreThanFourInfluences()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Art + "Models/Pet01.fbx");
            Assert.That(importer.maxBonesPerVertex, Is.EqualTo(8));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Models/Pet01.fbx");
            using var counts = model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.GetBonesPerVertex();
            Assert.That(counts.ToArray().Max(), Is.EqualTo(8));
            Assert.That(QualitySettings.skinWeights, Is.EqualTo(SkinWeights.Unlimited));
        }

        [UnityTest]
        public IEnumerator PreviewScene_ButtonsPlayAndAutomaticallyReturnToIdle()
        {
            _previousScenes = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/03_ExclusiveAnimationPreview.unity");
            yield return new EnterPlayMode();
            try
            {
                var previews = Object.FindObjectsByType<ExclusiveAnimationPreview>(FindObjectsSortMode.None);
                Assert.That(previews.Length, Is.EqualTo(4));
                yield return null;
                foreach (var preview in previews) preview.Play();
                var started = Time.time;
                var timeout = Time.realtimeSinceStartup + 15f;
                while (Time.time - started < .3f && Time.realtimeSinceStartup < timeout) yield return null;
                Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(.3f), "PlayMode clock did not advance");
                foreach (var preview in previews)
                {
                    Assert.That(preview.Catalog.TryGet(preview.Variant, out var entry), Is.True);
                    Assert.That(preview.Animator.GetCurrentAnimatorStateInfo(0).IsName(entry.StateName), Is.True,
                        $"{preview.Variant}: current={preview.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash}, expected={Animator.StringToHash(entry.StateName)}, time={Time.time}");
                    Assert.That(preview.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials.All(m => m != null && m.mainTexture != null), Is.True);
                }
                while (Time.time - started < 3f && Time.realtimeSinceStartup < timeout) yield return null;
                Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(3f));
                foreach (var preview in previews)
                    Assert.That(preview.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True,
                        preview.Variant + " must return via ExclusiveAnimationPreview.Update");
            }
            finally
            {
                // UnityTearDown restores editor mode even if an assertion fails.
                // Scene setup was captured before entering PlayMode.
            }
        }

        private SceneSetup[] _previousScenes;

        [UnityTearDown]
        public IEnumerator RestoreEditorScene()
        {
            if (_previousScenes == null) yield break;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.RestoreSceneManagerSetup(_previousScenes);
            _previousScenes = null;
        }
    }
}
