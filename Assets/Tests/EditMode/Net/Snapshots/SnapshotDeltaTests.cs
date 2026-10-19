using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Server.Snapshots;
using Arena.Net.Snapshots;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Snapshots
{
    /// <summary>
    /// Delta rules of snapshots (Server-Spec §4.4, §11.1, S3.5): a key frame and a delta of one state give the same view; a
    /// late snapshot serves as a baseline; a missing baseline is detected and a key frame request is honoured; a snapshot cut
    /// by the byte budget keeps both sides consistent and catches up; random states round-trip; projectile segments (spawn,
    /// pierce, bounce, end, linger); the per-client event filter and its redundancy window.
    /// </summary>
    public sealed class SnapshotDeltaTests
    {
        private const string GladeMapId = "acorns_glade";
        private readonly byte[] _buffer = new byte[NetLimits.MaxPacketBytes];
        private readonly NetSnapshotEvent[] _events = new NetSnapshotEvent[NetLimits.MaxSnapshotEvents];
        private readonly SnapshotDecoder _decoder = new SnapshotDecoder();

        [Test]
        public void KeyframeAndDelta_OfOneState_GiveTheSameView()
        {
            var rig = SnapshotMatchRig.Create(ModeKind.Acorns, 51);
            rig.AckDelayTicks = 3;
            int compared = 0;
            for (int t = 0; t < 1500; t++)
            {
                rig.Step();
                if (t % 50 != 49) continue;
                int tick = rig.Sim.State.Globals.Tick;
                for (int s = 0; s < rig.Slots; s++)
                {
                    SnapshotFrame delta = rig.Views[s].Sent.Get(tick);
                    Assert.IsFalse(delta.Header.IsKeyframe, "the rig sends deltas");
                    var fresh = new ClientView(s);
                    var ring = new SnapshotFrameRing();
                    int bytes = rig.Builder.Build(rig.Sim.State, fresh, default, _buffer, 0);
                    Assert.IsTrue(rig.Builder.LastWasKeyframe);
                    if (rig.Builder.LastDeferredItems != 0 || rig.LastDeferredBySlot[s] != 0) continue;
                    Assert.AreEqual(SnapshotDecodeStatus.Ok, _decoder.Decode(_buffer, 0, bytes, ring, _events, out _, out SnapshotFrame key, out _));
                    Assert.IsTrue(key.ContentEquals(delta), "slot " + s + " tick " + tick + ": key frame view = delta view");
                    Assert.IsTrue(key.ContentEquals(rig.Builder.LastTarget), "complete view = filtered truth");
                    compared++;
                }
            }
            Assert.Greater(compared, 100);
        }

        [Test]
        public void LateSnapshot_IsKept_AndServesAsBaseline()
        {
            var rig = SnapshotMatchRig.Create(ModeKind.Acorns, 52);
            for (int t = 0; t < 200; t++) rig.Step();
            var view = new ClientView(0);
            var ring = new SnapshotFrameRing();
            byte[] first = new byte[NetLimits.MaxPacketBytes];
            byte[] second = new byte[NetLimits.MaxPacketBytes];

            StepSimOnly(rig);
            int tickA = rig.Sim.State.Globals.Tick;
            int lengthA = rig.Builder.Build(rig.Sim.State, view, default, first, 0);
            StepSimOnly(rig);
            int lengthB = rig.Builder.Build(rig.Sim.State, view, default, second, 0);

            Assert.AreEqual(SnapshotDecodeStatus.Ok, Decode(second, lengthB, ring), "newer arrives first");
            Assert.AreEqual(SnapshotDecodeStatus.Ok, Decode(first, lengthA, ring), "late one is still decoded");
            Assert.IsNotNull(ring.Get(tickA));
            view.AckTick(tickA); // the transport acknowledged only the late packet

            StepSimOnly(rig);
            int lengthC = rig.Builder.Build(rig.Sim.State, view, default, _buffer, 0);
            Assert.IsFalse(rig.Builder.LastWasKeyframe);
            Assert.AreEqual(SnapshotDecodeStatus.Ok, _decoder.Decode(_buffer, 0, lengthC, ring, _events, out _, out SnapshotFrame c, out _));
            Assert.AreEqual(tickA, c.Header.BaselineTick);
            Assert.IsTrue(c.ContentEquals(view.Sent.Get(rig.Sim.State.Globals.Tick)));
            Assert.AreEqual(SnapshotDecodeStatus.Duplicate, _decoder.Decode(_buffer, 0, lengthC, ring, _events, out _, out _, out _));
        }

        [Test]
        public void MissingBaseline_IsReported_KeyframeRequestResets()
        {
            var rig = SnapshotMatchRig.Create(ModeKind.Acorns, 53);
            for (int t = 0; t < 100; t++) rig.Step();
            ClientView view = rig.Views[0];
            var emptyRing = new SnapshotFrameRing();
            int bytes = rig.Builder.Build(rig.Sim.State, view, default, _buffer, 0);
            Assert.IsFalse(rig.Builder.LastWasKeyframe);
            Assert.AreEqual(SnapshotDecodeStatus.MissingBaseline, _decoder.Decode(_buffer, 0, bytes, emptyRing, _events, out _, out _, out int tick));
            Assert.AreEqual(rig.Sim.State.Globals.Tick, tick);

            view.RequestKeyframe();
            StepSimOnly(rig);
            bytes = rig.Builder.Build(rig.Sim.State, view, default, _buffer, 0);
            Assert.IsTrue(rig.Builder.LastWasKeyframe);
            Assert.AreEqual(SnapshotDecodeStatus.Ok, _decoder.Decode(_buffer, 0, bytes, emptyRing, _events, out _, out _, out _));
        }

        [Test]
        public void SmallBudget_CutsSnapshots_ViewsStayExact_AndCatchUp()
        {
            SnapshotSettings settings = SnapshotSettings.CreateDefault();
            settings.MaxSnapshotBytes = 160;
            // Glade: its bots meet within the first 30 s, so the small budget is exceeded (Snow Valley, D-141, is quieter early).
            var rig = SnapshotMatchRig.Create(ModeKind.Acorns, 54, settings, 1, GladeMapId);
            rig.AckDelayTicks = 2;
            for (int t = 0; t < 900; t++) rig.Step(); // the rig asserts decoded view == server view on every snapshot
            Assert.Greater(rig.DeferredItems, 0, "some snapshots were cut");
            Assert.LessOrEqual(rig.MaxBytes, settings.MaxSnapshotBytes);

            // With a normal budget the views converge to the full truth.
            var full = new SnapshotBuilder(rig.Defs, SnapshotSettings.CreateDefault());
            MatchState state = rig.Sim.State;
            ClientView view = rig.Views[0];
            int last = 0;
            for (int i = 0; i < NetLimits.SnapshotHistory; i++)
            {
                state.Globals.Tick++; // the same world one tick later; the previous view is acknowledged
                full.Capture(state, null);
                last = full.Build(state, view, default, _buffer, 0);
                view.AckTick(state.Globals.Tick);
                if (full.LastDeferredItems == 0) break;
            }
            Assert.Greater(last, 0);
            Assert.IsTrue(view.Sent.Get(rig.Sim.State.Globals.Tick).ContentEquals(full.LastTarget), "complete again");
        }

        [Test]
        public void RandomStates_RoundTrip_KeyframeAndDelta()
        {
            var rig = SnapshotMatchRig.Create(ModeKind.LastBeast, 55);
            rig.AckDelayTicks = 1;
            var rng = new System.Random(17);
            MatchState state = rig.Sim.State;
            for (int round = 0; round < 300; round++)
            {
                state.Globals.Tick++;
                Scramble(state, rng);
                rig.Builder.Capture(state, null);
                rig.SendAll(); // asserts decoded view == server view
            }
            Assert.Greater(rig.Packets, 0);
        }

        [Test]
        public void ProjectileTracker_Segments_SpawnPierceBounceEndLinger()
        {
            var tracker = new ProjectileSegmentTracker();
            var state = new MatchState();
            state.Globals.Tick = 10;
            ref ProjectileState p = ref state.Projectiles[3];
            p.Active = true;
            p.SpawnTick = 9;
            p.Owner = 2;
            p.AbilityRef = 4;
            p.Position = new Vec2(5f, 6f);
            p.Dir = new Vec2(1f, 0f);
            p.PierceLeft = 1;
            tracker.Capture(state, 2);
            Assert.IsTrue(tracker.Has(3));
            NetProjectileSegment s = tracker.Get(3);
            Assert.AreEqual(10, s.StartTick);
            Assert.AreEqual(9, s.SpawnTick);
            Assert.AreEqual(Quantize.ToPosition(5f), s.OriginX);
            Assert.IsFalse(s.AfterBounce);

            state.Globals.Tick = 11;
            p.Position = new Vec2(5.4f, 6f);
            p.PierceLeft = 0;
            p.HitMask = 1u << 4;
            tracker.Capture(state, 2);
            s = tracker.Get(3);
            Assert.AreEqual(10, s.StartTick, "a pierce keeps the segment");
            Assert.AreEqual(0, s.PierceLeft);
            Assert.AreEqual(1 << 4, s.HitMask);

            state.Globals.Tick = 12;
            p.Position = new Vec2(5.6f, 6f);
            p.Dir = new Vec2(0f, 1f);
            tracker.Capture(state, 2);
            s = tracker.Get(3);
            Assert.AreEqual(12, s.StartTick, "a bounce starts a segment");
            Assert.IsTrue(s.AfterBounce);
            Assert.AreEqual(Quantize.ToPosition(5.6f), s.OriginX);

            state.Globals.Tick = 13;
            p.Active = false;
            p.Position = new Vec2(5.6f, 6.3f);
            tracker.Capture(state, 2);
            s = tracker.Get(3);
            Assert.IsTrue(s.Ended);
            Assert.AreEqual(13, s.EndTick);
            Assert.AreEqual(Quantize.ToPosition(6.3f), s.EndY);

            state.Globals.Tick = 15;
            tracker.Capture(state, 2);
            Assert.IsTrue(tracker.Has(3), "lingers");
            state.Globals.Tick = 16;
            tracker.Capture(state, 2);
            Assert.IsFalse(tracker.Has(3), "forgotten after the linger");

            state.Globals.Tick = 17;
            p.Active = true;
            p.SpawnTick = 17;
            p.Dir = new Vec2(0f, 1f);
            tracker.Capture(state, 2);
            Assert.IsFalse(tracker.Get(3).AfterBounce, "a new projectile in the slot is not a bounce");
            state.Globals.Tick = 18;
            p.SpawnTick = 18; // reused slot within one tick: new identity
            tracker.Capture(state, 2);
            Assert.AreEqual(18, tracker.Get(3).SpawnTick);
            Assert.AreEqual(18, tracker.Get(3).StartTick);
        }

        [Test]
        public void EventFilter_HiddenTarget_OnlySourceWithoutPoint_UltReadyOwnOnly_Redundancy()
        {
            var state = new MatchState();
            state.Globals.FighterCount = 3;
            SetFighter(state, 0, 0, ~0u);
            SetFighter(state, 1, 1, 1u << 1); // in a bush: only its own team sees it
            SetFighter(state, 2, 0, ~0u);
            int ultRef = MatchDefs.MakeAbilityRef(0, AbilitySlot.Ult);
            int attackRef = MatchDefs.MakeAbilityRef(0, AbilitySlot.Attack);

            var journal = new EventJournal();
            var events = new SimEventBuffer();
            state.Globals.Tick = 100;
            events.Add(new SimEvent(SimEventType.Damaged, 99, 0, 1, 400, new Vec2(3f, 4f), Vec2.Zero, attackRef));
            events.Add(new SimEvent(SimEventType.Damaged, 99, 1, 0, 300, new Vec2(1f, 1f), Vec2.Zero, attackRef));
            events.Add(new SimEvent(SimEventType.UltReady, 99, 0, -1, 2000, Vec2.Zero, Vec2.Zero));
            events.Add(new SimEvent(SimEventType.AbilityFired, 99, 1, -1, MatchDefs.MakeAbilityRef(1, AbilitySlot.Ult), new Vec2(3f, 4f), Vec2.Zero));
            events.Add(new SimEvent(SimEventType.AbilityFired, 99, 0, -1, attackRef, new Vec2(1f, 1f), Vec2.Zero));
            events.Add(new SimEvent(SimEventType.AbilityFired, 99, 2, -1, ultRef, new Vec2(1f, 1f), Vec2.Zero));
            events.Add(new SimEvent(SimEventType.ProjectileHit, 99, 0, 1, 5, new Vec2(3f, 4f), Vec2.Zero));
            journal.Capture(state, events);

            // Slot 0 (source of the hit on the hidden slot 1).
            int n = journal.Collect(100, 3, 0, 0, _events, _events.Length);
            Assert.IsTrue(Find(n, SimEventType.Damaged, 0, out NetSnapshotEvent hidden));
            Assert.AreEqual(-1, hidden.Target);
            Assert.IsFalse(hidden.HasPoint);
            Assert.AreEqual(400, hidden.Value, "the amount stays: sound and vibration by size");
            Assert.IsTrue(Find(n, SimEventType.Damaged, 1, out NetSnapshotEvent onMe), "a hit on me from a hidden enemy");
            Assert.IsTrue(onMe.HasPoint);
            Assert.AreEqual(0, onMe.Target);
            Assert.IsTrue(Find(n, SimEventType.UltReady, 0, out _));
            Assert.IsFalse(Find(n, SimEventType.AbilityFired, 1, out _), "ult of a hidden enemy");
            Assert.IsTrue(Find(n, SimEventType.AbilityFired, 2, out _), "ally ult");
            Assert.IsFalse(Find(n, SimEventType.AbilityFired, 0, out _), "attacks are not view events");
            Assert.IsFalse(Find(n, SimEventType.ProjectileHit, 0, out _));

            // Slot 2 (ally of the source): the hit on the hidden target does not reach it at all.
            n = journal.Collect(100, 3, 2, 0, _events, _events.Length);
            for (int i = 0; i < n; i++)
                Assert.IsFalse(_events[i].Type == SimEventType.Damaged && _events[i].Source == 0, "ally of the source learns nothing");
            Assert.IsFalse(Find(n, SimEventType.UltReady, 0, out _), "UltReady of another fighter");

            // Slot 1 (the hidden target's team sees itself): full event with the point.
            n = journal.Collect(100, 3, 1, 1, _events, _events.Length);
            Assert.IsTrue(Find(n, SimEventType.Damaged, 0, out NetSnapshotEvent full));
            Assert.IsTrue(full.HasPoint);
            Assert.AreEqual(1, full.Target);

            // Redundancy: the events ride in the snapshots of ticks 100, 101, 102 — not 103.
            for (int t = 101; t <= 103; t++)
            {
                state.Globals.Tick = t;
                events.Clear();
                journal.Capture(state, events);
                n = journal.Collect(t, 3, 0, 0, _events, _events.Length);
                Assert.AreEqual(t <= 102, Find(n, SimEventType.UltReady, 0, out _), "tick " + t);
            }

            // Limit: newest first.
            Assert.AreEqual(1, journal.Collect(100, 3, 0, 0, _events, 1));
        }

        // ---- helpers ----

        private static void StepSimOnly(SnapshotMatchRig rig)
        {
            var inputs = new Arena.Core.Input.PlayerInput[SimLimits.MaxFighters];
            rig.Sim.Tick(inputs);
            rig.Builder.Capture(rig.Sim.State, rig.Sim.Events);
        }

        private SnapshotDecodeStatus Decode(byte[] data, int length, SnapshotFrameRing ring) =>
            _decoder.Decode(data, 0, length, ring, _events, out _, out _, out _);

        private bool Find(int count, SimEventType type, int source, out NetSnapshotEvent found)
        {
            for (int i = 0; i < count; i++)
            {
                if (_events[i].Type != type || _events[i].Source != source) continue;
                found = _events[i];
                return true;
            }
            found = default;
            return false;
        }

        private static void SetFighter(MatchState state, int slot, int team, uint visibleTo)
        {
            ref FighterState f = ref state.Fighters[slot];
            f.Active = true;
            f.Alive = true;
            f.Team = team;
            f.VisibleToTeams = visibleTo | (1u << team);
        }

        /// <summary>Random but plausible changes of fighters, projectiles, areas, pickups, mode and tiles.</summary>
        private static void Scramble(MatchState state, System.Random rng)
        {
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (rng.Next(3) == 0) f.Position = new Vec2((float)rng.NextDouble() * state.Globals.MapWidth, (float)rng.NextDouble() * state.Globals.MapHeight);
                if (rng.Next(4) == 0) f.Health = rng.Next(1, 6000);
                if (rng.Next(5) == 0) f.Velocity = new Vec2((float)rng.NextDouble() * 8f - 4f, (float)rng.NextDouble() * 8f - 4f);
                if (rng.Next(6) == 0) f.VisibleToTeams = (uint)rng.Next() | (1u << f.Team);
                if (rng.Next(8) == 0) f.Alive = !f.Alive;
                if (rng.Next(8) == 0) f.Berries = rng.Next(10);
            }
            for (int k = 0; k < 6; k++)
            {
                ref ProjectileState p = ref state.Projectiles[rng.Next(state.Projectiles.Length)];
                p.Active = rng.Next(3) != 0;
                p.SpawnTick = state.Globals.Tick - rng.Next(3);
                p.Position = new Vec2((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 40f);
                p.Dir = MathUtil.AngleToDir((float)rng.NextDouble() * 360f);
                p.HitMask = (uint)rng.Next(1 << 10);
                p.Owner = rng.Next(count);
            }
            for (int k = 0; k < 3; k++)
            {
                ref AreaState a = ref state.Areas[rng.Next(state.Areas.Length)];
                a.Active = rng.Next(3) != 0;
                a.Position = new Vec2((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 40f);
                a.Radius = (float)rng.NextDouble() * 3f;
                a.VisibleToTeams = (uint)rng.Next();
                a.ExpireTick = state.Globals.Tick + rng.Next(100);
            }
            for (int k = 0; k < 4; k++)
            {
                ref PickupState p = ref state.Pickups[rng.Next(state.Pickups.Length)];
                p.Active = rng.Next(3) != 0;
                p.Kind = PickupKind.Berry;
                p.Value = 1 + rng.Next(2);
                p.Position = new Vec2((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 40f);
                p.SpawnTick = state.Globals.Tick;
            }
            state.Mode.FogStep = rng.Next(5);
            state.Mode.SafeMinX = (float)rng.NextDouble() * 10f;
            state.Mode.AliveCount = rng.Next(11);
            int cell = rng.Next(state.Globals.MapWidth * state.Globals.MapHeight);
            state.Tiles[cell] = Arena.Core.Map.TileType.Floor;
        }
    }
}
