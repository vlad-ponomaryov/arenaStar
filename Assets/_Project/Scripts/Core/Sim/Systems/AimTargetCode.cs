namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// One int for an auto-aim target (D-129), stored in <c>FighterState.AimTargetCode</c>: 0 — none; 1 … MaxFighters — fighter
    /// slot + 1; above that — berry box index + MaxFighters + 1. Zero is "none" so a fresh <c>FighterState</c> has no target.
    /// </summary>
    public static class AimTargetCode
    {
        public const int None = 0;
        private const int BoxBase = SimLimits.MaxFighters + 1;

        public static int Fighter(int slot) => slot + 1;
        public static int Box(int box) => BoxBase + box;
        public static bool IsFighter(int code) => code > None && code < BoxBase;
        public static bool IsBox(int code) => code >= BoxBase;

        /// <summary>Fighter slot of a fighter code, else −1.</summary>
        public static int FighterSlot(int code) => IsFighter(code) ? code - 1 : -1;

        /// <summary>Box index of a box code, else −1.</summary>
        public static int BoxIndex(int code) => IsBox(code) ? code - BoxBase : -1;
    }
}
