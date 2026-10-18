using System;
using System.Net;
using System.Net.Sockets;

namespace Arena.Net.Transport
{
    /// <summary>
    /// The client's unconnected UDP socket for the menu (Server-Spec §3.2, §6.3): region pings and LAN discovery
    /// (broadcast enabled). Allocates an endpoint per packet — fine in menus, never used in a match. Non-blocking, never throws
    /// in normal operation; Windows SIO_UDP_CONNRESET is off. IPv4 only for now (LAN broadcast is IPv4; regions of stage 3 are
    /// IPv4 hosts — an IPv6 region would need a second socket).
    /// </summary>
    public sealed class UdpProbeSocket : IServerDatagramSocket
    {
        private readonly Socket _socket;
        private EndPoint _any = new IPEndPoint(IPAddress.Any, 0);
        private bool _closed;

        /// <summary>Binds to <paramref name="local"/> (null — any address, a port the OS picks).</summary>
        public UdpProbeSocket(IPEndPoint local = null)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                SocketSetup.DisableConnectionReset(_socket);
                _socket.Blocking = false;
                _socket.EnableBroadcast = true;
                _socket.Bind(local ?? new IPEndPoint(IPAddress.Any, 0));
            }
            catch
            {
                _socket.Dispose();
                throw;
            }
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
            if (_closed || !to.IsIPv4) return false;
            try
            {
                _socket.SendTo(buffer, offset, length, SocketFlags.None, to.ToEndPoint(AddressFamily.InterNetwork));
                return true;
            }
            catch (SocketException)
            {
                ResetCount++;
                return false;
            }
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _socket.Dispose();
        }
    }
}
