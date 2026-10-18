using System;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Core.Random;
using Arena.Net.Client;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Net.Server.Match;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Net.Server;
using Arena.Tests.EditMode.Net.Snapshots;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>
    /// A <see cref="MatchHost"/> and real match clients (<see cref="NetClient"/> + <see cref="MatchClientSession"/>) on one
    /// <see cref="InMemoryNetwork"/> with virtual time (Server-Spec §11.1, S3.7a): one room on the committed content pack, players
    /// in the given slots, bots elsewhere. A frame is 1/60 s with a deterministic spread (like a phone); every frame advances the
    /// clock, runs the host, then every client: transport update → <c>BeginFrame</c> → <c>Step</c> per due prediction tick, the
    /// input from the client's script for that tick (<see cref="UseBots"/>: a <see cref="ClientBot"/> reading the display state,
    /// S3.7b). <paramref name="fighters"/> of the constructor picks the fighter of each player (default: player k → fighter k).
    /// </summary>
    public sealed class MatchClientRig
    {
        public const long FrameMicros = 1000000L / 60L;

        /// <summary>± spread of a frame (µs).</summary>
        public const int FrameJitterMicros = 4000;

        public readonly InMemoryNetwork Network;
        public readonly ContentBundle Bundle;
        public readonly MatchHost Host;
        public readonly MatchRoom Room;
        public readonly NetClient[] Clients;
        public readonly MatchClientSession[] Sessions;
        public readonly InMemoryClientSocket[] Sockets;
        public readonly int[] ClientSlots;
        public readonly Func<int, PlayerInput>[] Scripts;
        public readonly ClientBot[] Bots;

        /// <summary>A paused client is not updated at all (the app is in the background or was killed, S3.12).</summary>
        public readonly bool[] Paused;

        private DeterministicRng _frames;

        public MatchClientRig(ModeKind mode, int[] playerSlots, int seed = 7, NetworkConditions conditions = null,
            ClientMatchSettings clientSettings = null, int[] fighters = null, ServerSettings serverSettings = null, int slotCount = -1,
            int teamSize = 0)
        {
            Network = new InMemoryNetwork(seed);
            Network.DefaultConditions = conditions ?? new NetworkConditions();
            Bundle = SnapshotMatchRig.LoadBundle();
            ServerSettings settings = serverSettings ?? MatchServerRig.CreateSettings();
            var transport = TransportSettings.CreateDefault();
            ulong hash = 0x5EEDu;
            Host = new MatchHost(Network.CreateSocket(MatchServerRig.ServerAddress), transport, settings, Bundle, hash,
                new DeterministicNetRandom(seed + 1));

            ContentModeMaps maps = Bundle.MapsFor(mode);
            int mapIndex = maps.MapIndices[0];
            var defs = Bundle.CreateMatchDefs(mapIndex, mode);
            int slots = slotCount > 0 ? slotCount
                : mode == ModeKind.LastBeast
                ? defs.Map.SpawnCount(Arena.Core.Map.MapSpawn.SharedTeam)
                : defs.Map.SpawnCount(0) + defs.Map.SpawnCount(1);
            if (slots > Arena.Core.Sim.SimLimits.MaxFighters) slots = Arena.Core.Sim.SimLimits.MaxFighters;
            var slotFighters = new int[slots];
            for (int i = 0; i < slots; i++) slotFighters[i] = -1;
            for (int k = 0; k < playerSlots.Length; k++)
                slotFighters[playerSlots[k]] = fighters != null ? fighters[k] : k % Bundle.Fighters.Length;
            Room = Host.CreateMatch(MatchRoomRequest.FromBundle(Bundle, mapIndex, mode, slotFighters, seed, teamSize), Network.NowMicros);

            Clients = new NetClient[playerSlots.Length];
            Sessions = new MatchClientSession[playerSlots.Length];
            Sockets = new InMemoryClientSocket[playerSlots.Length];
            Scripts = new Func<int, PlayerInput>[playerSlots.Length];
            Bots = new ClientBot[playerSlots.Length];
            Paused = new bool[playerSlots.Length];
            ClientSlots = playerSlots;
            for (int k = 0; k < playerSlots.Length; k++)
            {
                Clients[k] = new NetClient(transport, hash, new DeterministicNetRandom(seed * 31 + k));
                Sessions[k] = new MatchClientSession(Clients[k], Bundle, clientSettings ?? ClientMatchSettings.CreateDefault());
                Sockets[k] = Network.CreateClientSocket(NetAddress.FromIPv4(10, 0, 1, (byte)(k + 1), (ushort)(5000 + k)),
                    MatchServerRig.ServerAddress);
            }
            _frames = DeterministicRng.FromSeed(seed * 7 + 3);
        }

        public long Now => Network.NowMicros;

        /// <summary>Every client is played by a bot of <paramref name="difficulty"/> that reads its display state.</summary>
        public void UseBots(BotDifficulty difficulty, int seed = 1)
        {
            BotDifficultyDef def = Bundle.FindBotDifficulty(difficulty) ?? Bundle.BotDifficulties[0];
            for (int k = 0; k < Sessions.Length; k++)
            {
                var bot = new ClientBot(Bundle.BotBehaviour, def, ClientSlots[k], seed * 977 + k);
                MatchClientSession s = Sessions[k];
                Bots[k] = bot;
                Scripts[k] = tick => bot.Think(s.Presentation, s.Defs);
            }
        }

        public void ConnectAll()
        {
            for (int k = 0; k < Clients.Length; k++)
                Clients[k].Connect(Sockets[k], ConnectPurpose.Match, Room.GetSlot(ClientSlots[k]).Token, Now);
        }

        /// <summary>One frame of every client (and the host).</summary>
        public void Step()
        {
            long frame = FrameMicros + _frames.Range(-FrameJitterMicros, FrameJitterMicros + 1);
            Network.Advance(frame);
            long now = Network.NowMicros;
            Host.Tick(now);
            for (int k = 0; k < Clients.Length; k++)
            {
                if (Paused[k]) continue;
                Clients[k].Update(now);
                MatchClientSession s = Sessions[k];
                int ticks = s.BeginFrame(now);
                for (int i = 0; i < ticks; i++)
                {
                    PlayerInput input = Scripts[k] != null ? Scripts[k](s.PredictionTick) : default;
                    s.Step(in input, now);
                }
            }
        }

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
            for (int k = 0; k < Sessions.Length; k++)
                if (!Sessions[k].HasEnded) return false;
            return true;
        }
    }
}
