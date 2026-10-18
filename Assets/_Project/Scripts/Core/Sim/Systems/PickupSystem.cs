using System;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Items on the map (<see cref="MatchState.Pickups"/>): placing one at a random walkable point of a disc (match RNG) and
    /// raising <see cref="SimEventType.PickupSpawned"/>. Shared by the modes: acorns (<c>AcornsRules</c>) and berries
    /// (<c>LastBeastRules</c>, <c>BoxSystem</c>). Collecting is the mode's rule.
    /// </summary>
    public static class PickupSystem
    {
        /// <summary>
        /// Puts an item on the first free pickup slot at a random walkable point within <paramref name="radius"/> of
        /// <paramref name="centre"/>. Returns the slot, or −1 if all slots are busy (counted in <c>DroppedPickups</c>).
        /// </summary>
        public static int Spawn(MatchState state, SimEventBuffer events, PickupKind kind, int value, Vec2 centre, float radius,
            int attempts, int now, int pickableFrom)
        {
            PickupState[] pickups = state.Pickups;
            int slot = -1;
            for (int i = 0; i < pickups.Length; i++)
            {
                if (pickups[i].Active) continue;
                slot = i;
                break;
            }
            if (slot < 0)
            {
                state.Globals.DroppedPickups++;
                return -1;
            }

            Vec2 position = ScatterPoint(state, centre, radius, attempts);
            pickups[slot] = new PickupState
            {
                Active = true, Kind = kind, Value = value, Position = position, PickableFromTick = pickableFrom, SpawnTick = now
            };
            events.Add(new SimEvent(SimEventType.PickupSpawned, state.Globals.Tick, -1, -1, slot, position, Vec2.Zero));
            return slot;
        }

        /// <summary>Uniform random point of the disc that is on the map and not in a blocking tile; else the centre.</summary>
        public static Vec2 ScatterPoint(MatchState state, Vec2 centre, float radius, int attempts)
        {
            if (radius <= 0f) return centre;
            TileGrid grid = state.Grid;
            ref DeterministicRng rng = ref state.Globals.Rng;
            for (int i = 0; i < attempts; i++)
            {
                float angle = rng.Range(0f, 360f);
                float r = radius * MathF.Sqrt(rng.NextFloat01());
                Vec2 p = centre + MathUtil.AngleToDir(angle) * r;
                GridCell cell = GridCell.Of(p);
                if (grid.InBounds(cell.Column, cell.Row) && !grid.BlocksMovement(cell.Column, cell.Row)) return p;
            }
            return centre;
        }
    }
}
