using System;
using Arena.Core.Math;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// Field-by-field delta coding of snapshot entries (Server-Spec §4.3, §4.4): every field costs one bit "changed" and, if
    /// changed, its new value. A new entry is coded against <c>default</c>, so the same code writes key frames and deltas.
    /// Ticks are written relative to the snapshot tick (zigzag varint — small for "soon" and "just now"); integer arithmetic is
    /// unchecked, so any tick value (sentinels too) round-trips. Floats are compared and written by their bits (exact).
    /// The reader mirrors every method; it never throws (overflow is a flag of <see cref="BitReader"/>).
    /// </summary>
    public static class DeltaCodec
    {
        public static void WriteBool(BitWriter w, bool value, bool prev) => w.WriteBool(value != prev);

        public static bool ReadBool(BitReader r, bool prev) => r.ReadBool() ? !prev : prev;

        public static void WriteInt(BitWriter w, int value, int prev)
        {
            bool changed = value != prev;
            w.WriteBool(changed);
            if (changed) w.WriteVarInt(value);
        }

        public static int ReadInt(BitReader r, int prev) => r.ReadBool() ? r.ReadVarInt() : prev;

        public static void WriteTick(BitWriter w, int value, int prev, int baseTick)
        {
            bool changed = value != prev;
            w.WriteBool(changed);
            if (changed) w.WriteVarInt(unchecked(value - baseTick));
        }

        public static int ReadTick(BitReader r, int prev, int baseTick) => r.ReadBool() ? unchecked(r.ReadVarInt() + baseTick) : prev;

        public static void WriteUInt(BitWriter w, uint value, uint prev)
        {
            bool changed = value != prev;
            w.WriteBool(changed);
            if (changed) w.WriteVarUInt(value);
        }

        public static uint ReadUInt(BitReader r, uint prev) => r.ReadBool() ? r.ReadVarUInt() : prev;

        /// <summary>Fixed-width field (quantized positions, angles, masks): <paramref name="bits"/> ≤ 32.</summary>
        public static void WriteBits(BitWriter w, uint value, uint prev, int bits)
        {
            bool changed = value != prev;
            w.WriteBool(changed);
            if (changed) w.WriteBits(value, bits);
        }

        public static uint ReadBits(BitReader r, uint prev, int bits) => r.ReadBool() ? r.ReadBits(bits) : prev;

        public static void WriteFloat(BitWriter w, float value, float prev)
        {
            bool changed = BitConverter.SingleToInt32Bits(value) != BitConverter.SingleToInt32Bits(prev);
            w.WriteBool(changed);
            if (changed) w.WriteFloat(value);
        }

        public static float ReadFloat(BitReader r, float prev) => r.ReadBool() ? r.ReadFloat() : prev;

        /// <summary>Both components as one field (they usually change together).</summary>
        public static void WriteVec2(BitWriter w, Vec2 value, Vec2 prev)
        {
            bool changed = BitConverter.SingleToInt32Bits(value.X) != BitConverter.SingleToInt32Bits(prev.X)
                || BitConverter.SingleToInt32Bits(value.Y) != BitConverter.SingleToInt32Bits(prev.Y);
            w.WriteBool(changed);
            if (!changed) return;
            w.WriteFloat(value.X);
            w.WriteFloat(value.Y);
        }

        public static Vec2 ReadVec2(BitReader r, Vec2 prev)
        {
            if (!r.ReadBool()) return prev;
            float x = r.ReadFloat();
            float y = r.ReadFloat();
            return new Vec2(x, y);
        }

        /// <summary>Quantized point (2 × u16) as one field.</summary>
        public static void WritePoint(BitWriter w, ushort x, ushort y, ushort prevX, ushort prevY)
        {
            bool changed = x != prevX || y != prevY;
            w.WriteBool(changed);
            if (!changed) return;
            w.WriteUInt16(x);
            w.WriteUInt16(y);
        }

        public static void ReadPoint(BitReader r, ref ushort x, ref ushort y)
        {
            if (!r.ReadBool()) return;
            x = r.ReadUInt16();
            y = r.ReadUInt16();
        }
    }
}
