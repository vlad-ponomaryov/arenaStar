using Arena.Core.Sim.Events;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// Wire form of a <see cref="NetSnapshotEvent"/> (Server-Spec §4.4 item 9): type 5 bits, age in ticks (snapshot tick −
    /// event tick, varuint), source and target 4 + 4 bits (<see cref="NetProtocol.NoSlot"/> = none), value zigzag varint,
    /// point flag + u16 × 2, ability ref + 1 as varuint (one byte for every fighter set below 42 fighters).
    /// </summary>
    public static class EventPacker
    {
        /// <summary>True for the event types snapshots carry (the subset views read online).</summary>
        public static bool IsViewEvent(SimEventType type) =>
            type == SimEventType.Damaged || type == SimEventType.Healed || type == SimEventType.AbilityFired
            || type == SimEventType.UltReady;

        public static void Write(BitWriter w, in NetSnapshotEvent e, int snapshotTick)
        {
            w.WriteBits((uint)e.Type, NetProtocol.EventTypeBits);
            w.WriteVarUInt((uint)unchecked(snapshotTick - e.Tick));
            w.WriteBits(SlotToWire(e.Source), NetProtocol.SlotBits);
            w.WriteBits(SlotToWire(e.Target), NetProtocol.SlotBits);
            w.WriteVarInt(e.Value);
            w.WriteBool(e.HasPoint);
            if (e.HasPoint)
            {
                w.WriteUInt16(e.PointX);
                w.WriteUInt16(e.PointY);
            }
            w.WriteVarUInt((uint)unchecked(e.AbilityRef + 1));
        }

        public static NetSnapshotEvent Read(BitReader r, int snapshotTick)
        {
            var e = new NetSnapshotEvent();
            e.Type = (SimEventType)r.ReadBits(NetProtocol.EventTypeBits);
            e.Tick = unchecked(snapshotTick - (int)r.ReadVarUInt());
            e.Source = SlotFromWire(r.ReadBits(NetProtocol.SlotBits));
            e.Target = SlotFromWire(r.ReadBits(NetProtocol.SlotBits));
            e.Value = r.ReadVarInt();
            e.HasPoint = r.ReadBool();
            if (e.HasPoint)
            {
                e.PointX = r.ReadUInt16();
                e.PointY = r.ReadUInt16();
            }
            e.AbilityRef = unchecked((int)r.ReadVarUInt() - 1);
            return e;
        }

        /// <summary>Slot (−1 or out of range → <see cref="NetProtocol.NoSlot"/>) as a 4-bit field.</summary>
        public static uint SlotToWire(int slot) => slot >= 0 && slot < NetProtocol.NoSlot ? (uint)slot : NetProtocol.NoSlot;

        public static int SlotFromWire(uint wire) => wire == NetProtocol.NoSlot ? -1 : (int)wire;
    }
}
