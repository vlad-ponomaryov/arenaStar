using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Balance
{
    /// <summary>
    /// Automatic check of the GDD §6 balance targets on the real simulation (D-061): two fighters of level 1 on an empty
    /// all-floor map, the defender standing still at <see cref="BalanceProbeSettings.DistanceFraction"/> of the attack range,
    /// the attacker pressing its main attack with auto-aim whenever a charge is ready (talents unlocked, no ult or trick).
    /// "Continuous hitting" starts without a charge stock: the attacker has one charge for the first shot at tick 0 and then
    /// only what recharges (so the result is the sustained rate, not a burst of the 3 stored charges);
    /// <see cref="BalanceProbeSettings.FullCharges"/> starts with all charges instead (the burst of a fresh fighter, D-149).
    ///   - <see cref="KillSeconds"/>: time from the first shot to the defeat of the defender;
    ///   - <see cref="UltsPerFight"/>: ults charged in <see cref="BalanceProbeSettings.FightSeconds"/> of such a fight against an
    ///     unkillable dummy, × <see cref="BalanceProbeSettings.HitFraction"/> (in a real fight not every attack lands).
    /// A tool for tests / editor tables: allocates freely, never runs inside a match.
    /// </summary>
    public static class BalanceProbe
    {
        private const int AttackerSlot = 0;
        private const int DefenderSlot = 1;
        private const string DummyId = "balance_probe_dummy";

        /// <summary>Seconds the fighter <paramref name="attacker"/> needs to defeat <paramref name="defender"/> (indices in
        /// <c>defs.Fighters</c>), or <see cref="float.PositiveInfinity"/> if not within <see cref="BalanceProbeSettings.MaxKillSeconds"/>.</summary>
        public static float KillSeconds(MatchDefs defs, int attacker, int defender, BalanceProbeSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            MatchDefs probe = ProbeDefs(defs, null);
            MatchSimulation sim = CreateSim(probe, attacker, defender, settings);
            var inputs = new PlayerInput[SimLimits.MaxFighters];
            int maxTicks = SimTime.SecondsToTicks(settings.MaxKillSeconds);
            for (int t = 0; t < maxTicks; t++)
            {
                Step(sim, inputs);
                if (!sim.State.Fighters[DefenderSlot].Alive) return SimTime.TicksToSeconds(t);
            }
            return float.PositiveInfinity;
        }

        /// <summary>Ults the fighter <paramref name="attacker"/> charges in an active fight (see the class summary).</summary>
        public static float UltsPerFight(MatchDefs defs, int attacker, BalanceProbeSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            FighterDef source = defs.Fighters[attacker];
            var dummy = new FighterDef
            {
                Id = DummyId, NameKey = DummyId, MaxHealth = settings.DummyHealth, MoveSpeed = source.MoveSpeed, Radius = source.Radius,
                AttackRechargeTicks = source.AttackRechargeTicks, Attack = source.Attack
            };
            MatchDefs probe = ProbeDefs(defs, dummy);
            MatchSimulation sim = CreateSim(probe, attacker, probe.Fighters.Length - 1, settings);
            var inputs = new PlayerInput[SimLimits.MaxFighters];
            int ticks = SimTime.SecondsToTicks(settings.FightSeconds);
            for (int t = 0; t < ticks; t++) Step(sim, inputs);

            int required = UltSystem.Required(probe, in sim.State.Fighters[AttackerSlot]);
            if (required <= 0) return 0f;
            return sim.State.Stats[AttackerSlot].DamageDealt * settings.HitFraction / required;
        }

        private static MatchDefs ProbeDefs(MatchDefs defs, FighterDef extra)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            FighterDef[] fighters = defs.Fighters;
            if (extra != null)
            {
                fighters = new FighterDef[defs.Fighters.Length + 1];
                Array.Copy(defs.Fighters, fighters, defs.Fighters.Length);
                fighters[fighters.Length - 1] = extra;
            }
            return new MatchDefs { Rules = defs.Rules, Fighters = fighters, Mode = ModeKind.None };
        }

        private static MatchSimulation CreateSim(MatchDefs probe, int attacker, int defender, BalanceProbeSettings s)
        {
            probe.Map = MapDef.CreateEmpty(s.MapWidth, s.MapHeight, "balance_probe");
            float distance = probe.Fighters[attacker].AttackRange * s.DistanceFraction;
            float x = s.MapWidth * 0.5f;
            float y = (s.MapHeight - distance) * 0.5f;
            float margin = MathUtil.Max(probe.Fighters[attacker].Radius, probe.Fighters[defender].Radius);
            if (y < margin) throw new ArgumentException("BalanceProbe: map " + s.MapWidth + "×" + s.MapHeight + " is too small for range " + distance);

            var setup = new MatchSetup
            {
                Seed = 1,
                UnlockAllAbilities = true,
                Slots = new[]
                {
                    new PlayerSlotSetup(attacker, 0, 1, new Vec2(x, y)),
                    new PlayerSlotSetup(defender, 1, 1, new Vec2(x, y + distance))
                }
            };
            var sim = new MatchSimulation(probe, setup);
            ref FighterState a = ref sim.State.Fighters[AttackerSlot];
            a.Charges = s.FullCharges ? probe.Rules.MaxCharges : 1;
            a.ChargeProgressTicks = 0;
            return sim;
        }

        /// <summary>One tick: the attacker presses (rising edge) whenever it can start a new attack.</summary>
        private static void Step(MatchSimulation sim, PlayerInput[] inputs)
        {
            ref FighterState a = ref sim.State.Fighters[AttackerSlot];
            bool press = a.Alive && a.Charges > 0 && AttackSystem.IsReady(in a, sim.State.Globals.Tick) && !a.IsDashing && !a.IsHeld(InputButtons.Attack);
            inputs[AttackerSlot] = press ? new PlayerInput(Vec2.Zero, Vec2.Zero, InputButtons.Attack | InputButtons.AutoAim) : default;
            sim.Tick(inputs);
        }
    }
}
