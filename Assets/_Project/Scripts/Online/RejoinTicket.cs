using Arena.Net.Protocol;
using Arena.Net.Transport;

namespace Arena.Online
{
    /// <summary>What a return to a running match needs (Server-Spec §7): the match server, the match, the slot and the rejoin
    /// token from <c>MatchAssigned</c>, and when it was saved. No personal data.</summary>
    public struct RejoinTicket
    {
        public NetAddress Server;
        public ulong MatchId;
        public int Slot;
        public NetToken Token;

        /// <summary>Unix time of saving, seconds.</summary>
        public long SavedAtSeconds;
    }
}
