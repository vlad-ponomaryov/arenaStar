using System;

namespace Arena.Net.Protocol
{
    /// <summary>A 128-bit opaque token (match ticket, <c>rejoinToken</c>, Server-Spec §4.2, §7). Value type, no allocations.</summary>
    public readonly struct NetToken : IEquatable<NetToken>
    {
        public readonly ulong High;
        public readonly ulong Low;

        public NetToken(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public bool IsEmpty => High == 0ul && Low == 0ul;

        public bool Equals(NetToken other) => High == other.High && Low == other.Low;

        public override bool Equals(object obj) => obj is NetToken other && Equals(other);

        public override int GetHashCode() => (High ^ (Low * 0x9E3779B97F4A7C15ul)).GetHashCode();
    }
}
