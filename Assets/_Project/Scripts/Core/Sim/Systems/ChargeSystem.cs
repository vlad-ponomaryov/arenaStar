using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Attack charges (GDD §5.2): up to <see cref="CombatRules.MaxCharges"/>, restored one at a time every
    /// <see cref="FighterDef.AttackRechargeTicks"/>. Spending a charge does not reset the progress of the one in recharge.
    /// </summary>
    public static class ChargeSystem
    {
        public static void Run(MatchState state, MatchDefs defs, SimEventBuffer events)
        {
            int count = state.Globals.FighterCount;
            int maxCharges = defs.Rules.MaxCharges;
            for (int i = 0; i < count; i++)
            {
                if (!state.Globals.Simulates(i)) continue;
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || !f.Alive) continue;

                if (f.Charges >= maxCharges)
                {
                    f.Charges = maxCharges;
                    f.ChargeProgressTicks = 0;
                    continue;
                }

                f.ChargeProgressTicks++;
                if (f.ChargeProgressTicks >= defs.Fighters[f.DefIndex].AttackRechargeTicks)
                {
                    f.ChargeProgressTicks = 0;
                    f.Charges++;
                    events.Add(new SimEvent(SimEventType.ChargeRestored, state.Globals.Tick, i, -1, f.Charges, f.Position, Vec2.Zero));
                }
            }
        }

        /// <summary>Spends one charge if there is any. Returns false (and changes nothing) with no charges.</summary>
        public static bool TryConsumeCharge(ref FighterState f)
        {
            if (f.Charges <= 0) return false;
            f.Charges--;
            return true;
        }
    }
}
