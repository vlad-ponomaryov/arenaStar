using System;
using System.Reflection;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.State;
using Arena.Net;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Snapshots
{
    /// <summary>
    /// Snapshot coding pieces (Server-Spec §4.4, §11.1, S3.5): every field of <see cref="FighterState"/> and of every view
    /// entry goes through the delta coder (reflection — a new field must be coded), every field takes part in Equals, own
    /// statuses, <see cref="BitWriter.Rewind"/>, and the decoder survives random and truncated input without exceptions or
    /// allocations.
    /// </summary>
    public sealed class SnapshotCodecTests
    {
        private const int BaseTick = 1234;
        private const int FuzzRounds = 3000;

        private readonly byte[] _buffer = new byte[NetLimits.MaxPacketBytes];

        [Test]
        public void OwnFighter_EveryFieldRoundTrips()
        {
            var rng = new System.Random(5);
            foreach (FieldInfo field in Fields<FighterState>())
            {
                for (int round = 0; round < 4; round++)
                {
                    FighterState prev = round % 2 == 0 ? default : RandomFighter(rng);
                    object boxed = prev;
                    field.SetValue(boxed, RandomValue(field, rng));
                    FighterState cur = (FighterState)boxed;
                    var w = new BitWriter(_buffer);
                    OwnFighterCodec.Write(w, in cur, in prev, BaseTick);
                    Assert.IsFalse(w.IsOverflow);
                    var r = new BitReader(_buffer, w.BytesWritten);
                    FighterState back = OwnFighterCodec.Read(r, in prev, BaseTick);
                    Assert.IsFalse(r.IsOverflow);
                    Assert.IsTrue(cur.Equals(back), "FighterState." + field.Name + " is not coded by OwnFighterCodec");
                    Assert.AreEqual(BitConverter.SingleToInt32Bits(cur.Position.X), BitConverter.SingleToInt32Bits(back.Position.X), "exact floats");
                }
            }
        }

        [Test]
        public void OwnFighter_RandomStates_RoundTripExactly()
        {
            var rng = new System.Random(11);
            for (int i = 0; i < 500; i++)
            {
                FighterState prev = RandomFighter(rng);
                FighterState cur = RandomFighter(rng);
                var w = new BitWriter(_buffer);
                OwnFighterCodec.Write(w, in cur, in prev, BaseTick + i);
                var r = new BitReader(_buffer, w.BytesWritten);
                Assert.IsTrue(cur.Equals(OwnFighterCodec.Read(r, in prev, BaseTick + i)));
            }
        }

        [Test] public void FighterView_EveryFieldRoundTrips() => AssertEntryFieldsRoundTrip<NetFighterView>();
        [Test] public void ProjectileSegment_EveryFieldRoundTrips() => AssertEntryFieldsRoundTrip<NetProjectileSegment>();
        [Test] public void AreaView_EveryFieldRoundTrips() => AssertEntryFieldsRoundTrip<NetAreaView>();
        [Test] public void PickupView_EveryFieldRoundTrips() => AssertEntryFieldsRoundTrip<NetPickupView>();
        [Test] public void ModeView_EveryFieldRoundTrips() => AssertEntryFieldsRoundTrip<NetModeView>();

        [Test] public void FighterView_EveryFieldInEquals() => AssertEveryFieldInEquals<NetFighterView>();
        [Test] public void ProjectileSegment_EveryFieldInEquals() => AssertEveryFieldInEquals<NetProjectileSegment>();
        [Test] public void AreaView_EveryFieldInEquals() => AssertEveryFieldInEquals<NetAreaView>();
        [Test] public void PickupView_EveryFieldInEquals() => AssertEveryFieldInEquals<NetPickupView>();
        [Test] public void ModeView_EveryFieldInEquals() => AssertEveryFieldInEquals<NetModeView>();
        [Test] public void SnapshotEvent_EveryFieldInEquals() => AssertEveryFieldInEquals<NetSnapshotEvent>();
        [Test] public void SnapshotHeader_EveryFieldInEquals() => AssertEveryFieldInEquals<SnapshotHeader>();

        [Test]
        public void OwnStatuses_RoundTrip_AndUnchangedCostsOneBit()
        {
            var items = new StatusEffectState[NetLimits.MaxSnapshotOwnStatuses];
            items[0] = new StatusEffectState { Active = true, Target = 3, Source = 5, SourceTeam = 1, Kind = EffectKind.Slow, Value = 40, ExpireTick = BaseTick + 45 };
            items[1] = new StatusEffectState { Active = true, Target = 3, Source = -1, SourceTeam = -1, Kind = EffectKind.Stun, Value = 0, ExpireTick = BaseTick - 2 };
            var empty = new StatusEffectState[NetLimits.MaxSnapshotOwnStatuses];

            var w = new BitWriter(_buffer);
            OwnFighterCodec.WriteStatuses(w, items, 2, empty, 0, BaseTick);
            var back = new StatusEffectState[NetLimits.MaxSnapshotOwnStatuses];
            int count = 0;
            var r = new BitReader(_buffer, w.BytesWritten);
            Assert.IsTrue(OwnFighterCodec.ReadStatuses(r, back, ref count, 3, BaseTick));
            Assert.AreEqual(2, count);
            Assert.IsTrue(items[0].Equals(back[0]) && items[1].Equals(back[1]));

            w = new BitWriter(_buffer);
            OwnFighterCodec.WriteStatuses(w, items, 2, back, 2, BaseTick);
            Assert.AreEqual(1, w.BitsWritten, "same list = one bit");
        }

        [Test]
        public void BitWriter_Rewind_ForgetsTheTail()
        {
            var w = new BitWriter(_buffer);
            w.WriteBits(0x5u, 3);
            int mark = w.BitsWritten;
            w.WriteBits(0x7Fu, 7);
            w.Rewind(mark);
            w.WriteBits(0x2u, 2);
            var r = new BitReader(_buffer, w.BytesWritten);
            Assert.AreEqual(0x5u, r.ReadBits(3));
            Assert.AreEqual(0x2u, r.ReadBits(2));
            Assert.AreEqual(0u, r.ReadBits(3), "bits after the rewind point are cleared");

            var small = new BitWriter(new byte[1]);
            small.WriteBits(1u, 4);
            small.WriteBits(1u, 8);
            Assert.IsTrue(small.IsOverflow);
            small.Rewind(4);
            Assert.IsFalse(small.IsOverflow, "rewind clears the overflow");
        }

        [Test]
        public void Decoder_RandomAndTruncatedInput_NoExceptionsNoAllocations()
        {
            var rig = SnapshotMatchRig.Create(Arena.Core.Modes.ModeKind.Acorns, 3);
            for (int i = 0; i < 120; i++) rig.Step();
            var view = new Arena.Net.Server.Snapshots.ClientView(0);
            byte[] key = new byte[NetLimits.MaxPacketBytes];
            int keyLength = rig.Builder.Build(rig.Sim.State, view, default, key, 0);
            Assert.Greater(keyLength, 16);

            var decoder = new SnapshotDecoder();
            var ring = new SnapshotFrameRing();
            var events = new NetSnapshotEvent[NetLimits.MaxSnapshotEvents];
            var rng = new System.Random(9);
            byte[] junk = new byte[NetLimits.MaxPacketBytes];

            TestDelegate fuzz = () =>
            {
                for (int round = 0; round < FuzzRounds; round++)
                {
                    int length = 1 + rng.Next(junk.Length - 1);
                    for (int i = 0; i < length; i++) junk[i] = (byte)rng.Next(256);
                    ring.Clear();
                    decoder.Decode(junk, 0, length, ring, events, out _, out _, out _);
                }
                for (int cut = 1; cut < keyLength; cut++)
                {
                    ring.Clear();
                    SnapshotDecodeStatus s = decoder.Decode(key, 0, cut, ring, events, out _, out _, out _);
                    if (s == SnapshotDecodeStatus.Ok && cut < keyLength - 1) Assert.Fail("truncated key frame decoded at " + cut);
                }
                for (int round = 0; round < FuzzRounds; round++)
                {
                    Buffer.BlockCopy(key, 0, junk, 0, keyLength);
                    junk[rng.Next(keyLength)] ^= (byte)(1 << rng.Next(8));
                    ring.Clear();
                    decoder.Decode(junk, 0, keyLength, ring, events, out _, out _, out _);
                }
            };
            fuzz();
            AllocationAssert.NoGcAllocations(fuzz, "decoder fuzzing allocates");

            ring.Clear();
            Assert.AreEqual(SnapshotDecodeStatus.Ok, decoder.Decode(key, 0, keyLength, ring, events, out _, out SnapshotFrame frame, out _));
            Assert.IsTrue(frame.ContentEquals(view.Sent.Get(rig.Sim.State.Globals.Tick)));
        }

        // ---- helpers ----

        private void AssertEntryFieldsRoundTrip<T>() where T : struct, IEquatable<T>, ISnapshotEntry<T>
        {
            var rng = new System.Random(typeof(T).Name.Length * 7919);
            foreach (FieldInfo field in Fields<T>())
            {
                for (int round = 0; round < 4; round++)
                {
                    T prev = round % 2 == 0 ? default : RandomEntry<T>(rng);
                    object boxed = prev;
                    field.SetValue(boxed, RandomValue(field, rng));
                    T cur = (T)boxed;
                    var w = new BitWriter(_buffer);
                    cur.WriteDelta(w, in prev, BaseTick);
                    Assert.IsFalse(w.IsOverflow);
                    var r = new BitReader(_buffer, w.BytesWritten);
                    T back = prev.ReadDelta(r, BaseTick);
                    Assert.IsFalse(r.IsOverflow);
                    Assert.IsTrue(cur.Equals(back), typeof(T).Name + "." + field.Name + " is not coded");
                }
            }
            T same = RandomEntry<T>(rng);
            var w2 = new BitWriter(_buffer);
            same.WriteDelta(w2, in same, BaseTick);
            Assert.AreEqual(Fields<T>().Length - CompositeFieldSavings<T>(), w2.BitsWritten, typeof(T).Name + ": unchanged = one bit per field");
        }

        /// <summary>Point fields (X and Y) share one "changed" bit.</summary>
        private static int CompositeFieldSavings<T>()
        {
            int n = 0;
            foreach (FieldInfo f in Fields<T>())
                if (f.FieldType == typeof(ushort) && f.Name.EndsWith("X") && typeof(T).GetField(f.Name.Substring(0, f.Name.Length - 1) + "Y") != null) n++;
            return n;
        }

        private static void AssertEveryFieldInEquals<T>() where T : struct, IEquatable<T>
        {
            var rng = new System.Random(3);
            foreach (FieldInfo field in Fields<T>())
            {
                T a = default;
                object boxed = a;
                field.SetValue(boxed, RandomValue(field, rng));
                Assert.IsFalse(a.Equals((T)boxed), typeof(T).Name + "." + field.Name + " is not compared in Equals");
            }
        }

        private static FieldInfo[] Fields<T>() => typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static T RandomEntry<T>(System.Random rng) where T : struct
        {
            object boxed = default(T);
            foreach (FieldInfo f in Fields<T>()) f.SetValue(boxed, RandomValue(f, rng));
            return (T)boxed;
        }

        private static FighterState RandomFighter(System.Random rng) => RandomEntry<FighterState>(rng);

        /// <summary>A random non-default value that fits the field's wire width.</summary>
        private static object RandomValue(FieldInfo field, System.Random rng)
        {
            Type t = field.FieldType;
            if (t == typeof(bool)) return true;
            if (t == typeof(float)) return (float)(rng.NextDouble() * 200.0 - 100.0) + 0.001f;
            if (t == typeof(Vec2)) return new Vec2((float)(rng.NextDouble() * 50.0) + 0.5f, (float)(rng.NextDouble() * -50.0) - 0.5f);
            if (t.IsEnum) return Enum.ToObject(t, 1 + rng.Next(3));
            if (t == typeof(int)) return rng.Next(2) == 0 ? 1 + rng.Next(int.MaxValue - 1) : -1 - rng.Next(100000);
            if (t == typeof(uint)) return (uint)(1 + rng.Next(int.MaxValue - 1)) | 0x80000000u;
            if (t == typeof(short)) return (short)(1 + rng.Next(short.MaxValue - 1) * (rng.Next(2) == 0 ? 1 : -1));
            if (t == typeof(ushort))
            {
                if (field.Name == "HitMask") return (ushort)(1 + rng.Next((1 << 10) - 1));
                return (ushort)(1 + rng.Next(ushort.MaxValue - 1));
            }
            if (t == typeof(byte)) return (byte)(1 + rng.Next(254));
            throw new NotSupportedException(field.DeclaringType.Name + "." + field.Name + ": " + t.Name);
        }
    }
}
