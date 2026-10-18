using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Client
{
    /// <summary>
    /// Receiver of the match messages <see cref="NetClient"/> takes off the connection (Server-Spec §4.2): reliable
    /// <c>MatchStart</c> / <c>MatchEnded</c> / <c>Eliminated</c> (the reader is positioned after the kind byte) and unreliable
    /// snapshots (the bytes after the kind byte). Return false for a malformed message (counted by the client).
    /// </summary>
    public interface IMatchMessageHandler
    {
        bool OnMatchMessage(MessageKind kind, BitReader reader);

        bool OnSnapshot(byte[] buffer, int offset, int length);
    }
}
