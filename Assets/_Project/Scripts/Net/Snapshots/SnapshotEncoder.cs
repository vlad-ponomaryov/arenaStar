using System;
using Arena.Core.State;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// Writes a snapshot as the delta between two views (Server-Spec §4.4) and builds the view the client will restore from it.
    /// Layout: header → mode → own fighter → own statuses → removals of all lists (never dropped: a fighter that became
    /// hidden must disappear) → updates of fighters, projectiles, areas, pickups (each list in the target's order = priority)
    /// → changed cells → events. Each update item is <c>1 bit "more" + slot + entry delta</c>; an item that does not fit the
    /// byte budget is taken back (<see cref="BitWriter.Rewind"/>) and everything after it waits for the next snapshot, with
    /// the old value staying in the produced view. No allocations.
    /// </summary>
    public sealed class SnapshotEncoder
    {
        /// <summary>Bits kept free for the end markers of the lists after the budget runs out.</summary>
        private const int TerminatorReserveBits = 8;

        private readonly BitWriter _w = new BitWriter();
        private int _budgetBits;
        private bool _exhausted;

        /// <summary>Update items, cells and events of the last <see cref="Encode"/> that did not fit the budget.</summary>
        public int LastDeferredItems { get; private set; }

        /// <summary>
        /// Encodes <paramref name="target"/> against <paramref name="baseline"/> (null — key frame) into
        /// <paramref name="buffer"/> at <paramref name="offset"/>, at most <paramref name="maxBytes"/> bytes, and fills
        /// <paramref name="result"/> with the view the receiver will have (header included, <c>Valid</c> set).
        /// <paramref name="target"/>.Header must be filled except <c>IsKeyframe</c> / <c>BaselineTick</c>. Events are written
        /// in the given order (newest first) while they fit. Returns the byte count, or 0 if even the mandatory part did not fit.
        /// </summary>
        public int Encode(SnapshotFrame baseline, SnapshotFrame target, NetSnapshotEvent[] events, int eventCount,
            SnapshotFrame result, byte[] buffer, int offset, int maxBytes)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (result == null || ReferenceEquals(result, target) || ReferenceEquals(result, baseline)) throw new ArgumentException("SnapshotEncoder: result must be a separate frame");
            bool key = baseline == null;
            if (!key && !SnapshotFrameRing.InBaselineRange(target.Header.ServerTick, baseline.Header.ServerTick))
                throw new ArgumentException("SnapshotEncoder: baseline out of range");

            if (key) result.Clear();
            else result.CopyFrom(baseline);
            result.Header = target.Header;
            result.Header.IsKeyframe = key;
            result.Header.BaselineTick = key ? 0 : baseline.Header.ServerTick;
            result.Valid = false;
            int tick = result.Header.ServerTick;

            _w.Reset(buffer, offset, maxBytes);
            _budgetBits = maxBytes * 8 - TerminatorReserveBits;
            _exhausted = false;
            LastDeferredItems = 0;

            WriteHeader(in result.Header);
            target.Mode.WriteDelta(_w, in result.Mode, tick);
            result.Mode = target.Mode;
            OwnFighterCodec.Write(_w, in target.Own, in result.Own, tick);
            result.Own = target.Own;
            OwnFighterCodec.WriteStatuses(_w, target.OwnStatuses, target.OwnStatusCount, result.OwnStatuses, result.OwnStatusCount, tick);
            Array.Copy(target.OwnStatuses, result.OwnStatuses, result.OwnStatuses.Length);
            result.OwnStatusCount = target.OwnStatusCount;

            WriteRemovals(target.Fighters, result.Fighters, NetProtocol.SlotBits);
            WriteRemovals(target.Projectiles, result.Projectiles, NetProtocol.ProjectileSlotBits);
            WriteRemovals(target.Areas, result.Areas, NetProtocol.AreaSlotBits);
            WriteRemovals(target.Pickups, result.Pickups, NetProtocol.PickupSlotBits);
            if (_w.IsOverflow || _w.BitsWritten > _budgetBits) return 0;

            WriteUpdates(target.Fighters, result.Fighters, NetProtocol.SlotBits, tick);
            WriteUpdates(target.Projectiles, result.Projectiles, NetProtocol.ProjectileSlotBits, tick);
            WriteUpdates(target.Areas, result.Areas, NetProtocol.AreaSlotBits, tick);
            WriteUpdates(target.Pickups, result.Pickups, NetProtocol.PickupSlotBits, tick);
            WriteCells(target, result);
            WriteEvents(events, eventCount, tick);

            if (_w.IsOverflow) return 0;
            result.Valid = true;
            return _w.BytesWritten;
        }

        private void WriteHeader(in SnapshotHeader h)
        {
            _w.WriteVarInt(h.ServerTick);
            _w.WriteBool(h.IsKeyframe);
            if (!h.IsKeyframe) _w.WriteVarUInt((uint)unchecked(h.ServerTick - h.BaselineTick));
            _w.WriteBits(EventPacker.SlotToWire(h.OwnSlot), NetProtocol.SlotBits);
            _w.WriteVarInt(unchecked(h.LastAppliedInputTick - h.ServerTick));
            _w.WriteVarInt(h.InputMarginMin);
            _w.WriteBool(h.BotControlled);
        }

        private void WriteRemovals<T>(SnapshotEntityList<T> target, SnapshotEntityList<T> result, int slotBits)
            where T : struct, IEquatable<T>, ISnapshotEntry<T>
        {
            for (int i = result.Count - 1; i >= 0; i--)
            {
                int slot = result.SlotAt(i);
                if (target.Contains(slot)) continue;
                _w.WriteBool(true);
                _w.WriteBits((uint)slot, slotBits);
                result.Remove(slot);
            }
            _w.WriteBool(false);
        }

        private void WriteUpdates<T>(SnapshotEntityList<T> target, SnapshotEntityList<T> result, int slotBits, int tick)
            where T : struct, IEquatable<T>, ISnapshotEntry<T>
        {
            for (int i = 0; i < target.Count; i++)
            {
                int slot = target.SlotAt(i);
                int at = result.IndexOf(slot);
                ref T item = ref target.ItemAt(i);
                if (at >= 0 && item.Equals(result.ItemAt(at))) continue;
                if (_exhausted || (at < 0 && result.IsFull))
                {
                    LastDeferredItems++;
                    continue;
                }
                T prev = at >= 0 ? result.ItemAt(at) : default;
                int mark = _w.BitsWritten;
                _w.WriteBool(true);
                _w.WriteBits((uint)slot, slotBits);
                item.WriteDelta(_w, in prev, tick);
                if (!Fits(mark)) continue;
                result.Set(slot, in item);
            }
            _w.WriteBool(false);
        }

        private void WriteCells(SnapshotFrame target, SnapshotFrame result)
        {
            int prev = -1;
            for (int word = 0; word < SnapshotFrame.CellWords; word++)
            {
                uint added = target.ChangedCells[word] & ~result.ChangedCells[word];
                while (added != 0u)
                {
                    int bit = LowestBit(added);
                    added &= added - 1u;
                    if (_exhausted)
                    {
                        LastDeferredItems++;
                        continue;
                    }
                    int cell = (word << 5) + bit;
                    int mark = _w.BitsWritten;
                    _w.WriteBool(true);
                    _w.WriteVarUInt((uint)(cell - prev - 1));
                    if (!Fits(mark)) continue;
                    result.SetCellChanged(cell);
                    prev = cell;
                }
            }
            _w.WriteBool(false);
        }

        private void WriteEvents(NetSnapshotEvent[] events, int count, int tick)
        {
            for (int i = 0; i < count; i++)
            {
                if (_exhausted)
                {
                    LastDeferredItems++;
                    continue;
                }
                int mark = _w.BitsWritten;
                _w.WriteBool(true);
                EventPacker.Write(_w, in events[i], tick);
                Fits(mark);
            }
            _w.WriteBool(false);
        }

        /// <summary>True if the item written since <paramref name="mark"/> fits; otherwise takes it back and closes the budget.</summary>
        private bool Fits(int mark)
        {
            if (!_w.IsOverflow && _w.BitsWritten <= _budgetBits) return true;
            _w.Rewind(mark);
            _exhausted = true;
            LastDeferredItems++;
            return false;
        }

        private static int LowestBit(uint v)
        {
            int n = 0;
            while ((v & 1u) == 0u)
            {
                v >>= 1;
                n++;
            }
            return n;
        }
    }
}
