using System;

namespace Arena.Net.Serialization
{
    /// <summary>
    /// Reads what <see cref="BitWriter"/> wrote. Input is untrusted: reading past the end does not throw, it returns zeros
    /// and sets <see cref="IsOverflow"/>; the caller checks it once after decoding and drops the packet. No allocations.
    /// </summary>
    public sealed class BitReader
    {
        private byte[] _buffer;
        private int _start;
        private int _lengthBits;
        private int _bitPos;

        public BitReader() { }

        public BitReader(byte[] buffer, int length) => Reset(buffer, 0, length);

        /// <summary>True if a read went past the end of the data or a varint was malformed (decoded values are invalid).</summary>
        public bool IsOverflow { get; private set; }

        public int BitsRead => _bitPos;

        public int BytesRead => (_bitPos + 7) >> 3;

        public int RemainingBits => _lengthBits - _bitPos;

        public void Reset(byte[] buffer, int length) => Reset(buffer, 0, length);

        public void Reset(byte[] buffer, int offset, int length)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || length < 0 || offset + length > buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));
            _buffer = buffer;
            _start = offset;
            _lengthBits = length * 8;
            _bitPos = 0;
            IsOverflow = false;
        }

        /// <summary>Reads <paramref name="bits"/> (0...32) bits.</summary>
        public uint ReadBits(int bits)
        {
            if (bits <= 0) return 0u;
            if (bits > 32) bits = 32;
            if (IsOverflow || _bitPos + bits > _lengthBits)
            {
                Fail();
                return 0u;
            }
            uint value = 0u;
            int shift = 0;
            while (bits > 0)
            {
                int index = _start + (_bitPos >> 3);
                int offset = _bitPos & 7;
                int n = 8 - offset;
                if (n > bits) n = bits;
                uint chunk = ((uint)_buffer[index] >> offset) & ((1u << n) - 1u);
                value |= chunk << shift;
                shift += n;
                bits -= n;
                _bitPos += n;
            }
            return value;
        }

        /// <summary>Reads <paramref name="bits"/> (0...64) bits.</summary>
        public ulong ReadBits64(int bits)
        {
            if (bits <= 32) return ReadBits(bits);
            ulong low = ReadBits(32);
            ulong high = ReadBits(bits > 64 ? 32 : bits - 32);
            return low | (high << 32);
        }

        public bool ReadBool() => ReadBits(1) != 0u;

        public byte ReadByte() => (byte)ReadBits(8);

        public sbyte ReadSByte() => (sbyte)(byte)ReadBits(8);

        public ushort ReadUInt16() => (ushort)ReadBits(16);

        public short ReadInt16() => (short)(ushort)ReadBits(16);

        public uint ReadUInt32() => ReadBits(32);

        public int ReadInt32() => (int)ReadBits(32);

        public ulong ReadUInt64() => ReadBits64(64);

        public float ReadFloat() => BitConverter.Int32BitsToSingle((int)ReadBits(32));

        /// <summary>Reads an unsigned LEB128 value. More than 5 bytes, or bits beyond 32, mark the data invalid.</summary>
        public uint ReadVarUInt()
        {
            uint result = 0u;
            for (int i = 0; i < VarInt.MaxBytes32; i++)
            {
                uint b = ReadBits(8);
                if (IsOverflow) return 0u;
                if (i == VarInt.MaxBytes32 - 1 && b > 0x0Fu)
                {
                    Fail();
                    return 0u;
                }
                result |= (b & 0x7Fu) << (7 * i);
                if ((b & 0x80u) == 0u) return result;
            }
            Fail();
            return 0u;
        }

        /// <summary>Reads an unsigned LEB128 value of up to 64 bits (10 bytes).</summary>
        public ulong ReadVarULong()
        {
            ulong result = 0ul;
            for (int i = 0; i < VarInt.MaxBytes64; i++)
            {
                uint b = ReadBits(8);
                if (IsOverflow) return 0ul;
                if (i == VarInt.MaxBytes64 - 1 && b > 0x01u)
                {
                    Fail();
                    return 0ul;
                }
                result |= (ulong)(b & 0x7Fu) << (7 * i);
                if ((b & 0x80u) == 0u) return result;
            }
            Fail();
            return 0ul;
        }

        public int ReadVarInt() => VarInt.ZigZagDecode(ReadVarUInt());

        public long ReadVarLong() => VarInt.ZigZagDecode64(ReadVarULong());

        public void AlignToByte()
        {
            int pad = (8 - (_bitPos & 7)) & 7;
            if (pad > 0) ReadBits(pad);
        }

        /// <summary>Byte-aligns, then copies <paramref name="count"/> bytes into <paramref name="destination"/>.</summary>
        public void ReadBytes(byte[] destination, int offset, int count)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (offset < 0 || count < 0 || offset + count > destination.Length) throw new ArgumentOutOfRangeException(nameof(count));
            AlignToByte();
            if (IsOverflow || _bitPos + count * 8 > _lengthBits)
            {
                Fail();
                return;
            }
            System.Buffer.BlockCopy(_buffer, _start + (_bitPos >> 3), destination, offset, count);
            _bitPos += count * 8;
        }

        /// <summary>Byte-aligns, then skips <paramref name="count"/> bytes (handshake padding).</summary>
        public void SkipBytes(int count)
        {
            if (count <= 0) return;
            AlignToByte();
            if (IsOverflow || _bitPos + count * 8 > _lengthBits)
            {
                Fail();
                return;
            }
            _bitPos += count * 8;
        }

        private void Fail()
        {
            IsOverflow = true;
            _bitPos = _lengthBits;
        }
    }
}
