using System;

namespace Arena.Net.Content
{
    /// <summary>
    /// Match policy of the content pack (Server-Spec §5.4): what every online match uses until the meta game (stage 4)
    /// brings levels — fighter level and whether trick and talent are open regardless of it (D-012).
    /// </summary>
    public sealed class ContentMatchPolicy
    {
        /// <summary>Level of every fighter in an online match (1 in the prototype).</summary>
        public int FighterLevel;

        /// <summary>Trick and talent open regardless of level (D-012).</summary>
        public bool UnlockAllAbilities;

        public void Validate()
        {
            if (FighterLevel < 1) throw new ArgumentException("ContentMatchPolicy.FighterLevel must be ≥ 1");
        }
    }
}
