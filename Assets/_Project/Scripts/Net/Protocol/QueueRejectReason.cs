namespace Arena.Net.Protocol
{
    /// <summary>Why the lobby refused <c>QueueJoin</c> or dropped a ticket (<see cref="QueueRejectedMessage"/>, Server-Spec §6). Wire format.</summary>
    public enum QueueRejectReason : byte
    {
        None = 0,

        /// <summary>The mode is not played online on this server (no maps for it in the content pack).</summary>
        InvalidMode = 1,

        /// <summary>The fighter index is outside the content pack's fighter list.</summary>
        InvalidFighter = 2,

        /// <summary>The server is in maintenance: no new matches, running ones play to the end (§5.3).</summary>
        Maintenance = 3,

        /// <summary>The connection already belongs to a match.</summary>
        InMatch = 4,
    }
}
