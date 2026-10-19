using System;
using Arena.Net;
using Arena.Net.Server.Snapshots;
using Arena.Net.Transport;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Server
{
    /// <summary>
    /// Config values that used to pass validation and then broke the game thread (audit F2): a zero cookie slice (division by
    /// zero in the handshake), a snapshot budget equal to the payload (no room for the message kind), more repeated events than
    /// the client remembers (damage numbers twice).
    /// </summary>
    public sealed class SettingsValidationTests
    {
        [Test]
        public void TransportSettings_Defaults_Pass_ZeroCookieSliceOrRetry_Fails()
        {
            Assert.DoesNotThrow(() => TransportSettings.CreateDefault().Validate());
            TransportSettings cookie = TransportSettings.CreateDefault();
            cookie.CookieSliceMs = 0;
            Assert.Throws<ArgumentException>(() => cookie.Validate());
            TransportSettings retry = TransportSettings.CreateDefault();
            retry.HandshakeRetryMs = 0;
            Assert.Throws<ArgumentException>(() => retry.Validate());
            Assert.Throws<ArgumentException>(() => new ServerTransport(new InMemoryNetwork(1).CreateSocket(
                NetAddress.FromIPv4(10, 0, 0, 1, 7777)), cookie, 1ul, new DeterministicNetRandom(1), null), "checked at start");
        }

        [Test]
        public void SnapshotSettings_BudgetLeavesRoomForTheMessageKind_EventsFitTheClientHistory()
        {
            Assert.DoesNotThrow(() => SnapshotSettings.CreateDefault().Validate());
            SnapshotSettings bytes = SnapshotSettings.CreateDefault();
            bytes.MaxSnapshotBytes = NetConnection.MaxPayloadBytes;
            Assert.Throws<ArgumentException>(() => bytes.Validate());
            bytes.MaxSnapshotBytes = NetConnection.MaxPayloadBytes - 1;
            Assert.DoesNotThrow(() => bytes.Validate());
            SnapshotSettings events = SnapshotSettings.CreateDefault();
            events.EventRedundancyTicks = 5;
            events.MaxEventsPerSnapshot = NetLimits.MaxSnapshotEvents;
            Assert.Greater(events.EventRedundancyTicks * events.MaxEventsPerSnapshot, NetLimits.RecentEventHistory);
            Assert.Throws<ArgumentException>(() => events.Validate());
        }
    }
}
