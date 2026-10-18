using Arena.Editor.Build;
using Arena.Gameplay.Cameras;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Modes;
using Arena.Online;
using Arena.UI.Hud;
using Arena.UI.Menu;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Arena.Tests.EditMode.Setup
{
    /// <summary>The generated scenes have their bootstrap objects and every serialized reference set (S2.2).</summary>
    public sealed class SceneIntegrityTests
    {
        private Scene _scene;

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.CloseScene(_scene, true);
            _scene = default;
        }

        private Scene Open(string sceneName)
        {
            // Additive: the current (untitled) scene stays, the opened one is closed in TearDown.
            _scene = EditorSceneManager.OpenScene(SceneBuilder.ScenePath(sceneName), OpenSceneMode.Additive);
            Assert.IsTrue(_scene.IsValid(), sceneName + " opened");
            return _scene;
        }

        private static T FindSingle<T>(Scene scene) where T : Component
        {
            T found = null;
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T c in root.GetComponentsInChildren<T>(true))
                {
                    found = c;
                    count++;
                }
            }
            Assert.AreEqual(1, count, "exactly one " + typeof(T).Name + " in " + scene.name);
            return found;
        }

        private static void AssertAllReferencesSet(Object component)
        {
            var so = new SerializedObject(component);
            SerializedProperty p = so.GetIterator();
            int checkedRefs = 0;
            while (p.NextVisible(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference || p.name == "m_Script") continue;
                Assert.IsNotNull(p.objectReferenceValue, component.GetType().Name + "." + p.propertyPath + " is null");
                checkedRefs++;
            }
            Assert.Greater(checkedRefs, 0, component.GetType().Name + " has serialized references");
        }

        [Test]
        public void Match_RunnerHudAndCamera_HaveAllReferences()
        {
            Scene scene = Open(SceneNames.Match);

            MatchRunner runner = FindSingle<MatchRunner>(scene);
            AssertAllReferencesSet(runner);
            Assert.IsNotNull(runner.Database.Camera, "GameDatabase.Camera");
            Assert.IsNotNull(runner.Database.MatchRunner, "GameDatabase.MatchRunner");
            Assert.IsNotNull(runner.ViewCatalog.FighterViewPrefab);
            Assert.IsNotNull(runner.ViewCatalog.AimIndicatorPrefab);
            Assert.IsNotNull(runner.ViewCatalog.ProjectilePrefab);
            Assert.IsNotNull(runner.ViewCatalog.HealthBarPrefab);

            MatchHud hud = FindSingle<MatchHud>(scene);
            AssertAllReferencesSet(hud);
            Assert.AreSame(runner, hud.Runner);
            Assert.IsNotNull(hud.Style.CircleSprite);

            MatchCameraController cam = FindSingle<MatchCameraController>(scene);
            Assert.AreSame(cam, runner.CameraController);
            Assert.IsNotNull(cam.GetComponent<Camera>());
        }

        [Test]
        public void Match_StartMatch_CreatesViews_AndHudBuilds()
        {
            Scene scene = Open(SceneNames.Match);
            MatchRunner runner = FindSingle<MatchRunner>(scene);
            runner.StartMatch();

            Assert.IsNotNull(runner.Simulation);
            Assert.AreEqual(6, runner.Simulation.FighterCount);
            Assert.AreEqual(0, runner.LocalTeam);
            Assert.IsFalse(runner.InputCollector.ViewRotated);
            Assert.AreEqual(6, runner.GetComponentsInChildren<Arena.Gameplay.View.FighterView>(true).Length);
            Assert.AreEqual(1, runner.GetComponentsInChildren<Arena.Gameplay.View.AimIndicatorView>(true).Length);
            Assert.AreEqual(6, runner.GetComponentsInChildren<Arena.Gameplay.View.HealthBarView>(true).Length);
            int pooled = 0;
            foreach (Transform child in runner.transform) if (child.name == "Projectile") pooled++;
            Assert.AreEqual(runner.ViewCatalog.ProjectilePoolPrewarm, pooled, "projectile views prewarmed at match start");

            Transform cam = runner.CameraController.transform;
            Assert.Greater(cam.position.y, 0f, "camera above the ground");
            Assert.Less(cam.position.z, runner.Simulation.State.Fighters[runner.LocalSlot].Position.Y + 2f, "camera behind the player");

            MatchHud hud = FindSingle<MatchHud>(scene);
            MatchHudControls controls = MatchHudBuilder.Build(hud.transform, hud.Style);
            Assert.IsNotNull(controls.Canvas);
            Assert.IsNotNull(controls.MoveStick);
            Assert.AreEqual(Arena.UI.Controls.StickMode.Floating, controls.MoveStick.Mode);
            Assert.AreEqual(Arena.UI.Controls.StickMode.Fixed, controls.AttackStick.Mode);
            Assert.IsNotNull(controls.UltStick);
            Assert.IsNotNull(controls.TrickButton);
            Assert.AreEqual(Vector2.zero, controls.AttackStick.Aim);
        }

        [Test]
        public void Match_AcornsModeView_ReferencesTheRunner()
        {
            Scene scene = Open(SceneNames.Match);
            AcornsModeView view = FindSingle<AcornsModeView>(scene);
            AssertAllReferencesSet(view);
            Assert.AreSame(FindSingle<MatchRunner>(scene), view.Runner);
            Assert.IsNotNull(view.Runner.Database.AcornsMode, "GameDatabase.AcornsMode");
            Assert.IsNotNull(view.Runner.Database.Strings, "GameDatabase.Strings");
        }

        [Test]
        public void Menu_MenuScreen_HasDatabaseAndStyle()
        {
            Scene scene = Open(SceneNames.Menu);
            MenuScreen menu = FindSingle<MenuScreen>(scene);
            AssertAllReferencesSet(menu);
            Assert.IsNotNull(menu.Database.Strings);
            Assert.Greater(menu.Database.Fighters.Count, 0);
        }

        [Test]
        public void Boot_OnlineSession_HasTheDatabaseWithNetConfig()
        {
            Scene scene = Open(SceneNames.Boot);
            FindSingle<BootLoader>(scene);
            OnlineSession online = FindSingle<OnlineSession>(scene);
            AssertAllReferencesSet(online);
            var so = new SerializedObject(online);
            var db = (Arena.Data.GameDatabase)so.FindProperty("_database").objectReferenceValue;
            Assert.IsNotNull(db.Net, "GameDatabase.Net");
            Assert.IsNotNull(db.Net.ContentPack, "NetConfig.ContentPack");
        }

        [Test]
        public void Match_OnlineMatchBootstrap_ReferencesTheRunner()
        {
            Scene scene = Open(SceneNames.Match);
            OnlineMatchBootstrap bootstrap = FindSingle<OnlineMatchBootstrap>(scene);
            AssertAllReferencesSet(bootstrap);
            var so = new SerializedObject(bootstrap);
            Assert.AreSame(FindSingle<MatchRunner>(scene), so.FindProperty("_runner").objectReferenceValue);
        }

        [Test]
        public void Match_HasEventSystemWithInputSystemModule()
        {
            Scene scene = Open(SceneNames.Match);
            EventSystem es = FindSingle<EventSystem>(scene);
            Assert.IsNotNull(es.GetComponent<InputSystemUIInputModule>());
        }

        [Test]
        public void Menu_HasEventSystemWithInputSystemModule()
        {
            Scene scene = Open(SceneNames.Menu);
            EventSystem es = FindSingle<EventSystem>(scene);
            Assert.IsNotNull(es.GetComponent<InputSystemUIInputModule>());
        }
    }
}
