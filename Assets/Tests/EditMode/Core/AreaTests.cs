using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    public sealed class AreaTests
    {
        private const float Radius = 1.2f;
        private const int Delay = 15;
        private const int Lifetime = 20;

        [Test]
        public void Area_AppliesOnceAtActivation_ToEnemiesInside_ThenExpires()
        {
            var defs = F.CreateDefs();
            var centre = new Vec2(10f, 15f);
            var sim = F.CreateWith(defs,
                F.Slot(0, 0, 10f, 3f),               // owner
                F.Slot(0, 1, 10.5f, 15f),            // enemy inside
                F.Slot(0, 1, 12.5f, 15f),            // enemy outside (2.5 > 1.2 + 0.5)
                F.Slot(0, 0, 10f, 15.5f));           // ally inside
            var inputs = F.Inputs();
            int start = sim.State.Tick;
            int abilityRef = MatchDefs.MakeAbilityRef(0, AbilitySlot.Attack);
            int slot = AreaSystem.TrySpawn(sim.State, 0, 0, centre, Radius, start + Delay, start + Lifetime, abilityRef);
            Assert.AreEqual(0, slot);

            F.Run(sim, inputs, Delay);
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[1].Health, "nothing before activation");

            int tick = CombatTestUtil.RunUntil(sim, inputs, SimEventType.AreaTriggered, 1);
            Assert.AreEqual(start + Delay, tick);
            Assert.AreEqual(F.MaxHealth - F.DropDamage, sim.State.Fighters[1].Health);
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[2].Health);
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[3].Health);

            F.Run(sim, inputs, Lifetime - Delay);
            Assert.IsFalse(sim.State.Areas[0].Active);
            Assert.AreEqual(F.MaxHealth - F.DropDamage, sim.State.Fighters[1].Health, "applied once");
        }

        [Test]
        public void AreaOverflow_DropsAndCounts()
        {
            var sim = F.CreateSingle();
            int extra = 6;
            for (int i = 0; i < SimLimits.MaxAreas + extra; i++)
                AreaSystem.TrySpawn(sim.State, 0, 0, Vec2.Zero, Radius, 100, 200, 0);
            Assert.AreEqual(extra, sim.State.Globals.DroppedAreas);
        }
    }
}
