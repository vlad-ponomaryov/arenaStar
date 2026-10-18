using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using NUnit.Framework;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    public sealed class DamageRespawnTests
    {
        [Test]
        public void Damage_ReducesHealth_CountsStats_AndRestartsRegenDelay()
        {
            var sim = F.CreateWith(F.CreateDefs(), F.Slot(0, 0, 5f, 5f), F.Slot(0, 1, 5f, 10f));
            int dealt = DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 0, 1, 1000);
            Assert.AreEqual(1000, dealt);
            Assert.AreEqual(F.MaxHealth - 1000, sim.State.Fighters[1].Health);
            Assert.AreEqual(1000, sim.State.Stats[0].DamageDealt);
            Assert.AreEqual(sim.State.Tick, sim.State.Fighters[1].LastCombatTick);
            Assert.AreEqual(1, sim.Events.CountOf(SimEventType.Damaged));
        }

        [Test]
        public void Damage_ToAlly_IsIgnored()
        {
            var sim = F.CreateWith(F.CreateDefs(), F.Slot(0, 0, 5f, 5f), F.Slot(0, 0, 5f, 10f));
            Assert.AreEqual(0, DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 0, 1, 1000));
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[1].Health);
        }

        [Test]
        public void Damage_IsLevelScaled_BySource()
        {
            var defs = F.CreateDefs();
            var sim = F.CreateWith(defs, new Arena.Core.Defs.PlayerSlotSetup(0, 0, 2, new Vec2(5f, 5f)), F.Slot(0, 1, 5f, 10f));
            EffectSystem.ApplyAbility(sim.State, defs, sim.Events, defs.Fighters[0].Attack, 0, 0, 1);
            Assert.AreEqual(F.MaxHealth - 403, sim.State.Fighters[1].Health, "380 × 1.06 = 402.8 → 403");
        }

        [Test]
        public void Heal_ClampsToMax_AndCountsStats()
        {
            var sim = F.CreateWith(F.CreateDefs(), F.Slot(0, 0, 5f, 5f), F.Slot(0, 0, 5f, 10f));
            sim.State.Fighters[1].Health = F.MaxHealth - 100;
            Assert.AreEqual(100, DamageSystem.ApplyHeal(sim.State, sim.Events, 0, 1, 500));
            Assert.AreEqual(F.MaxHealth, sim.State.Fighters[1].Health);
            Assert.AreEqual(100, sim.State.Stats[0].HealingDone);
        }

        [Test]
        public void Death_AtZeroHealth_Respawn90TicksLater_FullHealthAndCharges_AtSpawn()
        {
            var spawn = new Vec2(10f, 10f);
            var sim = F.CreateWith(F.CreateDefs(), F.Slot(0, 0, 10f, 5f), F.Slot(0, 1, spawn.X, spawn.Y));
            var inputs = F.Inputs();
            ref FighterState victim = ref sim.State.Fighters[1];
            victim.Health = F.DropDamage; // exactly one drop
            victim.Charges = 1;
            victim.Position = new Vec2(10f, 8f); // moved away from the spawn point

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            int deathTick = CombatTestUtil.RunUntil(sim, inputs, SimEventType.FighterDefeated, 30);
            Assert.GreaterOrEqual(deathTick, 0, "the drop must defeat the victim");
            Assert.IsFalse(victim.Alive);
            Assert.AreEqual(0, victim.Health);
            Assert.AreEqual(deathTick + F.RespawnTicks, victim.RespawnTick);
            Assert.AreEqual(1, sim.State.Stats[1].Deaths);
            Assert.AreEqual(1, sim.State.Stats[0].Kills);

            F.Run(sim, inputs, F.RespawnTicks - 1);
            Assert.IsFalse(victim.Alive, "still waiting one tick before the respawn");

            sim.Tick(inputs);
            Assert.IsTrue(victim.Alive);
            Assert.AreEqual(1, sim.Events.CountOf(SimEventType.FighterSpawned));
            Assert.AreEqual(victim.MaxHealth, victim.Health);
            Assert.AreEqual(F.MaxHealth, victim.MaxHealth);
            Assert.AreEqual(3, victim.Charges);
            Assert.AreEqual(spawn, victim.Position);
            Assert.AreEqual(1, sim.State.Stats[1].Deaths, "stats survive the respawn");
        }

        [Test]
        public void DeadFighter_IsNotHit_AndDoesNotAct()
        {
            var sim = F.CreateWith(F.CreateDefs(), F.Slot(0, 0, 10f, 5f), F.Slot(0, 1, 10f, 8f));
            var inputs = F.Inputs();
            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 0, 1, 100000);
            Assert.IsFalse(sim.State.Fighters[1].Alive);

            CombatTestUtil.PressAttack(sim, inputs, 1, -Vec2.Up);
            Assert.AreEqual(0, sim.Events.CountOf(SimEventType.AbilityFired), "the dead do not shoot");
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            F.Run(sim, inputs, 20);
            Assert.AreEqual(0, sim.State.Fighters[1].Health);
            Assert.AreEqual(F.MaxHealth, sim.State.Stats[0].DamageDealt, "overkill is not counted: only the health removed");
        }
    }
}
