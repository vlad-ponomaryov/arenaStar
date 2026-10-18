using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;

namespace Arena.Gameplay.Match
{
    /// <summary>
    /// Positions of the two latest ticks for view interpolation (D-015): views lag the simulation by at most one tick and
    /// render <c>Lerp(previous, current, alpha)</c>. Preallocated, no allocations per tick.
    /// </summary>
    public sealed class MatchViewState
    {
        private readonly Vec2[] _prev = new Vec2[SimLimits.MaxFighters];
        private readonly Vec2[] _curr = new Vec2[SimLimits.MaxFighters];
        private readonly Vec2[] _facing = new Vec2[SimLimits.MaxFighters];
        private readonly bool[] _alive = new bool[SimLimits.MaxFighters];

        public int Count { get; private set; }

        /// <summary>Both ticks = the given state (match start).</summary>
        public void Reset(MatchState state)
        {
            Count = state.FighterCount;
            for (int i = 0; i < Count; i++)
            {
                _prev[i] = _curr[i] = state.Fighters[i].Position;
                _facing[i] = state.Fighters[i].Facing;
                _alive[i] = state.Fighters[i].Active && state.Fighters[i].Alive;
            }
        }

        /// <summary>Call after every simulation tick: current becomes previous, the state becomes current.</summary>
        public void Capture(MatchState state)
        {
            Count = state.FighterCount;
            for (int i = 0; i < Count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                bool alive = f.Active && f.Alive;
                // A fighter that just appeared (spawn/respawn) snaps instead of sliding from its old place.
                _prev[i] = alive && !_alive[i] ? f.Position : _curr[i];
                _curr[i] = f.Position;
                _facing[i] = f.Facing;
                _alive[i] = alive;
            }
        }

        /// <param name="alpha">Fraction of the tick elapsed since the latest tick, 0..1.</param>
        public Vec2 Position(int slot, float alpha) => Vec2.Lerp(_prev[slot], _curr[slot], alpha);

        public Vec2 Facing(int slot) => _facing[slot];

        public bool Alive(int slot) => _alive[slot];
    }
}
