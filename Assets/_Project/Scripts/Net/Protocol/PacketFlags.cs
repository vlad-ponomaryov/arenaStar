namespace Arena.Net.Protocol
{
    /// <summary>The high 4 bits of the first header byte (Server-Spec §3.2).</summary>
    [System.Flags]
    public enum PacketFlags : byte
    {
        None = 0,

        /// <summary>A <see cref="PacketKind.Data"/> packet carries a reliable section.</summary>
        HasReliable = 1,

        /// <summary>A <see cref="PacketKind.Data"/> packet carries an unreliable payload (to the end of the packet).</summary>
        HasPayload = 2,

        /// <summary>The ack fields of the header are valid (the sender has received at least one packet).</summary>
        HasAcks = 4,

        /// <summary>Reserved for stage 4: the packet ends with an 8-byte SipHash tag (D-074, D-088). Never set on stage 3.</summary>
        Signed = 8,
    }
}
