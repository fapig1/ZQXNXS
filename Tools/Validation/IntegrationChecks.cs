// Exercises application wiring and conditional control flow against recorded API calls.
// These are offline substitute checks, not Unity scene/Animator/Android/Vuforia tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZQXNXS.ARPet.App;
using ZQXNXS.ARPet.AR;
using ZQXNXS.ARPet.Core;
using ZQXNXS.ARPet.Interaction;
using ZQXNXS.ARPet.Persistence;
using ZQXNXS.ARPet.Pet;
using ZQXNXS.ARPet.Platform;
using ZQXNXS.ARPet.Presentation;
#if VALIDATION_DEVICE
using UnityEngine.Android;
using Vuforia;
#else
using UnityEditor.SceneManagement;
using ZQXNXS.ARPet.EditorTools;
#endif

namespace ARPet.Validation
{
    public sealed class IntegrationChecks
    {
        private string _oldRoot, _caseRoot, _oldDirectory;
        private readonly List<ITrackingSource> _sources = new();

        [SetUp]
        public void SetUp()
        {
            _oldRoot = Environment.GetEnvironmentVariable("ARPET_VALIDATION_ROOT");
            _caseRoot = Path.GetFullPath(Path.Combine(_oldRoot, "case_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_caseRoot);
            _oldDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(_caseRoot);
            Environment.SetEnvironmentVariable("ARPET_VALIDATION_ROOT", _caseRoot);
            GameObject.Created.Clear();
            ValidationClock.time = 0f;
            ValidationClock.deltaTime = 0.1f;
#if VALIDATION_DEVICE
            Permission.Authorized = false;
            Permission.Requests = 0;
            Permission.LastCallbacks = null;
            VuforiaApplication.Instance.IsRunning = false;
            VuforiaApplication.Instance.InitializeCount = 0;
            VuforiaBehaviour.Instance = new GameObject("Vuforia").AddComponent<VuforiaBehaviour>();
#else
            EditorSceneManager.SavedObjects.Clear();
            EditorSceneManager.AllowSceneSwitch = true;
            EditorSceneManager.NewSceneCount = 0;
            EditorSceneManager.SavePromptCount = 0;
#endif
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var source in _sources) source.PauseTracking("Offline test cleanup");
            _sources.Clear();
            Environment.SetEnvironmentVariable("ARPET_VALIDATION_ROOT", _oldRoot);
            Directory.SetCurrentDirectory(_oldDirectory);
            var expectedPrefix = Path.GetFullPath(_oldRoot) + Path.DirectorySeparatorChar;
            if (!_caseRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe test cleanup path");
            Directory.Delete(_caseRoot, recursive: true);
        }

        private static FieldInfo Field(object value, string name) => value.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingFieldException(value.GetType().FullName, name);
        private static void Set(object value, string name, object data) => Field(value, name).SetValue(value, data);
        private static T Get<T>(object value, string name) => (T)Field(value, name).GetValue(value);
        private static void Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(value, args);

        private AppRoot App(PetConfig config, MonoBehaviour source, out Animator animator)
        {
            var pet = new GameObject("Pet");
            animator = pet.AddComponent<Animator>();
            var presenter = pet.AddComponent<PetPresenter>();
            Set(presenter, "animator", animator);

            // 离线替身现在对齐 Unity：AddComponent 会立即调用 Awake。这里需要先注入字段再跑 Awake，
            // 因此先建一个未激活的物体（Unity 里 Awake 也是在物体激活时才跑）。
            var appGo = new GameObject("App");
            appGo.SetActive(false);
            var app = appGo.AddComponent<AppRoot>();
            Set(app, "config", config);
            Set(app, "trackingSourceBehaviour", source);
            Set(app, "markerRegistryBehaviour", source);
            // 宠物装配已改为 pets 数组：一条绑定 = 一只宠物（pet_classic + 它的表现层）。
            Set(app, "pets", new[]
            {
                new AppRoot.PetBinding { petId = "pet_classic", presenter = presenter },
            });
            appGo.SetActive(true);
            Invoke(app, "Awake");
            Invoke(app, "Start");
            return app;
        }

        private StubTrackingSource Stub()
        {
            var source = new GameObject("Tracking").AddComponent<StubTrackingSource>();
            var proxy = new GameObject("ClassicProxy").transform;
            // 走 SetTargets 而不是反射写字段：AddComponent 已经跑过 Awake 并在空表上建了索引。
            source.SetTargets(new[]
            {
                new StubTrackingSource.StubTarget
                {
                    targetId = "card_classic",
                    proxy = proxy,
                    size = new Vector3(0.066f, 0f, 0.094f),
                    simulatedStatus = TrackingStatus.Tracked,
                },
            });
            _sources.Add(source);
            return source;
        }

        private static void Frame(AppRoot app, StubTrackingSource source, float dt = 0.1f,
            TrackingStatus denStatus = TrackingStatus.Tracked)
        {
            ValidationClock.time += dt;
            ValidationClock.deltaTime = dt;
            source.SetSimulatedStatus("card_classic", denStatus);
            // 真实播放时跟踪源与 AppRoot 每帧各跑一次；离线替身必须同样先走跟踪源，再走应用层。
            Invoke(source, "Update");
            Invoke(app, "Update");
        }

#if !VALIDATION_DEVICE
        [Test]
        public void FreshApp_PreservesConfiguredInitialHappiness()
        {
            var config = ScriptableObject.CreateInstance<PetConfig>();
            config.NeedSeeds[0].Initial = 0.42f;
            var app = App(config, Stub(), out _);

            Assert.AreEqual(0.42f, app.PetSnapshot.Happiness, 0.0001f);
            // 旧版字段不再由配置初始化，只保留 PetState 的历史默认值，供 v1 存档迁移。
            Assert.AreEqual(0.30f, app.PetSnapshot.Hunger, 0.0001f);
            Assert.AreEqual(0.85f, app.PetSnapshot.Energy, 0.0001f);
        }

        [Test]
        public void App_UiDebounceDoesNotPermitTouchOnInvalidPose()
        {
            var source = Stub();
            var app = App(ScriptableObject.CreateInstance<PetConfig>(), source, out _);
            Frame(app, source);
            app.OnTouchPointerDown(Vector2.zero, false, "pet_classic");
            Frame(app, source, 0.01f, TrackingStatus.Lost);
            Assert.AreEqual(TrackingUiState.Tracking, app.TrackingState);
            Assert.IsFalse(app.OnTouchPointerUp(Vector2.zero, "pet_classic"));
            Assert.AreEqual(0, Get<TouchPokeRule>(app, "_touch").QualifiedTapCount);
        }

        [Test]
        public void GeneratedSmokeScene_AssignsSingleCharacterCardTarget()
        {
            ProjectSetup.GenerateAll();
            var objects = EditorSceneManager.SavedObjects["Assets/_Project/Scenes/01_TrackingSmoke.unity"];
            var source = objects.Select(value => value.GetComponent<StubTrackingSource>()).First(value => value != null);
            Assert.IsTrue(source.IsRegistered("card_classic"));
            // 01_TrackingSmoke 仍是单宠冒烟场景（02_Main 才是四色多宠主场景），只注册一张模拟角色卡。
            Assert.AreEqual(1, source.TargetIds.Count, "单宠冒烟场景只注册一张角色卡");
            // 数组条目里的 Vector3 必须真的写回场景对象，而不是只改了替身副本。
            Assert.AreEqual(0.08f, source.GetTargetSize("card_classic").x, 0.0001f);
            Assert.AreEqual(0.08f, source.GetTargetSize("card_classic").z, 0.0001f);
            Invoke(source, "Update");
            ValidationClock.time = 0.02f;
            Invoke(source, "Update");
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample("card_classic"), Time.time));
        }

        [Test]
        public void Regeneration_PreservesBothExistingSceneFiles()
        {
            // 02_Main 现在是四色多宠主场景，由菜单 5/6（或 RebuildMainSceneForBatch）接线；
            // 本检查只关心"已存在的场景文件不被 GenerateAll 覆盖"，与场景内部结构无关。
            ProjectSetup.GenerateAll();
            const string main = "Assets/_Project/Scenes/02_Main.unity";
            const string smoke = "Assets/_Project/Scenes/01_TrackingSmoke.unity";
            File.WriteAllText(main, "hand-configured ARCamera and four card ImageTargets");
            File.WriteAllText(smoke, "hand-configured smoke scene");
            ProjectSetup.GenerateAll();
            Assert.AreEqual("hand-configured ARCamera and four card ImageTargets", File.ReadAllText(main));
            Assert.AreEqual("hand-configured smoke scene", File.ReadAllText(smoke));
            Assert.AreEqual(2, EditorSceneManager.NewSceneCount);
            Assert.AreEqual(1, EditorSceneManager.SavePromptCount);
        }

        [Test]
        public void CancelledSceneSavePrompt_StopsGeneration()
        {
            EditorSceneManager.AllowSceneSwitch = false;
            ProjectSetup.GenerateAll();
            Assert.AreEqual(0, EditorSceneManager.NewSceneCount);
            Assert.IsFalse(Directory.Exists("Assets/_Project/Scenes"));
        }

        [Test]
        public void MainScene_DoesNotAutoSubstituteSimulatedTracking()
        {
            ProjectSetup.GenerateAll();
            var objects = EditorSceneManager.SavedObjects["Assets/_Project/Scenes/02_Main.unity"];
            var app = objects.Select(value => value.GetComponent<AppRoot>()).First(value => value != null);
            Assert.IsFalse(Get<bool>(app, "autoCreateStubSource"));
        }
#else
        private VuforiaTrackingSource VuforiaSource()
        {
            var source = new GameObject("Tracking").AddComponent<VuforiaTrackingSource>();
            // 同 Stub：AddComponent 已跑过 Awake，目标表必须走 SetTargets 才会重建索引。
            source.SetTargets(new[]
            {
                VuforiaTarget("card_classic", new Vector3(0.066f, 0f, 0.094f)),
                VuforiaTarget("card_calm", new Vector3(0.066f, 0f, 0.094f)),
            });
            _sources.Add(source);
            return source;
        }

        private static VuforiaTrackingSource.TargetEntry VuforiaTarget(string targetId, Vector3 size)
        {
            var target = new GameObject(targetId);
            target.AddComponent<ObserverBehaviour>().TargetStatus = new TargetStatus { Status = Status.TRACKED };
            return new VuforiaTrackingSource.TargetEntry
            {
                targetId = targetId,
                targetObject = target,
                size = size,
            };
        }

        [Test]
        public void AndroidPermission_UsesRuntimeAuthorizationState()
        {
            Assert.IsFalse(CameraPermission.HasPermission);
            Permission.Authorized = true;
            Assert.IsTrue(CameraPermission.HasPermission);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AndroidPermission_DenialCompletesExactlyOnce(bool permanent)
        {
            var calls = 0;
            var result = true;
            CameraPermission.Request(granted => { result = granted; calls++; });
            var callbacks = Permission.LastCallbacks;
            if (permanent) callbacks.DenyPermanently(); else callbacks.Deny();
            callbacks.Deny();
            Assert.IsFalse(result);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, Permission.Requests);
        }

        [Test]
        public void AndroidPermission_ConcurrentRequestsShareOnePrompt()
        {
            var grants = 0;
            CameraPermission.Request(granted => { if (granted) grants++; });
            CameraPermission.Request(granted => { if (granted) grants++; });
            Permission.Authorized = true;
            Permission.LastCallbacks.Grant();
            Permission.LastCallbacks.Grant();
            Assert.AreEqual(2, grants);
            Assert.AreEqual(1, Permission.Requests);
        }

        [Test]
        public void VuforiaPause_ImmediatelyInvalidatesCachedSamplesAndRegistry()
        {
            var source = VuforiaSource();
            VuforiaApplication.Instance.IsRunning = true;
            source.StartTracking();
            Invoke(source, "Update");
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample("card_calm"), Time.time));
            source.PauseTracking("test");
            Assert.AreEqual(TrackingStatus.Paused, source.AllSamples[1].Status);
            ValidationClock.time += 1;
            Invoke(source, "Update");
            Assert.AreEqual(TrackingStatus.Paused, source.GetSample("card_calm").Status);
            Assert.IsFalse(source.TryGetTargetPose("card_classic", out _, out _));
        }

        [TestCase(Status.EXTENDED_TRACKED)]
        [TestCase(Status.LIMITED)]
        [TestCase((Status)999)]
        public void VuforiaEstimatedOrUnknownPose_CannotSettle(Status status)
        {
            var source = VuforiaSource();
            Get<VuforiaTrackingSource.TargetEntry[]>(source, "targets")[1].targetObject
                .GetComponent<ObserverBehaviour>().TargetStatus = new TargetStatus { Status = status };
            VuforiaApplication.Instance.IsRunning = true;
            source.StartTracking();
            Invoke(source, "Update");
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample("card_classic"), Time.time));
            Assert.IsFalse(PoseFreshness.IsSettleable(source.GetSample("card_calm"), Time.time));
        }

        [Test]
        public void VuforiaLateStartedCallback_CannotUndoPause()
        {
            var source = VuforiaSource();
            source.StartTracking();
            source.PauseTracking("background");
            VuforiaApplication.Instance.CompleteStart();
            Assert.IsFalse(source.IsRunning);
            Assert.AreEqual(TrackingStatus.Paused, source.GetSample("card_classic").Status);
        }

        [Test]
        public void VuforiaResume_RequiresNewSamples()
        {
            var source = VuforiaSource();
            VuforiaApplication.Instance.IsRunning = true;
            source.StartTracking();
            Invoke(source, "Update");
            source.PauseTracking("test");
            source.StartTracking();
            Assert.IsFalse(PoseFreshness.IsSettleable(source.GetSample("card_classic"), Time.time));
            Invoke(source, "Update");
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample("card_classic"), Time.time));
        }

        [Test]
        public void VuforiaDisabledObserver_CannotPublishTrackedPose()
        {
            var source = VuforiaSource();
            Get<VuforiaTrackingSource.TargetEntry[]>(source, "targets")[1].targetObject.SetActive(false);
            VuforiaApplication.Instance.IsRunning = true;
            source.StartTracking();
            Invoke(source, "Update");
            Assert.IsFalse(PoseFreshness.IsSettleable(source.GetSample("card_calm"), Time.time));
        }

        [Test]
        public void PermissionDenialAndFocusReturn_DoNotStartTracking()
        {
            var source = VuforiaSource();
            var app = App(ScriptableObject.CreateInstance<PetConfig>(), source, out _);
            Invoke(app, "OnApplicationFocus", false);
            Permission.LastCallbacks.Deny();
            Invoke(app, "OnApplicationFocus", true);
            Assert.IsFalse(source.IsRunning);
            Assert.AreEqual(0, VuforiaApplication.Instance.InitializeCount);
            Assert.AreEqual(1, Permission.Requests);
        }

        [Test]
        public void PermissionGrantedInBackground_WaitsForForeground()
        {
            var source = VuforiaSource();
            var app = App(ScriptableObject.CreateInstance<PetConfig>(), source, out _);
            Invoke(app, "OnApplicationFocus", false);
            Permission.Authorized = true;
            Permission.LastCallbacks.Grant();
            Assert.AreEqual(0, VuforiaApplication.Instance.InitializeCount);
            Invoke(app, "OnApplicationFocus", true);
            Assert.AreEqual(1, VuforiaApplication.Instance.InitializeCount);
            VuforiaApplication.Instance.CompleteStart();
            Assert.IsTrue(source.IsRunning);
        }
#endif
    }
}
