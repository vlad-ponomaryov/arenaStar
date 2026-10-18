using System;
using System.Net;
using System.Net.Sockets;

namespace Arena.Net.Transport
{
    /// <summary>
    /// A UDP endpoint as a value type: 128-bit IPv6 address (IPv4 as IPv4-mapped <c>::ffff:a.b.c.d</c>) + port. Used as the
    /// key of connections and limiter entries without allocating an <see cref="IPEndPoint"/> per packet. Conversion to and
    /// from <see cref="IPEndPoint"/> allocates — only for setup and the portable socket.
    /// </summary>
    public readonly struct NetAddress : IEquatable<NetAddress>
    {
        private const ulong IPv4MappedPrefix = 0x0000FFFF00000000ul;
        private const int IPv6Bytes = 16;
        private const int IPv4Bytes = 4;

        /// <summary>Bytes 0..7 of the IPv6 address, big-endian.</summary>
        public readonly ulong High;

        /// <summary>Bytes 8..15 of the IPv6 address, big-endian.</summary>
        public readonly ulong Low;

        public readonly ushort Port;

        public NetAddress(ulong high, ulong low, ushort port)
        {
            High = high;
            Low = low;
            Port = port;
        }

        /// <summary>IPv4 <c>a.b.c.d</c> given as <c>a &lt;&lt; 24 | b &lt;&lt; 16 | c &lt;&lt; 8 | d</c>.</summary>
        public static NetAddress FromIPv4(uint address, ushort port) => new NetAddress(0ul, IPv4MappedPrefix | address, port);

        public static NetAddress FromIPv4(byte a, byte b, byte c, byte d, ushort port) =>
            FromIPv4(((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d, port);

        /// <summary>The IPv4 limited broadcast 255.255.255.255 on <paramref name="port"/> (LAN discovery, §3.2).</summary>
        public static NetAddress IPv4Broadcast(ushort port) => FromIPv4(uint.MaxValue, port);

        public bool IsIPv4 => High == 0ul && (Low & 0xFFFFFFFF00000000ul) == IPv4MappedPrefix;

        /// <summary>255.255.255.255 (any port).</summary>
        public bool IsBroadcast => IsIPv4 && IPv4 == uint.MaxValue;

        public uint IPv4 => (uint)Low;

        public bool IsEmpty => High == 0ul && Low == 0ul && Port == 0;

        /// <summary>Same host (address without the port): the per-IP limiter key.</summary>
        public bool SameHost(in NetAddress other) => High == other.High && Low == other.Low;

        public int HostHash()
        {
            ulong h = High * 0x9E3779B97F4A7C15ul ^ Low;
            h ^= h >> 29;
            h *= 0xBF58476D1CE4E5B9ul;
            h ^= h >> 32;
            return (int)h & int.MaxValue;
        }

        /// <summary>
        /// Private, loopback and link-local ranges (10/8, 172.16/12, 192.168/16, 127/8, 169.254/16, ::1, fc00::/7, fe80::/10):
        /// the LAN discovery answers only these (§3.2).
        /// </summary>
        public bool IsPrivate
        {
            get
            {
                if (IsIPv4)
                {
                    uint a = IPv4;
                    uint first = a >> 24;
                    uint second = (a >> 16) & 0xFFu;
                    return first == 10u || first == 127u || (first == 172u && second >= 16u && second <= 31u)
                        || (first == 192u && second == 168u) || (first == 169u && second == 254u);
                }
                if (High == 0ul && Low == 1ul) return true;
                uint top = (uint)(High >> 48);
                return (top & 0xFE00u) == 0xFC00u || (top & 0xFFC0u) == 0xFE80u;
            }
        }

        /// <summary>Reads 4 (IPv4) or 16 (IPv6) address bytes in network order.</summary>
        public static NetAddress FromBytes(byte[] bytes, int offset, int count, ushort port)
        {
            if (count == IPv4Bytes)
            {
                uint v = ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
                return FromIPv4(v, port);
            }
            if (count != IPv6Bytes) throw new ArgumentOutOfRangeException(nameof(count));
            ulong high = 0ul, low = 0ul;
            for (int i = 0; i < 8; i++) high = (high << 8) | bytes[offset + i];
            for (int i = 8; i < 16; i++) low = (low << 8) | bytes[offset + i];
            return new NetAddress(high, low, port);
        }

        /// <summary>Writes the 16 IPv6 address bytes in network order.</summary>
        public void WriteIPv6Bytes(byte[] destination, int offset)
        {
            for (int i = 0; i < 8; i++) destination[offset + i] = (byte)(High >> (56 - 8 * i));
            for (int i = 0; i < 8; i++) destination[offset + 8 + i] = (byte)(Low >> (56 - 8 * i));
        }

        /// <summary>Allocates (address bytes); IPv4-mapped IPv6 addresses become IPv4.</summary>
        public static NetAddress FromEndPoint(IPEndPoint endPoint)
        {
            if (endPoint == null) throw new ArgumentNullException(nameof(endPoint));
            IPAddress address = endPoint.Address;
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            byte[] bytes = address.GetAddressBytes();
            return FromBytes(bytes, 0, bytes.Length, (ushort)endPoint.Port);
        }

        /// <summary>Allocates. For an IPv4 socket, IPv4 addresses stay IPv4; for an IPv6 socket they are mapped.</summary>
        public IPEndPoint ToEndPoint(AddressFamily family)
        {
            if (IsIPv4 && family == AddressFamily.InterNetwork)
            {
                uint v = IPv4;
                var bytes4 = new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
                return new IPEndPoint(new IPAddress(bytes4), Port);
            }
            var bytes = new byte[IPv6Bytes];
            WriteIPv6Bytes(bytes, 0);
            return new IPEndPoint(new IPAddress(bytes), Port);
        }

        public bool Equals(NetAddress other) => High == other.High && Low == other.Low && Port == other.Port;

        public override bool Equals(object obj) => obj is NetAddress other && Equals(other);

        public override int GetHashCode() => HostHash() ^ (Port * 31);

        public static bool operator ==(NetAddress a, NetAddress b) => a.Equals(b);

        public static bool operator !=(NetAddress a, NetAddress b) => !a.Equals(b);

        /// <summary>Debug only (allocates).</summary>
        public override string ToString()
        {
            if (IsIPv4)
            {
                uint v = IPv4;
                return (v >> 24) + "." + ((v >> 16) & 255u) + "." + ((v >> 8) & 255u) + "." + (v & 255u) + ":" + Port;
            }
            return "[" + High.ToString("x16") + Low.ToString("x16") + "]:" + Port;
        }
    }
}
