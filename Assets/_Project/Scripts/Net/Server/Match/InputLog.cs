using System;
using Arena.Core.Input;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// The inputs the simulation got, for every slot and every tick from 0 (Server-Spec §5.3, §8): what <c>MatchSimulation.Tick</c>
    /// was called with — players, repeats, carried presses and bots alike — so <see cref="ReplayVerifier"/> replays the match
    /// bit for bit without bots. Preallocated for <see cref="CapacityTicks"/>; past it recording stops and
    /// <see cref="Overflowed"/> is set. No allocations while recording.
    /// </summary>
    public sealed class InputLog
    {
        private readonly PlayerInput[] _inputs;

        public InputLog(int slots, int capacityTicks)
        {
            if (slots < 1) throw new ArgumentOutOfRangeException(nameof(slots));
            if (capacityTicks < 1) throw new ArgumentOutOfRangeException(nameof(capacityTicks));
            Slots = slots;
            CapacityTicks = capacityTicks;
            _inputs = new PlayerInput[slots * capacityTicks];
        }

        public int Slots { get; }
        public int CapacityTicks { get; }

        /// <summary>Recorded ticks: 0 … <see cref="Ticks"/> − 1.</summary>
        public int Ticks { get; private set; }

        public bool Overflowed { get; private set; }

        /// <summary>Records the inputs of tick <paramref name="tick"/> (must be the next one).</summary>
        public void Record(int tick, PlayerInput[] inputs)
        {
            if (Overflowed) return;
            if (tick != Ticks) throw new ArgumentException("InputLog: tick " + tick + " is not the next one (" + Ticks + ")");
            if (Ticks >= CapacityTicks)
            {
                Overflowed = true;
                return;
            }
            Array.Copy(inputs, 0, _inputs, Ticks * Slots, Slots);
            Ticks++;
        }

        public PlayerInput Get(int tick, int slot) => _inputs[tick * Slots + slot];

        /// <summary>Copies the inputs of <paramref name="tick"/> into <paramref name="destination"/> (index = slot).</summary>
        public void CopyTick(int tick, PlayerInput[] destination)
        {
            if ((uint)tick >= (uint)Ticks) throw new ArgumentOutOfRangeException(nameof(tick));
            Array.Copy(_inputs, tick * Slots, destination, 0, Slots);
        }
    }
}
