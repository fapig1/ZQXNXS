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
            Time.time = 0f;
            Time.deltaTime = 0.1f;
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
            var app = new GameObject("App").AddComponent<AppRoot>();
            Set(app, "config", config);
            Set(app, "trackingSourceBehaviour", source);
            Set(app, "markerRegistryBehaviour", source);
            Set(app, "presenter", presenter);
            Invoke(app, "Awake");
            Invoke(app, "Start");
            return app;
        }

        private StubTrackingSource Stub()
        {
            var source = new GameObject("Tracking").AddComponent<StubTrackingSource>();
            Set(source, "denProxy", new GameObject("Den").transform);
            var food = new GameObject("Food").transform;
            food.position = new Vector3(0.05f, 0f, 0f);
            Set(source, "foodProxy", food);
            _sources.Add(source);
            return source;
        }

        private static void Frame(AppRoot app, StubTrackingSource source, float dt = 0.1f,
            TrackingStatus denStatus = TrackingStatus.Tracked)
        {
            Time.time += dt;
            Time.deltaTime = dt;
            for (var index = 0; index < 2; index++)
            {
                source.TryGetMarkerPose((MarkerSlot)index, out var position, out var rotation);
                source.AllMarkers[index] = new PoseSample { Marker = (MarkerSlot)index,
                    Status = index == 0 ? denStatus : TrackingStatus.Tracked,
                    Position = position, Rotation = rotation, SampleTime = Time.time };
            }
            Invoke(app, "Update");
        }

#if !VALIDATION_DEVICE
        [Test]
        public void FreshApp_PreservesConfiguredInitialNeeds()
        {
            var config = ScriptableObject.CreateInstance<PetConfig>();
            config.NeedSeeds[0].Initial = 0.77f;
            config.NeedSeeds[1].Initial = 0.42f;
            config.NeedSeeds[2].Initial = 0.38f;
            var app = App(config, Stub(), out _);
            Assert.AreEqual(0.77f, app.PetSnapshot.Hunger, 0.0001f);
            Assert.AreEqual(0.42f, app.PetSnapshot.Happiness, 0.0001f);
            Assert.AreEqual(0.38f, app.PetSnapshot.Energy, 0.0001f);
        }

        [Test]
        public void App_DenLossClearsPartialDwell()
        {
            var source = Stub();
            var app = App(ScriptableObject.CreateInstance<PetConfig>(), source, out _);
            for (var i = 0; i < 5; i++) Frame(app, source);
            Assert.AreEqual(0, app.PetSnapshot.FeedCount);
            Frame(app, source, denStatus: TrackingStatus.Lost);
            for (var i = 0; i < 5; i++) Frame(app, source);
            Assert.AreEqual(0, app.PetSnapshot.FeedCount);
            Frame(app, source);
            Assert.AreEqual(1, app.PetSnapshot.FeedCount);
        }

        [Test]
        public void App_UiDebounceDoesNotPermitTouchOnInvalidPose()
        {
            var source = Stub();
            var app = App(ScriptableObject.CreateInstance<PetConfig>(), source, out _);
            Frame(app, source);
            app.FeedTouchPointerDown(Vector2.zero, false);
            Frame(app, source, 0.01f, TrackingStatus.Lost);
            Assert.AreEqual(TrackingUiState.Tracking, app.TrackingState);
            Assert.IsFalse(app.FeedTouchPointerUp(Vector2.zero, true));
            Assert.AreEqual(0, Get<TouchPokeRule>(app, "_touch").IssuedEventCount);
        }

        [Test]
        public void App_PauseWithoutInterveningUpdate_PreservesExitLock()
        {
            var source = Stub();
            var app = App(ScriptableObject.CreateInstance<PetConfig>(), source, out _);
            for (var i = 0; i < 7; i++) Frame(app, source);
            Assert.AreEqual(1, app.PetSnapshot.FeedCount);
            Invoke(app, "OnApplicationPause", true);
            Assert.IsFalse(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Den), Time.time));
            Invoke(app, "OnApplicationPause", false);
            for (var i = 0; i < 60; i++) Frame(app, source);
            Assert.AreEqual(1, app.PetSnapshot.FeedCount);
        }

        [Test]
        public void App_NewlyAcceptedFeedDoesNotConsumePreviousFrameTime()
        {
            var source = Stub();
            var app = App(ScriptableObject.CreateInstance<PetConfig>(), source, out _);
            for (var i = 0; i < 6; i++) Frame(app, source);
            Assert.AreEqual(PetBehaviorId.Eating, app.CurrentBehavior);
            Assert.AreEqual(0f, Get<BehaviorStateMachine>(app, "_behavior").ElapsedSeconds, 0.00001f);
        }

        [Test]
        public void SleepyApp_OnlySlowsIdleAndAllowsFullEatDuration()
        {
            var config = ScriptableObject.CreateInstance<PetConfig>();
            config.NeedSeeds[2].Initial = 0.1f;
            var source = Stub();
            var app = App(config, source, out var animator);
            Frame(app, source);
            Assert.AreEqual(0.75f, animator.speed, 0.0001f);
            for (var i = 0; i < 5; i++) Frame(app, source);
            Assert.AreEqual(PetBehaviorId.Eating, app.CurrentBehavior);
            Assert.AreEqual(Animator.StringToHash("Eat"), animator.LastStateHash);
            Assert.AreEqual(1f, animator.speed, 0.0001f);
            Frame(app, source, 3.19f);
            Assert.AreEqual(PetBehaviorId.Eating, app.CurrentBehavior);
            Assert.AreEqual(1f, animator.speed, 0.0001f);
            Frame(app, source, 0.02f);
            Assert.AreEqual(PetBehaviorId.Idle, app.CurrentBehavior);
            Assert.AreEqual(0.75f, animator.speed, 0.0001f);
        }

        [Test]
        public void GeneratedSmokeScene_AssignsBothTrackingProxies()
        {
            ProjectSetup.GenerateAll();
            var objects = EditorSceneManager.SavedObjects["Assets/_Project/Scenes/01_TrackingSmoke.unity"];
            var source = objects.Select(value => value.GetComponent<StubTrackingSource>()).First(value => value != null);
            Assert.IsTrue(source.IsRegistered(MarkerSlot.Den));
            Assert.IsTrue(source.IsRegistered(MarkerSlot.Food));
            Invoke(source, "Update");
            Time.time = 0.02f;
            Invoke(source, "Update");
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Den), Time.time));
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Food), Time.time));
        }

        [Test]
        public void Regeneration_PreservesBothExistingSceneFiles()
        {
            ProjectSetup.GenerateAll();
            const string main = "Assets/_Project/Scenes/02_Main.unity";
            const string smoke = "Assets/_Project/Scenes/01_TrackingSmoke.unity";
            File.WriteAllText(main, "hand-configured ARCamera and ImageTargets");
            File.WriteAllText(smoke, "hand-configured smoke scene");
            ProjectSetup.GenerateAll();
            Assert.AreEqual("hand-configured ARCamera and ImageTargets", File.ReadAllText(main));
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
            foreach (var field in new[] { "denTargetObject", "foodTargetObject" })
            {
                var target = new GameObject(field);
                target.AddComponent<ObserverBehaviour>().TargetStatus = new TargetStatus { Status = Status.TRACKED };
                Set(source, field, target);
            }
            _sources.Add(source);
            return source;
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
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Food), Time.time));
            source.PauseTracking("test");
            Assert.AreEqual(TrackingStatus.Paused, source.AllMarkers[1].Status);
            Time.time += 1;
            Invoke(source, "Update");
            Assert.AreEqual(TrackingStatus.Paused, source.GetSample(MarkerSlot.Food).Status);
            Assert.IsFalse(source.TryGetMarkerPose(MarkerSlot.Den, out _, out _));
        }

        [TestCase(Status.EXTENDED_TRACKED)]
        [TestCase(Status.LIMITED)]
        [TestCase((Status)999)]
        public void VuforiaEstimatedOrUnknownPose_CannotSettle(Status status)
        {
            var source = VuforiaSource();
            Get<GameObject>(source, "foodTargetObject").GetComponent<ObserverBehaviour>().TargetStatus = new TargetStatus { Status = status };
            VuforiaApplication.Instance.IsRunning = true;
            source.StartTracking();
            Invoke(source, "Update");
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Den), Time.time));
            Assert.IsFalse(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Food), Time.time));
        }

        [Test]
        public void VuforiaLateStartedCallback_CannotUndoPause()
        {
            var source = VuforiaSource();
            source.StartTracking();
            source.PauseTracking("background");
            VuforiaApplication.Instance.CompleteStart();
            Assert.IsFalse(source.IsRunning);
            Assert.AreEqual(TrackingStatus.Paused, source.GetSample(MarkerSlot.Den).Status);
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
            Assert.IsFalse(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Den), Time.time));
            Invoke(source, "Update");
            Assert.IsTrue(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Den), Time.time));
        }

        [Test]
        public void VuforiaDisabledObserver_CannotPublishTrackedPose()
        {
            var source = VuforiaSource();
            Get<GameObject>(source, "foodTargetObject").SetActive(false);
            VuforiaApplication.Instance.IsRunning = true;
            source.StartTracking();
            Invoke(source, "Update");
            Assert.IsFalse(PoseFreshness.IsSettleable(source.GetSample(MarkerSlot.Food), Time.time));
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
