using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;
using N = Arena.Tests.EditMode.Core.ScoutFighterTestFactory;
using S = Arena.Tests.EditMode.Core.SupportFighterTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Prediction mode of the simulation (Server-Spec §2.5 rules 1–6, D-080, S3.7a): off by default and after <c>Clear()</c>; only
    /// the predicted fighter acts and the other fighters stay bit for bit; the match RNG, regeneration and mode rules stand still;
    /// effects apply only from the own fighter to itself and never as damage or healing; hooks do not pull, mines do not go off;
    /// own projectiles stop on shown enemies without damage; visibility is recomputed only for the own fighter; only the own
    /// fighter respawns; the shadow reproduces the full simulation for the own fighter on the same inputs.
    /// </summary>
    public sealed class PredictionModeTests
    {
        private static void StartPredicting(MatchSimulation sim, int slot)
        {
            sim.State.Globals.Predicting = true;
            sim.State.Globals.PredictedSlot = slot;
        }

        private static int CountEvents(MatchSimulation sim, SimEventType type) => sim.Events.CountOf(type);

        [Test]
        public void Default_IsOff_InNewStateAndAfterClearAndReset()
        {
            var state = new MatchState();
            Assert.IsFalse(state.Globals.Predicting);
            Assert.IsTrue(state.Globals.Simulates(0) && state.Globals.Simulates(5), "every slot acts without prediction");

            MatchSimulation sim = F.CreateSingle();
            StartPredicting(sim, 0);
            sim.Reset();
            Assert.IsFalse(sim.State.Globals.Predicting, "Reset clears the flag");
            StartPredicting(sim, 0);
            sim.State.Clear();
            Assert.IsFalse(sim.State.Globals.Predicting, "Clear clears the flag");
        }

        [Test]
        public void PredictingOff_WithAnySlotNumber_IsTheNormalSimulation()
        {
            var a = new MatchSimulation(F.CreateDefs(), F.CreateSetup(4, seed: 9));
            var b = new MatchSimulation(F.CreateDefs(), F.CreateSetup(4, seed: 9));
            b.State.Globals.PredictedSlot = 2; // not predicting: the slot number means nothing
            var inputs = F.Inputs();
            DeterministicRng rng = DeterministicRng.FromSeed(5);
            for (int t = 0; t < 300; t++)
            {
                F.FillRandomInputs(ref rng, inputs, 4);
                a.Tick(inputs);
                b.Tick(inputs);
            }
            b.State.Globals.PredictedSlot = 0;
            Assert.IsTrue(a.State.ContentEquals(b.State));
        }

        [Test]
        public void OnlyTheOwnFighterActs_OthersStayBitForBit_RngModeAndStatsUntouched()
        {
            var sim = new MatchSimulation(F.CreateDefs(), F.CreateSetup(4, seed: 3));
            var inputs = F.Inputs();
            DeterministicRng rng = DeterministicRng.FromSeed(11);
            for (int t = 0; t < 30; t++)
            {
                F.FillRandomInputs(ref rng, inputs, 4);
                sim.Tick(inputs); // some history: moved fighters, spent charges
            }
            var before = new MatchState();
            before.CopyFrom(sim.State);
            StartPredicting(sim, 0);
            for (int t = 0; t < 150; t++)
            {
                F.FillRandomInputs(ref rng, inputs, 4);
                sim.Tick(inputs);
                for (int i = 1; i < 4; i++)
                    Assert.IsTrue(before.Fighters[i].Equals(sim.State.Fighters[i]), "fighter " + i + " unchanged at tick " + t);
            }
            Assert.AreEqual(before.Tick + 150, sim.State.Tick);
            Assert.IsTrue(before.Globals.Rng.Equals(sim.State.Globals.Rng), "the match RNG is not used");
            Assert.IsTrue(before.Mode.Equals(sim.State.Mode), "mode rules stand still");
            for (int i = 0; i < 4; i++) Assert.IsTrue(before.Stats[i].Equals(sim.State.Stats[i]), "no damage, no statistics");
            Assert.AreNotEqual(before.Fighters[0].Position, sim.State.Fighters[0].Position, "the own fighter moves");
        }

        [Test]
        public void Shadow_ReproducesTheFullSimulation_ForTheOwnFighter_OnTheSameInputs()
        {
            MatchDefs defs = F.CreateDefs();
            MatchSimulation full = F.CreateWith(defs, F.Slot(0, 0, 4f, 4f), F.Slot(0, 1, 17f, 27f));
            MatchSimulation shadow = F.CreateWith(defs, F.Slot(0, 0, 4f, 4f), F.Slot(0, 1, 17f, 27f));
            shadow.State.CopyFrom(full.State);
            StartPredicting(shadow, 0);
            var inputs = F.Inputs();
            DeterministicRng rng = DeterministicRng.FromSeed(21);
            for (int t = 0; t < 600; t++)
            {
                F.FillRandomInputs(ref rng, inputs, 1);
                inputs[0] = new PlayerInput(inputs[0].Move, inputs[0].Aim, inputs[0].Buttons & ~InputButtons.AutoAim);
                full.Tick(inputs);
                shadow.Tick(inputs);
                ref FighterState a = ref full.State.Fighters[0];
                ref FighterState b = ref shadow.State.Fighters[0];
                Assert.AreEqual(a.Position, b.Position, "position at tick " + t);
                Assert.AreEqual(a.Facing, b.Facing, "facing at tick " + t);
                Assert.AreEqual(a.Charges, b.Charges, "charges at tick " + t);
                Assert.AreEqual(a.ChargeProgressTicks, b.ChargeProgressTicks);
                Assert.AreEqual(a.VisibleToTeams, b.VisibleToTeams);
            }
        }

        [Test]
        public void Effects_OwnSelfBuffApplies_DamageOnOthersDoesNot()
        {
            MatchSimulation sim = A.Create(A.Slot(A.Bruno, 0, 5.5f, 5.5f), A.Slot(A.Dummy, 1, 6.6f, 5.5f));
            var inputs = F.Inputs();
            StartPredicting(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(1f - A.HelmetPercent / 100f, sim.State.Fighters[0].DamageTakenMul, 1e-5f, "own self-buff (Helmet) is predicted");

            FighterState dummy = sim.State.Fighters[1];
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            Assert.AreEqual(0, CountEvents(sim, SimEventType.Damaged), "no damage in prediction");
            Assert.IsTrue(dummy.Equals(sim.State.Fighters[1]), "the cone does not touch the enemy");
            Assert.AreEqual(2, sim.State.Fighters[0].Charges, "the attack itself is predicted (a charge spent)");

            A.ChargeUlt(sim, 0);
            Vec2 start = sim.State.Fighters[0].Position;
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Right);
            F.Run(sim, inputs, 20);
            Assert.Greater(sim.State.Fighters[0].Position.X, start.X + 2f, "the own ram dash is predicted");
            Assert.IsTrue(dummy.Equals(sim.State.Fighters[1]), "no knockback, stun or damage on the enemy");
            Assert.AreEqual(0, StatusOf(sim, 1), "no status on the enemy");
        }

        [Test]
        public void Effects_NoHealingOfSelf()
        {
            MatchSimulation sim = S.Create(S.Slot(S.Kora, 0, 5.5f, 5.5f), S.Slot(S.Dummy, 1, 15.5f, 5.5f));
            var inputs = F.Inputs();
            S.Wound(sim, 0, 1000);
            StartPredicting(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(1000, sim.State.Fighters[0].Health, "the brew does not heal in prediction: health is the server's");
            Assert.AreEqual(0, CountEvents(sim, SimEventType.Healed));
        }

        [Test]
        public void Hook_DoesNotPull()
        {
            string[] wall = { "........#.......", "........#.......", "........#.......", "........#.......", "........#......." };
            MatchSimulation sim = N.Create(false, N.CreateDefsOnMap(wall), N.Slot(N.Vex, 0, 2.5f, 2.5f), N.Slot(N.Dummy, 1, 2.5f, 0.5f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            StartPredicting(sim, 0);
            Vec2 start = sim.State.Fighters[0].Position;
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Right);
            F.Run(sim, inputs, 40);
            Assert.AreEqual(start, sim.State.Fighters[0].Position, "the pull comes from the server, not from the shadow");
            Assert.IsFalse(sim.State.Fighters[0].IsDashing);
        }

        [Test]
        public void Mine_DoesNotGoOff()
        {
            MatchSimulation sim = S.Create(S.Slot(S.Chomp, 0, 5.5f, 5.5f), S.Slot(S.Dummy, 1, 15.5f, 5.5f));
            var inputs = F.Inputs();
            StartPredicting(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Trick);
            int mine = -1;
            for (int i = 0; i < sim.State.Areas.Length; i++)
                if (sim.State.Areas[i].Active && sim.State.Areas[i].Kind == AreaKind.Mine) mine = i;
            Assert.GreaterOrEqual(mine, 0, "the own mine is placed in the shadow");
            F.Run(sim, inputs, 30);
            sim.State.Fighters[1].Position = new Vec2(5.5f, 6.5f); // an enemy is shown on it
            int triggered = CombatTestUtil.RunUntil(sim, inputs, SimEventType.AreaTriggered, 10);
            Assert.AreEqual(-1, triggered, "no blast in the shadow");
            Assert.IsTrue(sim.State.Areas[mine].Active);
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[1].Health);
        }

        [Test]
        public void OwnProjectile_StopsOnAShownEnemy_WithoutDamage()
        {
            MatchSimulation sim = F.CreateWith(F.CreateDefs(), F.Slot(0, 0, 5f, 10f), F.Slot(0, 1, 9f, 10f));
            var inputs = F.Inputs();
            StartPredicting(sim, 0);
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            int hit = CombatTestUtil.RunUntil(sim, inputs, SimEventType.ProjectileHit, 30);
            Assert.GreaterOrEqual(hit, 0, "a soft hit on the shown enemy");
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[1].Health);
            Assert.AreEqual(0, CountEvents(sim, SimEventType.Damaged));
        }

        [Test]
        public void Regeneration_AndModeRules_StandStill()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            var inputs = F.Inputs();
            F.Run(sim, inputs, 10);
            ref FighterState own = ref sim.State.Fighters[0];
            own.Health = own.MaxHealth / 2;
            own.LastCombatTick = 0;
            ModeState mode = sim.State.Mode;
            AcornsTestFactory.ActivePickups(sim.State, out int acornsBefore);
            StartPredicting(sim, 0);
            F.Run(sim, inputs, 600);
            Assert.AreEqual(sim.State.Fighters[0].MaxHealth / 2, sim.State.Fighters[0].Health, "no regeneration in prediction");
            Assert.IsTrue(mode.Equals(sim.State.Mode), "no acorns from the hollow, no countdown, no end");
            AcornsTestFactory.ActivePickups(sim.State, out int acornsAfter);
            Assert.AreEqual(acornsBefore, acornsAfter);
        }

        [Test]
        public void Visibility_OnlyTheOwnFighterIsRecomputed()
        {
            var sim = new MatchSimulation(F.CreateDefs(), F.CreateSetup(2));
            var inputs = F.Inputs();
            StartPredicting(sim, 0);
            sim.State.Fighters[0].VisibleToTeams = 0u;
            sim.State.Fighters[1].VisibleToTeams = 0x5u; // as shown by the server
            sim.Tick(inputs);
            Assert.AreNotEqual(0u, sim.State.Fighters[0].VisibleToTeams, "own mask recomputed");
            Assert.AreEqual(0x5u, sim.State.Fighters[1].VisibleToTeams, "other masks are the server's");
        }

        [Test]
        public void Respawn_OnlyTheOwnFighter()
        {
            var sim = new MatchSimulation(F.CreateDefs(), F.CreateSetup(2));
            var inputs = F.Inputs();
            for (int i = 0; i < 2; i++)
            {
                sim.State.Fighters[i].Alive = false;
                sim.State.Fighters[i].Health = 0;
                sim.State.Fighters[i].RespawnTick = 5;
            }
            StartPredicting(sim, 0);
            F.Run(sim, inputs, 10);
            Assert.IsTrue(sim.State.Fighters[0].Alive, "the own fighter respawns");
            Assert.IsFalse(sim.State.Fighters[1].Alive, "another one waits for the server");
        }

        private static int StatusOf(MatchSimulation sim, int target)
        {
            int n = 0;
            StatusEffectState[] items = sim.State.Statuses;
            for (int i = 0; i < items.Length; i++)
                if (items[i].Active && items[i].Target == target) n++;
            return n;
        }
    }
}
