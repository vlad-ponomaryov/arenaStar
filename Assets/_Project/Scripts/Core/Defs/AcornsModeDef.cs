using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Parameters of the Acorns mode (GDD §5.4, D-025), converted from <c>AcornsModeConfig</c>: ticks and cells.
    /// Two teams (0 and 1); acorns come from the map's hollow.
    /// </summary>
    public sealed class AcornsModeDef
    {
        /// <summary>A normal acorn falls from the hollow every … ticks (6 s = 180).</summary>
        public int SpawnIntervalTicks;

        /// <summary>A golden acorn falls in addition every … ticks (60 s = 1800).</summary>
        public int GoldenIntervalTicks;

        /// <summary>Acorns a golden one is worth (3).</summary>
        public int GoldenValue;

        /// <summary>Acorns a team needs to start the hold countdown (12).</summary>
        public int TargetAcorns;

        /// <summary>Hold countdown (12 s = 360).</summary>
        public int HoldTicks;

        /// <summary>Match length (2:30 = 4500).</summary>
        public int DurationTicks;

        /// <summary>New acorns land within this radius (cells) of the hollow centre.</summary>
        public float SpawnScatterRadius;

        /// <summary>Acorns of a defeated fighter land within this radius (cells) of it.</summary>
        public float DropScatterRadius;

        /// <summary>Acorn radius: picked up when the body touches it (body radius + this, D-025).</summary>
        public float PickupRadius;

        /// <summary>Dropped acorns cannot be picked up for … ticks (D-025).</summary>
        public int PickupDelayTicks;

        /// <summary>Random points tried for a walkable landing spot before falling back to the centre.</summary>
        public int ScatterAttempts;

        // MVP score = kills × KillWeight + damage × DamageWeight + healing × HealWeight + acorns × AcornWeight − deaths × DeathWeight.
        public float MvpKillWeight;
        public float MvpDamageWeight;
        public float MvpHealWeight;
        public float MvpAcornWeight;
        public float MvpDeathWeight;

        public void Validate()
        {
            if (SpawnIntervalTicks <= 0) throw new ArgumentException("AcornsModeDef.SpawnIntervalTicks must be > 0");
            if (GoldenIntervalTicks <= 0) throw new ArgumentException("AcornsModeDef.GoldenIntervalTicks must be > 0");
            if (GoldenValue <= 0) throw new ArgumentException("AcornsModeDef.GoldenValue must be > 0");
            if (TargetAcorns <= 0) throw new ArgumentException("AcornsModeDef.TargetAcorns must be > 0");
            if (HoldTicks <= 0) throw new ArgumentException("AcornsModeDef.HoldTicks must be > 0");
            if (DurationTicks <= 0) throw new ArgumentException("AcornsModeDef.DurationTicks must be > 0");
            if (SpawnScatterRadius < 0f) throw new ArgumentException("AcornsModeDef.SpawnScatterRadius must be ≥ 0");
            if (DropScatterRadius < 0f) throw new ArgumentException("AcornsModeDef.DropScatterRadius must be ≥ 0");
            if (PickupRadius < 0f) throw new ArgumentException("AcornsModeDef.PickupRadius must be ≥ 0");
            if (PickupDelayTicks < 0) throw new ArgumentException("AcornsModeDef.PickupDelayTicks must be ≥ 0");
            if (ScatterAttempts < 1) throw new ArgumentException("AcornsModeDef.ScatterAttempts must be ≥ 1");
        }
    }
}
