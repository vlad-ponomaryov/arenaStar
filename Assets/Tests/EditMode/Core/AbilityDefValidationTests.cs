using System;
using Arena.Core.Defs;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Data validation of abilities and talents (D-011): bad data fails at match start, not in the tick.</summary>
    public sealed class AbilityDefValidationTests
    {
        [Test]
        public void FullSets_AreValid()
        {
            Assert.DoesNotThrow(() => A.CreateDefs().Validate());
        }

        [TestCase(DeliveryKind.Lob)]
        [TestCase(DeliveryKind.PlaceArea)]
        [TestCase(DeliveryKind.Hook)]
        [TestCase(DeliveryKind.None)]
        public void ReservedDeliveries_AreRejected(DeliveryKind kind)
        {
            FighterDef pix = A.CreatePix();
            pix.Ult.Delivery = kind;
            Assert.Throws<ArgumentException>(() => pix.Validate());
        }

        [Test]
        public void UltWithBurst_IsRejected()
        {
            FighterDef pix = A.CreatePix();
            pix.Ult.BurstCount = 2;
            pix.Ult.BurstIntervalTicks = 3;
            Assert.Throws<ArgumentException>(() => pix.Validate());
        }

        [Test]
        public void UltWithoutRequirement_IsRejected()
        {
            FighterDef pix = A.CreatePix();
            pix.UltChargeRequired = 0;
            Assert.Throws<ArgumentException>(() => pix.Validate());
        }

        [Test]
        public void SelfBuffWithoutSelfEffect_IsRejected()
        {
            FighterDef bruno = A.CreateBruno();
            bruno.Trick.Effects[0].Filter = TargetFilter.Enemy;
            Assert.Throws<ArgumentException>(() => bruno.Validate());
        }

        [Test]
        public void EmptyEffects_AllowedOnlyForDash()
        {
            FighterDef bruno = A.CreateBruno();
            bruno.Trick.Effects = new EffectDef[0];
            Assert.Throws<ArgumentException>(() => bruno.Validate());
            Assert.DoesNotThrow(() => A.CreatePix().Validate(), "Pix's roll has no effects");
        }

        [TestCase(EffectKind.Stun, 0, 0)]
        [TestCase(EffectKind.Slow, 0, 30)]
        [TestCase(EffectKind.Slow, 101, 30)]
        [TestCase(EffectKind.Knockback, 0, 3)]
        public void BadEffects_AreRejected(EffectKind kind, int value, int duration)
        {
            var e = new EffectDef { Kind = kind, Filter = TargetFilter.Enemy, Value = value, DurationTicks = duration };
            Assert.Throws<ArgumentException>(() => e.Validate("test"));
        }

        [TestCase(TalentCondition.TargetMarked)]
        [TestCase(TalentCondition.OnKill)]
        [TestCase(TalentCondition.AllyAura)]
        public void ReservedTalentConditions_AreRejected(TalentCondition condition)
        {
            var t = new TalentDef { Condition = condition, Modifier = TalentModifier.DamageMul, Value = 10 };
            Assert.Throws<ArgumentException>(() => t.Validate("test"));
        }
    }
}
