using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Trick (GDD §5.2): one tap of <see cref="InputButtons.Trick"/>; <see cref="CombatRules.TrickUses"/> uses per match
    /// (kept through respawns), <see cref="CombatRules.TrickCooldownTicks"/> between uses; open from
    /// <see cref="CombatRules.TrickUnlockLevel"/> or with <c>MatchSetup.UnlockAllAbilities</c> (D-012).
    /// Direction: the movement input for <see cref="AbilityDef.UsesMoveDirection"/>, otherwise the aim / facing.
    /// </summary>
    public static class TrickSystem
    {
        /// <summary>True if the fighter has a trick it could use at <paramref name="tick"/> (ignores stun/dash).</summary>
        public static bool IsReady(MatchDefs defs, in FighterState f, int tick) =>
            defs.Fighters[f.DefIndex].Trick != null && f.TrickUnlocked && f.TrickUsesLeft > 0 && tick >= f.TrickReadyTick;

        /// <summary>Cooldown left as a fraction 0..1 for the HUD (0 = ready).</summary>
        public static float CooldownFraction(MatchDefs defs, in FighterState f, int tick)
        {
            int cooldown = defs.Rules.TrickCooldownTicks;
            if (cooldown <= 0 || tick >= f.TrickReadyTick) return 0f;
            return MathUtil.Clamp01((float)(f.TrickReadyTick - tick) / cooldown);
        }

        public static void TryUse(MatchState state, MatchDefs defs, in PlayerInput input, SimEventBuffer events, int slot)
        {
            ref FighterState f = ref state.Fighters[slot];
            if (!f.WasPressed(InputButtons.Trick)) return;
            int tick = state.Globals.Tick;
            if (!IsReady(defs, in f, tick) || !AbilitySystem.CanActivate(in f)) return;

            AbilityDef trick = defs.Fighters[f.DefIndex].Trick;
            f.TrickUsesLeft--;
            f.TrickReadyTick = tick + defs.Rules.TrickCooldownTicks;
            Vec2 dir = AbilitySystem.ResolveDirection(state, slot, in input, trick);
            AbilitySystem.Activate(state, defs, events, slot, trick, MatchDefs.MakeAbilityRef(f.DefIndex, AbilitySlot.Trick), dir);
        }
    }
}
