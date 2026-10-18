using Arena.Core.Defs;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Health regeneration (GDD §5.2): after <see cref="CombatRules.RegenDelayTicks"/> without attacking or taking damage,
    /// +<see cref="CombatRules.RegenFractionPerSecond"/> of max health per second. The fractional part is accumulated in
    /// <see cref="FighterState.RegenRemainder"/>, so the per-second amount is exact over time.
    /// Attack and damage systems call <see cref="MarkInCombat"/>.
    /// </summary>
    public static class RegenSystem
    {
        public static void Run(MatchState state, MatchDefs defs)
        {
            int count = state.Globals.FighterCount;
            int tick = state.Globals.Tick;
            CombatRules rules = defs.Rules;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || !f.Alive) continue;

                if (f.Health >= f.MaxHealth || tick - f.LastCombatTick < rules.RegenDelayTicks)
                {
                    f.RegenRemainder = 0f;
                    continue;
                }

                float amount = f.MaxHealth * rules.RegenFractionPerSecond * SimTime.Dt + f.RegenRemainder;
                int whole = (int)amount; // amount ≥ 0: truncation = floor
                f.RegenRemainder = amount - whole;
                f.Health += whole;
                if (f.Health >= f.MaxHealth)
                {
                    f.Health = f.MaxHealth;
                    f.RegenRemainder = 0f;
                }
            }
        }

        /// <summary>Restarts the regeneration delay: call when the fighter attacks or takes damage.</summary>
        public static void MarkInCombat(ref FighterState f, int tick)
        {
            f.LastCombatTick = tick;
            f.RegenRemainder = 0f;
        }
    }
}
