using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data.Conversion;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Projectiles, cones and dashes against map tiles; tiles in the snapshot (S2.5).</summary>
    public sealed class MapCombatTests
    {
        private static string[] Row2With(char blocker) => new[]
        {
            "............",
            "............",
            "......" + blocker + ".....",
            "............",
            "............"
        };

        private static MatchSimulation ShooterVsTank(char blocker)
        {
            var defs = F.CreateDefsOnMap(Row2With(blocker));
            return F.CreateWith(defs, F.Slot(0, 0, 2.5f, 2.5f), F.Slot(1, 1, 9.5f, 2.5f));
        }

        [Test]
        public void Projectile_IsStoppedByWall_TargetBehindIsSafe()
        {
            var sim = ShooterVsTank('#');
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            int ended = 0;
            for (int t = 0; t < 40; t++)
            {
                for (int e = 0; e < sim.Events.Count; e++)
                {
                    if (sim.Events[e].Type != SimEventType.ProjectileEnded) continue;
                    ended++;
                    Assert.AreEqual(6f, sim.Events[e].Position.X, 1e-3f, "ends at the wall face");
                }
                sim.Tick(inputs);
            }
            Assert.AreEqual(F.BurstCount, ended, "every drop of the burst hits the wall");
            Assert.AreEqual(F.TankHealth, sim.State.Fighters[1].Health);
        }

        [Test]
        public void Projectile_FliesOverWater()
        {
            var sim = ShooterVsTank('~');
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            F.Run(sim, inputs, 40);
            Assert.AreEqual(F.TankHealth - F.BurstCount * F.DropDamage, sim.State.Fighters[1].Health);
        }

        [Test]
        public void Projectile_DoesNotBreakBreakableWall_WithoutBreaksWalls()
        {
            var sim = ShooterVsTank('%');
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            F.Run(sim, inputs, 40);
            Assert.AreEqual(TileType.BreakableWall, sim.State.Grid.Get(6, 2));
            Assert.AreEqual(F.TankHealth, sim.State.Fighters[1].Health);
        }

        [Test]
        public void WallBreakingProjectile_BreaksAndFliesOn()
        {
            var defs = F.CreateDefsOnMap(Row2With('%'));
            defs.Fighters[0].Attack.BreaksWalls = true;
            var sim = F.CreateWith(defs, F.Slot(0, 0, 2.5f, 2.5f), F.Slot(1, 1, 9.5f, 2.5f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            int broken = 0;
            for (int t = 0; t < 40; t++)
            {
                broken += sim.Events.CountOf(SimEventType.TileBroken);
                sim.Tick(inputs);
            }
            Assert.AreEqual(1, broken);
            Assert.AreEqual(TileType.Floor, sim.State.Grid.Get(6, 2));
            Assert.AreEqual(F.TankHealth - F.BurstCount * F.DropDamage, sim.State.Fighters[1].Health);
        }

        [Test]
        public void Cone_DoesNotHitThroughWall()
        {
            var rows = new[] { "........", "...#....", "........" };
            var sim = F.CreateWith(F.CreateDefsOnMap(rows), F.Slot(1, 0, 2.45f, 1.5f), F.Slot(0, 1, 4.6f, 1.5f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[1].Health, "wall between");

            var open = F.CreateWith(F.CreateDefsOnMap("........", "........", "........"), F.Slot(1, 0, 2.45f, 1.5f), F.Slot(0, 1, 4.6f, 1.5f));
            CombatTestUtil.PressAttack(open, inputs, 0, Vec2.Right);
            Assert.AreEqual(F.MaxHealth - F.ConeDamage, open.State.Fighters[1].Health, "control: same distance, no wall");
        }

        private static MatchSimulation BrunoBelowRow(string blockerRow)
        {
            var rows = new[]
            {
                ".......", ".......", ".......", ".......", ".......", ".......",
                blockerRow,
                ".......", ".......", ".......", ".......", "......."
            };
            MatchDefs defs = A.CreateDefs();
            defs.Map = MapConverter.FromRows("ram", rows);
            return TestMatchFactory.CreateWith(defs, A.Slot(A.Bruno, 0, 3.5f, 1.5f));
        }

        [Test]
        public void BrunoRam_BreaksBreakableWall_CellBecomesFloor_WithEvent()
        {
            var sim = BrunoBelowRow("%%%%%%%"); // row 5
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);

            int brokenAt3 = 0, broken = 0;
            int index = sim.State.Grid.IndexOf(3, 5);
            for (int t = 0; t < A.RamTicks + 2; t++)
            {
                for (int e = 0; e < sim.Events.Count; e++)
                {
                    if (sim.Events[e].Type != SimEventType.TileBroken) continue;
                    broken++;
                    Assert.AreEqual(0, sim.Events[e].Source, "breaker = Bruno");
                    if (sim.Events[e].Value == index) brokenAt3++;
                }
                sim.Tick(inputs);
            }
            Assert.AreEqual(1, brokenAt3, "the cell on the path breaks once");
            Assert.GreaterOrEqual(broken, 1);
            Assert.AreEqual(TileType.Floor, sim.State.Grid.Get(3, 5));
            Assert.AreEqual(TileType.BreakableWall, sim.State.Grid.Get(0, 5), "cells the body did not touch stay");
            Assert.AreEqual(1.5f + A.RamDistance, sim.State.Fighters[0].Position.Y, 1e-3f, "the ram goes through");

            // The broken cell is walkable now: walk back down through it.
            inputs[0] = new PlayerInput(new Vec2(0f, -1f), Vec2.Zero, InputButtons.None);
            F.Run(sim, inputs, 90);
            Assert.Less(sim.State.Fighters[0].Position.Y, 5f);
        }

        [Test]
        public void BrunoRam_IsStoppedByPlainWall()
        {
            var sim = BrunoBelowRow("#######");
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            F.Run(sim, inputs, A.RamTicks + 2);
            Assert.AreEqual(5f - A.BrunoRadius, sim.State.Fighters[0].Position.Y, 1e-3f);
            Assert.AreEqual(TileType.Wall, sim.State.Grid.Get(3, 5));
        }

        [Test]
        public void Snapshot_IncludesTiles()
        {
            var sim = BrunoBelowRow("%%%%%%%");
            var snapshot = new MatchState();
            snapshot.CopyFrom(sim.State);
            Assert.AreEqual(7, snapshot.Globals.MapWidth);
            Assert.AreEqual(12, snapshot.Globals.MapHeight);

            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            snapshot.CopyFrom(sim.State);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            F.Run(sim, inputs, A.RamTicks + 2);
            Assert.AreEqual(TileType.Floor, sim.State.Grid.Get(3, 5));
            Assert.IsFalse(snapshot.ContentEquals(sim.State));

            sim.State.CopyFrom(snapshot);
            Assert.AreEqual(TileType.BreakableWall, sim.State.Grid.Get(3, 5), "restore brings the wall back");

            // Only the tiles differ → ContentEquals must notice.
            var other = new MatchState();
            other.CopyFrom(sim.State);
            other.Tiles[other.Grid.IndexOf(0, 0)] = TileType.Water;
            Assert.IsFalse(other.ContentEquals(sim.State));
        }

        [Test]
        public void Determinism_OnAMapWithWalls()
        {
            var rows = new[] { "..........", "..#..%..~.", "....\"\"....", "..~..#..%.", ".........." };
            MatchSimulation a = F.CreateWith(F.CreateDefsOnMap(rows), F.Slot(0, 0, 1.5f, 0.5f), F.Slot(1, 1, 8.5f, 4.5f));
            MatchSimulation b = F.CreateWith(F.CreateDefsOnMap(rows), F.Slot(0, 0, 1.5f, 0.5f), F.Slot(1, 1, 8.5f, 4.5f));
            var ia = F.Inputs();
            var ib = F.Inputs();
            DeterministicRng ra = DeterministicRng.FromSeed(9), rb = DeterministicRng.FromSeed(9);
            for (int t = 0; t < 600; t++)
            {
                F.FillRandomInputs(ref ra, ia, 2);
                F.FillRandomInputs(ref rb, ib, 2);
                a.Tick(ia);
                b.Tick(ib);
            }
            Assert.IsTrue(a.State.ContentEquals(b.State));
        }
    }
}
