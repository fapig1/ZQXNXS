using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZQXNXS.ARPet.App;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Presentation;

namespace ZQXNXS.ARPet.EditorTools
{
    /// <summary>
    /// 把 2026-09 生成的旧 <c>01_TrackingSmoke</c> 场景接回现役序列化契约。
    ///
    /// 旧场景停留在 P2 多宠改造之前：<see cref="StubTrackingSource"/> 还是
    /// <c>denProxy</c> / <c>foodProxy</c> 两个固定字段，<see cref="AppRoot"/> 还是单宠
    /// <c>presenter</c> / <c>anchoredPetRoot</c>。现役代码读的是 <c>targets[]</c> 与
    /// <c>pets[]</c>，两者在旧场景里都是空数组，于是运行期会出现
    /// “没有配置任何宠物绑定” 警告 + 该宠的目标卡未注册，最终 0 只可交互。
    ///
    /// 本工具只改这两处并补一只 <see cref="PetHitTarget"/>，不动模型、材质、Animator、
    /// 相机与 UI。幂等：重复运行结果一致。
    /// </summary>
    public static class SmokeSceneRepair
    {
        private const string ScenePath = "Assets/_Project/Scenes/01_TrackingSmoke.unity";
        private const string SmokePetId = "pet_classic";
        private const string SmokeTargetId = "card_classic";

        /// <summary>手动入口：修复后保持在场景里，直接按 Play 即可。</summary>
        [MenuItem("桌上有龙/10. 修复 01_TrackingSmoke 的模拟接线")]
        public static void RepairFromMenu()
        {
            if (!Repair()) return;
            EditorUtility.DisplayDialog("桌上有龙",
                "01_TrackingSmoke 已接回现役契约，可直接按 Play。", "好");
        }

        /// <summary>批处理入口：<c>-executeMethod ZQXNXS.ARPet.EditorTools.SmokeSceneRepair.RepairForBatch</c>。</summary>
        public static void RepairForBatch()
        {
            var ok = false;
            try
            {
                ok = Repair();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SmokeSceneRepair] 修复失败：" + e);
            }

            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Repair()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError($"[SmokeSceneRepair] 找不到场景 {ScenePath}；请先运行“1. 生成默认配置与场景”。");
                return false;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var appRoot = Object.FindFirstObjectByType<AppRoot>();
            var stub = Object.FindFirstObjectByType<StubTrackingSource>();
            var presenter = Object.FindFirstObjectByType<PetPresenter>();

            if (appRoot == null || stub == null || presenter == null)
            {
                Debug.LogError($"[SmokeSceneRepair] 场景缺组件：AppRoot={(appRoot != null)} " +
                               $"Stub={(stub != null)} PetPresenter={(presenter != null)}");
                return false;
            }

            var proxy = ResolveCardProxy(stub);

            // ── 1. 模拟跟踪源：targets[] 写一条 card_classic ──
            var stubSo = new SerializedObject(stub);
            var targets = stubSo.FindProperty("targets");
            targets.arraySize = 1;
            var entry = targets.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("targetId").stringValue = SmokeTargetId;
            entry.FindPropertyRelative("proxy").objectReferenceValue = proxy;
            entry.FindPropertyRelative("size").vector3Value = new Vector3(0.08f, 0f, 0.08f);
            entry.FindPropertyRelative("simulatedStatus").intValue = (int)TrackingStatus.Tracked;
            stubSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 2. AppRoot：pets[] 写一条 pet_classic ──
            var appSo = new SerializedObject(appRoot);
            var pets = appSo.FindProperty("pets");
            pets.arraySize = 1;
            var binding = pets.GetArrayElementAtIndex(0);
            binding.FindPropertyRelative("petId").stringValue = SmokePetId;
            binding.FindPropertyRelative("presenter").objectReferenceValue = presenter;
            appSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 3. 补触屏命中标记（旧场景没有，缺了点击不会路由到宠物）──
            var petRoot = presenter.transform;
            var hit = petRoot.GetComponent<PetHitTarget>();
            if (hit == null) hit = petRoot.gameObject.AddComponent<PetHitTarget>();

            var hitSo = new SerializedObject(hit);
            hitSo.FindProperty("petId").stringValue = SmokePetId;
            hitSo.FindProperty("hitRadiusMeters").floatValue = 0.08f;
            hitSo.FindProperty("hitHeightMeters").floatValue = 0.2f;
            var collider = petRoot.GetComponentInChildren<Collider>();
            if (collider != null)
                hitSo.FindProperty("hitCollider").objectReferenceValue = collider;
            hitSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[SmokeSceneRepair] 完成：targets[0]={SmokeTargetId}→{proxy.name}，" +
                      $"pets[0]={SmokePetId}→{presenter.name}，" +
                      $"PetHitTarget={(collider != null ? "有碰撞体" : "无碰撞体，用半径近似")}。");
            return true;
        }

        /// <summary>复用场景里已有的卡代理；旧场景叫 DenCardProxy，新场景叫 ClassicCardProxy。</summary>
        private static Transform ResolveCardProxy(StubTrackingSource stub)
        {
            foreach (var candidate in new[] { "ClassicCardProxy", "DenCardProxy" })
            {
                var found = GameObject.Find(candidate);
                if (found != null) return found.transform;
            }

            if (stub.transform.childCount > 0) return stub.transform.GetChild(0);

            var created = new GameObject("ClassicCardProxy");
            created.transform.SetParent(stub.transform, worldPositionStays: false);
            created.transform.localPosition = Vector3.zero;
            Debug.LogWarning("[SmokeSceneRepair] 场景里没有卡代理，已新建 ClassicCardProxy（位于原点）。");
            return created.transform;
        }
    }
}
