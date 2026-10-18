using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using NUnit.Framework;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    public sealed class ProjectileTests
    {
        private const float Step = F.DropSpeed / SimLimits.TickRate; // 0.4 cells per tick

        [Test]
        public void Projectile_MovesSpeedTimesDt_PerTick_AndEndsAtRange()
        {
            var sim = F.CreateSingle();
            var inputs = F.Inputs();
            Vec2 start = sim.State.Fighters[0].Position;

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            ProjectileState p = sim.State.Projectiles[0];
            Assert.IsTrue(p.Active);
            Assert.AreEqual(start.X, p.Position.X, 1e-5f);
            Assert.AreEqual(start.Y + Step, p.Position.Y, 1e-5f, "speed × dt in the firing tick");

            sim.Tick(inputs);
            Assert.AreEqual(start.Y + Step * 2f, sim.State.Projectiles[0].Position.Y, 1e-5f);

            int ticksToRange = (int)(F.ShooterRange / Step + 0.5f); // 20
            F.Run(sim, inputs, ticksToRange - 3);
            Assert.IsTrue(sim.State.Projectiles[0].Active, "still flying one tick before the range");
            Assert.AreEqual(start.Y + Step * (ticksToRange - 1), sim.State.Projectiles[0].Position.Y, 1e-3f);

            sim.Tick(inputs);
            Assert.IsFalse(sim.State.Projectiles[0].Active, "gone at the range");
            Assert.AreEqual(start.Y + F.ShooterRange, sim.State.Projectiles[0].Position.Y, 1e-3f);
            Assert.GreaterOrEqual(sim.Events.CountOf(SimEventType.ProjectileEnded), 1);
        }

        [Test]
        public void Projectile_HitsEnemy_NotAlly()
        {
            var defs = F.CreateDefs();
            var sim = F.CreateWith(defs, F.Slot(0, 0, 10f, 5f), F.Slot(0, 0, 10f, 7f), F.Slot(0, 1, 10f, 10f));
            var inputs = F.Inputs();

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            F.Run(sim, inputs, 40);

            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[1].Health, "no friendly fire: drops pass through the ally");
            Assert.AreEqual(F.MaxHealth - F.DropDamage * F.BurstCount, sim.State.Fighters[2].Health, "all 4 drops hit the enemy");
            Assert.AreEqual(F.DropDamage * F.BurstCount, sim.State.Stats[0].DamageDealt);
            for (int i = 0; i < SimLimits.MaxProjectiles; i++) Assert.IsFalse(sim.State.Projectiles[i].Active);
        }

        [Test]
        public void Projectile_StopsAtFirstEnemy_WithoutPierce()
        {
            var defs = F.CreateDefs();
            defs.Fighters[0].Attack.BurstCount = 1;
            var sim = F.CreateWith(defs, F.Slot(0, 0, 10f, 5f), F.Slot(0, 1, 10f, 8f), F.Slot(0, 1, 10f, 10f));
            var inputs = F.Inputs();

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            F.Run(sim, inputs, 30);
            Assert.AreEqual(F.MaxHealth - F.DropDamage, sim.State.Fighters[1].Health);
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[2].Health);
        }

        [Test]
        public void Projectile_Pierce_HitsNextTarget_Once()
        {
            var defs = F.CreateDefs();
            defs.Fighters[0].Attack.BurstCount = 1;
            defs.Fighters[0].Attack.PierceCount = 1;
            var sim = F.CreateWith(defs, F.Slot(0, 0, 10f, 5f), F.Slot(0, 1, 10f, 7f), F.Slot(0, 1, 10f, 9f),
                F.Slot(0, 1, 10f, 11f));
            var inputs = F.Inputs();

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            F.Run(sim, inputs, 30);
            Assert.AreEqual(F.MaxHealth - F.DropDamage, sim.State.Fighters[1].Health);
            Assert.AreEqual(F.MaxHealth - F.DropDamage, sim.State.Fighters[2].Health, "pierced into the second, hit once");
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[3].Health, "stopped after the pierce ran out");
        }

        [Test]
        public void PoolOverflow_DropsAndCounts_WithoutException()
        {
            var sim = F.CreateSingle();
            AbilityDef ability = sim.Defs.Fighters[0].Attack;
            int abilityRef = MatchDefs.MakeAbilityRef(0, AbilitySlot.Attack);
            int extra = 44;
            for (int i = 0; i < SimLimits.MaxProjectiles + extra; i++)
            {
                int slot = ProjectileSystem.TrySpawn(sim.State, 0, 0, sim.State.Fighters[0].Position, Vec2.Right, ability, abilityRef);
                Assert.AreEqual(i < SimLimits.MaxProjectiles ? i : -1, slot);
            }
            Assert.AreEqual(extra, sim.State.Globals.DroppedProjectiles);

            var inputs = F.Inputs();
            Assert.DoesNotThrow(() => CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up), "a shot with a full pool is dropped");
            Assert.AreEqual(extra + 1, sim.State.Globals.DroppedProjectiles);
        }
    }
}
