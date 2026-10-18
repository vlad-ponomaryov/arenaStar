using System;
using System.Globalization;
using System.IO;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Net.Content;
using Arena.Net.Server.Diagnostics;
using Arena.Net.Server.Match;
using Arena.Net.Transport;

namespace Arena.Server
{
    /// <summary>
    /// Self-test of the match server logic on the server runtime (S3.6): <see cref="MatchHost"/> and two scripted clients over the
    /// in-memory network in CI conditions (RTT 150 ms, 50 ms jitter, 2 % loss) play a whole Acorns match with 4 bots; both get
    /// <c>MatchEnded</c> equal to the server's result, the input log replays to the same state, late/missing inputs stay below
    /// 1 %, and the steady state of <c>MatchHost.Tick</c> allocates 0 bytes (exact on CoreCLR).
    /// </summary>
    public static class MatchHostSelfTest
    {
        private const long FrameMicros = 1000000L / 60L;
        private const long MaxMatchMicros = 180L * 1000000L;
        private const int WarmupTicks = 300;
        private const int MeasuredFrames = 600;
        private const double MaxMissingShare = 0.01;

        public static string Run(TextWriter output, ContentBundle bundle, ulong contentHash, int seed)
        {
            var network = new InMemoryNetwork(seed);
            network.DefaultConditions = new NetworkConditions { LatencyMs = 75f, JitterMs = 25f, LossPercent = 1f };
            ServerSettings settings = ServerSettings.CreateDefault();
            settings.RecordInputLog = true;
            TransportSettings transport = TransportSettings.CreateDefault();
            NetAddress serverAddress = NetAddress.FromIPv4(10, 0, 0, 1, 7777);
            var host = new MatchHost(network.CreateSocket(serverAddress), transport, settings, bundle, contentHash, new DeterministicNetRandom(seed));

            int mapIndex = bundle.MapsFor(ModeKind.Acorns).MapIndices[0];
            int[] players = { 0, 3 };
            var fighters = new[] { 0, -1, -1, 1, -1, -1 };
            MatchRoom room = host.CreateMatch(MatchRoomRequest.FromBundle(bundle, mapIndex, ModeKind.Acorns, fighters, seed), network.NowMicros);
            var clients = new ScriptedMatchClient[players.Length];
            for (int k = 0; k < players.Length; k++)
            {
                int salt = k * 7 + 3;
                clients[k] = new ScriptedMatchClient(transport, contentHash, new DeterministicNetRandom(seed + 10 + k))
                {
                    Script = (tick, latest) => Wander(tick, salt)
                };
                var socket = network.CreateClientSocket(NetAddress.FromIPv4(10, 0, 1, (byte)(k + 1), (ushort)(5000 + k)), serverAddress);
                clients[k].Connect(socket, room.GetSlot(players[k]).Token, network.NowMicros);
            }

            long allocStart = 0, allocated = -1;
            int frames = 0;
            bool measuring = false;
            while (network.NowMicros < MaxMatchMicros)
            {
                bool ended = true;
                for (int k = 0; k < clients.Length; k++) ended &= clients[k].HasEnded;
                if (ended) break;
                if (!measuring && allocated < 0 && room.Phase == MatchPhase.Running && room.Tick > WarmupTicks)
                {
                    measuring = true;
                    allocStart = GC.GetAllocatedBytesForCurrentThread();
                }
                network.Advance(FrameMicros);
                host.Tick(network.NowMicros);
                if (measuring && ++frames >= MeasuredFrames)
                {
                    allocated = GC.GetAllocatedBytesForCurrentThread() - allocStart;
                    measuring = false;
                }
                for (int k = 0; k < clients.Length; k++) clients[k].Update(network.NowMicros);
            }

            for (int k = 0; k < clients.Length; k++)
            {
                ScriptedMatchClient c = clients[k];
                if (!c.HasEnded) return "client " + k + " got no MatchEnded (room " + room.Phase + ", tick " + room.Tick + ")";
                if (c.Ended.EndTick != room.Result.EndTick || c.Ended.WinnerTeam != room.Result.WinnerTeam)
                    return "client " + k + " result differs from the server's";
                if (c.FirstServerTick >= 0) return "client " + k + " saw no countdown snapshots";
            }
            if (!ReplayVerifier.Verify(room.Defs, room.Setup, room.InputLog, room.Simulation.State, out string replayError)) return replayError;
            ServerMetrics m = host.Metrics;
            output.WriteLine("        match host: " + room.Simulation.State.Globals.Tick + " ticks, winner " + room.Result.WinnerTeam
                + ", missing inputs " + (m.MissingInputShare * 100.0).ToString("F2", CultureInfo.InvariantCulture) + " %, average snapshot "
                + m.AverageSnapshotBytes.ToString("F0", CultureInfo.InvariantCulture) + " B, max " + m.MaxSnapshotBytes + " B, steady state "
                + allocated + " B");
            if (m.MissingInputShare >= MaxMissingShare) return "late/missing inputs " + m.MissingInputShare.ToString("P2", CultureInfo.InvariantCulture);
            if (m.MalformedMessages != 0) return m.MalformedMessages + " malformed messages";
            if (allocated != 0) return "MatchHost.Tick allocated " + allocated + " B in steady state";
            return null;
        }

        private static PlayerInput Wander(int tick, int salt)
        {
            int phase = (tick / 45 + salt) & 7;
            float x = (phase & 1) == 0 ? 0.7f : -0.7f;
            float y = (phase & 2) == 0 ? 0.6f : -0.6f;
            InputButtons buttons = tick % 23 == salt % 23 ? InputButtons.Attack | InputButtons.AutoAim : InputButtons.None;
            return new PlayerInput(new Vec2(x, y), Vec2.Zero, buttons);
        }
    }
}
