using System;
using System.Net;
using Arena.Core.Random;
using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Transport;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Transport
{
    public sealed class TransportPrimitivesTests
    {
        // SipHash-2-4 reference vectors: key 00..0f, message 00..(n-1), output as little-endian bytes (vectors.h of the reference).
        private static readonly string[] SipHashVectors =
        {
            "310e0edd47db6f72", "fd67dc93c539f874", "5a4fa9d909806c0d", "2d7efbd796666785",
            "b7877127e09427cf", "8da699cd64557618", "cee3fe586e46c9cb", "37d1018bf50002ab",
            "6224939a79f5f593", "b0e4a90bdf82009e", "f3b9dd94c5bb5d7a", "a7ad6b22462fb3f4",
            "fbe50e86bc8f1e75", "903d84c02756ea14", "eef27a8e90ca23f7", "e545be4961ca29a1",
        };

        [Test]
        public void SipHash_MatchesReferenceVectors()
        {
            const ulong k0 = 0x0706050403020100ul;
            const ulong k1 = 0x0f0e0d0c0b0a0908ul;
            var message = new byte[SipHashVectors.Length];
            for (int i = 0; i < message.Length; i++) message[i] = (byte)i;
            for (int n = 0; n < SipHashVectors.Length; n++)
            {
                ulong expected = 0ul;
                for (int b = 7; b >= 0; b--) expected = (expected << 8) | Convert.ToByte(SipHashVectors[n].Substring(b * 2, 2), 16);
                Assert.AreEqual(expected, SipHash.Compute(k0, k1, message, 0, n), "vector " + n);
            }
        }

        [Test]
        public void SipHash_UsesOffsetAndKey()
        {
            var data = new byte[] { 9, 9, 0, 1, 2 };
            var plain = new byte[] { 0, 1, 2 };
            Assert.AreEqual(SipHash.Compute(1ul, 2ul, plain, 0, 3), SipHash.Compute(1ul, 2ul, data, 2, 3));
            Assert.AreNotEqual(SipHash.Compute(1ul, 2ul, plain, 0, 3), SipHash.Compute(1ul, 3ul, plain, 0, 3));
        }

        [Test]
        public void PacketHeader_RoundTrip()
        {
            var buffer = new byte[NetProtocol.HeaderBytes];
            var h = new PacketHeader
            {
                Kind = PacketKind.Data,
                Flags = PacketFlags.HasPayload | PacketFlags.HasAcks,
                ConnectionId = 0xF00DCAFE12345678ul,
                Sequence = 65535,
                Ack = 1234,
                AckBits = 0x80000001u,
                AckDelay = 200,
            };
            h.Write(buffer);
            Assert.IsTrue(PacketHeader.TryRead(buffer, buffer.Length, out PacketHeader r));
            Assert.AreEqual(h.Kind, r.Kind);
            Assert.AreEqual(h.Flags, r.Flags);
            Assert.AreEqual(h.ConnectionId, r.ConnectionId);
            Assert.AreEqual(h.Sequence, r.Sequence);
            Assert.AreEqual(h.Ack, r.Ack);
            Assert.AreEqual(h.AckBits, r.AckBits);
            Assert.AreEqual(h.AckDelay, r.AckDelay);
            Assert.IsFalse(PacketHeader.TryRead(buffer, NetProtocol.HeaderBytes - 1, out _), "short packet");
            buffer[0] = 0;
            Assert.IsFalse(PacketHeader.TryRead(buffer, buffer.Length, out _), "invalid kind");
            buffer[0] = 15;
            Assert.IsFalse(PacketHeader.TryRead(buffer, buffer.Length, out _), "unknown kind");
        }

        [Test]
        public void PacketHeader_PathDataRoundTrip()
        {
            var h = new PacketHeader { PathData = 0x0123456789ABCDEFul };
            Assert.AreEqual(0x0123456789ABCDEFul, h.PathData);
        }

        [Test]
        public void Sequence16_WrapsAround()
        {
            Assert.IsTrue(Sequence16.IsNewer(1, 0));
            Assert.IsTrue(Sequence16.IsNewer(0, 65535));
            Assert.IsFalse(Sequence16.IsNewer(65535, 0));
            Assert.IsFalse(Sequence16.IsNewer(5, 5));
            Assert.AreEqual(2, Sequence16.Distance(1, 65535));
        }

        [Test]
        public void AckTracker_ReplayWindow()
        {
            var t = new AckTracker();
            Assert.IsTrue(t.IsNew(100));
            t.MarkReceived(100, 0);
            Assert.IsFalse(t.IsNew(100), "duplicate");
            Assert.IsTrue(t.IsNew(99));
            t.MarkReceived(99, 0);
            Assert.IsFalse(t.IsNew(99), "duplicate older");
            Assert.IsTrue(t.IsNew(150));
            t.MarkReceived(150, 0);
            Assert.IsFalse(t.IsNew(117), "older than 32 behind the newest");
            Assert.IsTrue(t.IsNew(118));
            Assert.IsFalse(t.IsNew(100), "outside the window");
            t.MarkReceived(149, 0);
            var h = new PacketHeader();
            t.FillAcks(ref h, 0);
            Assert.AreEqual(150, h.Ack);
            Assert.AreEqual(1u, h.AckBits & 1u, "149 acknowledged");
            Assert.AreEqual(0u, h.AckBits & 2u, "148 missing");
        }

        [Test]
        public void AckTracker_AcknowledgesOnlySentPacketsOnce()
        {
            var t = new AckTracker();
            Assert.IsFalse(t.TryAcknowledge(0, out _), "nothing sent");
            for (int i = 0; i < 5; i++) t.TakeSequence(i * 10);
            Assert.IsTrue(t.TryAcknowledge(3, out long sentAt));
            Assert.AreEqual(30, sentAt);
            Assert.IsFalse(t.TryAcknowledge(3, out _), "twice");
            Assert.IsFalse(t.TryAcknowledge(7, out _), "never sent (future)");
        }

        [Test]
        public void AckTracker_FillAcksAfterBigJump()
        {
            var t = new AckTracker();
            t.MarkReceived(10, 0);
            t.MarkReceived(42, 0); // exactly 32 ahead: only 10 stays in the bits
            var h = new PacketHeader();
            t.FillAcks(ref h, 0);
            Assert.AreEqual(42, h.Ack);
            Assert.AreEqual(1u << 31, h.AckBits);
            t.MarkReceived(100, 0);
            t.FillAcks(ref h, 0);
            Assert.AreEqual(0u, h.AckBits);
        }

        [Test]
        public void RttEstimator_FollowsSamples()
        {
            var rtt = new RttEstimator(100000, 2000000, 500000);
            Assert.AreEqual(500000, rtt.RtoMicros, "initial");
            for (int i = 0; i < 50; i++) rtt.AddSample(150000);
            Assert.AreEqual(150000, rtt.SmoothedMicros, 2000);
            Assert.GreaterOrEqual(rtt.RtoMicros, 150000);
            Assert.LessOrEqual(rtt.RtoMicros, 200000);
            for (int i = 0; i < 50; i++) rtt.AddSample(1000);
            Assert.AreEqual(100000, rtt.RtoMicros, "min RTO");
        }

        [Test]
        public void ConnectionIdMap_RandomAddRemove()
        {
            var map = new ConnectionIdMap(64);
            var keys = new ulong[64];
            var present = new bool[64];
            var rng = DeterministicRng.FromSeed(5);
            for (int i = 0; i < keys.Length; i++) keys[i] = (rng.NextULong() & 0xFFul) | 1ul | ((ulong)i << 8); // clustered hashes
            for (int step = 0; step < 5000; step++)
            {
                int k = rng.Range(0, keys.Length);
                if (present[k]) Assert.IsTrue(map.Remove(keys[k]));
                else Assert.IsTrue(map.Add(keys[k], k));
                present[k] = !present[k];
                if (step % 97 != 0) continue;
                for (int j = 0; j < keys.Length; j++)
                {
                    Assert.AreEqual(present[j], map.TryGet(keys[j], out int v), "key " + j + " at step " + step);
                    if (present[j]) Assert.AreEqual(j, v);
                }
            }
            Assert.IsFalse(map.Add(0ul, 1), "id 0 is reserved");
        }

        [Test]
        public void TokenBucket_RateAndBurst()
        {
            var bucket = new TokenBucket();
            int taken = 0;
            for (int i = 0; i < 200; i++)
                if (bucket.TryTake(0, 100, 1)) taken++;
            Assert.AreEqual(100, taken, "burst = one second");
            Assert.IsFalse(bucket.TryTake(5000, 100, 1), "5 ms refills half a token");
            Assert.IsTrue(bucket.TryTake(10000, 100, 1), "10 ms refills one token");
        }

        [Test]
        public void RateLimiter_PerHostPerMinute()
        {
            var limiter = new RateLimiter();
            NetAddress a = NetAddress.FromIPv4(203, 0, 113, 5, 1000);
            NetAddress a2 = NetAddress.FromIPv4(203, 0, 113, 5, 2000);
            NetAddress b = NetAddress.FromIPv4(203, 0, 113, 6, 1000);
            int allowed = 0;
            for (int i = 0; i < 20; i++)
                if (limiter.Allow(i % 2 == 0 ? a : a2, 0, 10)) allowed++;
            Assert.AreEqual(10, allowed, "ports of one host share the limit");
            Assert.IsTrue(limiter.Allow(b, 0, 10), "another host has its own");
            Assert.IsTrue(limiter.Allow(a, 6000000, 10), "a minute / 10 later one more");
        }

        [Test]
        public void NetAddress_PrivateRangesAndEndPoints()
        {
            Assert.IsTrue(NetAddress.FromIPv4(192, 168, 1, 10, 1).IsPrivate);
            Assert.IsTrue(NetAddress.FromIPv4(10, 1, 2, 3, 1).IsPrivate);
            Assert.IsTrue(NetAddress.FromIPv4(172, 20, 0, 1, 1).IsPrivate);
            Assert.IsTrue(NetAddress.FromIPv4(127, 0, 0, 1, 1).IsPrivate);
            Assert.IsFalse(NetAddress.FromIPv4(172, 32, 0, 1, 1).IsPrivate);
            Assert.IsFalse(NetAddress.FromIPv4(8, 8, 8, 8, 1).IsPrivate);
            Assert.IsTrue(NetAddress.FromEndPoint(new IPEndPoint(IPAddress.IPv6Loopback, 1)).IsPrivate);
            Assert.IsTrue(NetAddress.FromEndPoint(new IPEndPoint(IPAddress.Parse("fe80::1"), 1)).IsPrivate);
            Assert.IsFalse(NetAddress.FromEndPoint(new IPEndPoint(IPAddress.Parse("2001:db8::1"), 1)).IsPrivate);

            var v4 = new IPEndPoint(IPAddress.Parse("192.0.2.33"), 7777);
            NetAddress a = NetAddress.FromEndPoint(v4);
            Assert.IsTrue(a.IsIPv4);
            Assert.AreEqual(v4, a.ToEndPoint(System.Net.Sockets.AddressFamily.InterNetwork));
            Assert.AreEqual(a, NetAddress.FromEndPoint(a.ToEndPoint(System.Net.Sockets.AddressFamily.InterNetworkV6)), "mapped back");
            var v6 = new IPEndPoint(IPAddress.Parse("2001:db8::abcd:1"), 1234);
            Assert.AreEqual(v6, NetAddress.FromEndPoint(v6).ToEndPoint(System.Net.Sockets.AddressFamily.InterNetworkV6));
        }

        [Test]
        public void ReliableChannel_RejectsOversizeMessage()
        {
            var channel = new ReliableChannel();
            var big = new byte[NetLimits.MaxReliableMessageBytes + 1];
            Assert.Throws<ArgumentOutOfRangeException>(() => channel.Enqueue(big, 0, big.Length));
            Assert.Throws<ArgumentOutOfRangeException>(() => channel.Enqueue(big, 0, 0));
            Assert.IsTrue(channel.Enqueue(big, 0, NetLimits.MaxReliableMessageBytes));
        }

        [Test]
        public void ReliableChannel_WindowAndRingLimits()
        {
            var channel = new ReliableChannel();
            var small = new byte[1];
            for (int i = 0; i < NetLimits.ReliableWindow; i++) Assert.IsTrue(channel.Enqueue(small, 0, 1));
            Assert.IsFalse(channel.Enqueue(small, 0, 1), "window full");

            channel.Reset();
            var big = new byte[NetLimits.MaxReliableMessageBytes];
            int fit = 0;
            while (channel.Enqueue(big, 0, big.Length)) fit++;
            Assert.AreEqual(NetLimits.ReliableBufferBytes / NetLimits.MaxReliableMessageBytes, fit, "ring full");
        }

        [Test]
        public void ReliableChannel_RingWrapsAndReceiverMirrors()
        {
            // Direct channel-to-channel exchange with reordering and loss: every message arrives in order exactly once.
            var sender = new ReliableChannel();
            var receiver = new ReliableChannel();
            var handler = new RecordingHandler();
            var writer = new BitWriter();
            var reader = new BitReader();
            var rng = DeterministicRng.FromSeed(77);
            var message = new byte[NetLimits.MaxReliableMessageBytes];
            var packets = new byte[8][];
            var lengths = new int[8];
            var seqs = new ushort[8];
            for (int i = 0; i < packets.Length; i++) packets[i] = new byte[NetLimits.MaxPacketBytes];
            int queued = 0;
            ushort seq = 0;
            long now = 0;
            const int total = 400;
            for (int round = 0; round < 20000 && handler.Reliable < total; round++)
            {
                now += 10000;
                while (queued < total && sender.Enqueue(message, 0, RecordingHandler.Fill(message, queued))) queued++;
                // Build a few packets, deliver them in random order, drop some, acknowledge the delivered ones.
                int built = 0;
                for (int p = 0; p < packets.Length; p++)
                {
                    writer.Reset(packets[p]);
                    seqs[p] = seq;
                    int n = sender.Write(writer, seq, now, 50000, NetLimits.MaxPacketBytes);
                    seq++;
                    if (n == 0) break;
                    lengths[p] = writer.BytesWritten;
                    built++;
                }
                for (int p = built - 1; p > 0; p--)
                {
                    int j = rng.Range(0, p + 1);
                    (packets[p], packets[j]) = (packets[j], packets[p]);
                    (lengths[p], lengths[j]) = (lengths[j], lengths[p]);
                    (seqs[p], seqs[j]) = (seqs[j], seqs[p]);
                }
                for (int p = 0; p < built; p++)
                {
                    if (rng.NextFloat01() < 0.25f) continue;
                    reader.Reset(packets[p], lengths[p]);
                    Assert.IsTrue(ReliableChannel.Validate(reader));
                    reader.Reset(packets[p], lengths[p]);
                    receiver.Receive(reader, packets[p], handler, 0);
                    if (rng.NextFloat01() < 0.8f) sender.OnPacketAcked(seqs[p]);
                }
            }
            Assert.AreEqual(total, handler.Reliable);
            Assert.AreEqual(0, handler.ReliableErrors);
            Assert.Greater(sender.RetransmittedMessages, 0);
        }
    }
}
