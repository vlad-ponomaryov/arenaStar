using System;
using Arena.Core.Map;
using Arena.Core.Sim;

namespace Arena.Core.State
{
    /// <summary>
    /// The complete mutable state of a match (D-002): scalar globals plus fixed-capacity arrays of blittable structs
    /// (capacities from <see cref="SimLimits"/>). Allocated once; a snapshot is another instance filled by
    /// <see cref="CopyFrom"/> (no allocations). Events of a tick are NOT part of the state (D-008).
    /// When adding an array: allocate it here and extend <see cref="CopyFrom"/>, <see cref="ContentEquals"/> and <see cref="Clear"/>.
    /// </summary>
    public sealed class MatchState
    {
        public MatchGlobals Globals;
        public readonly FighterState[] Fighters = new FighterState[SimLimits.MaxFighters];
        public readonly FighterStats[] Stats = new FighterStats[SimLimits.MaxFighters];
        public readonly ProjectileState[] Projectiles = new ProjectileState[SimLimits.MaxProjectiles];
        public readonly AreaState[] Areas = new AreaState[SimLimits.MaxAreas];
        public readonly StatusEffectState[] Statuses = new StatusEffectState[SimLimits.MaxStatusEffects];

        /// <summary>Items on the map (acorns of the Acorns mode).</summary>
        public readonly PickupState[] Pickups = new PickupState[SimLimits.MaxPickups];

        /// <summary>Berry boxes (Last Beast); inactive in other modes.</summary>
        public readonly BoxState[] Boxes = new BoxState[SimLimits.MaxBoxes];

        /// <summary>Per team: the team sees every enemy until this tick (exclusive) — Nova's "Owl Eye" (<c>EffectKind.TeamReveal</c>).</summary>
        public readonly int[] TeamRevealUntilTick = new int[SimLimits.MaxTeams];

        /// <summary>
        /// Lag compensation (Server-Spec §2.8): position and "alive" of every fighter slot at the end of each of the last
        /// <see cref="SimLimits.MaxRewindTicks"/> ticks — row <c>tick % MaxRewindTicks</c>, entry <c>row × MaxFighters + slot</c>.
        /// Written by <c>LagCompensation.Record</c>, read only through <c>LagCompensation.TargetPosition</c>.
        /// </summary>
        public readonly PositionSample[] PositionHistory = new PositionSample[SimLimits.MaxRewindTicks * SimLimits.MaxFighters];

        /// <summary>Per history row: the tick it holds + 1 (0 — empty).</summary>
        public readonly int[] PositionHistoryStamp = new int[SimLimits.MaxRewindTicks];

        /// <summary>Mode rules state (phase, timers, countdown, winner).</summary>
        public ModeState Mode;

        /// <summary>Mutable copy of the map tiles (broken walls), row-major; the first MapWidth × MapHeight entries are used.</summary>
        public readonly TileType[] Tiles = new TileType[SimLimits.MaxMapCells];

        /// <summary>Grid view over <see cref="Tiles"/> (no allocation).</summary>
        public TileGrid Grid => new TileGrid(Globals.MapWidth, Globals.MapHeight, Tiles);

        public int Tick => Globals.Tick;
        public int FighterCount => Globals.FighterCount;

        /// <summary>Copies everything from <paramref name="other"/> into this instance (snapshot / restore).</summary>
        public void CopyFrom(MatchState other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return;
            Globals = other.Globals;
            Array.Copy(other.Fighters, Fighters, Fighters.Length);
            Array.Copy(other.Stats, Stats, Stats.Length);
            Array.Copy(other.Projectiles, Projectiles, Projectiles.Length);
            Array.Copy(other.Areas, Areas, Areas.Length);
            Array.Copy(other.Statuses, Statuses, Statuses.Length);
            Array.Copy(other.Pickups, Pickups, Pickups.Length);
            Array.Copy(other.Boxes, Boxes, Boxes.Length);
            Array.Copy(other.TeamRevealUntilTick, TeamRevealUntilTick, TeamRevealUntilTick.Length);
            Array.Copy(other.PositionHistory, PositionHistory, PositionHistory.Length);
            Array.Copy(other.PositionHistoryStamp, PositionHistoryStamp, PositionHistoryStamp.Length);
            Mode = other.Mode;
            Array.Copy(other.Tiles, Tiles, Tiles.Length);
        }

        /// <summary>Field-by-field equality of the whole state (determinism and snapshot checks).</summary>
        public bool ContentEquals(MatchState other)
        {
            if (other == null) return false;
            if (!Globals.Equals(other.Globals) || !Mode.Equals(other.Mode)) return false;
            for (int i = 0; i < Fighters.Length; i++)
                if (!Fighters[i].Equals(other.Fighters[i]) || !Stats[i].Equals(other.Stats[i])) return false;
            for (int i = 0; i < Projectiles.Length; i++)
                if (!Projectiles[i].Equals(other.Projectiles[i])) return false;
            for (int i = 0; i < Areas.Length; i++)
                if (!Areas[i].Equals(other.Areas[i])) return false;
            for (int i = 0; i < Statuses.Length; i++)
                if (!Statuses[i].Equals(other.Statuses[i])) return false;
            for (int i = 0; i < Pickups.Length; i++)
                if (!Pickups[i].Equals(other.Pickups[i])) return false;
            for (int i = 0; i < Boxes.Length; i++)
                if (!Boxes[i].Equals(other.Boxes[i])) return false;
            for (int i = 0; i < Tiles.Length; i++)
                if (Tiles[i] != other.Tiles[i]) return false;
            for (int i = 0; i < TeamRevealUntilTick.Length; i++)
                if (TeamRevealUntilTick[i] != other.TeamRevealUntilTick[i]) return false;
            for (int i = 0; i < PositionHistory.Length; i++)
                if (!PositionHistory[i].Equals(other.PositionHistory[i])) return false;
            for (int i = 0; i < PositionHistoryStamp.Length; i++)
                if (PositionHistoryStamp[i] != other.PositionHistoryStamp[i]) return false;
            return true;
        }

        /// <summary>Clears all state to defaults (inactive slots, tick 0).</summary>
        public void Clear()
        {
            Globals = default;
            Array.Clear(Fighters, 0, Fighters.Length);
            Array.Clear(Stats, 0, Stats.Length);
            Array.Clear(Projectiles, 0, Projectiles.Length);
            Array.Clear(Areas, 0, Areas.Length);
            Array.Clear(Statuses, 0, Statuses.Length);
            Array.Clear(Pickups, 0, Pickups.Length);
            Array.Clear(Boxes, 0, Boxes.Length);
            Array.Clear(TeamRevealUntilTick, 0, TeamRevealUntilTick.Length);
            Array.Clear(PositionHistory, 0, PositionHistory.Length);
            Array.Clear(PositionHistoryStamp, 0, PositionHistoryStamp.Length);
            Mode = default;
            Array.Clear(Tiles, 0, Tiles.Length);
        }
    }
}
