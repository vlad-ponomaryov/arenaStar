using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Parameters of the «Last Beast» mode (GDD §5.4, Server-Spec §9), converted from <c>LastBeastModeConfig</c>: ticks, cells,
    /// health. Every fighter is its own team, there is no respawn (<see cref="MatchDefs.NoRespawn"/>), fighters spawn at the
    /// shared spawns of the map. Numbers that are not in the GDD are PROTO (D-089).
    /// </summary>
    public sealed class LastBeastModeDef
    {
        /// <summary>The first fog step (0:40 = 1200).</summary>
        public int FogStartTicks;

        /// <summary>Ticks between fog steps (20 s = 600).</summary>
        public int FogStepIntervalTicks;

        /// <summary>Each step moves every side of the safe square inwards by this many cells (2).</summary>
        public float FogShrinkCells;

        /// <summary>The safe square never gets smaller than this half size, cells (PROTO 2.5 → 5×5 cells).</summary>
        public float FogMinHalfSize;

        /// <summary>Fog damage per second at the first step (500).</summary>
        public int FogDamagePerSecond;

        /// <summary>Fog damage per second added with every further step (PROTO 250).</summary>
        public int FogDamageGrowthPerStep;

        /// <summary>Fog damage is dealt in pulses every … ticks (PROTO 0.5 s = 15: 250 per pulse at the first step).</summary>
        public int FogPulseTicks;

        /// <summary>Durability of a berry box (PROTO 3600).</summary>
        public int BoxHealth;

        /// <summary>Berries that drop from a broken box (PROTO 1).</summary>
        public int BerriesPerBox;

        /// <summary>Damage bonus per berry, percent of the base, additive (GDD: +8 %; D-131: 12 %).</summary>
        public int BerryDamagePercent;

        /// <summary>Max health bonus per berry, percent of the level-scaled base, additive (GDD: +8 %).</summary>
        public int BerryHealthPercent;

        /// <summary>
        /// Berries counted for the bonuses (D-131: a cap against a runaway leader; more berries are still carried and dropped);
        /// 0 — no cap.
        /// </summary>
        public int MaxBonusBerries;

        /// <summary>Health restored per picked berry, percent of the new max health (D-131: the pickup is felt at once).</summary>
        public int BerryHealPercent;

        /// <summary>A defeated fighter drops all its berries and this many more (GDD: 1).</summary>
        public int ExtraBerriesOnDefeat;

        /// <summary>Berry radius: picked up when the body touches it (body radius + this).</summary>
        public float BerryPickupRadius;

        /// <summary>Dropped berries land within this radius (cells) of the defeated fighter or the broken box.</summary>
        public float BerryDropScatterRadius;

        /// <summary>Dropped berries cannot be picked up for … ticks.</summary>
        public int BerryPickupDelayTicks;

        /// <summary>Random points tried for a walkable landing spot before falling back to the centre.</summary>
        public int ScatterAttempts;

        /// <summary>Safety limit of the match (PROTO 6 min = 10800): then the winner is chosen by health, then by damage.</summary>
        public int MaxDurationTicks;

        public void Validate()
        {
            if (FogStartTicks < 0) throw new ArgumentException("LastBeastModeDef.FogStartTicks must be ≥ 0");
            if (FogStepIntervalTicks <= 0) throw new ArgumentException("LastBeastModeDef.FogStepIntervalTicks must be > 0");
            if (FogShrinkCells <= 0f) throw new ArgumentException("LastBeastModeDef.FogShrinkCells must be > 0");
            if (FogMinHalfSize < 0f) throw new ArgumentException("LastBeastModeDef.FogMinHalfSize must be ≥ 0");
            if (FogDamagePerSecond < 0 || FogDamageGrowthPerStep < 0) throw new ArgumentException("LastBeastModeDef: fog damage must be ≥ 0");
            if (FogPulseTicks <= 0) throw new ArgumentException("LastBeastModeDef.FogPulseTicks must be > 0");
            if (BoxHealth <= 0) throw new ArgumentException("LastBeastModeDef.BoxHealth must be > 0");
            if (BerriesPerBox < 0 || ExtraBerriesOnDefeat < 0) throw new ArgumentException("LastBeastModeDef: berry counts must be ≥ 0");
            if (MaxBonusBerries < 0 || BerryHealPercent < 0) throw new ArgumentException("LastBeastModeDef: berry cap and heal must be ≥ 0");
            if (BerryDamagePercent < 0 || BerryHealthPercent < 0) throw new ArgumentException("LastBeastModeDef: berry bonuses must be ≥ 0");
            if (BerryPickupRadius < 0f || BerryDropScatterRadius < 0f) throw new ArgumentException("LastBeastModeDef: berry radii must be ≥ 0");
            if (BerryPickupDelayTicks < 0) throw new ArgumentException("LastBeastModeDef.BerryPickupDelayTicks must be ≥ 0");
            if (ScatterAttempts < 1) throw new ArgumentException("LastBeastModeDef.ScatterAttempts must be ≥ 1");
            if (MaxDurationTicks <= 0) throw new ArgumentException("LastBeastModeDef.MaxDurationTicks must be > 0");
        }
    }
}
