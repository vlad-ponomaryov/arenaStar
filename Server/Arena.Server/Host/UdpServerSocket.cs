#if NET8_0_OR_GREATER
using System;
using System.Net;
using System.Net.Sockets;
using Arena.Net;
using Arena.Net.Transport;

namespace Arena.Server.Host
{
    /// <summary>
    /// The server's UDP socket on the .NET 8+ <see cref="SocketAddress"/> API (Server-Spec §3.2, §4.6): <c>ReceiveFrom</c> /
    /// <c>SendTo</c> with <c>Span&lt;byte&gt;</c> and reused <see cref="SocketAddress"/> objects — no allocations per packet.
    /// Dual-stack when bound to <c>[::]</c>. Windows SIO_UDP_CONNRESET is off; <c>ConnectionReset</c> and <c>MessageSize</c> are
    /// counted and skipped. Only in the SDK build; the bootstrap build uses <c>PortableUdpServerSocket</c>.
    /// </summary>
    public sealed class UdpServerSocket : IServerDatagramSocket
    {
        // sockaddr_in / sockaddr_in6 layout: [0..1] family (platform-specific), [2..3] port (big-endian),
        // IPv4: [4..7] address; IPv6: [4..7] flow info, [8..23] address, [24..27] scope id.
        private const int PortOffset = 2;
        private const int IPv4Offset = 4;
        private const int IPv6Offset = 8;
        private const int IPv6FlowOffset = 4;
        private const int IPv6ScopeOffset = 24;
        private const int IPv6Bytes = 16;

        private readonly Socket _socket;
        private readonly AddressFamily _family;
        private readonly SocketAddress _receiveAddress;
        private readonly SocketAddress _sendAddress;
        private bool _closed;

        public UdpServerSocket(IPEndPoint local)
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
            IPEndPoint template = _family == AddressFamily.InterNetworkV6 ? new IPEndPoint(IPAddress.IPv6Any, 0) : new IPEndPoint(IPAddress.Any, 0);
            _receiveAddress = template.Serialize();
            _sendAddress = template.Serialize();
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
                    n = _socket.ReceiveFrom(buffer.AsSpan(), SocketFlags.None, _receiveAddress);
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
                from = Parse(_receiveAddress);
                return true;
            }
        }

        public bool SendTo(byte[] buffer, int offset, int length, in NetAddress to)
        {
            if (_closed) return false;
            if (!Fill(_sendAddress, to)) return false;
            try
            {
                _socket.SendTo(new ReadOnlySpan<byte>(buffer, offset, length), SocketFlags.None, _sendAddress);
                return true;
            }
            catch (SocketException)
            {
                ResetCount++;
                return false;
            }
        }

        private NetAddress Parse(SocketAddress address)
        {
            Span<byte> s = address.Buffer.Span;
            ushort port = (ushort)((s[PortOffset] << 8) | s[PortOffset + 1]);
            if (_family == AddressFamily.InterNetwork)
            {
                uint v4 = ((uint)s[IPv4Offset] << 24) | ((uint)s[IPv4Offset + 1] << 16) | ((uint)s[IPv4Offset + 2] << 8) | s[IPv4Offset + 3];
                return NetAddress.FromIPv4(v4, port);
            }
            ulong high = 0ul, low = 0ul;
            for (int i = 0; i < 8; i++) high = (high << 8) | s[IPv6Offset + i];
            for (int i = 8; i < IPv6Bytes; i++) low = (low << 8) | s[IPv6Offset + i];
            return new NetAddress(high, low, port);
        }

        private bool Fill(SocketAddress address, in NetAddress to)
        {
            Span<byte> s = address.Buffer.Span;
            s[PortOffset] = (byte)(to.Port >> 8);
            s[PortOffset + 1] = (byte)to.Port;
            if (_family == AddressFamily.InterNetwork)
            {
                if (!to.IsIPv4) return false;
                uint v4 = to.IPv4;
                s[IPv4Offset] = (byte)(v4 >> 24);
                s[IPv4Offset + 1] = (byte)(v4 >> 16);
                s[IPv4Offset + 2] = (byte)(v4 >> 8);
                s[IPv4Offset + 3] = (byte)v4;
                return true;
            }
            for (int i = 0; i < 4; i++)
            {
                s[IPv6FlowOffset + i] = 0;
                s[IPv6ScopeOffset + i] = 0;
            }
            for (int i = 0; i < 8; i++) s[IPv6Offset + i] = (byte)(to.High >> (56 - 8 * i));
            for (int i = 0; i < 8; i++) s[IPv6Offset + 8 + i] = (byte)(to.Low >> (56 - 8 * i));
            return true;
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _socket.Dispose();
        }
    }
}
#endif
