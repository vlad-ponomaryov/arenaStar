using System;

namespace Arena.Core.State
{
    /// <summary>
    /// Per-slot match statistics (MVP and results screen, S2.7). Separate from <see cref="FighterState"/> so a respawn
    /// (which resets the fighter state) keeps them. When adding a field: also add it to <see cref="Equals(FighterStats)"/>.
    /// </summary>
    public struct FighterStats : IEquatable<FighterStats>
    {
        /// <summary>Health actually removed from enemies (overkill not counted).</summary>
        public int DamageDealt;

        /// <summary>Health actually restored to allies or self.</summary>
        public int HealingDone;

        public int Kills;
        public int Deaths;

        public bool Equals(FighterStats o) =>
            DamageDealt == o.DamageDealt && HealingDone == o.HealingDone && Kills == o.Kills && Deaths == o.Deaths;

        public override bool Equals(object obj) => obj is FighterStats other && Equals(other);
        public override int GetHashCode() => (DamageDealt * 397) ^ (Kills * 31) ^ Deaths ^ HealingDone;
    }
}
