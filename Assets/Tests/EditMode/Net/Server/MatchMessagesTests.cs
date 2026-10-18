using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Server
{
    /// <summary>
    /// Match messages (Server-Spec §4.2, S3.6): round trips of <c>Input</c> (quantized, a repeated input is 1 bit),
    /// <c>MatchStart</c>, <c>MatchEnded</c>, <c>Eliminated</c>; truncated data is refused without exceptions or allocations.
    /// </summary>
    public sealed class MatchMessagesTests
    {
        private readonly byte[] _buffer = new byte[NetLimits.MaxPacketBytes];

        [Test]
        public void Input_RoundTrip_Quantized_RepeatsCostOneBit()
        {
            var m = new InputMessage { NewestTick = -7, Count = 4, RequestKeyframe = true, InterpDelayTicks = 6 };
            m.Inputs[0] = new PlayerInput(new Vec2(0.31f, -0.7f), new Vec2(0.123f, 0.456f), InputButtons.Attack | InputButtons.AutoAim, 200);
            m.Inputs[1] = new PlayerInput(new Vec2(0.31f, -0.7f), Vec2.Zero, InputButtons.None);
            m.Inputs[2] = m.Inputs[1];
            m.Inputs[3] = new PlayerInput(Vec2.Zero, Vec2.Zero, InputButtons.Trick);
            var w = new BitWriter(_buffer);
            m.Write(w);
            int full = w.BitsWritten;

            var r = new BitReader(_buffer, w.BytesWritten);
            Assert.AreEqual((byte)MessageKind.Input, r.ReadByte());
            var back = new InputMessage();
            Assert.IsTrue(back.Read(r));
            Assert.AreEqual(-7, back.NewestTick);
            Assert.AreEqual(4, back.Count);
            Assert.IsTrue(back.RequestKeyframe);
            Assert.AreEqual(6, back.InterpDelayTicks);
            Assert.AreEqual(200, back.Inputs[0].ViewDelayTicks);
            for (int i = 0; i < 4; i++) Assert.AreEqual(Quantize.Input(m.Inputs[i]), back.Inputs[i], "input " + i);
            Assert.AreEqual(-9, back.TickAt(2));

            m.Inputs[2] = new PlayerInput(new Vec2(-0.2f, 0.1f), Vec2.Zero, InputButtons.None);
            w.Reset(_buffer);
            m.Write(w);
            Assert.Greater(w.BitsWritten - full, 8 * 2, "a repeated input is a single bit");
        }

        [Test]
        public void MatchStart_RoundTrip()
        {
            var m = new MatchStartMessage
            {
                MatchId = 0xABCDEF0123456789ul, MapIndex = 2, Mode = ModeKind.LastBeast, OwnSlot = 7, CountdownTicks = 90,
                SnapshotRateDivisor = 1, InputRedundancy = 4, UnlockAllAbilities = true, ShowBots = true, SlotCount = 10
            };
            for (int i = 0; i < 10; i++)
                m.Slots[i] = new MatchSlotInfo { FighterIndex = i % 6, Team = i, Level = 1, SpawnX = 3.5f + i, SpawnY = 40.5f - i, IsBot = i % 3 == 0 };
            var w = new BitWriter(_buffer);
            m.Write(w);
            Assert.Less(w.BytesWritten, NetLimits.MaxReliableMessageBytes);
            var r = new BitReader(_buffer, w.BytesWritten);
            r.ReadByte();
            var back = new MatchStartMessage();
            Assert.IsTrue(back.Read(r));
            Assert.AreEqual(m.MatchId, back.MatchId);
            Assert.AreEqual(ModeKind.LastBeast, back.Mode);
            Assert.AreEqual(7, back.OwnSlot);
            Assert.AreEqual(90, back.CountdownTicks);
            Assert.AreEqual(10, back.SlotCount);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(m.Slots[i].FighterIndex, back.Slots[i].FighterIndex);
                Assert.AreEqual(m.Slots[i].Team, back.Slots[i].Team);
                Assert.AreEqual(m.Slots[i].SpawnX, back.Slots[i].SpawnX);
                Assert.AreEqual(m.Slots[i].SpawnY, back.Slots[i].SpawnY);
                Assert.AreEqual(m.Slots[i].IsBot, back.Slots[i].IsBot);
            }
        }

        [Test]
        public void MatchEnded_And_Eliminated_RoundTrip()
        {
            var m = new MatchEndedMessage { EndTick = 4500, WinnerTeam = -1, MvpSlot = 4, SlotCount = 6 };
            for (int i = 0; i < 6; i++)
                m.Slots[i] = new MatchSlotResult { DamageDealt = 1000 * i, HealingDone = i, Kills = i, Deaths = 6 - i, AcornsCollected = 2 * i, Place = 0, Afk = i == 2 };
            var w = new BitWriter(_buffer);
            m.Write(w);
            var r = new BitReader(_buffer, w.BytesWritten);
            r.ReadByte();
            var back = new MatchEndedMessage();
            Assert.IsTrue(back.Read(r));
            Assert.AreEqual(4500, back.EndTick);
            Assert.AreEqual(-1, back.WinnerTeam);
            Assert.AreEqual(4, back.MvpSlot);
            for (int i = 0; i < 6; i++) Assert.AreEqual(m.Slots[i], back.Slots[i]);

            var e = new EliminatedMessage { Tick = 1234, Slot = 9, Result = new MatchSlotResult { DamageDealt = 5, Place = 7 } };
            w.Reset(_buffer);
            e.Write(w);
            r.Reset(_buffer, w.BytesWritten);
            Assert.AreEqual((byte)MessageKind.Eliminated, r.ReadByte());
            var eb = new EliminatedMessage();
            Assert.IsTrue(eb.Read(r));
            Assert.AreEqual(9, eb.Slot);
            Assert.AreEqual(7, eb.Result.Place);
        }

        [Test]
        public void ClientStats_RoundTrip_RefusesTruncatedAndInconsistent()
        {
            var m = new ClientStatsMessage
            {
                PredictionSamples = 4000, PredictionCorrections = 30, LargeCorrections = 1, MaxErrorMilliCells = 312,
                PresentedTicks = 4500, ExtrapolatedTicks = 9, Teleports = 2, LargeTeleports = 0
            };
            var w = new BitWriter(_buffer);
            m.Write(w);
            int length = w.BytesWritten;
            var r = new BitReader(_buffer, length);
            Assert.AreEqual((byte)MessageKind.ClientStats, r.ReadByte());
            var back = new ClientStatsMessage();
            Assert.IsTrue(back.Read(r));
            Assert.AreEqual(4000, back.PredictionSamples);
            Assert.AreEqual(30, back.PredictionCorrections);
            Assert.AreEqual(1, back.LargeCorrections);
            Assert.AreEqual(312, back.MaxErrorMilliCells);
            Assert.AreEqual(4500, back.PresentedTicks);
            Assert.AreEqual(9, back.ExtrapolatedTicks);
            Assert.AreEqual(2, back.Teleports);
            Assert.Less(length, 20, "a report is a few bytes");
            for (int cut = 1; cut < length - 1; cut++)
            {
                r.Reset(_buffer, 1, cut);
                Assert.IsFalse(back.Read(r), "truncated at " + cut);
            }
            m.PredictionCorrections = 5000; // more corrections than samples
            w.Reset(_buffer);
            m.Write(w);
            r.Reset(_buffer, 1, w.BytesWritten - 1);
            Assert.IsFalse(back.Read(r), "inconsistent counters are refused");
        }

        [Test]
        public void TruncatedMessages_AreRefused_WithoutAllocations()
        {
            var m = new InputMessage { NewestTick = 100, Count = 4 };
            for (int i = 0; i < 4; i++) m.Inputs[i] = new PlayerInput(new Vec2(0.1f * i, 0.5f), new Vec2(0.3f, -0.2f * i), InputButtons.Attack);
            var w = new BitWriter(_buffer);
            m.Write(w);
            int length = w.BytesWritten;
            var back = new InputMessage();
            var start = new MatchStartMessage();
            var r = new BitReader();
            int accepted = 0;
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int cut = 1; cut < length - 1; cut++)
                {
                    r.Reset(_buffer, 1, cut);
                    if (back.Read(r)) accepted++;
                    r.Reset(_buffer, 1, cut);
                    start.Read(r);
                }
            });
            Assert.AreEqual(0, accepted, "every truncated input is refused");
        }
    }
}
