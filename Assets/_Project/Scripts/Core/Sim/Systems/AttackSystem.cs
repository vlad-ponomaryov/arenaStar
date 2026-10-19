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
    /// </summary>
    public static class AttackSystem
    {
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
                    f.NextBurstTick = tick + attack.BurstIntervalTicks;
                    AbilitySystem.Activate(state, defs, events, slot, attack, abilityRef, f.BurstDir, -1f,
                        EffectContext.FullDamagePercent + f.ShotBonusPercent, input.ViewDelayTicks);
                }
                return;
            }

            if (!f.WasPressed(InputButtons.Attack) || f.IsDashing) return;
            if (!ChargeSystem.TryConsumeCharge(ref f)) return;

            Vec2 dir = AbilitySystem.ResolveDirection(state, defs.Rules, slot, in input, attack);
            f.Facing = dir;
            f.BurstDir = dir;
            f.BurstShotsLeft = attack.BurstCount - 1;
            f.NextBurstTick = tick + attack.BurstIntervalTicks;
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
