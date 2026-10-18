using System;
using System.Net;
using System.Net.Sockets;

namespace Arena.Net.Transport
{
    /// <summary>
    /// The client's connected UDP socket (Server-Spec §3.2): <c>Connect</c> once, then <c>Send/Receive(byte[], …, out
    /// SocketError)</c> — no <see cref="EndPoint"/> per packet and no exceptions in normal operation (exceptions allocate).
    /// Non-blocking, polled from the main thread. The socket family follows the resolved address (A or AAAA), so IPv6-only
    /// networks work. Construction allocates; the hot path does not.
    /// </summary>
    public sealed class UdpDatagramSocket : IDatagramSocket
    {
        private readonly Socket _socket;
        private bool _closed;

        public UdpDatagramSocket(IPEndPoint remote)
        {
            if (remote == null) throw new ArgumentNullException(nameof(remote));
            _socket = new Socket(remote.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                SocketSetup.DisableConnectionReset(_socket);
                _socket.Blocking = false;
                _socket.ReceiveBufferSize = Math.Max(_socket.ReceiveBufferSize, NetLimits.SocketBufferBytes);
                _socket.Connect(remote);
                Remote = remote;
            }
            catch
            {
                _socket.Dispose();
                throw;
            }
        }

        public IPEndPoint Remote { get; }

        /// <summary>The local endpoint chosen by the OS (allocates; diagnostics and tests).</summary>
        public IPEndPoint Local => (IPEndPoint)_socket.LocalEndPoint;

        public bool PathBroken { get; private set; }

        public long ResetCount { get; private set; }

        public long OversizeCount { get; private set; }

        public bool Send(byte[] buffer, int offset, int length)
        {
            if (_closed) return false;
            _socket.Send(buffer, offset, length, SocketFlags.None, out SocketError error);
            if (error == SocketError.Success) return true;
            if (SocketSetup.IsPathError(error)) PathBroken = true;
            else if (error == SocketError.ConnectionReset || error == SocketError.ConnectionRefused) ResetCount++;
            return false;
        }

        public bool TryReceive(byte[] buffer, out int length)
        {
            length = 0;
            if (_closed) return false;
            while (true)
            {
                int n = _socket.Receive(buffer, 0, buffer.Length, SocketFlags.None, out SocketError error);
                switch (error)
                {
                    case SocketError.Success:
                        length = n;
                        return true;
                    case SocketError.WouldBlock:
                        return false;
                    case SocketError.MessageSize:
                        OversizeCount++;
                        continue;
                    case SocketError.ConnectionReset:
                    case SocketError.ConnectionRefused:
                        ResetCount++;
                        continue;
                    default:
                        if (SocketSetup.IsPathError(error)) PathBroken = true;
                        return false;
                }
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
