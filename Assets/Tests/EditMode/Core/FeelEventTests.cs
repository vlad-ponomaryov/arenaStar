using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>The simulation emits what the hit feedback needs (S2.10): who hit whom, how much, where, with which ability.</summary>
    public sealed class FeelEventTests
    {
        private static SimEvent Find(SimEventBuffer events, SimEventType type)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == type) return events[i];
            Assert.Fail("no " + type + " event");
            return default;
        }

        [Test]
        public void AttackHit_Damaged_HasSourceTargetAmountPositionAndAttackRef()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            Assert.GreaterOrEqual(CombatTestUtil.RunUntil(sim, inputs, SimEventType.Damaged, 30), 0);

            SimEvent e = Find(sim.Events, SimEventType.Damaged);
            Assert.AreEqual(0, e.Source);
            Assert.AreEqual(1, e.Target);
            Assert.AreEqual(F.DropDamage, e.Value);
            Assert.AreEqual(sim.State.Fighters[1].Position, e.Position);
            Assert.AreEqual(MatchDefs.MakeAbilityRef(A.Pix, AbilitySlot.Attack), e.AbilityRef);
        }

        [Test]
        public void UltHit_FiredAndDamaged_CarryTheUltRef()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            int ultRef = MatchDefs.MakeAbilityRef(A.Pix, AbilitySlot.Ult);
            Assert.AreEqual(ultRef, Find(sim.Events, SimEventType.AbilityFired).Value);

            Assert.GreaterOrEqual(CombatTestUtil.RunUntil(sim, inputs, SimEventType.Damaged, 30), 0);
            SimEvent e = Find(sim.Events, SimEventType.Damaged);
            Assert.AreEqual(0, e.Source);
            Assert.AreEqual(1, e.Target);
            Assert.AreEqual(ultRef, e.AbilityRef);
            Assert.AreEqual(AbilitySlot.Ult, MatchDefs.SlotOf(e.AbilityRef));
            Assert.AreEqual(A.PixUltDamage, e.Value);
        }

        [Test]
        public void EventsWithoutAbility_HaveMinusOne()
        {
            var e = new SimEvent(SimEventType.Healed, 1, 0, 0, 10, Vec2.Zero, Vec2.Zero);
            Assert.AreEqual(-1, e.AbilityRef);
        }
    }
}
