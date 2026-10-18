using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Passive talent of a fighter (D-011, GDD §5.2): a condition + a modifier. No state — <c>TalentSystem</c> evaluates it
    /// when movement or damage needs it. Opens at <see cref="CombatRules.TalentUnlockLevel"/> unless the match unlocks all.
    /// </summary>
    public sealed class TalentDef
    {
        public TalentCondition Condition;
        public TalentModifier Modifier;

        /// <summary>Size of the modifier in percent (+10 = +10 %).</summary>
        public int Value;

        /// <summary>Parameter of the condition: health threshold in percent for <see cref="TalentCondition.HealthBelow"/>;
        /// aura radius in cells for <see cref="TalentCondition.AllyAura"/>.</summary>
        public int Param;

        /// <summary>Time value of the modifier: the regeneration delay for <see cref="TalentModifier.RegenDelay"/>, ticks.</summary>
        public int Ticks;

        public void Validate(string owner)
        {
            switch (Modifier)
            {
                case TalentModifier.MoveSpeedMul:
                case TalentModifier.DamageMul:
                case TalentModifier.AreaRadiusMul:
                    if (Condition == TalentCondition.AllyAura) throw new ArgumentException(owner + ": an ally aura only changes the regen delay");
                    break;
                case TalentModifier.RegenDelay:
                    if (Condition != TalentCondition.AllyAura) throw new ArgumentException(owner + ": RegenDelay is an ally aura talent");
                    if (Ticks <= 0) throw new ArgumentException(owner + ": RegenDelay needs Ticks > 0");
                    break;
                default:
                    throw new ArgumentException(owner + ": talent modifier " + Modifier + " is not supported yet");
            }
            switch (Condition)
            {
                case TalentCondition.Always:
                case TalentCondition.UltCharged:
                    break;
                case TalentCondition.HealthBelow:
                    if (Param < 1 || Param > 100) throw new ArgumentException(owner + ": HealthBelow threshold must be in [1, 100] %");
                    break;
                case TalentCondition.AllyAura:
                    if (Param < 1) throw new ArgumentException(owner + ": AllyAura radius must be ≥ 1 cell");
                    break;
                default:
                    throw new ArgumentException(owner + ": talent condition " + Condition + " is not supported yet");
            }
            if (Value <= -100) throw new ArgumentException(owner + ": talent value must be > −100 %");
        }
    }
}
