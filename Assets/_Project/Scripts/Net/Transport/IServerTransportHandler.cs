using Arena.Net.Protocol;

namespace Arena.Net.Transport
{
    /// <summary>The server application above <see cref="ServerTransport"/> (lobby, matches — S3.6, S3.8).</summary>
    public interface IServerTransportHandler : IConnectionHandler
    {
        /// <summary>
        /// A client proved its address and versions; return <see cref="RejectReason.None"/> to accept it, or the reason to
        /// refuse (<c>Maintenance</c>, <c>InvalidToken</c>, <c>MatchOver</c> …). <c>ServerFull</c> is checked by the transport.
        /// </summary>
        RejectReason Approve(ConnectPurpose purpose, bool hasToken, in NetToken token);

        void OnConnected(int connection);

        /// <summary>The connection ended (timeout, peer closed, limits, or the server closed it). Its slot is freed after this.</summary>
        void OnDisconnected(int connection, DisconnectReason reason);
    }
}
