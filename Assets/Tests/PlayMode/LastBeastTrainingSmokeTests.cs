using System.Collections;
using Arena.Bots;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Modes;
using Arena.UI.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Arena.Tests.PlayMode
{
    /// <summary>
    /// S3.14 (Server-Spec §9.2): offline "Training" in «Last Beast» — the Match scene with the mode chosen in the menu runs 10 s
    /// without an error in the log: 1 player + 9 bots on the royale map, berry boxes on the map, the mode view on (the Acorns view
    /// off), the Last Beast HUD panel shown instead of the score bar, the camera follows along both axes and is not rotated.
    /// </summary>
    public sealed class LastBeastTrainingSmokeTests
    {
        private const float RunSeconds = 10f;

        [UnityTest]
        public IEnumerator LastBeastTraining_RunsTenSeconds_WithoutErrors()
        {
            SceneFlow.PlayMatch(new MatchLaunchParams("pix", BotDifficulty.Novice, 11, false, ModeKind.LastBeast));
            yield return null;
            yield return null;
            Assert.AreEqual(SceneNames.Match, SceneManager.GetActiveScene().name);

            var runner = Object.FindFirstObjectByType<MatchRunner>();
            Assert.IsNotNull(runner, "MatchRunner in the scene");
            Assert.IsNotNull(runner.Simulation, "the match started (see the log for the exception otherwise)");
            Assert.AreEqual(ModeKind.LastBeast, runner.Driver.Defs.Mode);
            Assert.AreEqual(SimLimits.MaxFighters, runner.Simulation.FighterCount, "10 fighters");
            Assert.AreEqual(SimLimits.MaxFighters - 1, runner.Bots.BotCount, "9 bots");
            Assert.Greater(runner.MapView.BoxCount, 0, "berry boxes on the map");
            Assert.IsTrue(runner.CameraController.FollowsBothAxes, "the camera follows along both axes");
            Assert.IsFalse(runner.CameraController.Rotated);

            float end = Time.realtimeSinceStartup + RunSeconds;
            while (Time.realtimeSinceStartup < end) yield return null;

            Assert.IsTrue(runner.enabled, "the runner did not stop on an exception");
            Assert.Greater(runner.Simulation.State.Tick, (int)(RunSeconds * SimLimits.TickRate * 0.5f), "ticks advanced");
            var modeView = Object.FindFirstObjectByType<LastBeastModeView>(FindObjectsInactive.Include);
            Assert.IsNotNull(modeView, "Last Beast view in the scene");
            Assert.IsTrue(modeView.enabled, "Last Beast view is on");
            var acorns = Object.FindFirstObjectByType<AcornsModeView>(FindObjectsInactive.Include);
            Assert.IsFalse(acorns.enabled, "Acorns view switched itself off");
            var hud = Object.FindFirstObjectByType<MatchHud>();
            Assert.IsTrue(hud.LastBeast.Root.activeSelf, "Last Beast HUD panel shown");
            Assert.IsFalse(hud.ScoreBar.Root.activeSelf, "no Acorns score bar");
            Assert.IsTrue(hud.LastBeast.FogTimerShown, "the fog timer counts down to the first step");
            Assert.That(int.Parse(hud.LastBeast.AliveText.text), Is.InRange(2, SimLimits.MaxFighters), "fighters left");
            SceneFlow.ClearLaunchParams();
        }
    }
}
