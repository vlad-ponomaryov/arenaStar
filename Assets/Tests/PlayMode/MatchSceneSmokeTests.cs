using System.Collections;
using Arena.Core.Math;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
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
    /// exist, the bots of the other 5 slots move. Run: Tools/test.ps1 -Platform PlayMode.
    /// </summary>
    public sealed class MatchSceneSmokeTests
    {
        private const float RunSeconds = 3f;
        private const int ExpectedFighters = 6;
        private const float EnemyDistance = 1.5f;
        private const float UltWaitSeconds = 2f;

        [UnityTest]
        public IEnumerator MatchScene_RunsThreeSeconds_WithoutErrors()
        {
            SceneFlow.ClearLaunchParams();
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

            Assert.IsNotNull(runner.Bots, "bots created");
            Assert.AreEqual(ExpectedFighters - 1, runner.Bots.BotCount, "every slot except the local one is a bot");
            int moved = 0;
            for (int i = 0; i < ExpectedFighters; i++)
            {
                if (i == runner.LocalSlot) continue;
                Vector2 spawn = new Vector2(runner.Simulation.Setup.Slots[i].SpawnPosition.X, runner.Simulation.Setup.Slots[i].SpawnPosition.Y);
                Vector2 now = new Vector2(runner.Simulation.State.Fighters[i].Position.X, runner.Simulation.State.Fighters[i].Position.Y);
                if (Vector2.Distance(spawn, now) > 1f) moved++;
            }
            Assert.Greater(moved, 0, "bots walk away from their spawns");

            Assert.IsNotNull(runner.MapView, "map view built");
            Assert.Greater(runner.MapView.BushCount, 0);
            Assert.Greater(runner.MapView.BreakableCount, 0);

            Assert.IsNotNull(runner.Feedback, "hit feedback created (S2.10)");
            Assert.Greater(runner.DamageNumbers.Capacity, 0, "damage number pool prewarmed");
            Assert.IsNotNull(runner.Telegraphs);
        }

        [UnityTest]
        public IEnumerator UltHit_OfTheLocalFighter_GivesHitStop_AndDamageNumbers()
        {
            SceneFlow.ClearLaunchParams();
            yield return SceneManager.LoadSceneAsync(SceneNames.Match, LoadSceneMode.Single);
            yield return null;
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            Assert.IsNotNull(runner.Simulation);

            // An enemy right in front of the local fighter, the ult charged and fired straight at it.
            MatchState state = runner.Simulation.State;
            int local = runner.LocalSlot;
            int enemy = -1;
            for (int i = 0; i < runner.Simulation.FighterCount; i++)
                if (state.Fighters[i].Team != runner.LocalTeam) { enemy = i; break; }
            Assert.GreaterOrEqual(enemy, 0);
            state.Fighters[local].UltCharge = UltSystem.Required(runner.Simulation.Defs, in state.Fighters[local]);
            state.Fighters[enemy].Position = state.Fighters[local].Position + new Vec2(0f, EnemyDistance);
            runner.InputCollector.PressUltSim(Vec2.Up, false);

            int hitsBefore = runner.Feedback.UltHitsFelt;
            float end = Time.realtimeSinceStartup + UltWaitSeconds;
            while (Time.realtimeSinceStartup < end && runner.Feedback.UltHitsFelt == hitsBefore)
            {
                state.Fighters[enemy].Position = state.Fighters[local].Position + new Vec2(0f, EnemyDistance);
                yield return null;
            }
            bool sawStop = runner.HitStop.IsActive || runner.Shake.IsActive;
            Assert.Greater(runner.Feedback.UltHitsFelt, hitsBefore, "the ult hit the enemy");
            Assert.IsTrue(sawStop, "hit stop / shake right after the ult hit");
            Assert.Greater(runner.DamageNumbers.ActiveCount, 0, "damage numbers shown");
            Assert.IsTrue(runner.enabled, "no exception");
        }
    }
}
