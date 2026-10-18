using System;
using Arena.Core.Sim;
using Arena.Core.State;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// A snapshot "view" (Server-Spec §4.4): exactly what a client restores from the snapshots it got — baseline + the changes
    /// that made it into the packet, already quantized — not the true state. The server keeps the views it sent
    /// (<c>ClientView</c>), the client the views it decoded (<see cref="SnapshotFrameRing"/>); a delta is coded between two
    /// views, so both sides stay bit-equal even when a packet was cut by the size limit. Preallocated compact lists;
    /// <see cref="CopyFrom"/> and <see cref="Clear"/> do not allocate.
    /// Changed cells: one bit per map cell whose tile differs from the map (a broken wall or box — now floor).
    /// </summary>
    public sealed class SnapshotFrame
    {
        public const int CellWords = SimLimits.MaxMapCells / 32;

        public SnapshotHeader Header;

        /// <summary>The frame holds a complete view (a ring slot can be empty or half-decoded).</summary>
        public bool Valid;

        public NetModeView Mode;

        /// <summary>The receiver's fighter, exact (every field of <see cref="FighterState"/>).</summary>
        public FighterState Own;

        public readonly StatusEffectState[] OwnStatuses = new StatusEffectState[NetLimits.MaxSnapshotOwnStatuses];
        public int OwnStatusCount;

        /// <summary>Allies and the visible enemies (not the own fighter), by fighter slot.</summary>
        public readonly SnapshotEntityList<NetFighterView> Fighters =
            new SnapshotEntityList<NetFighterView>(SimLimits.MaxFighters, SimLimits.MaxFighters);

        public readonly SnapshotEntityList<NetProjectileSegment> Projectiles =
            new SnapshotEntityList<NetProjectileSegment>(NetLimits.MaxSnapshotProjectiles, SimLimits.MaxProjectiles);

        public readonly SnapshotEntityList<NetAreaView> Areas =
            new SnapshotEntityList<NetAreaView>(NetLimits.MaxSnapshotAreas, SimLimits.MaxAreas);

        public readonly SnapshotEntityList<NetPickupView> Pickups =
            new SnapshotEntityList<NetPickupView>(NetLimits.MaxSnapshotPickups, SimLimits.MaxPickups);

        /// <summary>Bit per map cell (row × width + column) whose tile differs from the map.</summary>
        public readonly uint[] ChangedCells = new uint[CellWords];

        public int Tick => Header.ServerTick;

        public bool IsCellChanged(int cell) => (uint)cell < SimLimits.MaxMapCells && (ChangedCells[cell >> 5] & (1u << (cell & 31))) != 0;

        public void SetCellChanged(int cell)
        {
            if ((uint)cell < SimLimits.MaxMapCells) ChangedCells[cell >> 5] |= 1u << (cell & 31);
        }

        /// <summary>Copies the view (header included).</summary>
        public void CopyFrom(SnapshotFrame other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return;
            Header = other.Header;
            Valid = other.Valid;
            Mode = other.Mode;
            Own = other.Own;
            Array.Copy(other.OwnStatuses, OwnStatuses, OwnStatuses.Length);
            OwnStatusCount = other.OwnStatusCount;
            Fighters.CopyFrom(other.Fighters);
            Projectiles.CopyFrom(other.Projectiles);
            Areas.CopyFrom(other.Areas);
            Pickups.CopyFrom(other.Pickups);
            Array.Copy(other.ChangedCells, ChangedCells, ChangedCells.Length);
        }

        /// <summary>An empty view — the baseline of a key frame.</summary>
        public void Clear()
        {
            Header = default;
            Valid = false;
            Mode = default;
            Own = default;
            Array.Clear(OwnStatuses, 0, OwnStatuses.Length);
            OwnStatusCount = 0;
            Fighters.Clear();
            Projectiles.Clear();
            Areas.Clear();
            Pickups.Clear();
            Array.Clear(ChangedCells, 0, ChangedCells.Length);
        }

        /// <summary>Same view content (the header is not compared: a key frame and a delta of one state differ only there).</summary>
        public bool ContentEquals(SnapshotFrame other)
        {
            if (other == null) return false;
            if (!Mode.Equals(other.Mode) || !Own.Equals(other.Own) || OwnStatusCount != other.OwnStatusCount) return false;
            for (int i = 0; i < OwnStatusCount; i++)
                if (!OwnStatuses[i].Equals(other.OwnStatuses[i])) return false;
            if (!Fighters.ContentEquals(other.Fighters) || !Projectiles.ContentEquals(other.Projectiles)
                || !Areas.ContentEquals(other.Areas) || !Pickups.ContentEquals(other.Pickups)) return false;
            for (int i = 0; i < ChangedCells.Length; i++)
                if (ChangedCells[i] != other.ChangedCells[i]) return false;
            return true;
        }
    }
}
