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

        /// <summary>Parameter of the condition: health threshold in percent for <see cref="TalentCondition.HealthBelow"/>.</summary>
        public int Param;

        public void Validate(string owner)
        {
            switch (Modifier)
            {
                case TalentModifier.MoveSpeedMul:
                case TalentModifier.DamageMul:
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
                default:
                    throw new ArgumentException(owner + ": talent condition " + Condition + " is not supported yet");
            }
            if (Value <= -100) throw new ArgumentException(owner + ": talent value must be > −100 %");
        }
    }
}
