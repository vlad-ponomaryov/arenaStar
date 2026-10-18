using System;
using System.Collections;
using Arena.Data.Localization;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.UI.Hud;
using Arena.UI.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Arena.Tests.PlayMode
{
    /// <summary>
    /// The scene flow of S2.7 in play mode: Menu (choose Bruno, press Play) → Match with the chosen fighter and the Acorns
    /// score bar → the local team gets 12 acorns, the hold countdown is shortened, the match ends → results screen
    /// (victory, MVP) → "Play again" restarts the match with the same parameters → "Menu" returns to the menu with the
    /// choice kept. Any logged error fails the test.
    /// </summary>
    public sealed class SceneFlowSmokeTests
    {
        private const float StepTimeoutSeconds = 15f;

        [UnityTest]
        public IEnumerator MenuToMatchToResults_PlayAgain_ThenMenu()
        {
            SceneFlow.ClearLaunchParams();
            yield return SceneManager.LoadSceneAsync(SceneNames.Menu, LoadSceneMode.Single);
            yield return null;

            MenuScreen menu = Object.FindFirstObjectByType<MenuScreen>();
            Assert.IsNotNull(menu, "MenuScreen in the Menu scene");
            Assert.IsNotNull(menu.TrainingButton, "menu built");
            Assert.IsNotNull(menu.SettingsButton, "settings button (S2.10)");
            menu.SettingsButton.onClick.Invoke();
            Assert.IsTrue(menu.Settings.IsShown, "settings panel opens");
            menu.Settings.CloseButton.onClick.Invoke();
            Assert.IsFalse(menu.Settings.IsShown, "settings panel closes");
            menu.SelectFighter("bruno");
            menu.TrainingButton.onClick.Invoke();

            MatchRunner runner = null;
            yield return WaitFor(() => (runner = FindStartedRunner()) != null, "match started from the menu");
            Assert.AreEqual("bruno", runner.Simulation.Defs.Fighters[runner.Simulation.State.Fighters[runner.LocalSlot].DefIndex].Id);
            Assert.IsNotNull(runner.LaunchParams);
            int seed = runner.LaunchParams.Seed;

            MatchHud hud = Object.FindFirstObjectByType<MatchHud>();
            yield return WaitFor(() => hud.ScoreBar != null && hud.ScoreBar.Root.activeSelf, "score bar shown");

            yield return EndMatchWithLocalVictory(runner, hud);
            Assert.AreEqual(Loc.Get(LocKeys.ResultsVictory), hud.Results.TitleText.text);
            Assert.GreaterOrEqual(hud.Results.MvpSlot, 0, "MVP chosen");
            Assert.IsTrue(hud.Results.AgainButton.gameObject.activeInHierarchy, "Play again visible at once");
            Assert.IsTrue(hud.Results.MenuButton.gameObject.activeInHierarchy, "Menu visible at once");

            hud.Results.AgainButton.onClick.Invoke();
            MatchRunner old = runner;
            yield return WaitFor(() => old == null && (runner = FindStartedRunner()) != null, "match restarted");
            Assert.IsFalse(runner.IsMatchEnded);
            Assert.AreEqual(seed, runner.LaunchParams.Seed, "same launch parameters");
            Assert.AreEqual("bruno", runner.LaunchParams.FighterId);

            hud = Object.FindFirstObjectByType<MatchHud>();
            yield return EndMatchWithLocalVictory(runner, hud);
            hud.Results.MenuButton.onClick.Invoke();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneNames.Menu
                && (menu = Object.FindFirstObjectByType<MenuScreen>()) != null && menu.TrainingButton != null, "back in the menu");
            Assert.AreEqual("bruno", menu.SelectedFighterId, "choice kept");
        }

        private static MatchRunner FindStartedRunner()
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Match) return null;
            MatchRunner r = Object.FindFirstObjectByType<MatchRunner>();
            return r != null && r.Simulation != null ? r : null;
        }

        private static IEnumerator EndMatchWithLocalVictory(MatchRunner runner, MatchHud hud)
        {
            var state = runner.Simulation.State;
            state.Fighters[runner.LocalSlot].CarriedAcorns = runner.Simulation.Defs.Acorns.TargetAcorns;
            yield return WaitFor(() => state.Mode.CountdownTeam == runner.LocalTeam, "hold countdown started");
            yield return null;
            Assert.IsTrue(hud.ScoreBar.CountdownShown, "countdown on the score bar");
            state.Mode.CountdownEndTick = state.Tick + 1;
            yield return WaitFor(() => runner.IsMatchEnded && hud.Results.IsShown, "results shown");
            Assert.AreEqual(runner.LocalTeam, state.Mode.WinnerTeam);
        }

        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            float end = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("timed out: " + what);
                yield return null;
            }
        }
    }
}
