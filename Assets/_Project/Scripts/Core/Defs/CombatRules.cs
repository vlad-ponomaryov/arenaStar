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

        /// <summary>Fraction of the ult charge lost on defeat (0.5, GDD §5.2).</summary>
        public float UltLossOnDeath;

        /// <summary>Trick uses per match (3).</summary>
        public int TrickUses;

        /// <summary>Ticks between trick uses (5 s = 150).</summary>
        public int TrickCooldownTicks;

        /// <summary>Fighter level that opens the trick (5) unless the match unlocks everything (D-012).</summary>
        public int TrickUnlockLevel;

        /// <summary>Fighter level that opens the talent (8) unless the match unlocks everything (D-012).</summary>
        public int TalentUnlockLevel;

        /// <summary>An enemy closer than this (cells, centre to centre) sees a fighter in a bush (2, GDD §5.2, D-024).</summary>
        public float BushRevealDistance;

        /// <summary>Ticks an attack keeps the attacker visible (1 s = 30, GDD §5.2).</summary>
        public int RevealTicks;

        /// <summary>
        /// Auto-aim of wall-stopped deliveries prefers an enemy with a clear line over a nearer one behind a wall (D-129).
        /// </summary>
        public bool AutoAimPreferClearShot;

        /// <summary>Berry boxes (Last Beast) are auto-aim targets, after enemies with a clear line (D-129).</summary>
        public bool AutoAimTargetsBoxes;

        /// <summary>Auto-aim score of an enemy: distance + this × health fraction (cells; a wounded enemy is preferred).</summary>
        public float AutoAimHealthWeightCells;

        /// <summary>Ticks the last auto-aim target is kept (0 — no stickiness).</summary>
        public int AutoAimStickyTicks;

        /// <summary>The kept target may score this many cells worse than the best one of its tier.</summary>
        public float AutoAimStickyMarginCells;

        /// <summary>
        /// An enemy with a clear line nearer than this × the ability range is a threat and beats any berry box (D-131); a
        /// farther one competes with the boxes by score.
        /// </summary>
        public float AutoAimThreatRangeFraction;

        /// <summary>A berry box scores this many cells better than its distance when it competes with a far enemy (D-131).</summary>
        public float AutoAimBoxBonusCells;

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
            if (UltLossOnDeath < 0f || UltLossOnDeath > 1f) throw new ArgumentException("CombatRules.UltLossOnDeath must be in [0, 1]");
            if (TrickUses < 0) throw new ArgumentException("CombatRules.TrickUses must be ≥ 0");
            if (TrickCooldownTicks < 0) throw new ArgumentException("CombatRules.TrickCooldownTicks must be ≥ 0");
            if (TrickUnlockLevel < 1) throw new ArgumentException("CombatRules.TrickUnlockLevel must be ≥ 1");
            if (TalentUnlockLevel < 1) throw new ArgumentException("CombatRules.TalentUnlockLevel must be ≥ 1");
            if (BushRevealDistance < 0f) throw new ArgumentException("CombatRules.BushRevealDistance must be ≥ 0");
            if (RevealTicks < 0) throw new ArgumentException("CombatRules.RevealTicks must be ≥ 0");
            if (AutoAimHealthWeightCells < 0f) throw new ArgumentException("CombatRules.AutoAimHealthWeightCells must be ≥ 0");
            if (AutoAimStickyTicks < 0) throw new ArgumentException("CombatRules.AutoAimStickyTicks must be ≥ 0");
            if (AutoAimStickyMarginCells < 0f) throw new ArgumentException("CombatRules.AutoAimStickyMarginCells must be ≥ 0");
            if (AutoAimThreatRangeFraction < 0f || AutoAimThreatRangeFraction > 1f)
                throw new ArgumentException("CombatRules.AutoAimThreatRangeFraction must be in [0, 1]");
            if (AutoAimBoxBonusCells < 0f) throw new ArgumentException("CombatRules.AutoAimBoxBonusCells must be ≥ 0");
        }
    }
}
