using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;
using S = Arena.Tests.EditMode.Core.SupportFighterTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Chomp (S2.11): lobbed bomb, area talent, rolling log, mine — data + shared components only.</summary>
    public sealed class ChompTests
    {
        private const float Edge = 0.05f;

        private static readonly string[] WallRow =
        {
            "................",
            "................",
            "....#...........",
            "................",
            "................"
        };

        private static int AreaSlotOf(MatchSimulation sim, int owner)
        {
            for (int i = 0; i < sim.State.Areas.Length; i++)
                if (sim.State.Areas[i].Active && sim.State.Areas[i].Owner == owner) return i;
            return -1;
        }

        [Test]
        public void Bomb_ArcsOverWall_ExplodesAfter15TicksAtAimPoint_DamagesInsideRadiusOnly()
        {
            float reach = S.BlastRadius + AbilityTestFactory.DummyRadius;
            var point = new Vec2(8.5f, 2.5f); // 6 cells from Chomp, behind the wall at column 4
            var sim = S.Create(false, S.CreateDefsOnMap(WallRow),
                S.Slot(S.Chomp, 0, 2.5f, 2.5f),
                S.Slot(S.Dummy, 1, point.X + reach - Edge, 2.5f),   // body touches the blast
                S.Slot(S.Dummy, 1, point.X, 2.5f + reach + Edge),   // just outside
                S.Slot(S.Dummy, 0, point.X, 2.5f));                 // ally in the centre
            var inputs = F.Inputs();
            int start = sim.State.Tick;
            CombatTestUtil.PressAttack(sim, inputs, 0, new Vec2(6f / S.LobRange, 0f));

            int area = AreaSlotOf(sim, 0);
            Assert.GreaterOrEqual(area, 0, "the lob creates an area");
            Assert.IsTrue(Vec2.Approximately(point, sim.State.Areas[area].Position, 1e-4f), "lands at direction × aim length × range");
            Assert.AreEqual(S.BlastRadius, sim.State.Areas[area].Radius, 1e-5f, "talent locked");
            Assert.AreEqual(start + S.LobFlightTicks, sim.State.Areas[area].ActivateTick);

            int triggered = CombatTestUtil.RunUntil(sim, inputs, SimEventType.AreaTriggered, 30);
            Assert.AreEqual(start + S.LobFlightTicks, triggered, "explodes 0.5 s after the throw");
            Assert.AreEqual(A.DummyHealth - S.BombDamage, sim.State.Fighters[1].Health, "inside the radius");
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[2].Health, "outside the radius");
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[3].Health, "no friendly fire");
            Assert.IsFalse(sim.State.Areas[area].Active, "a burst disappears after exploding");
        }

        [Test]
        public void Bomb_WithoutAim_LandsAtFullRange_AutoAim_AtTheTarget()
        {
            var sim = S.Create(S.Slot(S.Chomp, 0, 10f, 5f), S.Slot(S.Dummy, 1, 10f, 9f));
            var inputs = F.Inputs();
            sim.State.Fighters[0].Facing = Vec2.Up;
            A.Press(sim, inputs, 0, InputButtons.Attack);
            Assert.IsTrue(Vec2.Approximately(new Vec2(10f, 5f + S.LobRange), sim.State.Areas[AreaSlotOf(sim, 0)].Position, 1e-4f));

            F.Run(sim, inputs, 60);
            A.Press(sim, inputs, 0, InputButtons.Attack | InputButtons.AutoAim);
            int newest = -1;
            for (int i = 0; i < sim.State.Areas.Length; i++)
                if (sim.State.Areas[i].Active && !sim.State.Areas[i].Triggered) newest = i;
            Assert.IsTrue(Vec2.Approximately(new Vec2(10f, 9f), sim.State.Areas[newest].Position, 1e-3f));
        }

        [Test]
        public void Talent_RaisesBlastRadiusTo144()
        {
            float reach = S.BlastRadius * 1.2f + AbilityTestFactory.DummyRadius;
            var point = new Vec2(10f, 10f);
            var sim = S.Create(S.Slot(S.Chomp, 0, 10f, 5f), S.Slot(S.Dummy, 1, point.X + reach - Edge, point.Y));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, new Vec2(0f, 5f / S.LobRange));
            Assert.AreEqual(1.44f, sim.State.Areas[AreaSlotOf(sim, 0)].Radius, 1e-5f);
            F.Run(sim, inputs, S.LobFlightTicks);
            Assert.AreEqual(A.DummyHealth - S.BombDamage, sim.State.Fighters[1].Health, "hit by the bigger blast");
        }

        [Test]
        public void Log_PiercesEveryoneOnTheLine_AndKnocksBack()
        {
            var sim = S.Create(S.Slot(S.Chomp, 0, 2.5f, 10f),
                S.Slot(S.Dummy, 1, 4.5f, 10f), S.Slot(S.Dummy, 1, 7.5f, 10.3f), S.Slot(S.Dummy, 1, 10.5f, 9.7f),
                S.Slot(S.Dummy, 1, 6f, 12f)); // off the line
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Right);
            F.Run(sim, inputs, 40);

            for (int i = 1; i <= 3; i++)
            {
                Assert.AreEqual(A.DummyHealth - S.LogDamage, sim.State.Fighters[i].Health, "target " + i);
                Assert.Greater(sim.State.Fighters[i].Position.X, 0f);
            }
            Assert.AreEqual(4.5f + S.LogPush, sim.State.Fighters[1].Position.X, 0.05f, "pushed 2 cells along the roll");
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[4].Health, "off the line");
        }

        [Test]
        public void Log_BreaksBreakableWall_AndHitsBehindIt()
        {
            var defs = S.CreateDefsOnMap(
                "................",
                "................",
                "......%.........",
                "................",
                "................");
            var sim = S.Create(true, defs, S.Slot(S.Chomp, 0, 2.5f, 2.5f), S.Slot(S.Dummy, 1, 9.5f, 2.5f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Right);
            int broken = sim.Events.CountOf(SimEventType.TileBroken);
            for (int t = 0; t < 40; t++)
            {
                sim.Tick(inputs);
                broken += sim.Events.CountOf(SimEventType.TileBroken);
            }
            Assert.AreEqual(1, broken);
            Assert.AreEqual(TileType.Floor, sim.State.Grid.Get(6, 2));
            Assert.AreEqual(A.DummyHealth - S.LogDamage, sim.State.Fighters[1].Health, "behind the broken wall");
        }

        [Test]
        public void Mine_TriggersOnEnemy_NotOnAlly()
        {
            var sim = S.Create(S.Slot(S.Chomp, 0, 5.5f, 5.5f), S.Slot(S.Dummy, 0, 6f, 5.5f), S.Slot(S.Dummy, 1, 15.5f, 5.5f));
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Trick);
            int mine = AreaSlotOf(sim, 0);
            Assert.GreaterOrEqual(mine, 0);
            Assert.AreEqual(AreaKind.Mine, sim.State.Areas[mine].Kind);

            F.Run(sim, inputs, 30);
            Assert.IsTrue(sim.State.Areas[mine].Active, "Chomp and the ally stand on it");
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[1].Health);

            sim.State.Fighters[2].Position = new Vec2(5.5f, 6.5f);
            int tick = CombatTestUtil.RunUntil(sim, inputs, SimEventType.AreaTriggered, 2);
            Assert.GreaterOrEqual(tick, 0, "an enemy steps on it");
            Assert.AreEqual(A.DummyHealth - S.MineDamage, sim.State.Fighters[2].Health);
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[1].Health, "no friendly fire");
            Assert.IsFalse(sim.State.Areas[mine].Active, "used up");
        }

        [Test]
        public void Mine_DisappearsAfter10Seconds_AndIsHiddenFromFarEnemies()
        {
            var sim = S.Create(S.Slot(S.Chomp, 0, 5.5f, 5.5f), S.Slot(S.Dummy, 1, 15.5f, 5.5f));
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Trick);
            int mine = AreaSlotOf(sim, 0);
            Assert.IsTrue(sim.State.Areas[mine].IsVisibleTo(0), "own team sees it");
            Assert.IsFalse(sim.State.Areas[mine].IsVisibleTo(1), "far enemy does not (D-030)");

            sim.State.Fighters[1].Position = new Vec2(5.5f, 7.3f); // 1.8 cells: closer than 2, but not touching (1 + 0.5)
            sim.Tick(inputs);
            Assert.IsTrue(sim.State.Areas[mine].Active);
            Assert.IsTrue(sim.State.Areas[mine].IsVisibleTo(1), "close enemy sees it");
            sim.State.Fighters[1].Position = new Vec2(15.5f, 5.5f);

            F.Run(sim, inputs, S.MineTicks - 2);
            Assert.IsTrue(sim.State.Areas[mine].Active, "still there just before 10 s");
            sim.Tick(inputs);
            Assert.IsFalse(sim.State.Areas[mine].Active, "gone after 10 s");
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[1].Health, "expired without exploding");
        }

        [Test]
        public void BombDamage_ChargesUlt()
        {
            var sim = S.Create(S.Slot(S.Chomp, 0, 10f, 5f), S.Slot(S.Dummy, 1, 10f, 10f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, new Vec2(0f, 5f / S.LobRange));
            F.Run(sim, inputs, S.LobFlightTicks);
            Assert.AreEqual(S.BombDamage, sim.State.Fighters[0].UltCharge);
        }
    }
}
