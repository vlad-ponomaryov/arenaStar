using Arena.Core.Random;
using Arena.Net;
using Arena.Net.Serialization;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net
{
    public sealed class BitPackingTests
    {
        private readonly byte[] _buffer = new byte[NetLimits.MaxPacketBytes];
        private readonly BitWriter _writer = new BitWriter();
        private readonly BitReader _reader = new BitReader();

        [Test]
        public void MixedFields_RoundTrip()
        {
            _writer.Reset(_buffer);
            _writer.WriteBool(true);
            _writer.WriteBits(5u, 3);
            _writer.WriteByte(0xAB);
            _writer.WriteSByte(-100);
            _writer.WriteUInt16(65535);
            _writer.WriteInt16(short.MinValue);
            _writer.WriteUInt32(0xDEADBEEFu);
            _writer.WriteInt32(int.MinValue);
            _writer.WriteUInt64(0x0123456789ABCDEFul);
            _writer.WriteFloat(-1234.5678f);
            _writer.WriteFloat(float.NaN);
            _writer.WriteBool(false);
            _writer.WriteBits(0u, 0);
            _writer.WriteBits64(0x1FFFFFFFFFul, 37);
            Assert.IsFalse(_writer.IsOverflow);

            _reader.Reset(_buffer, _writer.BytesWritten);
            Assert.IsTrue(_reader.ReadBool());
            Assert.AreEqual(5u, _reader.ReadBits(3));
            Assert.AreEqual(0xAB, _reader.ReadByte());
            Assert.AreEqual(-100, _reader.ReadSByte());
            Assert.AreEqual(65535, _reader.ReadUInt16());
            Assert.AreEqual(short.MinValue, _reader.ReadInt16());
            Assert.AreEqual(0xDEADBEEFu, _reader.ReadUInt32());
            Assert.AreEqual(int.MinValue, _reader.ReadInt32());
            Assert.AreEqual(0x0123456789ABCDEFul, _reader.ReadUInt64());
            Assert.AreEqual(System.BitConverter.SingleToInt32Bits(-1234.5678f), System.BitConverter.SingleToInt32Bits(_reader.ReadFloat()));
            Assert.IsTrue(float.IsNaN(_reader.ReadFloat()));
            Assert.IsFalse(_reader.ReadBool());
            Assert.AreEqual(0u, _reader.ReadBits(0));
            Assert.AreEqual(0x1FFFFFFFFFul, _reader.ReadBits64(37));
            Assert.IsFalse(_reader.IsOverflow);
            Assert.AreEqual(_writer.BitsWritten, _reader.BitsRead);
        }

        [Test]
        public void RandomBitWidths_RoundTrip()
        {
            var rng = DeterministicRng.FromSeed(7);
            var widths = new int[2000];
            var values = new uint[2000];
            _writer.Reset(_buffer);
            int count = 0;
            while (count < widths.Length)
            {
                int bits = 1 + (int)(rng.NextUInt() % 32u);
                if (_writer.RemainingBits < bits) break;
                uint v = rng.NextUInt();
                if (bits < 32) v &= (1u << bits) - 1u;
                widths[count] = bits;
                values[count] = v;
                _writer.WriteBits(v, bits);
                count++;
            }
            Assert.Greater(count, 100);
            Assert.IsFalse(_writer.IsOverflow);

            _reader.Reset(_buffer, _writer.BytesWritten);
            for (int i = 0; i < count; i++) Assert.AreEqual(values[i], _reader.ReadBits(widths[i]), "field " + i);
            Assert.IsFalse(_reader.IsOverflow);
        }

        [Test]
        public void HighBitsOfValue_AreMasked()
        {
            _writer.Reset(_buffer);
            _writer.WriteBits(0xFFu, 3);
            _writer.WriteBits(0u, 5);
            Assert.AreEqual(1, _writer.BytesWritten);
            Assert.AreEqual(0x07, _buffer[0]);
        }

        [Test]
        public void LittleEndian_LsbFirstLayout()
        {
            _writer.Reset(_buffer);
            _writer.WriteUInt32(0x04030201u);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, new[] { _buffer[0], _buffer[1], _buffer[2], _buffer[3] });
        }

        [Test]
        public void Writer_ReusedBuffer_ClearsOldBits()
        {
            for (int i = 0; i < _buffer.Length; i++) _buffer[i] = 0xFF;
            _writer.Reset(_buffer);
            _writer.WriteBits(0u, 4);
            _writer.WriteBits(1u, 1);
            Assert.AreEqual(0x10, _buffer[0]);
        }

        [Test]
        public void Writer_Overflow_SetsFlagAndStops()
        {
            var small = new byte[2];
            _writer.Reset(small);
            _writer.WriteUInt16(0xFFFF);
            Assert.IsFalse(_writer.IsOverflow);
            _writer.WriteBool(true);
            Assert.IsTrue(_writer.IsOverflow);
            Assert.AreEqual(16, _writer.BitsWritten);
            _writer.WriteByte(1);
            Assert.AreEqual(16, _writer.BitsWritten);
        }

        [Test]
        public void Writer_OffsetAndLength_AreRespected()
        {
            for (int i = 0; i < 8; i++) _buffer[i] = 0xEE;
            _writer.Reset(_buffer, 2, 2);
            _writer.WriteUInt16(0x1234);
            _writer.WriteBool(true);
            Assert.IsTrue(_writer.IsOverflow);
            Assert.AreEqual(0xEE, _buffer[1]);
            Assert.AreEqual(0x34, _buffer[2]);
            Assert.AreEqual(0x12, _buffer[3]);
            Assert.AreEqual(0xEE, _buffer[4]);

            _reader.Reset(_buffer, 2, 2);
            Assert.AreEqual(0x1234, _reader.ReadUInt16());
            Assert.IsFalse(_reader.IsOverflow);
        }

        [Test]
        public void Reader_PastEnd_ReturnsZeroAndSetsFlag()
        {
            _buffer[0] = 0xFF;
            _reader.Reset(_buffer, 1);
            Assert.AreEqual(0x7Fu, _reader.ReadBits(7));
            Assert.AreEqual(0u, _reader.ReadBits(2));
            Assert.IsTrue(_reader.IsOverflow);
            Assert.AreEqual(0u, _reader.ReadBits(1), "stays failed");
            Assert.AreEqual(0, _reader.RemainingBits);
        }

        [Test]
        public void Bytes_AlignAndCopy()
        {
            var src = new byte[] { 9, 8, 7, 6, 5 };
            _writer.Reset(_buffer);
            _writer.WriteBits(1u, 3);
            _writer.WriteBytes(src, 1, 3);
            _writer.WriteZeroBytes(4);
            _writer.WriteBool(true);
            Assert.AreEqual(1 + 3 + 4 + 1, _writer.BytesWritten);

            var dst = new byte[3];
            _reader.Reset(_buffer, _writer.BytesWritten);
            Assert.AreEqual(1u, _reader.ReadBits(3));
            _reader.ReadBytes(dst, 0, 3);
            CollectionAssert.AreEqual(new byte[] { 8, 7, 6 }, dst);
            _reader.SkipBytes(4);
            Assert.IsTrue(_reader.ReadBool());
            Assert.IsFalse(_reader.IsOverflow);
        }

        [Test]
        public void Bytes_PastEnd_SetOverflow()
        {
            var small = new byte[3];
            _writer.Reset(small);
            _writer.WriteZeroBytes(4);
            Assert.IsTrue(_writer.IsOverflow);

            _reader.Reset(small, 3);
            _reader.SkipBytes(4);
            Assert.IsTrue(_reader.IsOverflow);
        }

        [TestCase(0u, 1)]
        [TestCase(1u, 1)]
        [TestCase(127u, 1)]
        [TestCase(128u, 2)]
        [TestCase(16383u, 2)]
        [TestCase(16384u, 3)]
        [TestCase(2097151u, 3)]
        [TestCase(2097152u, 4)]
        [TestCase(268435455u, 4)]
        [TestCase(268435456u, 5)]
        [TestCase(uint.MaxValue, 5)]
        public void VarUInt_RoundTripAndSize(uint value, int bytes)
        {
            Assert.AreEqual(bytes, VarInt.ByteCount(value));
            _writer.Reset(_buffer);
            _writer.WriteVarUInt(value);
            Assert.AreEqual(bytes, _writer.BytesWritten);
            _reader.Reset(_buffer, _writer.BytesWritten);
            Assert.AreEqual(value, _reader.ReadVarUInt());
            Assert.IsFalse(_reader.IsOverflow);
        }

        [TestCase(0ul, 1)]
        [TestCase(127ul, 1)]
        [TestCase(128ul, 2)]
        [TestCase(0xFFFFFFFFul, 5)]
        [TestCase(0x100000000ul, 5)]
        [TestCase(ulong.MaxValue, 10)]
        public void VarULong_RoundTripAndSize(ulong value, int bytes)
        {
            Assert.AreEqual(bytes, VarInt.ByteCount(value));
            _writer.Reset(_buffer);
            _writer.WriteVarULong(value);
            Assert.AreEqual(bytes, _writer.BytesWritten);
            _reader.Reset(_buffer, _writer.BytesWritten);
            Assert.AreEqual(value, _reader.ReadVarULong());
            Assert.IsFalse(_reader.IsOverflow);
        }

        [TestCase(0, 0u)]
        [TestCase(-1, 1u)]
        [TestCase(1, 2u)]
        [TestCase(-2, 3u)]
        [TestCase(2, 4u)]
        [TestCase(int.MaxValue, 0xFFFFFFFEu)]
        [TestCase(int.MinValue, 0xFFFFFFFFu)]
        public void ZigZag_Mapping(int value, uint encoded)
        {
            Assert.AreEqual(encoded, VarInt.ZigZagEncode(value));
            Assert.AreEqual(value, VarInt.ZigZagDecode(encoded));
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(1L)]
        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public void ZigZag64_RoundTrip(long value)
        {
            Assert.AreEqual(value, VarInt.ZigZagDecode64(VarInt.ZigZagEncode64(value)));
            _writer.Reset(_buffer);
            _writer.WriteVarLong(value);
            _reader.Reset(_buffer, _writer.BytesWritten);
            Assert.AreEqual(value, _reader.ReadVarLong());
        }

        [Test]
        public void VarInt_SignedTicks_RoundTrip()
        {
            int[] ticks = { 0, -1, -90, 1, 63, -64, 64, 4500, -4500, 10800, int.MaxValue, int.MinValue };
            _writer.Reset(_buffer);
            for (int i = 0; i < ticks.Length; i++) _writer.WriteVarInt(ticks[i]);
            _reader.Reset(_buffer, _writer.BytesWritten);
            for (int i = 0; i < ticks.Length; i++) Assert.AreEqual(ticks[i], _reader.ReadVarInt(), "tick " + ticks[i]);
            Assert.IsFalse(_reader.IsOverflow);
            Assert.AreEqual(1, VarInt.ByteCountSigned(-64));
            Assert.AreEqual(2, VarInt.ByteCountSigned(64));
        }

        [Test]
        public void VarInt_NotByteAligned_RoundTrip()
        {
            _writer.Reset(_buffer);
            _writer.WriteBits(1u, 1);
            _writer.WriteVarUInt(300u);
            _writer.WriteVarInt(-3);
            _reader.Reset(_buffer, _writer.BytesWritten);
            Assert.AreEqual(1u, _reader.ReadBits(1));
            Assert.AreEqual(300u, _reader.ReadVarUInt());
            Assert.AreEqual(-3, _reader.ReadVarInt());
        }

        [Test]
        public void VarUInt_TooLong_IsRejected()
        {
            for (int i = 0; i < 6; i++) _buffer[i] = 0x80;
            _reader.Reset(_buffer, 6);
            Assert.AreEqual(0u, _reader.ReadVarUInt());
            Assert.IsTrue(_reader.IsOverflow);
        }

        [Test]
        public void VarUInt_BitsBeyond32_AreRejected()
        {
            byte[] data = { 0xFF, 0xFF, 0xFF, 0xFF, 0x1F };
            _reader.Reset(data, data.Length);
            _reader.ReadVarUInt();
            Assert.IsTrue(_reader.IsOverflow);
        }

        [Test]
        public void VarUInt_Truncated_IsRejected()
        {
            _buffer[0] = 0x80;
            _reader.Reset(_buffer, 1);
            _reader.ReadVarUInt();
            Assert.IsTrue(_reader.IsOverflow);
        }

        [Test]
        public void EncodeDecode_DoesNotAllocate()
        {
            RoundTripPacket();
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 100; i++) RoundTripPacket();
            });
        }

        private void RoundTripPacket()
        {
            _writer.Reset(_buffer);
            for (int i = 0; i < 50; i++)
            {
                _writer.WriteVarInt(i - 25);
                _writer.WriteBits((uint)i, 6);
                _writer.WriteFloat(i * 0.5f);
                _writer.WriteUInt16((ushort)(i * 1000));
                _writer.WriteBool((i & 1) == 0);
            }
            _writer.WriteZeroBytes(16);
            _reader.Reset(_buffer, _writer.BytesWritten);
            for (int i = 0; i < 50; i++)
            {
                _reader.ReadVarInt();
                _reader.ReadBits(6);
                _reader.ReadFloat();
                _reader.ReadUInt16();
                _reader.ReadBool();
            }
            _reader.SkipBytes(16);
        }
    }
}
