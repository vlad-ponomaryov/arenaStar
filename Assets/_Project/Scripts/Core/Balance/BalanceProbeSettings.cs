using System;

namespace Arena.Core.Balance
{
    /// <summary>
    /// Parameters of <see cref="BalanceProbe"/> (supplied by the caller — a test or a tool; the probe has no numbers of its own).
    /// </summary>
    public sealed class BalanceProbeSettings
    {
        /// <summary>Size of the all-floor probe map, cells (must fit the longest attack range at <see cref="DistanceFraction"/>).</summary>
        public int MapWidth;
        public int MapHeight;

        /// <summary>The defender stands at this fraction of the attacker's attack range (centre to centre).</summary>
        public float DistanceFraction;

        /// <summary>A kill that takes longer than this counts as "never" (<see cref="float.PositiveInfinity"/>).</summary>
        public float MaxKillSeconds;

        /// <summary>Length of the "active fight" for the ult charge measure (GDD §6: 30 s).</summary>
        public float FightSeconds;

        /// <summary>Share of the attacks that land in an active fight (the probe itself always hits; the charge is scaled by it).</summary>
        public float HitFraction;

        /// <summary>Health of the target dummy of the ult measure (never defeated during <see cref="FightSeconds"/>).</summary>
        public int DummyHealth;

        /// <summary>The attacker starts with all its charges (the burst kill time) instead of one (the sustained rate).</summary>
        public bool FullCharges;

        public void Validate()
        {
            if (MapWidth < 3 || MapHeight < 3) throw new ArgumentException("BalanceProbeSettings: map must be at least 3×3");
            if (DistanceFraction <= 0f || DistanceFraction > 1f) throw new ArgumentException("BalanceProbeSettings: DistanceFraction must be in (0, 1]");
            if (MaxKillSeconds <= 0f) throw new ArgumentException("BalanceProbeSettings: MaxKillSeconds must be > 0");
            if (FightSeconds <= 0f) throw new ArgumentException("BalanceProbeSettings: FightSeconds must be > 0");
            if (HitFraction <= 0f || HitFraction > 1f) throw new ArgumentException("BalanceProbeSettings: HitFraction must be in (0, 1]");
            if (DummyHealth <= 0) throw new ArgumentException("BalanceProbeSettings: DummyHealth must be > 0");
        }
    }
}
