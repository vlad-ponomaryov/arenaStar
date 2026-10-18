using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>ServerNotice</c> (reliable, server → client, Server-Spec §4.2, §5.3): maintenance or shutdown; <see cref="Seconds"/> —
    /// how long running matches may still last (0 — not limited).
    /// </summary>
    public struct ServerNoticeMessage
    {
        public ServerNoticeKind Kind;
        public int Seconds;

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.ServerNotice);
            w.WriteByte((byte)Kind);
            w.WriteVarUInt((uint)Seconds);
        }

        /// <summary>Reads the body after the kind byte. False — malformed.</summary>
        public bool Read(BitReader r)
        {
            Kind = (ServerNoticeKind)r.ReadByte();
            uint seconds = r.ReadVarUInt();
            if (r.IsOverflow || Kind == ServerNoticeKind.None || seconds > int.MaxValue) return false;
            Seconds = (int)seconds;
            return true;
        }
    }
}
