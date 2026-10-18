using System;
using Arena.Core.Sim;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// Restores a view from a snapshot (mirror of <see cref="SnapshotEncoder"/>, Server-Spec §4.4) into the client's
    /// <see cref="SnapshotFrameRing"/>: the baseline is taken from the ring, the new view goes into the slot of its tick — also
    /// for a late snapshot (it can still be the server's next baseline). Input is untrusted: no exceptions, no allocations;
    /// truncated or invalid data gives <see cref="SnapshotDecodeStatus.Malformed"/>.
    /// </summary>
    public sealed class SnapshotDecoder
    {
        private readonly BitReader _r = new BitReader();

        /// <summary>
        /// Decodes <paramref name="length"/> bytes at <paramref name="offset"/>. On <see cref="SnapshotDecodeStatus.Ok"/>,
        /// <paramref name="frame"/> is the new valid view in <paramref name="ring"/> and <paramref name="events"/> holds
        /// <paramref name="eventCount"/> events. On <see cref="SnapshotDecodeStatus.MissingBaseline"/>,
        /// <paramref name="serverTick"/> is still set.
        /// </summary>
        public SnapshotDecodeStatus Decode(byte[] buffer, int offset, int length, SnapshotFrameRing ring,
            NetSnapshotEvent[] events, out int eventCount, out SnapshotFrame frame, out int serverTick)
        {
            if (ring == null) throw new ArgumentNullException(nameof(ring));
            if (events == null) throw new ArgumentNullException(nameof(events));
            eventCount = 0;
            frame = null;
            serverTick = 0;
            if (buffer == null || offset < 0 || length <= 0 || offset + length > buffer.Length) return SnapshotDecodeStatus.Malformed;
            _r.Reset(buffer, offset, length);

            var header = new SnapshotHeader();
            header.ServerTick = _r.ReadVarInt();
            header.IsKeyframe = _r.ReadBool();
            uint age = header.IsKeyframe ? 0u : _r.ReadVarUInt();
            header.OwnSlot = EventPacker.SlotFromWire(_r.ReadBits(NetProtocol.SlotBits));
            header.LastAppliedInputTick = unchecked(_r.ReadVarInt() + header.ServerTick);
            header.InputMarginMin = _r.ReadVarInt();
            header.BotControlled = _r.ReadBool();
            if (_r.IsOverflow) return SnapshotDecodeStatus.Malformed;
            serverTick = header.ServerTick;
            if ((uint)header.OwnSlot >= SimLimits.MaxFighters) return SnapshotDecodeStatus.Malformed;

            SnapshotFrame baseline = null;
            if (!header.IsKeyframe)
            {
                if (age == 0u || age >= NetLimits.SnapshotHistory) return SnapshotDecodeStatus.Malformed;
                header.BaselineTick = unchecked(header.ServerTick - (int)age);
                baseline = ring.Get(header.BaselineTick);
                if (baseline == null) return SnapshotDecodeStatus.MissingBaseline;
            }
            if (ring.Get(header.ServerTick) != null) return SnapshotDecodeStatus.Duplicate;

            SnapshotFrame f = ring.Acquire(header.ServerTick);
            if (baseline == null) f.Clear();
            else f.CopyFrom(baseline);
            f.Header = header;
            f.Valid = false;
            if (!ReadBody(f, events, out eventCount) || _r.IsOverflow)
            {
                eventCount = 0;
                return SnapshotDecodeStatus.Malformed;
            }
            f.Valid = true;
            frame = f;
            return SnapshotDecodeStatus.Ok;
        }

        private bool ReadBody(SnapshotFrame f, NetSnapshotEvent[] events, out int eventCount)
        {
            eventCount = 0;
            int tick = f.Header.ServerTick;
            f.Mode = f.Mode.ReadDelta(_r, tick);
            f.Own = OwnFighterCodec.Read(_r, in f.Own, tick);
            if (!OwnFighterCodec.ReadStatuses(_r, f.OwnStatuses, ref f.OwnStatusCount, f.Header.OwnSlot, tick)) return false;

            if (!ReadRemovals(f.Fighters, NetProtocol.SlotBits)) return false;
            if (!ReadRemovals(f.Projectiles, NetProtocol.ProjectileSlotBits)) return false;
            if (!ReadRemovals(f.Areas, NetProtocol.AreaSlotBits)) return false;
            if (!ReadRemovals(f.Pickups, NetProtocol.PickupSlotBits)) return false;

            if (!ReadUpdates(f.Fighters, NetProtocol.SlotBits, tick)) return false;
            if (!ReadUpdates(f.Projectiles, NetProtocol.ProjectileSlotBits, tick)) return false;
            if (!ReadUpdates(f.Areas, NetProtocol.AreaSlotBits, tick)) return false;
            if (!ReadUpdates(f.Pickups, NetProtocol.PickupSlotBits, tick)) return false;

            int prev = -1;
            while (_r.ReadBool())
            {
                uint gap = _r.ReadVarUInt();
                long cell = (long)prev + 1 + gap;
                if (_r.IsOverflow || cell >= SimLimits.MaxMapCells) return false;
                f.SetCellChanged((int)cell);
                prev = (int)cell;
            }

            while (_r.ReadBool())
            {
                if (eventCount >= events.Length) return false;
                events[eventCount++] = EventPacker.Read(_r, tick);
            }
            return !_r.IsOverflow;
        }

        private bool ReadRemovals<T>(SnapshotEntityList<T> list, int slotBits) where T : struct, IEquatable<T>, ISnapshotEntry<T>
        {
            while (_r.ReadBool())
            {
                int slot = (int)_r.ReadBits(slotBits);
                if (_r.IsOverflow || !list.Remove(slot)) return false;
            }
            return true;
        }

        private bool ReadUpdates<T>(SnapshotEntityList<T> list, int slotBits, int tick) where T : struct, IEquatable<T>, ISnapshotEntry<T>
        {
            while (_r.ReadBool())
            {
                int slot = (int)_r.ReadBits(slotBits);
                if (_r.IsOverflow || (uint)slot >= (uint)list.SlotRange) return false;
                T prev = list.GetOrDefault(slot);
                T item = prev.ReadDelta(_r, tick);
                if (_r.IsOverflow || !list.Set(slot, in item)) return false;
            }
            return true;
        }
    }
}
