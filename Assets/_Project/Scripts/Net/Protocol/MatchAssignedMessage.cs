using Arena.Core.Sim;
using Arena.Net.Serialization;
using Arena.Net.Transport;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>MatchAssigned</c> (reliable, server → client, Server-Spec §4.2, §6.1): the lobby assembled a match. The same connection
    /// now belongs to the match — <c>MatchStart</c> follows on it, no reconnect. <see cref="Address"/> is empty for "this
    /// server" (separate game nodes later); <see cref="RejoinToken"/> is the slot's ticket for a return (§7).
    /// </summary>
    public struct MatchAssignedMessage
    {
        public ulong MatchId;

        /// <summary>Empty — the match runs on this server.</summary>
        public NetAddress Address;

        public NetToken RejoinToken;
        public int Slot;

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.MatchAssigned);
            w.WriteUInt64(MatchId);
            bool hasAddress = !Address.IsEmpty;
            w.WriteBool(hasAddress);
            if (hasAddress)
            {
                w.WriteUInt64(Address.High);
                w.WriteUInt64(Address.Low);
                w.WriteUInt16(Address.Port);
            }
            w.WriteUInt64(RejoinToken.High);
            w.WriteUInt64(RejoinToken.Low);
            w.WriteBits((uint)Slot, NetProtocol.SlotBits);
        }

        /// <summary>Reads the body after the kind byte. False — malformed.</summary>
        public bool Read(BitReader r)
        {
            MatchId = r.ReadUInt64();
            Address = default;
            if (r.ReadBool())
            {
                ulong high = r.ReadUInt64();
                ulong low = r.ReadUInt64();
                ushort port = r.ReadUInt16();
                Address = new NetAddress(high, low, port);
            }
            ulong tokenHigh = r.ReadUInt64();
            ulong tokenLow = r.ReadUInt64();
            RejoinToken = new NetToken(tokenHigh, tokenLow);
            Slot = (int)r.ReadBits(NetProtocol.SlotBits);
            return !r.IsOverflow && !RejoinToken.IsEmpty && Slot < SimLimits.MaxFighters;
        }
    }
}
