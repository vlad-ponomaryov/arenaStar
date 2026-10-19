using Arena.Bots;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Editor.Art;
using Arena.Editor.Data;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Input;
using Arena.Gameplay.Match;
using Arena.Gameplay.Settings;
using Arena.UI.Hud;
using Arena.UI.Style;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.UI
{
    /// <summary>Match menu (D-129): pause button, Resume / Settings / Leave with a confirmation, Back key layers, offline pause.</summary>
    public sealed class PauseMenuTests
    {
        private GameObject _root;
        private int _pauses;
        private int _resumes;
        private int _leaves;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PauseMenuTestRoot");
            _pauses = _resumes = _leaves = 0;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private PauseMenu Build(bool online)
        {
            var style = AssetDatabase.LoadAssetAtPath<UiStyle>(ViewAssetBuilder.UiStylePath);
            Assert.IsNotNull(style, ViewAssetBuilder.UiStylePath);
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            var hud = new GameObject("HudCanvas", typeof(RectTransform));
            hud.transform.SetParent(_root.transform, false);
            return PauseMenu.Build(hud.transform, _root.transform, style, online, db != null ? db.Feel : null,
                p => { if (p) _pauses++; else _resumes++; }, () => _leaves++);
        }

        [Test]
        public void PauseButton_SitsInTheTopRightCorner_PanelStartsHidden()
        {
            PauseMenu menu = Build(false);
            var rt = (RectTransform)menu.PauseButton.transform;
            Assert.AreEqual(Vector2.one, rt.anchorMin, "top-right corner, away from the sticks at the bottom");
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.Less(rt.anchoredPosition.x, 0f);
            Assert.Less(rt.anchoredPosition.y, 0f);
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(Loc.Get(LocKeys.PauseTitle), menu.Title.text);
        }

        [Test]
        public void PauseButton_Opens_Resume_Closes_AndPausesOffline()
        {
            PauseMenu menu = Build(false);
            menu.PauseButton.onClick.Invoke();
            Assert.IsTrue(menu.IsOpen);
            Assert.AreEqual(1, _pauses);

            menu.ResumeButton.onClick.Invoke();
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(1, _resumes);
            Assert.AreEqual(0, _leaves);
        }

        [Test]
        public void Leave_AsksFirst_Stay_GoesBack_Leave_LeavesOnce()
        {
            PauseMenu menu = Build(false);
            menu.Open();
            menu.LeaveButton.onClick.Invoke();
            Assert.IsTrue(menu.IsConfirming, "no leaving without the confirmation");
            Assert.AreEqual(0, _leaves);

            menu.LeaveNoButton.onClick.Invoke();
            Assert.IsFalse(menu.IsConfirming);
            Assert.IsTrue(menu.IsOpen);

            menu.LeaveButton.onClick.Invoke();
            menu.LeaveYesButton.onClick.Invoke();
            menu.LeaveYesButton.onClick.Invoke();
            Assert.AreEqual(1, _leaves);
        }

        [Test]
        public void BackKey_ClosesTheInnermostLayer_ThenOpensAgain()
        {
            PauseMenu menu = Build(false);
            menu.Back();
            Assert.IsTrue(menu.IsOpen, "Back opens the menu during the match");

            menu.SettingsButton.onClick.Invoke();
            Assert.IsTrue(menu.IsSettingsOpen);
            menu.Back();
            Assert.IsFalse(menu.IsSettingsOpen, "Back closes the settings first");
            Assert.IsTrue(menu.IsOpen);

            menu.AskLeave();
            menu.Back();
            Assert.IsFalse(menu.IsConfirming, "Back answers the question with Stay");
            Assert.IsTrue(menu.IsOpen);

            menu.Back();
            Assert.IsFalse(menu.IsOpen, "Back resumes");
            Assert.AreEqual(1, _pauses);
            Assert.AreEqual(1, _resumes);
            Assert.AreEqual(0, _leaves);
        }

        [Test]
        public void Online_TitleIsMenu_WithBattleGoesOnHint()
        {
            PauseMenu menu = Build(true);
            Assert.AreEqual(Loc.Get(LocKeys.PauseMenuTitle), menu.Title.text);
            bool hint = false;
            foreach (var t in _root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (t.text == Loc.Get(LocKeys.PauseOnlineHint)) hint = true;
            Assert.IsTrue(hint, "online: no pause, the panel says the battle goes on");
        }

        [Test]
        public void OfflineDriver_Paused_DoesNotTick_AndResumesWhereItStopped()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db, "seeded database");
            var driver = new OfflineMatchDriver(db, new MatchLaunchParams("bruno", BotDifficulty.Easy, 3),
                GameSettings.FromDefaults(db.Feel));
            var input = new LocalInputCollector();
            int ticks = driver.BeginFrame(0.1f);
            for (int i = 0; i < ticks; i++) driver.Step(input);
            int tick = driver.State.Globals.Tick;
            float alpha = driver.Alpha;

            driver.Paused = true;
            Assert.AreEqual(0, driver.BeginFrame(5f), "no ticks while the menu is open");
            Assert.AreEqual(tick, driver.State.Globals.Tick);
            Assert.AreEqual(alpha, driver.Alpha, 1e-6f, "the views stand still");

            driver.Paused = false;
            Assert.Greater(driver.BeginFrame(0.1f), 0, "runs again after Resume (no backlog from the pause)");
        }
    }
}
