namespace Arena.Net.Protocol
{
    /// <summary>State of a ticket in <see cref="QueueStatusMessage"/> (Server-Spec §6.2). Wire format.</summary>
    public enum QueueState : byte
    {
        None = 0,

        /// <summary>Waiting for players; bots fill the match when the oldest ticket has waited <c>BotFillSeconds</c>.</summary>
        Searching = 1,
    }
}
