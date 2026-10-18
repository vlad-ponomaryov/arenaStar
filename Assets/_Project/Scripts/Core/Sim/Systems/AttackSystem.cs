using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Main attack (GDD §5.2): on the rising edge of <see cref="InputButtons.Attack"/> with a charge left, spends one charge,
    /// resolves the aim (<see cref="TargetingSystem"/>) and fires the first shot; the rest of a burst
    /// (<see cref="AbilityDef.BurstCount"/>) follows every <see cref="AbilityDef.BurstIntervalTicks"/> in the same direction.
    /// A press during a burst is ignored. Shots are delivered by <see cref="Fire"/> (shared with ult/trick in S2.4).
    /// </summary>
    public static class AttackSystem
    {
        public static void Run(MatchState state, MatchDefs defs, PlayerInput[] inputs, SimEventBuffer events)
        {
            int count = state.Globals.FighterCount;
            int tick = state.Globals.Tick;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || !f.Alive) continue;

                AbilityDef attack = defs.Fighters[f.DefIndex].Attack;
                int abilityRef = MatchDefs.MakeAbilityRef(f.DefIndex, AbilitySlot.Attack);

                if (f.BurstShotsLeft > 0)
                {
                    if (tick >= f.NextBurstTick)
                    {
                        f.BurstShotsLeft--;
                        f.NextBurstTick = tick + attack.BurstIntervalTicks;
                        RegenSystem.MarkInCombat(ref f, tick);
                        Fire(state, defs, events, i, attack, abilityRef, f.BurstDir);
                    }
                    continue;
                }

                if (!f.WasPressed(InputButtons.Attack)) continue;
                if (!ChargeSystem.TryConsumeCharge(ref f)) continue;

                Vec2 dir = TargetingSystem.ResolveAim(state, i, in inputs[i], attack.Range);
                f.Facing = dir;
                f.BurstDir = dir;
                f.BurstShotsLeft = attack.BurstCount - 1;
                f.NextBurstTick = tick + attack.BurstIntervalTicks;
                RegenSystem.MarkInCombat(ref f, tick);
                Fire(state, defs, events, i, attack, abilityRef, dir);
            }
        }

        /// <summary>Delivers one shot of <paramref name="ability"/> from the fighter in <paramref name="slot"/> along <paramref name="dir"/>.</summary>
        public static void Fire(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, AbilityDef ability, int abilityRef, Vec2 dir)
        {
            ref FighterState f = ref state.Fighters[slot];
            events.Add(new SimEvent(SimEventType.AbilityFired, state.Globals.Tick, slot, -1, abilityRef, f.Position, dir));
            switch (ability.Delivery)
            {
                case DeliveryKind.Projectile:
                    ProjectileSystem.TrySpawn(state, slot, f.Team, f.Position, dir, ability, abilityRef);
                    break;
                case DeliveryKind.MeleeCone:
                    HitCone(state, defs, events, slot, f.Team, f.Position, dir, ability);
                    break;
            }
        }

        /// <summary>
        /// Instant cone: every matching fighter whose centre is within <c>Range + its radius</c> and within half the cone
        /// angle of <paramref name="dir"/>.
        /// </summary>
        public static void HitCone(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, int team, Vec2 origin,
            Vec2 dir, AbilityDef ability)
        {
            float cosHalf = MathF.Cos(ability.ConeAngleDegrees * 0.5f * MathUtil.DegToRad);
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (j == slot || !EffectSystem.CanAffect(state, ability, slot, team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                float reach = ability.Range + defs.Fighters[t.DefIndex].Radius;
                Vec2 to = t.Position - origin;
                if (to.SqrLength > reach * reach) continue;
                if (!to.IsZero && Vec2.Dot(to.Normalized, dir) < cosHalf) continue;
                EffectSystem.ApplyAbility(state, defs, events, ability, slot, team, j);
            }
        }
    }
}
