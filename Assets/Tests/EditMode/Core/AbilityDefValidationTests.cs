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
        public void ReservedTalentConditions_AreRejected(TalentCondition condition)
        {
            var t = new TalentDef { Condition = condition, Modifier = TalentModifier.DamageMul, Value = 10 };
            Assert.Throws<ArgumentException>(() => t.Validate("test"));
        }

        [Test]
        public void ChompAndKoraSets_AreValid()
        {
            Assert.DoesNotThrow(() => SupportFighterTestFactory.CreateDefs().Validate());
        }

        [Test]
        public void AreaAbilities_NeedTheirParameters()
        {
            FighterDef chomp = SupportFighterTestFactory.CreateChomp();
            chomp.Attack.AreaRadius = 0f;
            Assert.Throws<ArgumentException>(() => chomp.Validate(), "lob without a blast radius");

            chomp = SupportFighterTestFactory.CreateChomp();
            chomp.Trick.AreaDurationTicks = 0;
            Assert.Throws<ArgumentException>(() => chomp.Validate(), "mine without a lifetime");

            FighterDef kora = SupportFighterTestFactory.CreateKora();
            kora.Ult.AreaPulseTicks = 0;
            Assert.Throws<ArgumentException>(() => kora.Validate(), "field without pulses");

            kora = SupportFighterTestFactory.CreateKora();
            kora.Attack.BounceRange = 0f;
            Assert.Throws<ArgumentException>(() => kora.Validate(), "bounce without a range");

            kora = SupportFighterTestFactory.CreateKora();
            kora.Trick.BlocksEnemyProjectiles = true;
            Assert.Throws<ArgumentException>(() => kora.Validate(), "only a placed field blocks projectiles");
        }

        [Test]
        public void RegenDelayTalent_IsAnAllyAura()
        {
            var aura = new TalentDef { Condition = TalentCondition.AllyAura, Param = 3, Modifier = TalentModifier.RegenDelay, Ticks = 60 };
            Assert.DoesNotThrow(() => aura.Validate("test"));
            Assert.Throws<ArgumentException>(() =>
                new TalentDef { Condition = TalentCondition.Always, Modifier = TalentModifier.RegenDelay, Ticks = 60 }.Validate("test"));
            Assert.Throws<ArgumentException>(() =>
                new TalentDef { Condition = TalentCondition.AllyAura, Param = 3, Modifier = TalentModifier.RegenDelay }.Validate("test"));
            Assert.Throws<ArgumentException>(() =>
                new TalentDef { Condition = TalentCondition.AllyAura, Param = 3, Modifier = TalentModifier.DamageMul, Value = 10 }.Validate("test"));
        }
    }
}
