using System;
using System.Net;
using System.Net.Sockets;
using Arena.Net.Transport;

namespace Arena.Net.Server.Transport
{
    /// <summary>
    /// The server's UDP socket on the .NET Standard 2.1 API (Server-Spec §3.2, §5.2): for the bootstrap build (no
    /// <c>NET8_0_OR_GREATER</c>) and for tests in Unity. <c>ReceiveFrom(byte[], ref EndPoint)</c> allocates an endpoint per
    /// packet — acceptable for LAN and light load (§4.6); the .NET 8+ host uses <c>UdpServerSocket</c> instead. Sending reuses
    /// cached <see cref="IPEndPoint"/>s. Non-blocking; Windows SIO_UDP_CONNRESET is off; <c>ConnectionReset</c> and
    /// <c>MessageSize</c> are counted and skipped, the receive loop never throws in normal operation.
    /// </summary>
    public sealed class PortableUdpServerSocket : IServerDatagramSocket
    {
        private readonly Socket _socket;
        private readonly AddressFamily _family;
        private readonly NetAddress[] _cacheKeys = new NetAddress[NetLimits.EndPointCacheSize];
        private readonly IPEndPoint[] _cacheValues = new IPEndPoint[NetLimits.EndPointCacheSize];
        private EndPoint _any;
        private bool _closed;

        /// <summary>
        /// Binds to <paramref name="local"/>. <c>IPAddress.IPv6Any</c> gives a dual-stack socket (IPv4 clients arrive as
        /// IPv4-mapped addresses). Tests bind 127.0.0.1 only (no firewall prompt). Port 0 — the OS picks one.
        /// </summary>
        public PortableUdpServerSocket(IPEndPoint local)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            _family = local.AddressFamily;
            _socket = new Socket(_family, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                if (_family == AddressFamily.InterNetworkV6 && local.Address.Equals(IPAddress.IPv6Any)) _socket.DualMode = true;
                SocketSetup.DisableConnectionReset(_socket);
                _socket.Blocking = false;
                _socket.ReceiveBufferSize = Math.Max(_socket.ReceiveBufferSize, NetLimits.SocketBufferBytes);
                _socket.SendBufferSize = Math.Max(_socket.SendBufferSize, NetLimits.SocketBufferBytes);
                _socket.Bind(local);
            }
            catch
            {
                _socket.Dispose();
                throw;
            }
            _any = _family == AddressFamily.InterNetworkV6 ? new IPEndPoint(IPAddress.IPv6Any, 0) : new IPEndPoint(IPAddress.Any, 0);
            LocalEndPoint = (IPEndPoint)_socket.LocalEndPoint;
        }

        public IPEndPoint LocalEndPoint { get; }

        public long ResetCount { get; private set; }

        public long OversizeCount { get; private set; }

        public bool TryReceive(byte[] buffer, out int length, out NetAddress from)
        {
            length = 0;
            from = default;
            if (_closed) return false;
            while (true)
            {
                if (_socket.Available <= 0 && !_socket.Poll(0, SelectMode.SelectRead)) return false;
                int n;
                try
                {
                    n = _socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref _any);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.WouldBlock) return false;
                    if (e.SocketErrorCode == SocketError.MessageSize)
                    {
                        OversizeCount++;
                        continue;
                    }
                    if (e.SocketErrorCode == SocketError.ConnectionReset || e.SocketErrorCode == SocketError.ConnectionRefused)
                    {
                        ResetCount++;
                        continue;
                    }
                    return false;
                }
                length = n;
                from = NetAddress.FromEndPoint((IPEndPoint)_any);
                return true;
            }
        }

        public bool SendTo(byte[] buffer, int offset, int length, in NetAddress to)
        {
            if (_closed) return false;
            IPEndPoint endPoint = EndPointOf(to);
            try
            {
                _socket.SendTo(buffer, offset, length, SocketFlags.None, endPoint);
                return true;
            }
            catch (SocketException)
            {
                // A full send buffer or an unreachable client: the packet is lost, like any UDP loss.
                ResetCount++;
                return false;
            }
        }

        private IPEndPoint EndPointOf(in NetAddress address)
        {
            int slot = (address.HostHash() ^ address.Port) & (NetLimits.EndPointCacheSize - 1);
            IPEndPoint cached = _cacheValues[slot];
            if (cached != null && _cacheKeys[slot] == address) return cached;
            cached = address.ToEndPoint(_family);
            _cacheKeys[slot] = address;
            _cacheValues[slot] = cached;
            return cached;
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _socket.Dispose();
        }
    }
}
