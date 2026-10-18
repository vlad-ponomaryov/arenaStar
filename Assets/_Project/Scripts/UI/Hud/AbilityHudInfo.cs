using Arena.Core.Defs;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.UI.Hud
{
    /// <summary>
    /// What the ability controls of the HUD show for one fighter (ult charge, trick uses / cooldown), read from the
    /// simulation state each frame. Plain data — tested without a canvas.
    /// </summary>
    public struct AbilityHudInfo
    {
        public bool HasUlt;
        public bool UltCharged;

        /// <summary>0..1.</summary>
        public float UltCharge;

        public bool HasTrick;

        /// <summary>Trick usable right now (unlocked, uses left, cooldown over, fighter alive).</summary>
        public bool TrickReady;

        public int TrickUsesLeft;
        public int TrickMaxUses;

        public static AbilityHudInfo From(MatchDefs defs, MatchState state, int slot)
        {
            var info = new AbilityHudInfo();
            if (defs == null || state == null || slot < 0 || slot >= state.Globals.FighterCount) return info;

            ref FighterState f = ref state.Fighters[slot];
            FighterDef def = defs.Fighters[f.DefIndex];

            info.HasUlt = def.Ult != null;
            info.UltCharged = f.Alive && UltSystem.IsCharged(defs, in f);
            info.UltCharge = UltSystem.ChargeFraction(defs, in f);

            info.HasTrick = def.Trick != null && f.TrickUnlocked;
            info.TrickMaxUses = info.HasTrick ? defs.Rules.TrickUses : 0;
            info.TrickUsesLeft = info.HasTrick ? f.TrickUsesLeft : 0;
            info.TrickReady = f.Alive && TrickSystem.IsReady(defs, in f, state.Tick);
            return info;
        }
    }
}
