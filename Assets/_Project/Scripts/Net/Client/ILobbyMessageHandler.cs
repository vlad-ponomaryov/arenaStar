using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Client
{
    /// <summary>
    /// Receiver of the lobby messages <see cref="NetClient"/> takes off the connection (Server-Spec §4.2, §6.1): reliable
    /// <c>MatchAssigned</c> / <c>QueueRejected</c> / <c>ServerNotice</c> and the unreliable <c>QueueStatus</c>; the reader is
    /// positioned after the kind byte. Return false for a malformed message (counted by the client).
    /// </summary>
    public interface ILobbyMessageHandler
    {
        bool OnLobbyMessage(MessageKind kind, BitReader reader);
    }
}
