using System;
using System.Diagnostics;
using System.IO;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net;
using Arena.Net.Content;
using Arena.Net.Diagnostics;
using Arena.Net.Server.Snapshots;
using Arena.Net.Snapshots;

namespace Arena.Server
{
    /// <summary>
    /// Self-test of snapshots on the server runtime (S3.5): a whole bot match, every tick a snapshot to every slot, decoded by
    /// an in-process client; views must match, sizes stay within the budget, and the steady state allocates 0 bytes
    /// (<c>GC.GetAllocatedBytesForCurrentThread</c> is exact on CoreCLR). Acknowledgements return 5 ticks later (≈ RTT 150 ms).
    /// </summary>
    public static class SnapshotSelfTest
    {
        private const int AckDelayTicks = 5;
        private const int WarmupTicks = 300;
        private const double AverageBudgetBytes = 250.0;
        private const int MaxBudgetBytes = 1200;

        public static string Run(TextWriter output, ContentBundle bundle, ModeKind mode, int seed)
        {
            ContentModeMaps maps = bundle.MapsFor(mode);
            if (maps == null) return "no " + mode + " maps";
            MatchDefs defs = bundle.CreateMatchDefs(maps.MapIndices[0], mode);
            BotDifficultyDef novice = bundle.FindBotDifficulty(BotDifficulty.Novice);
            MatchSetup setup = HeadlessMatch.CreateBotSetup(defs, bundle.Policy, seed);
            var sim = new MatchSimulation(defs, setup);
            var bots = new BotController(bundle.BotBehaviour);
            for (int i = 0; i < setup.SlotCount; i++) bots.AddBot(i, novice, setup.Seed);
            var inputs = new PlayerInput[SimLimits.MaxFighters];
            var builder = new SnapshotBuilder(defs, SnapshotSettings.CreateDefault());
            var decoder = new SnapshotDecoder();
            var events = new NetSnapshotEvent[NetLimits.MaxSnapshotEvents];
            var buffer = new byte[NetLimits.MaxPacketBytes];
            int slots = setup.SlotCount;
            var views = new ClientView[slots];
            var rings = new SnapshotFrameRing[slots];
            for (int i = 0; i < slots; i++)
            {
                views[i] = new ClientView(i);
                rings[i] = new SnapshotFrameRing();
            }
            int maxTicks = (mode == ModeKind.LastBeast ? defs.LastBeast.MaxDurationTicks : defs.Acorns.DurationTicks) + 1;

            long totalBytes = 0, packets = 0, buildTicks = 0, allocStart = 0;
            bool measured = false;
            int maxBytes = 0;
            string error = null;
            builder.Capture(sim.State, null);
            for (int t = 0; t < maxTicks && !sim.IsEnded && error == null; t++)
            {
                if (t == WarmupTicks)
                {
                    measured = true;
                    allocStart = GC.GetAllocatedBytesForCurrentThread();
                }
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, sim.Events, inputs);
                sim.Tick(inputs);
                long start = Stopwatch.GetTimestamp();
                builder.Capture(sim.State, sim.Events);
                int tick = sim.State.Globals.Tick;
                for (int s = 0; s < slots && error == null; s++)
                {
                    views[s].AckTick(tick - AckDelayTicks);
                    int bytes = builder.Build(sim.State, views[s], default, buffer, 0);
                    if (bytes <= 0)
                    {
                        error = "tick " + tick + " slot " + s + ": snapshot did not fit";
                        break;
                    }
                    totalBytes += bytes;
                    packets++;
                    if (bytes > maxBytes) maxBytes = bytes;
                    SnapshotDecodeStatus status = decoder.Decode(buffer, 0, bytes, rings[s], events, out _, out SnapshotFrame frame, out _);
                    if (status != SnapshotDecodeStatus.Ok) error = "tick " + tick + " slot " + s + ": " + status;
                    else if (!frame.ContentEquals(views[s].Sent.Get(tick))) error = "tick " + tick + " slot " + s + ": views differ";
                }
                buildTicks += Stopwatch.GetTimestamp() - start;
            }
            long allocated = measured ? GC.GetAllocatedBytesForCurrentThread() - allocStart : 0L;
            if (error != null) return error;
            double average = packets == 0 ? 0.0 : (double)totalBytes / packets;
            double usPerSnapshot = packets == 0 ? 0.0 : buildTicks * 1e6 / Stopwatch.Frequency / packets;
            output.WriteLine("        snapshots " + mode + ": " + slots + " clients, " + sim.State.Globals.Tick + " ticks, average " +
                average.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " B, max " + maxBytes + " B, " +
                usPerSnapshot.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " us per build+decode, allocated " + allocated + " B");
            if (!sim.IsEnded) return "the match did not end";
            if (average > AverageBudgetBytes) return "average " + average + " B over the budget";
            if (maxBytes > MaxBudgetBytes) return "max " + maxBytes + " B over the limit";
            return allocated == 0 ? null : "steady state allocated " + allocated + " bytes";
        }
    }
}
