using System;
using System.IO;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.State;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Net;
using Arena.Net.Content;
using Arena.Net.Diagnostics;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Server
{
    /// <summary>
    /// <c>--selftest</c>: checks that run on the server's own runtime (CoreCLR), outside Unity (Server-Spec §11.2). Stage 3.1:
    /// bit packing round trip, varint / zigzag edges, aim quantization, <c>MatchState.CopyFrom</c> / <c>ContentEquals</c>,
    /// zero allocations of the coder (<c>GC.GetAllocatedBytesForCurrentThread</c> is exact on CoreCLR); S3.2: the content
    /// pack re-encodes to the same bytes, headless Acorns match of bots by the pack (ms/tick, two runs give one
    /// <c>StateHash</c>). Real UDP arrives in S3.9.
    /// </summary>
    public static class SelfTest
    {
        private const int RandomFields = 4000;
        private const int AllocationRounds = 2000;
        private const int QuantizeSamples = 20000;

        private static int _passed;
        private static int _failed;
        private const int HeadlessSeed = 20261018;

        private static TextWriter _out;
        private static ServerContent _content;

        public static bool Run(TextWriter output, ServerContent content)
        {
            _out = output;
            _content = content;
            _passed = 0;
            _failed = 0;
            Check("bit packing round trip", BitPackingRoundTrip);
            Check("varint and zigzag edges", VarIntEdges);
            Check("reader rejects truncated data", ReaderRejectsTruncated);
            Check("aim and move quantization", QuantizeAimMove);
            Check("MatchState CopyFrom / ContentEquals", MatchStateCopy);
            Check("coder allocates 0 bytes", CoderAllocations);
            Check("content pack re-encodes to the same bytes", ContentPackRoundTrip);
            Check("headless Acorns match of bots, two runs give one state hash", HeadlessAcornsMatch);
            _out.WriteLine("SELFTEST: " + (_failed == 0 ? "OK" : "FAILED") + ", passed " + _passed + ", failed " + _failed);
            return _failed == 0;
        }

        private static void Check(string name, Func<string> check)
        {
            string error;
            try
            {
                error = check();
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
            }
            if (error == null)
            {
                _passed++;
                _out.WriteLine("  ok    " + name);
            }
            else
            {
                _failed++;
                _out.WriteLine("  FAIL  " + name + ": " + error);
            }
        }

        private static string BitPackingRoundTrip()
        {
            var buffer = new byte[RandomFields * 8];
            var widths = new int[RandomFields];
            var values = new ulong[RandomFields];
            var rng = DeterministicRng.FromSeed(2024);
            var writer = new BitWriter(buffer);
            for (int i = 0; i < RandomFields; i++)
            {
                int bits = 1 + (int)(rng.NextUInt() % 64u);
                ulong v = rng.NextULong();
                if (bits < 64) v &= (1ul << bits) - 1ul;
                widths[i] = bits;
                values[i] = v;
                writer.WriteBits64(v, bits);
            }
            if (writer.IsOverflow) return "writer overflow";
            var reader = new BitReader(buffer, writer.BytesWritten);
            for (int i = 0; i < RandomFields; i++)
            {
                ulong got = reader.ReadBits64(widths[i]);
                if (got != values[i]) return "field " + i + ": " + got + " != " + values[i];
            }
            if (reader.IsOverflow) return "reader overflow";
            writer.Reset(buffer);
            writer.WriteFloat(-0.1f);
            writer.WriteFloat(float.Epsilon);
            reader.Reset(buffer, writer.BytesWritten);
            if (BitConverter.SingleToInt32Bits(reader.ReadFloat()) != BitConverter.SingleToInt32Bits(-0.1f)) return "float bits";
            if (reader.ReadFloat() != float.Epsilon) return "denormal float";
            return null;
        }

        private static string VarIntEdges()
        {
            var buffer = new byte[256];
            int[] ints = { 0, -1, 1, -64, 63, 64, -65, 4500, -90, int.MaxValue, int.MinValue };
            uint[] uints = { 0u, 127u, 128u, 16383u, 16384u, uint.MaxValue };
            var writer = new BitWriter(buffer);
            writer.WriteBool(true);
            for (int i = 0; i < ints.Length; i++) writer.WriteVarInt(ints[i]);
            for (int i = 0; i < uints.Length; i++) writer.WriteVarUInt(uints[i]);
            writer.WriteVarULong(ulong.MaxValue);
            writer.WriteVarLong(long.MinValue);
            var reader = new BitReader(buffer, writer.BytesWritten);
            reader.ReadBool();
            for (int i = 0; i < ints.Length; i++)
                if (reader.ReadVarInt() != ints[i]) return "varint " + ints[i];
            for (int i = 0; i < uints.Length; i++)
                if (reader.ReadVarUInt() != uints[i]) return "varuint " + uints[i];
            if (reader.ReadVarULong() != ulong.MaxValue) return "varulong max";
            if (reader.ReadVarLong() != long.MinValue) return "varlong min";
            if (reader.IsOverflow) return "reader overflow";
            if (VarInt.ZigZagEncode(-1) != 1u || VarInt.ZigZagEncode(int.MinValue) != uint.MaxValue) return "zigzag mapping";
            if (VarInt.ByteCount(uint.MaxValue) != VarInt.MaxBytes32) return "byte count";
            return null;
        }

        private static string ReaderRejectsTruncated()
        {
            var data = new byte[] { 0x80, 0x80 };
            var reader = new BitReader(data, data.Length);
            reader.ReadVarUInt();
            if (!reader.IsOverflow) return "truncated varint accepted";
            reader.Reset(data, 1);
            reader.ReadBits(9);
            if (!reader.IsOverflow) return "read past end accepted";
            return null;
        }

        private static string QuantizeAimMove()
        {
            var rng = DeterministicRng.FromSeed(77);
            for (int i = 0; i < QuantizeSamples; i++)
            {
                var v = new Vec2(rng.NextFloat01() * 2.2f - 1.1f, rng.NextFloat01() * 2.2f - 1.1f);
                Quantize.ToAim(v, out short ax, out short ay);
                Vec2 aim = Quantize.FromAim(ax, ay);
                if (aim.X != ax / (float)NetProtocol.AimScale || aim.Y != ay / (float)NetProtocol.AimScale) return "aim decode is not a division";
                Quantize.ToAim(aim, out short ax2, out short ay2);
                if (ax2 != ax || ay2 != ay) return "aim re-quantization changed bits for " + v;
                if (aim.SqrLength > 1f) return "aim longer than 1 for " + v;
                Quantize.ToMove(v, out sbyte mx, out sbyte my);
                Vec2 move = Quantize.FromMove(mx, my);
                if (move.SqrLength > 1f) return "move longer than 1 for " + v;
            }
            return null;
        }

        private static string MatchStateCopy()
        {
            var a = new MatchState();
            a.Globals.Tick = 123;
            a.Globals.FighterCount = 2;
            a.Globals.Rng = DeterministicRng.FromSeed(5);
            a.Fighters[1].Active = true;
            a.Fighters[1].Position = new Vec2(3.25f, 7.5f);
            a.Fighters[1].Health = 4200;
            a.Projectiles[10].Active = true;
            var b = new MatchState();
            if (b.ContentEquals(a)) return "different states compare equal";
            b.CopyFrom(a);
            if (!b.ContentEquals(a)) return "copy differs from source";
            b.Fighters[1].Health--;
            if (b.ContentEquals(a)) return "changed fighter not detected";
            b.CopyFrom(a);
            b.Clear();
            if (b.ContentEquals(a) || b.Globals.Tick != 0) return "Clear did not reset";
            return null;
        }

        private static string CoderAllocations()
        {
            var buffer = new byte[NetLimits.MaxPacketBytes];
            var writer = new BitWriter();
            var reader = new BitReader();
            EncodeDecode(writer, reader, buffer);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < AllocationRounds; i++) EncodeDecode(writer, reader, buffer);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            _out.WriteLine("        coder: " + AllocationRounds + " packets, allocated " + allocated + " B");
            return allocated == 0 ? null : "allocated " + allocated + " bytes";
        }

        private static void EncodeDecode(BitWriter writer, BitReader reader, byte[] buffer)
        {
            writer.Reset(buffer);
            for (int i = 0; i < 40; i++)
            {
                writer.WriteVarInt(i - 20);
                writer.WriteBits((uint)i, 7);
                writer.WriteFloat(i * 0.25f);
                Quantize.ToAim(new Vec2(i * 0.01f, -i * 0.02f), out short ax, out short ay);
                writer.WriteInt16(ax);
                writer.WriteInt16(ay);
                writer.WriteUInt16(Quantize.ToPosition(i * 1.5f));
            }
            reader.Reset(buffer, writer.BytesWritten);
            for (int i = 0; i < 40; i++)
            {
                reader.ReadVarInt();
                reader.ReadBits(7);
                reader.ReadFloat();
                Quantize.FromAim(reader.ReadInt16(), reader.ReadInt16());
                Quantize.FromPosition(reader.ReadUInt16());
            }
        }

        private static string ContentPackRoundTrip()
        {
            byte[] file = File.ReadAllBytes(_content.Path);
            byte[] again = ContentPackWriter.Write(_content.Bundle);
            if (again.Length != file.Length) return "re-encoded " + again.Length + " bytes, file has " + file.Length;
            for (int i = 0; i < file.Length; i++)
                if (again[i] != file[i]) return "byte " + i + " differs";
            _out.WriteLine("        content: " + file.Length + " B, hash " + ContentPackHeader.FormatHash(_content.Hash) + ", fighters " +
                _content.Bundle.Fighters.Length + ", maps " + _content.Bundle.Maps.Length);
            return null;
        }

        private static string HeadlessAcornsMatch()
        {
            ContentBundle bundle = _content.Bundle;
            ContentModeMaps maps = bundle.MapsFor(ModeKind.Acorns);
            if (maps == null) return "no Acorns maps in the content pack";
            MatchDefs defs = bundle.CreateMatchDefs(maps.MapIndices[0], ModeKind.Acorns);
            BotDifficultyDef novice = bundle.FindBotDifficulty(BotDifficulty.Novice);
            if (novice == null) return "no Novice bots in the content pack";
            int maxTicks = defs.Acorns.DurationTicks + 1;

            HeadlessMatchResult first = HeadlessMatch.Run(defs, HeadlessMatch.CreateBotSetup(defs, bundle.Policy, HeadlessSeed),
                bundle.BotBehaviour, novice, maxTicks);
            HeadlessMatchResult second = HeadlessMatch.Run(defs, HeadlessMatch.CreateBotSetup(defs, bundle.Policy, HeadlessSeed),
                bundle.BotBehaviour, novice, maxTicks);
            _out.WriteLine("        match: map " + defs.Map.Id + ", " + first.Ticks + " ticks, winner " + first.WinnerTeam + ", damage " +
                first.TotalDamage + ", " + first.AverageTickMs.ToString("0.0000") + " ms/tick (max " + first.MaxTickMs.ToString("0.000") +
                "), state hash " + StateHash.Format(first.StateHash));
            if (!first.Ended) return "the match did not end within " + maxTicks + " ticks";
            if (first.TotalDamage <= 0) return "the bots did not fight";
            if (first.StateHash != second.StateHash || first.Ticks != second.Ticks)
                return "second run differs: " + second.Ticks + " ticks, hash " + StateHash.Format(second.StateHash);
            return null;
        }
    }
}
