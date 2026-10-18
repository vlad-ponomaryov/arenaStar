using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Acorns mode rules (GDD §5.4, D-025): spawns, golden acorn, pickup, drop on defeat, countdown, time limit, end.</summary>
    public sealed class AcornsRulesTests
    {
        private static void Run(MatchSimulation sim, int ticks) => TestMatchFactory.Run(sim, TestMatchFactory.Inputs(), ticks);

        [Test]
        public void Acorn_FallsEvery180Ticks_AroundTheHollow()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            Run(sim, AcornsTestFactory.SpawnInterval - 1);
            Assert.AreEqual(0, AcornsTestFactory.ActivePickups(sim.State, out _));

            Run(sim, 1);
            Assert.AreEqual(1, AcornsTestFactory.ActivePickups(sim.State, out int value));
            Assert.AreEqual(1, value);

            Run(sim, AcornsTestFactory.SpawnInterval * 4);
            Assert.AreEqual(5, AcornsTestFactory.ActivePickups(sim.State, out _));
            Vec2 hollow = AcornsTestFactory.HollowCell.Center;
            for (int i = 0; i < sim.State.Pickups.Length; i++)
            {
                if (!sim.State.Pickups[i].Active) continue;
                Assert.LessOrEqual(Vec2.Distance(sim.State.Pickups[i].Position, hollow), AcornsTestFactory.SpawnScatter + 1e-4f);
            }
        }

        [Test]
        public void GoldenAcorn_At60Seconds_InAddition_WorthThree()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            Run(sim, AcornsTestFactory.GoldenInterval - 1);
            int normal = AcornsTestFactory.ActivePickups(sim.State, out int before);
            Assert.AreEqual(before, normal, "no golden acorn yet");

            Run(sim, 1);
            int count = AcornsTestFactory.ActivePickups(sim.State, out int after);
            Assert.AreEqual(AcornsTestFactory.GoldenInterval / AcornsTestFactory.SpawnInterval + 1, count, "10 normal + 1 golden");
            Assert.AreEqual(count - 1 + AcornsTestFactory.GoldenValue, after);
        }

        [Test]
        public void Pickup_ByTouch_AddsToCarriedAndStats()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            Vec2 p = sim.State.Fighters[0].Position;
            float reach = TestMatchFactory.Radius + AcornsTestFactory.PickupRadius;
            AcornsTestFactory.Place(sim.State, 0, p + new Vec2(reach - 0.05f, 0f), 1);
            AcornsTestFactory.Place(sim.State, 1, p + new Vec2(0f, reach + 0.05f), 3);

            Run(sim, 1);
            Assert.AreEqual(1, sim.State.Fighters[0].CarriedAcorns);
            Assert.AreEqual(1, sim.State.Stats[0].AcornsCollected);
            Assert.IsFalse(sim.State.Pickups[0].Active);
            Assert.IsTrue(sim.State.Pickups[1].Active, "out of reach");
            Assert.AreEqual(1, AcornsRules.TeamAcorns(sim.State, 0));
        }

        [Test]
        public void Pickup_WaitsForPickableTick()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            AcornsTestFactory.Place(sim.State, 0, sim.State.Fighters[0].Position, 1, 5);
            Run(sim, 4);
            Assert.AreEqual(0, sim.State.Fighters[0].CarriedAcorns);
            Run(sim, 1);
            Assert.AreEqual(1, sim.State.Fighters[0].CarriedAcorns);
        }

        [Test]
        public void Defeat_DropsAllAcorns_AsNormalOnes_WithPickupDelay()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            Run(sim, 1);
            sim.State.Fighters[0].CarriedAcorns = 5; // e.g. a golden (3) + 2 normal
            Vec2 at = sim.State.Fighters[0].Position;

            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 1, 0, 100000);

            Assert.IsFalse(sim.State.Fighters[0].Alive);
            Assert.AreEqual(0, sim.State.Fighters[0].CarriedAcorns);
            Assert.AreEqual(5, AcornsTestFactory.ActivePickups(sim.State, out int value));
            Assert.AreEqual(5, value, "each dropped acorn is worth 1");
            int expectedPickable = sim.State.Tick + 1 + AcornsTestFactory.PickupDelay;
            for (int i = 0; i < sim.State.Pickups.Length; i++)
            {
                ref PickupState p = ref sim.State.Pickups[i];
                if (!p.Active) continue;
                Assert.AreEqual(1, p.Value);
                Assert.AreEqual(expectedPickable, p.PickableFromTick);
                Assert.LessOrEqual(Vec2.Distance(p.Position, at), AcornsTestFactory.DropScatter + 1e-4f);
            }
            Assert.AreEqual(0, AcornsRules.TeamAcorns(sim.State, 0));
        }

        [Test]
        public void TwelveAcorns_StartCountdown_FallingBelowResetsIt()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            sim.State.Fighters[0].CarriedAcorns = AcornsTestFactory.Target;
            Run(sim, 1);
            Assert.AreEqual(0, sim.State.Mode.CountdownTeam);
            Assert.AreEqual(1 + AcornsTestFactory.Hold, sim.State.Mode.CountdownEndTick);
            Assert.AreEqual(AcornsTestFactory.Hold, AcornsRules.CountdownTicksLeft(sim.State));

            sim.State.Fighters[0].CarriedAcorns = AcornsTestFactory.Target - 1;
            Run(sim, 1);
            Assert.AreEqual(AcornsRules.NoTeam, sim.State.Mode.CountdownTeam);
            Assert.AreEqual(0, AcornsRules.CountdownTicksLeft(sim.State));
        }

        [Test]
        public void Countdown_360Ticks_Wins()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            sim.State.Fighters[1].CarriedAcorns = AcornsTestFactory.Target;
            Run(sim, AcornsTestFactory.Hold);
            Assert.IsFalse(sim.IsEnded, "first tick starts the countdown, 360 more are needed");
            Run(sim, 1);
            Assert.IsTrue(sim.IsEnded);
            Assert.AreEqual(1, sim.State.Mode.WinnerTeam);
            Assert.AreEqual(AcornsTestFactory.Hold + 1, sim.State.Mode.EndTick);
        }

        [Test]
        public void EqualTwelvePlus_NoCountdown_AndOvertakingRestartsIt()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            sim.State.Fighters[0].CarriedAcorns = AcornsTestFactory.Target;
            sim.State.Fighters[1].CarriedAcorns = AcornsTestFactory.Target;
            Run(sim, 10);
            Assert.AreEqual(AcornsRules.NoTeam, sim.State.Mode.CountdownTeam, "equal 12+ resets (D-025)");

            sim.State.Fighters[0].CarriedAcorns = AcornsTestFactory.Target + 1;
            Run(sim, 100);
            Assert.AreEqual(0, sim.State.Mode.CountdownTeam);

            sim.State.Fighters[1].CarriedAcorns = AcornsTestFactory.Target + 2;
            Run(sim, 1);
            Assert.AreEqual(1, sim.State.Mode.CountdownTeam);
            Assert.AreEqual(sim.State.Tick + AcornsTestFactory.Hold, sim.State.Mode.CountdownEndTick, "leader changed: full countdown");
        }

        [Test]
        public void TimeUp_MoreAcornsWins()
        {
            const int duration = 100;
            MatchSimulation sim = AcornsTestFactory.Create(duration);
            sim.State.Fighters[0].CarriedAcorns = 3;
            sim.State.Fighters[1].CarriedAcorns = 2;
            Run(sim, duration - 1);
            Assert.IsFalse(sim.IsEnded);
            Assert.AreEqual(1, AcornsRules.TimeLeftTicks(sim.State, sim.Defs));
            Run(sim, 1);
            Assert.IsTrue(sim.IsEnded);
            Assert.AreEqual(0, sim.State.Mode.WinnerTeam);
            Assert.AreEqual(0, AcornsRules.TimeLeftTicks(sim.State, sim.Defs));
        }

        [Test]
        public void TimeUp_Equal_IsDraw()
        {
            const int duration = 100;
            MatchSimulation sim = AcornsTestFactory.Create(duration);
            sim.State.Fighters[0].CarriedAcorns = 4;
            sim.State.Fighters[1].CarriedAcorns = 4;
            Run(sim, duration);
            Assert.IsTrue(sim.IsEnded);
            Assert.AreEqual(AcornsRules.NoTeam, sim.State.Mode.WinnerTeam);
        }

        [Test]
        public void AfterEnd_InputIsIgnored_AndStateStandsStill()
        {
            const int duration = 30;
            MatchSimulation sim = AcornsTestFactory.Create(duration);
            Run(sim, duration);
            Assert.IsTrue(sim.IsEnded);

            var snapshot = new MatchState();
            snapshot.CopyFrom(sim.State);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            for (int i = 0; i < 2; i++) inputs[i] = new PlayerInput(new Vec2(1f, 0f), new Vec2(0f, 1f), InputButtons.Attack | InputButtons.Trick);
            TestMatchFactory.Run(sim, inputs, 60);

            Assert.IsTrue(sim.State.ContentEquals(snapshot), "nothing changes after the end");
            Assert.AreEqual(0, sim.Events.Count);
        }

        [Test]
        public void ModeNone_NoAcorns_NoEnd()
        {
            MatchSimulation sim = TestMatchFactory.CreateWith(TestMatchFactory.CreateDefs(), TestMatchFactory.Slot(0, 0, 5f, 5f));
            Run(sim, 600);
            Assert.AreEqual(0, AcornsTestFactory.ActivePickups(sim.State, out _));
            Assert.IsFalse(sim.IsEnded);
        }

        [Test]
        public void AcornsMatch_IsDeterministic_AndSnapshotRestores()
        {
            MatchSimulation a = AcornsTestFactory.Create();
            MatchSimulation b = AcornsTestFactory.Create();
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            var rng = DeterministicRng.FromSeed(9);
            for (int t = 0; t < 2000; t++)
            {
                TestMatchFactory.FillRandomInputs(ref rng, inputs, 2);
                a.Tick(inputs);
                b.Tick(inputs);
            }
            Assert.IsTrue(a.State.ContentEquals(b.State));
            Assert.Greater(AcornsTestFactory.ActivePickups(a.State, out _) + a.State.Fighters[0].CarriedAcorns
                + a.State.Fighters[1].CarriedAcorns, 0);
        }
    }
}
