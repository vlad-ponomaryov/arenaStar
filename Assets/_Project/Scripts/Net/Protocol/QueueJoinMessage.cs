using Arena.Core.Modes;
using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>QueueJoin</c> (reliable, client → server, Server-Spec §4.2, §6.1): the mode and the chosen fighter (index into the
    /// content pack's fighters). The server checks both and answers <see cref="QueueRejectedMessage"/> when they are invalid.
    /// </summary>
    public struct QueueJoinMessage
    {
        public ModeKind Mode;
        public int FighterIndex;

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.QueueJoin);
            w.WriteByte((byte)Mode);
            w.WriteVarUInt((uint)FighterIndex);
        }

        /// <summary>Reads the body after the kind byte. False — malformed.</summary>
        public bool Read(BitReader r)
        {
            Mode = (ModeKind)r.ReadByte();
            uint fighter = r.ReadVarUInt();
            FighterIndex = fighter > int.MaxValue ? int.MaxValue : (int)fighter;
            return !r.IsOverflow;
        }
    }
}
