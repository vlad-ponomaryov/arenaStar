using System;

namespace Arena.Net.Snapshots
{
    /// <summary>Flags of a foreign fighter in a snapshot (Server-Spec §4.4); 8 bits on the wire (<c>NetProtocol.FighterFlagBits</c>).</summary>
    [Flags]
    public enum NetFighterFlags : byte
    {
        None = 0,

        /// <summary>The body centre is in a bush.</summary>
        InBush = 1 << 0,

        /// <summary>Invisible (only sent for allies — an invisible enemy is not in the snapshot at all): drawn translucent.</summary>
        Invisible = 1 << 1,

        /// <summary>Ally only: another team sees it now (translucency in a bush, as offline).</summary>
        SeenByEnemies = 1 << 2,

        Stunned = 1 << 3,

        /// <summary>A slow is active (speed multiplier below 1).</summary>
        Slowed = 1 << 4,

        Dashing = 1 << 5,

        /// <summary>Marked by the viewer's team.</summary>
        MarkedByViewer = 1 << 6,

        /// <summary>The ult is fully charged (foreign fighters get this bit instead of the charge, §4.3).</summary>
        UltReady = 1 << 7
    }
}
