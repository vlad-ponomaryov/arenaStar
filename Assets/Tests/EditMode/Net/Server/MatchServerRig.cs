using System;
using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Net.Server.Diagnostics;
using Arena.Net.Server.Match;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Net.Snapshots;

namespace Arena.Tests.EditMode.Net.Server
{
    /// <summary>
    /// A <see cref="MatchHost"/> and scripted clients on one <see cref="InMemoryNetwork"/> with virtual time (Server-Spec §11.1,
    /// S3.6): one room on the committed content pack, players in the given slots, bots elsewhere. Every step advances the clock
    /// by 1/60 s, runs the host, then every client.
    /// </summary>
    public sealed class MatchServerRig
    {
        public const long FrameMicros = 1000000L / 60L;
        public static readonly NetAddress ServerAddress = NetAddress.FromIPv4(10, 0, 0, 1, 7777);

        public readonly InMemoryNetwork Network;
        public readonly ContentBundle Bundle;
        public readonly MatchHost Host;
        public readonly MatchRoom Room;
        public readonly ScriptedMatchClient[] Clients;
        public readonly int[] ClientSlots;
        public readonly InMemoryClientSocket[] Sockets;

        /// <summary>RTT 150 ms, 50 ms jitter, 2 % loss (GDD §8.3) — split over both directions.</summary>
        public static NetworkConditions CiConditions() => new NetworkConditions { LatencyMs = 75f, JitterMs = 25f, LossPercent = 1f };

        public MatchServerRig(ModeKind mode, int[] playerSlots, int seed = 7, ServerSettings settings = null, NetworkConditions conditions = null,
            int slotCount = -1)
        {
            Network = new InMemoryNetwork(seed);
            Network.DefaultConditions = conditions ?? new NetworkConditions();
            Bundle = SnapshotMatchRig.LoadBundle();
            settings = settings ?? CreateSettings();
            var transport = TransportSettings.CreateDefault();
            ulong hash = 0x5EEDu;
            Host = new MatchHost(Network.CreateSocket(ServerAddress), transport, settings, Bundle, hash, new DeterministicNetRandom(seed + 1));

            ContentModeMaps maps = Bundle.MapsFor(mode);
            int mapIndex = maps.MapIndices[0];
            var defs = Bundle.CreateMatchDefs(mapIndex, mode);
            int slots = slotCount > 0 ? slotCount
                : mode == ModeKind.LastBeast ? defs.Map.SpawnCount(Arena.Core.Map.MapSpawn.SharedTeam) : defs.Map.SpawnCount(0) + defs.Map.SpawnCount(1);
            if (slots > Arena.Core.Sim.SimLimits.MaxFighters) slots = Arena.Core.Sim.SimLimits.MaxFighters;
            var fighters = new int[slots];
            for (int i = 0; i < slots; i++) fighters[i] = -1;
            for (int k = 0; k < playerSlots.Length; k++) fighters[playerSlots[k]] = k % Bundle.Fighters.Length;
            MatchRoomRequest request = MatchRoomRequest.FromBundle(Bundle, mapIndex, mode, fighters, seed);
            Room = Host.CreateMatch(request, Network.NowMicros);

            Clients = new ScriptedMatchClient[playerSlots.Length];
            Sockets = new InMemoryClientSocket[playerSlots.Length];
            ClientSlots = playerSlots;
            for (int k = 0; k < playerSlots.Length; k++)
            {
                Clients[k] = new ScriptedMatchClient(transport, hash, new DeterministicNetRandom(seed * 31 + k));
                Sockets[k] = Network.CreateClientSocket(NetAddress.FromIPv4(10, 0, 1, (byte)(k + 1), (ushort)(5000 + k)), ServerAddress);
            }
        }

        /// <summary>Server settings of the tests: §13 defaults with the input log on.</summary>
        public static ServerSettings CreateSettings()
        {
            ServerSettings s = ServerSettings.CreateDefault();
            s.RecordInputLog = true;
            // The network tests were tuned on Novice fill bots (full-length matches); the default is Medium since F1 (D-107).
            s.FillBotDifficulty = Arena.Bots.BotDifficulty.Novice;
            return s;
        }

        public long Now => Network.NowMicros;

        public void ConnectAll()
        {
            for (int k = 0; k < Clients.Length; k++) Connect(k);
        }

        public void Connect(int k) => Clients[k].Connect(Sockets[k], Room.GetSlot(ClientSlots[k]).Token, Now);

        public void Step()
        {
            Network.Advance(FrameMicros);
            long now = Network.NowMicros;
            Host.Tick(now);
            for (int k = 0; k < Clients.Length; k++) Clients[k].Update(now);
        }

        /// <summary>Steps until <paramref name="done"/> or <paramref name="maxSeconds"/> of virtual time; true if done.</summary>
        public bool RunUntil(Func<bool> done, float maxSeconds)
        {
            long end = Now + (long)(maxSeconds * 1000000.0);
            while (Now < end)
            {
                if (done()) return true;
                Step();
            }
            return done();
        }

        public void RunSeconds(float seconds)
        {
            long end = Now + (long)(seconds * 1000000.0);
            while (Now < end) Step();
        }

        public bool AllEnded()
        {
            for (int k = 0; k < Clients.Length; k++)
                if (!Clients[k].HasEnded) return false;
            return true;
        }

        /// <summary>A deterministic "player": walks in a direction that turns with the tick, shoots now and then.</summary>
        public static PlayerInput Wander(int tick, int salt)
        {
            int phase = (tick / 45 + salt) & 7;
            float x = (phase & 1) == 0 ? 1f : -1f;
            float y = (phase & 2) == 0 ? 0.6f : -0.6f;
            var buttons = tick % 23 == salt % 23 ? InputButtons.Attack | InputButtons.AutoAim : InputButtons.None;
            if (tick % 211 == 0) buttons |= InputButtons.Trick;
            return new PlayerInput(new Arena.Core.Math.Vec2(x * 0.7f, y), Arena.Core.Math.Vec2.Zero, buttons);
        }
    }
}
