using Arena.Core.Sim;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>MatchEnded</c> (reliable, server → client, Server-Spec §4.2): end tick, winner team (−1 — draw), MVP slot (−1 — none)
    /// and the result of every slot. ≈ 10 B per slot — far below <c>MaxReliableMessageBytes</c>. Preallocated, no allocations.
    /// </summary>
    public sealed class MatchEndedMessage
    {
        public int EndTick;
        public int WinnerTeam;
        public int MvpSlot;
        public int SlotCount;
        public readonly MatchSlotResult[] Slots = new MatchSlotResult[SimLimits.MaxFighters];

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.MatchEnded);
            w.WriteVarInt(EndTick);
            w.WriteVarInt(WinnerTeam);
            w.WriteBits(EventPacker.SlotToWire(MvpSlot), NetProtocol.SlotBits);
            w.WriteVarUInt((uint)SlotCount);
            for (int i = 0; i < SlotCount; i++) Slots[i].Write(w);
        }

        /// <summary>Reads the body after the kind byte. False — malformed.</summary>
        public bool Read(BitReader r)
        {
            EndTick = r.ReadVarInt();
            WinnerTeam = r.ReadVarInt();
            MvpSlot = EventPacker.SlotFromWire(r.ReadBits(NetProtocol.SlotBits));
            uint count = r.ReadVarUInt();
            if (r.IsOverflow || count > (uint)Slots.Length) return false;
            SlotCount = (int)count;
            for (int i = 0; i < SlotCount; i++) Slots[i].Read(r);
            return !r.IsOverflow;
        }
    }
}
