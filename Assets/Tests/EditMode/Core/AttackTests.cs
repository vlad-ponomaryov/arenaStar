using System.Collections.Generic;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    public sealed class AttackTests
    {
        [Test]
        public void Attack_WithoutCharges_DoesNotFire()
        {
            var sim = F.CreateSingle();
            var inputs = F.Inputs();
            sim.State.Fighters[0].Charges = 0;

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            Assert.AreEqual(0, sim.Events.CountOf(SimEventType.AbilityFired));
            Assert.IsFalse(sim.State.Projectiles[0].Active);
            Assert.AreEqual(0, sim.State.Fighters[0].BurstShotsLeft);
        }

        [Test]
        public void Attack_SpendsOneCharge_ForWholeBurst_OfFourWithInterval()
        {
            var sim = F.CreateSingle();
            var inputs = F.Inputs();
            var fireTicks = new List<int>();

            inputs[0] = new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Attack);
            for (int t = 0; t < 15; t++)
            {
                int tick = sim.State.Tick;
                sim.Tick(inputs); // button held: only the rising edge starts an attack
                for (int e = 0; e < sim.Events.CountOf(SimEventType.AbilityFired); e++) fireTicks.Add(tick);
            }

            int i = F.BurstIntervalTicks;
            CollectionAssert.AreEqual(new[] { 0, i, 2 * i, 3 * i }, fireTicks);
            Assert.AreEqual(2, sim.State.Fighters[0].Charges, "one charge per burst");
            int active = 0;
            for (int p = 0; p < SimLimits.MaxProjectiles; p++) if (sim.State.Projectiles[p].Active) active++;
            Assert.AreEqual(F.BurstCount, active);
        }

        [Test]
        public void Recovery_DelaysTheNextAttack_AfterTheLastShotOfTheBurst()
        {
            const int recovery = 10;
            var defs = F.CreateDefs();
            defs.Fighters[0].Attack.RecoveryTicks = recovery;
            var sim = F.CreateWith(defs, F.Slot(0, 0, 10f, 5f));
            var inputs = F.Inputs();
            var startTicks = new List<int>();
            for (int t = 0; t < 60; t++)
            {
                // Mash: press on even ticks, release on odd ones (every press is a rising edge).
                inputs[0] = t % 2 == 0 ? new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Attack) : default;
                int charges = sim.State.Fighters[0].Charges;
                int tick = sim.State.Tick;
                sim.Tick(inputs);
                if (sim.State.Fighters[0].Charges < charges) startTicks.Add(tick);
            }

            int lastShot = (F.BurstCount - 1) * F.BurstIntervalTicks;
            Assert.GreaterOrEqual(startTicks.Count, 2);
            Assert.AreEqual(0, startTicks[0]);
            Assert.GreaterOrEqual(startTicks[1], lastShot + recovery, "no new attack during the recovery (D-149)");
            Assert.LessOrEqual(startTicks[1], lastShot + recovery + 1, "the attack is ready right after the recovery");
        }

        [Test]
        public void ManualAim_FiresAlongAimDirection()
        {
            var sim = F.CreateSingle();
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, new Vec2(0.5f, 0f));
            Assert.AreEqual(Vec2.Right, sim.State.Projectiles[0].Dir);
            Assert.AreEqual(Vec2.Right, sim.State.Fighters[0].Facing);
        }

        [Test]
        public void AutoAim_PicksNearestEnemyInRange_IgnoresAlliesAndFar()
        {
            var defs = F.CreateDefs();
            var sim = F.CreateWith(defs,
                F.Slot(0, 0, 10f, 5f),   // shooter
                F.Slot(0, 0, 11f, 5f),   // ally, nearest of all
                F.Slot(0, 1, 16f, 5f),   // enemy at 6
                F.Slot(0, 1, 10f, 9f),   // enemy at 4 — nearest enemy
                F.Slot(0, 1, 1f, 5f));   // enemy at 9 — out of range 8
            Assert.AreEqual(3, TargetingSystem.FindNearestEnemy(sim.State, 0, F.ShooterRange));

            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Zero, auto: true);
            Assert.IsTrue(Vec2.Approximately(Vec2.Up, sim.State.Projectiles[0].Dir, 1e-5f));
        }

        [Test]
        public void AutoAim_NoEnemyInRange_FiresAlongFacing()
        {
            var defs = F.CreateDefs();
            var sim = F.CreateWith(defs, F.Slot(0, 0, 10f, 5f), F.Slot(0, 1, 1f, 5f));
            Assert.AreEqual(-1, TargetingSystem.FindNearestEnemy(sim.State, 0, F.ShooterRange));
            sim.State.Fighters[0].Facing = Vec2.Right;

            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Zero, auto: true);
            Assert.AreEqual(Vec2.Right, sim.State.Projectiles[0].Dir);
        }

        [Test]
        public void Cone_HitsInside90AndRange_NotOutside_NotAllies()
        {
            var defs = F.CreateDefs();
            Vec2 o = new Vec2(10f, 10f);
            Vec2 inside40 = o + MathUtil.AngleToDir(90f - 40f) * 2f;
            Vec2 outside60 = o + MathUtil.AngleToDir(90f - 60f) * 2f;
            var sim = F.CreateWith(defs,
                F.Slot(1, 0, o.X, o.Y),                       // cone fighter (radius test def 0 for targets: 0.5)
                F.Slot(0, 1, o.X, o.Y + 2f),                  // straight ahead at 2 → hit
                F.Slot(0, 1, inside40.X, inside40.Y),         // 40° off, 2 away → hit
                F.Slot(0, 1, outside60.X, outside60.Y),       // 60° off → miss
                F.Slot(0, 1, o.X, o.Y + 3.2f),                // 3.2 > 2.5 + 0.5 → miss
                F.Slot(0, 0, o.X, o.Y + 1f));                 // ally in the cone → no damage
            var inputs = F.Inputs();

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            int hp = F.MaxHealth;
            Assert.AreEqual(hp - F.ConeDamage, sim.State.Fighters[1].Health);
            Assert.AreEqual(hp - F.ConeDamage, sim.State.Fighters[2].Health);
            Assert.AreEqual(hp, sim.State.Fighters[3].Health);
            Assert.AreEqual(hp, sim.State.Fighters[4].Health);
            Assert.AreEqual(hp, sim.State.Fighters[5].Health);
            Assert.IsFalse(sim.State.Projectiles[0].Active, "a cone spawns no projectile");
        }
    }
}
