using System;
using Arena.Core.Math;

namespace Arena.Core.Defs
{
    /// <summary>Common combat rules of a match (GDD §5.2), converted from CombatRulesConfig: ticks / fractions.</summary>
    public sealed class CombatRules
    {
        /// <summary>Maximum attack charges (3).</summary>
        public int MaxCharges;

        /// <summary>Ticks without attacking or taking damage before regeneration starts (3 s = 90).</summary>
        public int RegenDelayTicks;

        /// <summary>Regenerated fraction of max health per second (0.12).</summary>
        public float RegenFractionPerSecond;

        /// <summary>Ticks from defeat to respawn in team modes (3 s = 90, GDD §5.2).</summary>
        public int RespawnTicks;

        /// <summary>Health/damage bonus per level above 1 (0.06).</summary>
        public float LevelBonusPerLevel;

        /// <summary>
        /// Applies the level bonus to a level-1 value: <c>round(base × (1 + bonus × (level − 1)))</c>; level below 1 counts as 1.
        /// </summary>
        public int ScaleByLevel(int baseValue, int level)
        {
            int extra = level > 1 ? level - 1 : 0;
            return MathUtil.RoundToInt(baseValue * (1f + LevelBonusPerLevel * extra));
        }

        public void Validate()
        {
            if (MaxCharges <= 0) throw new ArgumentException("CombatRules.MaxCharges must be > 0");
            if (RegenDelayTicks < 0) throw new ArgumentException("CombatRules.RegenDelayTicks must be ≥ 0");
            if (RegenFractionPerSecond < 0f) throw new ArgumentException("CombatRules.RegenFractionPerSecond must be ≥ 0");
            if (RespawnTicks < 0) throw new ArgumentException("CombatRules.RespawnTicks must be ≥ 0");
            if (LevelBonusPerLevel < 0f) throw new ArgumentException("CombatRules.LevelBonusPerLevel must be ≥ 0");
        }
    }
}
