using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Gate of the main attack (GDD §5.2), called by <see cref="AbilitySystem.Run"/>: on the rising edge of
    /// <see cref="InputButtons.Attack"/> with a charge left (and not dashing), spends one charge, resolves the aim
    /// (<see cref="TargetingSystem"/>) and fires the first shot; the rest of a burst (<see cref="AbilityDef.BurstCount"/>)
    /// follows every <see cref="AbilityDef.BurstIntervalTicks"/> in the same direction. A press during a burst is ignored.
    /// A shot boost (<see cref="EffectKind.BoostShots"/>) is spent at the press and applies to the whole burst.
    /// Shots are delivered by <see cref="AbilitySystem.Activate"/> (shared with ult and trick).
    /// After the last shot the next press waits <see cref="AbilityDef.RecoveryTicks"/> (a heavy swing, D-149): the burst
    /// tick <see cref="FighterState.NextBurstTick"/> then marks the end of the recovery.
    /// </summary>
    public static class AttackSystem
    {
        /// <summary>True if a press of the attack would start a new attack now (no burst running, the recovery is over);
        /// charges, dash and stun are checked separately.</summary>
        public static bool IsReady(in FighterState f, int tick) => f.BurstShotsLeft <= 0 && tick >= f.NextBurstTick;

        public static void Update(MatchState state, MatchDefs defs, in PlayerInput input, SimEventBuffer events, int slot)
        {
            ref FighterState f = ref state.Fighters[slot];
            int tick = state.Globals.Tick;
            AbilityDef attack = defs.Fighters[f.DefIndex].Attack;
            int abilityRef = MatchDefs.MakeAbilityRef(f.DefIndex, AbilitySlot.Attack);

            if (f.BurstShotsLeft > 0)
            {
                if (tick >= f.NextBurstTick)
                {
                    f.BurstShotsLeft--;
                    f.NextBurstTick = tick + (f.BurstShotsLeft > 0 ? attack.BurstIntervalTicks : attack.RecoveryTicks);
                    AbilitySystem.Activate(state, defs, events, slot, attack, abilityRef, f.BurstDir, -1f,
                        EffectContext.FullDamagePercent + f.ShotBonusPercent, input.ViewDelayTicks);
                }
                return;
            }

            if (!f.WasPressed(InputButtons.Attack) || f.IsDashing || tick < f.NextBurstTick) return;
            if (!ChargeSystem.TryConsumeCharge(ref f)) return;

            Vec2 dir = AbilitySystem.ResolveDirection(state, defs.Rules, slot, in input, attack);
            f.Facing = dir;
            f.BurstDir = dir;
            f.BurstShotsLeft = attack.BurstCount - 1;
            f.NextBurstTick = tick + (f.BurstShotsLeft > 0 ? attack.BurstIntervalTicks : attack.RecoveryTicks);
            f.ShotBonusPercent = 0;
            if (f.BoostedShotsLeft > 0)
            {
                f.BoostedShotsLeft--;
                f.ShotBonusPercent = f.BoostPercent;
            }
            AbilitySystem.Activate(state, defs, events, slot, attack, abilityRef, dir,
                AbilitySystem.ResolveDistance(state, defs.Rules, slot, in input, attack), EffectContext.FullDamagePercent + f.ShotBonusPercent,
                input.ViewDelayTicks);
        }
    }
}
