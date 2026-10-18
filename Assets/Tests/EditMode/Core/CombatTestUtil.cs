using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Helpers for attack/projectile/damage tests.</summary>
    public static class CombatTestUtil
    {
        /// <summary>One tick with an attack press of <paramref name="slot"/> (manual aim, or auto-aim if aim is zero and <paramref name="auto"/>).</summary>
        public static void PressAttack(MatchSimulation sim, PlayerInput[] inputs, int slot, Vec2 aim, bool auto = false)
        {
            InputButtons buttons = InputButtons.Attack | (auto ? InputButtons.AutoAim : InputButtons.None);
            inputs[slot] = new PlayerInput(Vec2.Zero, aim, buttons);
            sim.Tick(inputs);
            inputs[slot] = default;
        }

        /// <summary>Ticks until an event of <paramref name="type"/> appears (returns the tick index it happened in) or −1.</summary>
        public static int RunUntil(MatchSimulation sim, PlayerInput[] inputs, SimEventType type, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                int tick = sim.State.Tick;
                sim.Tick(inputs);
                if (sim.Events.CountOf(type) > 0) return tick;
            }
            return -1;
        }
    }
}
