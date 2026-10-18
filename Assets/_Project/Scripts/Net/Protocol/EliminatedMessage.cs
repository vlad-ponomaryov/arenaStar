using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>Eliminated</c> (reliable, server → client, Server-Spec §4.2, §7): Last Beast — the receiver's fighter is out; its place
    /// and stats are final (the personal result screen). The connection stays for "Again".
    /// </summary>
    public struct EliminatedMessage
    {
        public int Tick;
        public int Slot;
        public MatchSlotResult Result;

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.Eliminated);
            w.WriteVarInt(Tick);
            w.WriteBits((uint)Slot, NetProtocol.SlotBits);
            Result.Write(w);
        }

        /// <summary>Reads the body after the kind byte. False — malformed.</summary>
        public bool Read(BitReader r)
        {
            Tick = r.ReadVarInt();
            Slot = (int)r.ReadBits(NetProtocol.SlotBits);
            Result.Read(r);
            return !r.IsOverflow;
        }
    }
}
