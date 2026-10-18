using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Editor.Net;
using Arena.Net;
using Arena.Net.Content;
using Arena.Net.Diagnostics;
using Arena.Net.Server.Snapshots;
using Arena.Net.Snapshots;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Net.Snapshots
{
    /// <summary>
    /// A bot match on the committed content pack with a snapshot pipeline per slot (Server-Spec §11.1, S3.5): every tick the
    /// server builds each client's snapshot, the "client" decodes it into its own ring, the server view and the decoded view
    /// are compared, and acknowledgements come back after <see cref="AckDelayTicks"/> (≈ RTT) unless the packet is "lost".
    /// <see cref="OnDecoded"/> lets a test inspect every decoded view. Steady-state steps do not allocate (no callback).
    /// </summary>
    public sealed class SnapshotMatchRig
    {
        public delegate void DecodedHandler(int slot, SnapshotFrame view, NetSnapshotEvent[] events, int eventCount);

        private const int PendingCapacity = 1024;

        public readonly MatchSimulation Sim;
        public readonly MatchDefs Defs;
        public readonly SnapshotBuilder Builder;
        public readonly ClientView[] Views;
        public readonly SnapshotFrameRing[] ClientRings;
        public readonly int Slots;

        /// <summary>Items the last snapshot of each slot deferred (byte budget).</summary>
        public readonly int[] LastDeferredBySlot = new int[SimLimits.MaxFighters];

        private readonly BotController _bots;
        private readonly PlayerInput[] _inputs = new PlayerInput[SimLimits.MaxFighters];
        private readonly SnapshotDecoder _decoder = new SnapshotDecoder();
        private readonly NetSnapshotEvent[] _events = new NetSnapshotEvent[NetLimits.MaxSnapshotEvents];
        private readonly byte[] _buffer = new byte[NetLimits.MaxPacketBytes];
        private readonly System.Random _loss;

        private readonly int[] _pendingDue = new int[PendingCapacity];
        private readonly int[] _pendingSlot = new int[PendingCapacity];
        private readonly int[] _pendingTick = new int[PendingCapacity];
        private int _pendingHead;
        private int _pendingCount;

        public int AckDelayTicks;
        public double LossRate;
        public DecodedHandler OnDecoded;

        public long TotalBytes;
        public long Packets;
        public int MaxBytes;
        public long Keyframes;
        public long DeferredItems;
        public long Lost;

        public static ContentBundle LoadBundle()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ContentPackExporter.PackPath);
            Assert.IsNotNull(asset, "ArenaContent.bytes");
            return ContentPackReader.Read(asset.bytes, true);
        }

        public static SnapshotMatchRig Create(ModeKind mode, int seed, SnapshotSettings settings = null, int lossSeed = 1)
        {
            ContentBundle bundle = LoadBundle();
            ContentModeMaps maps = bundle.MapsFor(mode);
            Assert.IsNotNull(maps, mode + " has maps");
            MatchDefs defs = bundle.CreateMatchDefs(maps.MapIndices[0], mode);
            MatchSetup setup = HeadlessMatch.CreateBotSetup(defs, bundle.Policy, seed);
            return new SnapshotMatchRig(defs, setup, bundle.BotBehaviour, bundle.FindBotDifficulty(BotDifficulty.Novice),
                settings ?? SnapshotSettings.CreateDefault(), lossSeed);
        }

        public SnapshotMatchRig(MatchDefs defs, MatchSetup setup, BotBehaviourDef behaviour, BotDifficultyDef difficulty,
            SnapshotSettings settings, int lossSeed)
        {
            Defs = defs;
            Sim = new MatchSimulation(defs, setup);
            Slots = setup.SlotCount;
            _bots = new BotController(behaviour);
            for (int i = 0; i < Slots; i++) _bots.AddBot(i, difficulty, setup.Seed);
            Builder = new SnapshotBuilder(defs, settings);
            Views = new ClientView[Slots];
            ClientRings = new SnapshotFrameRing[Slots];
            for (int i = 0; i < Slots; i++)
            {
                Views[i] = new ClientView(i);
                ClientRings[i] = new SnapshotFrameRing();
            }
            _loss = new System.Random(lossSeed);
            Builder.Capture(Sim.State, null);
        }

        public double AverageBytes => Packets == 0 ? 0.0 : (double)TotalBytes / Packets;

        public bool IsEnded => Sim.IsEnded;

        /// <summary>One server tick: bots, simulation, capture, and a snapshot to every slot.</summary>
        public void Step()
        {
            for (int i = 0; i < _inputs.Length; i++) _inputs[i] = default;
            _bots.Update(Sim.State, Sim.Defs, Sim.Events, _inputs);
            Sim.Tick(_inputs);
            Builder.Capture(Sim.State, Sim.Events);
            SendAll();
        }

        /// <summary>Builds, "sends" and decodes the snapshot of every slot for the current tick.</summary>
        public void SendAll()
        {
            int tick = Sim.State.Globals.Tick;
            DeliverAcks(tick);
            for (int s = 0; s < Slots; s++) SendTo(s, tick);
        }

        public void RunToEnd(int maxTicks)
        {
            for (int t = 0; t < maxTicks && !Sim.IsEnded; t++) Step();
        }

        private void SendTo(int slot, int tick)
        {
            int bytes = Builder.Build(Sim.State, Views[slot], default, _buffer, 0);
            if (bytes <= 0) Assert.Fail("snapshot did not fit");
            TotalBytes += bytes;
            Packets++;
            if (bytes > MaxBytes) MaxBytes = bytes;
            if (Builder.LastWasKeyframe) Keyframes++;
            DeferredItems += Builder.LastDeferredItems;
            LastDeferredBySlot[slot] = Builder.LastDeferredItems;

            if (LossRate > 0.0 && _loss.NextDouble() < LossRate)
            {
                Lost++;
                return;
            }
            SnapshotDecodeStatus status = _decoder.Decode(_buffer, 0, bytes, ClientRings[slot], _events, out int eventCount,
                out SnapshotFrame view, out _);
            if (status != SnapshotDecodeStatus.Ok) Assert.Fail("slot " + slot + " tick " + tick + ": " + status);
            SnapshotFrame sent = Views[slot].Sent.Get(tick);
            if (!sent.ContentEquals(view) || !sent.Header.Equals(view.Header))
                Assert.Fail("slot " + slot + " tick " + tick + ": decoded view differs from the server's view");
            OnDecoded?.Invoke(slot, view, _events, eventCount);
            Schedule(tick + AckDelayTicks, slot, tick);
        }

        private void Schedule(int due, int slot, int tick)
        {
            if (_pendingCount >= PendingCapacity) Assert.Fail("ack queue full");
            int i = (_pendingHead + _pendingCount) % PendingCapacity;
            _pendingDue[i] = due;
            _pendingSlot[i] = slot;
            _pendingTick[i] = tick;
            _pendingCount++;
        }

        private void DeliverAcks(int now)
        {
            while (_pendingCount > 0 && _pendingDue[_pendingHead] <= now)
            {
                Views[_pendingSlot[_pendingHead]].AckTick(_pendingTick[_pendingHead]);
                _pendingHead = (_pendingHead + 1) % PendingCapacity;
                _pendingCount--;
            }
        }
    }
}
