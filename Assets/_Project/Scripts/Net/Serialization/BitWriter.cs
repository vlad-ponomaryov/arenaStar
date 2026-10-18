using System;

namespace Arena.Net.Serialization
{
    /// <summary>
    /// Writes bits into a preallocated byte buffer, least significant bit first, little-endian (Server-Spec §4.3).
    /// No allocations: one instance is reused with <see cref="Reset(byte[], int, int)"/>. Writing past the end does not
    /// throw (that would allocate in the hot path): it sets <see cref="IsOverflow"/>, ignores the rest, and the caller
    /// drops the packet.
    /// </summary>
    public sealed class BitWriter
    {
        private byte[] _buffer;
        private int _start;
        private int _capacityBits;
        private int _bitPos;

        public BitWriter() { }

        public BitWriter(byte[] buffer) => Reset(buffer);

        /// <summary>True if a write did not fit; everything after it was ignored.</summary>
        public bool IsOverflow { get; private set; }

        /// <summary>Bits written since the last reset.</summary>
        public int BitsWritten => _bitPos;

        /// <summary>Bytes used, the last one possibly partial (its unused high bits are zero).</summary>
        public int BytesWritten => (_bitPos + 7) >> 3;

        public int CapacityBits => _capacityBits;

        public int RemainingBits => _capacityBits - _bitPos;

        public byte[] Buffer => _buffer;

        public void Reset(byte[] buffer) => Reset(buffer, 0, buffer == null ? 0 : buffer.Length);

        /// <summary>Starts writing at <paramref name="offset"/>; at most <paramref name="length"/> bytes are used.</summary>
        public void Reset(byte[] buffer, int offset, int length)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || length < 0 || offset + length > buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));
            _buffer = buffer;
            _start = offset;
            _capacityBits = length * 8;
            _bitPos = 0;
            IsOverflow = false;
        }

        /// <summary>Writes the low <paramref name="bits"/> (0...32) bits of <paramref name="value"/>.</summary>
        public void WriteBits(uint value, int bits)
        {
            if (bits <= 0) return;
            if (bits > 32) bits = 32;
            if (IsOverflow || _bitPos + bits > _capacityBits)
            {
                IsOverflow = true;
                return;
            }
            if (bits < 32) value &= (1u << bits) - 1u;
            while (bits > 0)
            {
                int index = _start + (_bitPos >> 3);
                int offset = _bitPos & 7;
                int n = 8 - offset;
                if (n > bits) n = bits;
                if (offset == 0) _buffer[index] = 0;
                _buffer[index] |= (byte)((value & ((1u << n) - 1u)) << offset);
                value >>= n;
                bits -= n;
                _bitPos += n;
            }
        }

        /// <summary>Writes the low <paramref name="bits"/> (0...64) bits of <paramref name="value"/>.</summary>
        public void WriteBits64(ulong value, int bits)
        {
            if (bits <= 32)
            {
                WriteBits((uint)value, bits);
                return;
            }
            WriteBits((uint)value, 32);
            WriteBits((uint)(value >> 32), bits > 64 ? 32 : bits - 32);
        }

        public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

        public void WriteByte(byte value) => WriteBits(value, 8);

        public void WriteSByte(sbyte value) => WriteBits((byte)value, 8);

        public void WriteUInt16(ushort value) => WriteBits(value, 16);

        public void WriteInt16(short value) => WriteBits((ushort)value, 16);

        public void WriteUInt32(uint value) => WriteBits(value, 32);

        public void WriteInt32(int value) => WriteBits((uint)value, 32);

        public void WriteUInt64(ulong value) => WriteBits64(value, 64);

        /// <summary>Writes the IEEE 754 bits of <paramref name="value"/> unchanged (exact).</summary>
        public void WriteFloat(float value) => WriteBits((uint)BitConverter.SingleToInt32Bits(value), 32);

        /// <summary>Unsigned LEB128 (<see cref="VarInt"/>): 1 byte below 128, up to 5 bytes.</summary>
        public void WriteVarUInt(uint value)
        {
            while (value >= 0x80u)
            {
                WriteBits((value & 0x7Fu) | 0x80u, 8);
                value >>= 7;
            }
            WriteBits(value, 8);
        }

        public void WriteVarULong(ulong value)
        {
            while (value >= 0x80ul)
            {
                WriteBits((uint)(value & 0x7Ful) | 0x80u, 8);
                value >>= 7;
            }
            WriteBits((uint)value, 8);
        }

        /// <summary>Signed value as zigzag varint (ticks: past, future and negative countdown ticks).</summary>
        public void WriteVarInt(int value) => WriteVarUInt(VarInt.ZigZagEncode(value));

        public void WriteVarLong(long value) => WriteVarULong(VarInt.ZigZagEncode64(value));

        /// <summary>Pads with zero bits up to the next byte boundary.</summary>
        public void AlignToByte()
        {
            int pad = (8 - (_bitPos & 7)) & 7;
            if (pad > 0) WriteBits(0u, pad);
        }

        /// <summary>Byte-aligns, then copies <paramref name="count"/> bytes.</summary>
        public void WriteBytes(byte[] source, int offset, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (offset < 0 || count < 0 || offset + count > source.Length) throw new ArgumentOutOfRangeException(nameof(count));
            AlignToByte();
            if (IsOverflow || _bitPos + count * 8 > _capacityBits)
            {
                IsOverflow = true;
                return;
            }
            System.Buffer.BlockCopy(source, offset, _buffer, _start + (_bitPos >> 3), count);
            _bitPos += count * 8;
        }

        /// <summary>Byte-aligns, then writes <paramref name="count"/> zero bytes (handshake padding to 1200 B, §3.2).</summary>
        public void WriteZeroBytes(int count)
        {
            if (count <= 0) return;
            AlignToByte();
            if (IsOverflow || _bitPos + count * 8 > _capacityBits)
            {
                IsOverflow = true;
                return;
            }
            Array.Clear(_buffer, _start + (_bitPos >> 3), count);
            _bitPos += count * 8;
        }
    }
}
