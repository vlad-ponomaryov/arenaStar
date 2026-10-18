using System;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class SpawnTests
    {
        [Test]
        public void Spawn_FullHealthAndCharges_FacingCentre_Events()
        {
            var setup = TestMatchFactory.CreateSetup(6);
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), setup);

            Assert.AreEqual(6, sim.FighterCount);
            Assert.AreEqual(6, sim.Events.CountOf(SimEventType.FighterSpawned));
            for (int i = 0; i < 6; i++)
            {
                FighterState f = sim.State.Fighters[i];
                Assert.IsTrue(f.Active && f.Alive);
                Assert.AreEqual(setup.Slots[i].Team, f.Team);
                Assert.AreEqual(f.MaxHealth, f.Health);
                Assert.AreEqual(3, f.Charges);
                Assert.AreEqual(setup.Slots[i].SpawnPosition, f.Position);
                Vec2 toCentre = (sim.Defs.Map.Center - f.Position).Normalized;
                Assert.IsTrue(Vec2.Approximately(toCentre, f.Facing, 1e-5f));
            }
            for (int i = 6; i < SimLimits.MaxFighters; i++) Assert.IsFalse(sim.State.Fighters[i].Active);
            Assert.AreEqual(3400, sim.State.Fighters[0].MaxHealth);
            Assert.AreEqual(5600, sim.State.Fighters[1].MaxHealth);
        }

        [Test]
        public void Spawn_OutsideArena_IsClampedInside()
        {
            var setup = new MatchSetup { Slots = new[] { new PlayerSlotSetup(0, 0, 1, new Vec2(-5f, 100f)) } };
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), setup);
            Assert.AreEqual(new Vec2(TestMatchFactory.Radius, TestMatchFactory.ArenaHeight - TestMatchFactory.Radius),
                sim.State.Fighters[0].Position);
        }

        [TestCase(1, 3400)]
        [TestCase(2, 3604)]
        [TestCase(10, 5236)]
        public void Level_AddsSixPercentPerLevel(int level, int expectedHealth)
        {
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(1, 1, level));
            Assert.AreEqual(expectedHealth, sim.State.Fighters[0].MaxHealth);
            Assert.AreEqual(expectedHealth, sim.State.Fighters[0].Health);
            Assert.AreEqual(level, sim.State.Fighters[0].Level);
        }

        [Test]
        public void InvalidSetup_Throws()
        {
            var defs = TestMatchFactory.CreateDefs();
            Assert.Throws<ArgumentException>(() => new MatchSimulation(defs, new MatchSetup { Slots = new PlayerSlotSetup[0] }));
            Assert.Throws<ArgumentException>(() => new MatchSimulation(defs, TestMatchFactory.CreateSetup(SimLimits.MaxFighters + 1)));
            Assert.Throws<ArgumentException>(() => new MatchSimulation(defs,
                new MatchSetup { Slots = new[] { new PlayerSlotSetup(5, 0, 1, Vec2.Zero) } }));
            Assert.Throws<ArgumentException>(() => new MatchSimulation(defs,
                new MatchSetup { Slots = new[] { new PlayerSlotSetup(0, 0, 0, Vec2.Zero) } }));
            defs.Fighters[1].Id = defs.Fighters[0].Id;
            Assert.Throws<ArgumentException>(() => new MatchSimulation(defs, TestMatchFactory.CreateSetup(1)));
        }
    }
}
