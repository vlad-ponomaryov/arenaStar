using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// «Last Beast» rules (Server-Spec §9, S3.3): fog steps, damage and growth (environment damage charges no ult), berries
    /// (damage, max health, drop of all + 1), berry boxes (projectile, cone, burst; broken → floor, TileBroken, berry),
    /// places and simultaneous elimination, no respawn, the time limit.
    /// </summary>
    public sealed class LastBeastRulesTests
    {
        private static readonly Vec2 Centre = new Vec2(10.5f, 10.5f);

        private static int CountEvents(MatchSimulation sim, SimEventType type)
        {
            int n = 0;
            for (int i = 0; i < sim.Events.Count; i++)
                if (sim.Events[i].Type == type) n++;
            return n;
        }

        private static int ActiveBerries(MatchState state)
        {
            int n = 0;
            for (int i = 0; i < state.Pickups.Length; i++)
                if (state.Pickups[i].Active && state.Pickups[i].Kind == PickupKind.Berry) n++;
            return n;
        }

        // ------------------------------------------------------------------ fog

        [Test]
        public void Fog_FirstStepAtStart_ShrinksEverySideByTwo()
        {
            MatchSimulation sim = LastBeastTestFactory.Create(LastBeastTestFactory.Defs(), Centre, new Vec2(1.5f, 10.5f));
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, LastBeastTestFactory.FogStart - 1);
            Assert.AreEqual(0, sim.State.Mode.FogStep, "no fog before 0:40");
            Assert.IsFalse(LastBeastRules.IsInFog(sim.State, new Vec2(0.5f, 0.5f)));

            sim.Tick(inputs);
            ref ModeState m = ref sim.State.Mode;
            Assert.AreEqual(1, m.FogStep);
            Assert.AreEqual(2f, m.SafeMinX);
            Assert.AreEqual(19f, m.SafeMaxX);
            Assert.AreEqual(2f, m.SafeMinY);
            Assert.AreEqual(19f, m.SafeMaxY);
            Assert.AreEqual(1, CountEvents(sim, SimEventType.FogAdvanced));
            Assert.AreEqual(LastBeastTestFactory.FogStart + LastBeastTestFactory.FogInterval, m.NextFogStepTick);
            Assert.IsTrue(LastBeastRules.IsInFog(sim.State, sim.State.Fighters[1].Position), "centre outside the square = in the fog");
            Assert.IsFalse(LastBeastRules.IsInFog(sim.State, sim.State.Fighters[0].Position));
        }

        [Test]
        public void Fog_PulsesDamageOutside_FromTheEnvironment_NoUltCharge()
        {
            MatchSimulation sim = LastBeastTestFactory.Create(LastBeastTestFactory.Defs(), Centre, new Vec2(1.5f, 10.5f));
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, LastBeastTestFactory.FogStart + LastBeastTestFactory.FogPulse - 1);
            Assert.AreEqual(TestMatchFactory.MaxHealth, sim.State.Fighters[1].Health, "first pulse 0.5 s after the step");

            sim.Tick(inputs);
            Assert.AreEqual(TestMatchFactory.MaxHealth - 250, sim.State.Fighters[1].Health, "500 per second in pulses of 0.5 s");
            Assert.AreEqual(TestMatchFactory.MaxHealth, sim.State.Fighters[0].Health, "inside the square: no damage");
            bool sawFog = false;
            for (int i = 0; i < sim.Events.Count; i++)
            {
                if (sim.Events[i].Type != SimEventType.Damaged) continue;
                Assert.AreEqual(-1, sim.Events[i].Source, "the fog is the environment");
                sawFog = true;
            }
            Assert.IsTrue(sawFog);
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(0, sim.State.Fighters[i].UltCharge, "fog damage charges no ult");
                Assert.AreEqual(0, sim.State.Stats[i].DamageDealt, "nobody is credited");
            }
        }

        [Test]
        public void Fog_DamageGrowsPerStep_AndSquareStopsAtMinimum()
        {
            LastBeastModeDef lb = LastBeastTestFactory.Def();
            Assert.AreEqual(250, LastBeastRules.FogPulseDamage(lb, 1));
            Assert.AreEqual(375, LastBeastRules.FogPulseDamage(lb, 2), "+250 per second per step");
            Assert.AreEqual(500, LastBeastRules.FogPulseDamage(lb, 3));

            MatchSimulation sim = LastBeastTestFactory.Create(LastBeastTestFactory.Defs(), Centre, new Vec2(9.5f, 10.5f));
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, LastBeastTestFactory.FogStart + 10 * LastBeastTestFactory.FogInterval);
            ref ModeState m = ref sim.State.Mode;
            Assert.AreEqual(11, m.FogStep);
            Assert.AreEqual(10.5f - LastBeastTestFactory.FogMinHalf, m.SafeMinX, 1e-5f, "half size 2.5 → 5×5 cells");
            Assert.AreEqual(10.5f + LastBeastTestFactory.FogMinHalf, m.SafeMaxY, 1e-5f);
            Assert.IsTrue(sim.State.Fighters[0].Alive && sim.State.Fighters[1].Alive, "both in the last safe square");
            Assert.IsFalse(sim.IsEnded);
        }

        [Test]
        public void Fog_DefeatsFighterOutside_LastOneWins()
        {
            MatchSimulation sim = LastBeastTestFactory.Create(LastBeastTestFactory.Defs(), Centre, new Vec2(1.5f, 10.5f));
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            int guard = LastBeastTestFactory.FogStart + 2 * LastBeastTestFactory.FogInterval;
            for (int i = 0; i < guard && !sim.IsEnded; i++) sim.Tick(inputs);

            Assert.IsTrue(sim.IsEnded);
            Assert.IsFalse(sim.State.Fighters[1].Alive);
            Assert.AreEqual(2, sim.State.Stats[1].Place);
            Assert.AreEqual(1, sim.State.Stats[0].Place);
            Assert.AreEqual(0, sim.State.Mode.WinnerTeam);
            Assert.AreEqual(0, sim.State.Stats[0].Kills, "a fog defeat gives no kill");
        }

        // ------------------------------------------------------------------ berries

        [Test]
        public void Berry_Pickup_AddsMaxHealthAndHealth_Additively()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            MatchState state = sim.State;
            state.Fighters[0].Health = 3000;
            Place(state, 0, state.Fighters[0].Position);
            Place(state, 1, state.Fighters[0].Position);
            sim.Tick(TestMatchFactory.Inputs());

            ref FighterState f = ref state.Fighters[0];
            Assert.AreEqual(2, f.Berries);
            int expectedMax = MathUtil.RoundToInt(TestMatchFactory.MaxHealth * (1f + 0.08f * 2));
            Assert.AreEqual(expectedMax, f.MaxHealth, "+8 % per berry, additive (×1.16, not ×1.08²)");
            Assert.AreEqual(3000 + expectedMax - TestMatchFactory.MaxHealth, f.Health, "current health grows by the gain");
            Assert.AreEqual(0, ActiveBerries(state));
        }

        [Test]
        public void Berry_DelayedPickup_WaitsItsTick()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            Place(sim.State, 0, sim.State.Fighters[0].Position, 5);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, 4);
            Assert.AreEqual(0, sim.State.Fighters[0].Berries);
            sim.Tick(inputs);
            Assert.AreEqual(1, sim.State.Fighters[0].Berries);
        }

        [Test]
        public void Berry_DamageMultiplier_AppliesToEffects()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            MatchState state = sim.State;
            state.Fighters[0].Berries = 2;
            AbilityDef attack = sim.Defs.Fighters[0].Attack;
            var ctx = new EffectContext(attack, MatchDefs.MakeAbilityRef(0, AbilitySlot.Attack), 0, 0, Centre, Vec2.Up);
            int before = state.Fighters[1].Health;
            DamageEffect.Apply(state, sim.Defs, sim.Events, in ctx, attack.Effects[0], 1);
            Assert.AreEqual(MathUtil.RoundToInt(TestMatchFactory.DropDamage * 1.16f), before - state.Fighters[1].Health);

            state.Fighters[0].Berries = 0;
            before = state.Fighters[2].Health;
            DamageEffect.Apply(state, sim.Defs, sim.Events, in ctx, attack.Effects[0], 2);
            Assert.AreEqual(TestMatchFactory.DropDamage, before - state.Fighters[2].Health, "no berries: as before");
        }

        [Test]
        public void Berry_Pickup_Heals_ShareOfTheNewMax()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            sim.Defs.LastBeast.BerryHealPercent = 10;
            MatchState state = sim.State;
            state.Fighters[0].Health = 1000;
            Place(state, 0, state.Fighters[0].Position);
            sim.Tick(TestMatchFactory.Inputs());

            ref FighterState f = ref state.Fighters[0];
            int max = MathUtil.RoundToInt(TestMatchFactory.MaxHealth * 1.08f);
            Assert.AreEqual(max, f.MaxHealth);
            Assert.AreEqual(1000 + (max - TestMatchFactory.MaxHealth) + MathUtil.RoundToInt(max * 0.1f), f.Health,
                "the gain of the max + 10 % of the new max (D-131)");

            f.Health = f.MaxHealth - 1;
            Place(state, 1, f.Position);
            sim.Tick(TestMatchFactory.Inputs());
            Assert.AreEqual(state.Fighters[0].MaxHealth, state.Fighters[0].Health, "never above the max");
        }

        [Test]
        public void Berries_AboveTheCap_DoNotAddBonus()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            sim.Defs.LastBeast.MaxBonusBerries = 3;
            sim.State.Fighters[0].Berries = 7;
            Assert.AreEqual(1f + 0.08f * 3, LastBeastRules.DamageMultiplier(sim.State, sim.Defs, 0), 1e-5f);
            Assert.AreEqual(MathUtil.RoundToInt(TestMatchFactory.MaxHealth * (1f + 0.08f * 3)),
                LastBeastRules.MaxHealthWithBerries(sim.Defs, in sim.State.Fighters[0]));
            Assert.AreEqual(24, LastBeastRules.DamageBonusPercent(sim.Defs.LastBeast, 7));
            Assert.AreEqual(16, LastBeastRules.DamageBonusPercent(sim.Defs.LastBeast, 2));
            sim.Defs.LastBeast.MaxBonusBerries = 0;
            Assert.AreEqual(56, LastBeastRules.DamageBonusPercent(sim.Defs.LastBeast, 7), "0 — no cap");
        }

        [Test]
        public void Berries_AreIgnoredOutsideTheMode()
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            MatchSimulation sim = TestMatchFactory.CreateWith(defs, TestMatchFactory.Slot(0, 0, 5f, 5f), TestMatchFactory.Slot(0, 1, 8f, 5f));
            sim.State.Fighters[0].Berries = 3;
            Assert.AreEqual(1f, LastBeastRules.DamageMultiplier(sim.State, defs, 0));
        }

        [Test]
        public void Defeat_DropsAllBerriesPlusOne()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            MatchState state = sim.State;
            state.Fighters[1].Berries = 2;
            DamageSystem.ApplyDamage(state, sim.Defs, sim.Events, 0, 1, 100000);
            Assert.IsFalse(state.Fighters[1].Alive);
            Assert.AreEqual(0, state.Fighters[1].Berries);
            Assert.AreEqual(3, ActiveBerries(state), "two carried + one");
            for (int i = 0; i < state.Pickups.Length; i++)
                if (state.Pickups[i].Active)
                    Assert.AreEqual(state.Tick + 1 + LastBeastTestFactory.BerryDelay, state.Pickups[i].PickableFromTick);
        }

        // ------------------------------------------------------------------ boxes

        [Test]
        public void Boxes_LoadOnlyInLastBeast()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            Assert.AreEqual(2, BoxSystem.ActiveCount(sim.State));
            int box = BoxSystem.Find(sim.State, LastBeastTestFactory.BoxA.Column, LastBeastTestFactory.BoxA.Row);
            Assert.GreaterOrEqual(box, 0);
            Assert.AreEqual(LastBeastTestFactory.BoxHealth, sim.State.Boxes[box].Health);

            MatchDefs plain = TestMatchFactory.CreateDefs();
            plain.Map = LastBeastTestFactory.Map();
            MatchSimulation other = TestMatchFactory.CreateWith(plain, TestMatchFactory.Slot(0, 0, 5f, 5f), TestMatchFactory.Slot(0, 1, 8f, 5f));
            Assert.AreEqual(0, BoxSystem.ActiveCount(other.State), "no boxes in other modes");
            BoxSystem.Damage(other.State, plain, other.Events, 0, 0, 5000);
            Assert.AreEqual(TileType.BerryBox, other.State.Grid.Get(LastBeastTestFactory.BoxA), "the box tile stays a wall there");
        }

        [Test]
        public void Box_StopsProjectiles_AndTakesTheirDamage()
        {
            MatchDefs defs = LastBeastTestFactory.Defs();
            MatchSimulation sim = LastBeastTestFactory.Create(defs, new Vec2(10.5f, 11.5f), new Vec2(2.5f, 2.5f), new Vec2(18.5f, 2.5f));
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            inputs[0] = new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Attack);
            TestMatchFactory.Run(sim, inputs, 30);
            int box = BoxSystem.Find(sim.State, LastBeastTestFactory.BoxA.Column, LastBeastTestFactory.BoxA.Row);
            Assert.AreEqual(LastBeastTestFactory.BoxHealth - TestMatchFactory.BurstCount * TestMatchFactory.DropDamage, sim.State.Boxes[box].Health,
                "every drop of the burst hits the box");
        }

        [Test]
        public void Box_TakesConeDamage()
        {
            MatchDefs defs = LastBeastTestFactory.Defs();
            MatchSimulation sim = TestMatchFactory.CreateWith(defs, new PlayerSlotSetup(1, 0, 1, new Vec2(10.5f, 12.5f)),
                new PlayerSlotSetup(0, 1, 1, new Vec2(2.5f, 2.5f)), new PlayerSlotSetup(0, 2, 1, new Vec2(18.5f, 2.5f)));
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            inputs[0] = new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Attack);
            sim.Tick(inputs);
            int box = BoxSystem.Find(sim.State, LastBeastTestFactory.BoxA.Column, LastBeastTestFactory.BoxA.Row);
            Assert.AreEqual(LastBeastTestFactory.BoxHealth - TestMatchFactory.ConeDamage, sim.State.Boxes[box].Health);
            Assert.AreEqual(1, CountEvents(sim, SimEventType.BoxDamaged));
            int other = BoxSystem.Find(sim.State, LastBeastTestFactory.BoxB.Column, LastBeastTestFactory.BoxB.Row);
            Assert.AreEqual(LastBeastTestFactory.BoxHealth, sim.State.Boxes[other].Health, "a box out of the cone is untouched");
        }

        [Test]
        public void Box_TakesBurstAreaDamage()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            int abilityRef = MatchDefs.MakeAbilityRef(0, AbilitySlot.Attack);
            AreaSystem.TrySpawn(sim.State, 0, 0, new Vec2(10.5f, 13f), 1.2f, 0, 0, abilityRef);
            sim.Tick(TestMatchFactory.Inputs());
            int box = BoxSystem.Find(sim.State, LastBeastTestFactory.BoxA.Column, LastBeastTestFactory.BoxA.Row);
            Assert.AreEqual(LastBeastTestFactory.BoxHealth - TestMatchFactory.DropDamage, sim.State.Boxes[box].Health);
        }

        [Test]
        public void Box_Broken_BecomesFloor_RaisesTileBroken_DropsBerry()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            MatchState state = sim.State;
            GridCell cell = LastBeastTestFactory.BoxA;
            int box = BoxSystem.Find(state, cell.Column, cell.Row);
            sim.Events.Clear();
            BoxSystem.Damage(state, sim.Defs, sim.Events, box, 0, LastBeastTestFactory.BoxHealth + 100);

            Assert.IsFalse(state.Boxes[box].Active);
            Assert.AreEqual(TileType.Floor, state.Grid.Get(cell));
            Assert.AreEqual(1, CountEvents(sim, SimEventType.TileBroken));
            Assert.AreEqual(1, ActiveBerries(state));
            for (int i = 0; i < state.Pickups.Length; i++)
                if (state.Pickups[i].Active) Assert.AreEqual(cell.Center, state.Pickups[i].Position, "the berry lies where the box stood");
            Assert.AreEqual(-1, BoxSystem.Find(state, cell.Column, cell.Row));
        }

        // ------------------------------------------------------------------ places, respawn, end

        [Test]
        public void Places_SharedInATick_BestDamageWins()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            MatchState state = sim.State;
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            DamageSystem.ApplyDamage(state, sim.Defs, sim.Events, 0, 2, 100000);
            Assert.AreEqual(3, state.Stats[2].Place, "first out of three = 3rd");
            sim.Tick(inputs);
            Assert.IsFalse(sim.IsEnded);
            Assert.AreEqual(2, state.Mode.AliveCount);

            DamageSystem.ApplyDamage(state, sim.Defs, sim.Events, -1, 0, 100000);
            DamageSystem.ApplyDamage(state, sim.Defs, sim.Events, -1, 1, 100000);
            Assert.AreEqual(2, state.Stats[0].Place);
            Assert.AreEqual(2, state.Stats[1].Place, "out in the same tick: the same place");
            sim.Tick(inputs);

            Assert.IsTrue(sim.IsEnded);
            Assert.AreEqual(1, state.Stats[0].Place, "slot 0 dealt more damage");
            Assert.AreEqual(2, state.Stats[1].Place);
            Assert.AreEqual(0, state.Mode.WinnerTeam);
        }

        [Test]
        public void Places_SharedInATick_EqualDamage_LowerSlotWins()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            MatchState state = sim.State;
            DamageSystem.ApplyDamage(state, sim.Defs, sim.Events, -1, 1, 100000);
            DamageSystem.ApplyDamage(state, sim.Defs, sim.Events, -1, 2, 100000);
            DamageSystem.ApplyDamage(state, sim.Defs, sim.Events, -1, 0, 100000);
            sim.Tick(TestMatchFactory.Inputs());
            Assert.IsTrue(sim.IsEnded);
            Assert.AreEqual(1, state.Stats[0].Place);
            Assert.AreEqual(3, state.Stats[1].Place);
            Assert.AreEqual(3, state.Stats[2].Place);
            Assert.AreEqual(0, state.Mode.WinnerTeam);
        }

        [Test]
        public void NoRespawn_DefeatedStayOut()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 0, 2, 100000);
            TestMatchFactory.Run(sim, TestMatchFactory.Inputs(), TestMatchFactory.RespawnTicks * 2);
            Assert.IsFalse(sim.State.Fighters[2].Alive);
            Assert.AreEqual(2, LastBeastRules.CountAlive(sim.State));
        }

        [Test]
        public void NoRespawnFalse_RespawnsAsBefore()
        {
            MatchDefs defs = LastBeastTestFactory.Defs();
            defs.NoRespawn = false;
            MatchSimulation sim = LastBeastTestFactory.Create(defs, Centre, new Vec2(6.5f, 6.5f), new Vec2(14.5f, 14.5f));
            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 0, 2, 100000);
            TestMatchFactory.Run(sim, TestMatchFactory.Inputs(), TestMatchFactory.RespawnTicks + 2);
            Assert.IsTrue(sim.State.Fighters[2].Alive, "respawn is on unless NoRespawn is set");

            MatchSimulation plain = TestMatchFactory.CreateWith(TestMatchFactory.CreateDefs(), TestMatchFactory.Slot(0, 0, 5f, 5f),
                TestMatchFactory.Slot(0, 1, 8f, 5f));
            Assert.IsFalse(plain.Defs.NoRespawn, "default: respawn");
            DamageSystem.ApplyDamage(plain.State, plain.Defs, plain.Events, 0, 1, 100000);
            TestMatchFactory.Run(plain, TestMatchFactory.Inputs(), TestMatchFactory.RespawnTicks + 2);
            Assert.IsTrue(plain.State.Fighters[1].Alive);
        }

        [Test]
        public void TimeLimit_RanksByHealth()
        {
            const int limit = 30;
            MatchSimulation sim = LastBeastTestFactory.CreateThree(limit);
            sim.State.Fighters[0].Health = 2000;
            sim.State.Fighters[1].Health = 1000;
            TestMatchFactory.Run(sim, TestMatchFactory.Inputs(), limit);
            Assert.IsTrue(sim.IsEnded);
            Assert.AreEqual(limit, sim.State.Mode.EndTick);
            Assert.AreEqual(1, sim.State.Stats[2].Place, "full health");
            Assert.AreEqual(2, sim.State.Stats[0].Place);
            Assert.AreEqual(3, sim.State.Stats[1].Place);
            Assert.AreEqual(2, sim.State.Mode.WinnerTeam);
        }

        [Test]
        public void Validate_RequiresSharedSpawns_AndModeDef()
        {
            MatchDefs defs = LastBeastTestFactory.Defs();
            defs.Map = TestMatchFactory.TestMap();
            Assert.Throws<System.ArgumentException>(() => defs.Validate(), "team spawns only");
            defs = LastBeastTestFactory.Defs();
            defs.LastBeast = null;
            Assert.Throws<System.ArgumentException>(() => defs.Validate());
            Assert.DoesNotThrow(() => LastBeastTestFactory.Defs().Validate());
        }

        [Test]
        public void SnapshotCopy_KeepsBoxesAndFog()
        {
            MatchSimulation sim = LastBeastTestFactory.CreateThree();
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, LastBeastTestFactory.FogStart + 1);
            BoxSystem.Damage(sim.State, sim.Defs, sim.Events, 0, 0, 100);
            var copy = new MatchState();
            copy.CopyFrom(sim.State);
            Assert.IsTrue(copy.ContentEquals(sim.State));
            copy.Boxes[0].Health++;
            Assert.IsFalse(copy.ContentEquals(sim.State), "boxes take part in ContentEquals");
        }

        private static void Place(MatchState state, int slot, Vec2 position, int pickableFrom = 0)
        {
            state.Pickups[slot] = new PickupState
            {
                Active = true, Kind = PickupKind.Berry, Value = 1, Position = position, PickableFromTick = pickableFrom, SpawnTick = state.Tick
            };
        }
    }
}
