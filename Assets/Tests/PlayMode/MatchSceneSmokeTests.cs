using System.Collections;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Arena.Tests.PlayMode
{
    /// <summary>
    /// Smoke test of the real Match scene in play mode: it loads, the match starts from the seeded data, runs ~3 s without a
    /// single error or exception in the log (the Test Framework fails the test on any logged error), fighters and the map
    /// exist. Run: Tools/test.ps1 -Platform PlayMode.
    /// </summary>
    public sealed class MatchSceneSmokeTests
    {
        private const float RunSeconds = 3f;
        private const int ExpectedFighters = 6;

        [UnityTest]
        public IEnumerator MatchScene_RunsThreeSeconds_WithoutErrors()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.Match, LoadSceneMode.Single);
            yield return null;

            var runner = Object.FindFirstObjectByType<MatchRunner>();
            Assert.IsNotNull(runner, "MatchRunner in the scene");
            Assert.IsNotNull(runner.Simulation, "the match started (see the log for the exception otherwise)");
            Assert.IsTrue(runner.enabled);

            float end = Time.realtimeSinceStartup + RunSeconds;
            while (Time.realtimeSinceStartup < end) yield return null;

            Assert.IsTrue(runner.enabled, "the runner did not stop on an exception");
            Assert.AreEqual(ExpectedFighters, runner.Simulation.FighterCount);
            Assert.Greater(runner.Simulation.State.Tick, (int)(RunSeconds * 30f * 0.5f), "ticks advanced at ~30 Hz");

            FighterView[] views = Object.FindObjectsByType<FighterView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(ExpectedFighters, views.Length, "one view per fighter");
            for (int i = 0; i < ExpectedFighters; i++)
                Assert.IsTrue(runner.Simulation.State.Fighters[i].Active, "fighter " + i);

            Assert.IsNotNull(runner.MapView, "map view built");
            Assert.Greater(runner.MapView.BushCount, 0);
            Assert.Greater(runner.MapView.BreakableCount, 0);
        }
    }
}
