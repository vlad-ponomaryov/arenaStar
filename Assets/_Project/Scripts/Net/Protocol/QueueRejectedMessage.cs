using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary><c>QueueRejected</c> (reliable, server → client, Server-Spec §6): the join was refused or the ticket dropped; no ticket is left.</summary>
    public struct QueueRejectedMessage
    {
        public QueueRejectReason Reason;

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.QueueRejected);
            w.WriteByte((byte)Reason);
        }

        /// <summary>Reads the body after the kind byte. False — malformed.</summary>
        public bool Read(BitReader r)
        {
            Reason = (QueueRejectReason)r.ReadByte();
            return !r.IsOverflow && Reason != QueueRejectReason.None;
        }
    }
}
