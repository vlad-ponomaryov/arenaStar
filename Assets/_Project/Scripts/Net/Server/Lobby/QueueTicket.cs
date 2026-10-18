using Arena.Core.Modes;

namespace Arena.Net.Server.Lobby
{
    /// <summary>
    /// A place in a matchmaking queue (Server-Spec §6.2, §6.4): one per connection — the lobby knows nothing about the person,
    /// only the connection, the mode and the chosen fighter. Preallocated per connection index by the <see cref="Matchmaker"/>.
    /// </summary>
    public sealed class QueueTicket
    {
        public bool Active;
        public int Connection = -1;
        public ModeKind Mode;
        public int FighterIndex;

        /// <summary>When the ticket joined its queue (FIFO order; bot fill counts from the oldest ticket).</summary>
        public long EnqueuedMicros;

        /// <summary>When the last <c>QueueStatus</c> went out.</summary>
        public long LastStatusMicros;

        public void Clear()
        {
            Active = false;
            Connection = -1;
            Mode = ModeKind.None;
            FighterIndex = 0;
            EnqueuedMicros = 0;
            LastStatusMicros = 0;
        }
    }
}
