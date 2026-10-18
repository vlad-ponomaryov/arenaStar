namespace Arena.Core.Modes
{
    /// <summary>Game mode rules run by the simulation (GDD §5.4). <see cref="None"/> = free fight without an end (tests, sandbox).</summary>
    public enum ModeKind : byte
    {
        None = 0,

        /// <summary>«Acorns» 3×3: collect 12 acorns and hold them for 12 s (GDD §5.4, D-025).</summary>
        Acorns = 1,

        /// <summary>«Last Beast»: 10 fighters each on their own, no respawn, berry boxes, shrinking fog; the last one alive wins
        /// (GDD §5.4, Server-Spec §9).</summary>
        LastBeast = 2
    }
}
