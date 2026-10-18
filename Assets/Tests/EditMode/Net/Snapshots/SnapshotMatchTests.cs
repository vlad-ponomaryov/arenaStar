using Arena.Bots;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Net;
using Arena.Net.Serialization;
using Arena.Net.Server.Snapshots;
using Arena.Net.Snapshots;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Net.Snapshots
{
    /// <summary>
    /// Snapshots on whole bot matches (Server-Spec §11.1, S3.5): on every tick of a 3×3 Acorns match and a 10-fighter Last
    /// Beast match each client's decoded view equals the server's view of it; the visibility property holds (no hidden fighter
    /// or mine, no enemy beyond the area of interest, an event about a hidden target carries no point and target and goes
    /// only to the source); projectile segments restore the true positions; sizes stay within the budget (average ≤ 250 B,
    /// maximum ≤ 1200 B); lossy acknowledgements fall back to key frames; the steady state does not allocate.
    /// </summary>
    public sealed class SnapshotMatchTests
    {
        private const int AcornsMaxTicks = 4600;
        private const int LastBeastMaxTicks = 11000;
        private const int AckDelayTicks = 5;
        private const double AverageBudgetBytes = 250.0;
        private const int MaxBudgetBytes = 1200;
        private const float SegmentTolerance = 0.01f;
        private const float AoiTolerance = 0.01f;

        private const int HistoryTicks = 16;
        private readonly uint[,] _seenBy = new uint[HistoryTicks, SimLimits.MaxFighters];
        private readonly int[] _historyTick = new int[HistoryTicks];
        private int _lastRecordedTick = int.MinValue;
        private int _hiddenTargetEvents;
        private int _checkedSegments;
        private int _checkedFighters;
        private SnapshotMatchRig _rig;

        [SetUp]
        public void SetUp()
        {
            _lastRecordedTick = int.MinValue;
            _hiddenTargetEvents = 0;
            _checkedSegments = 0;
            _checkedFighters = 0;
            for (int i = 0; i < HistoryTicks; i++) _historyTick[i] = int.MinValue;
        }

        [Test]
        public void Acorns3v3_FullBotMatch_ViewsMatch_VisibilityHolds_WithinBudget()
        {
            RunFullMatch(ModeKind.Acorns, 41, AcornsMaxTicks);
        }

        [Test]
        public void LastBeast10_FullBotMatch_ViewsMatch_VisibilityAndAoiHold_WithinBudget()
        {
            RunFullMatch(ModeKind.LastBeast, 42, LastBeastMaxTicks);
            Assert.AreEqual(10, _rig.Slots);
        }

        [Test]
        public void LostPackets_FallBackToKeyframes_AndViewsStayExact()
        {
            _rig = SnapshotMatchRig.Create(ModeKind.Acorns, 43, null, 7);
            _rig.AckDelayTicks = AckDelayTicks;
            _rig.LossRate = 0.2;
            _rig.OnDecoded = CheckView;
            for (int t = 0; t < 600; t++) _rig.Step();
            Assert.Greater(_rig.Lost, 0);
            Assert.Less(_rig.Keyframes, _rig.Packets / 10, "with 20 % loss most snapshots are still deltas");

            _rig.LossRate = 1.0; // nothing arrives for longer than the view history
            for (int t = 0; t < NetLimits.SnapshotHistory + 5; t++) _rig.Step();
            Assert.IsTrue(_rig.Builder.LastWasKeyframe, "no acknowledged view in range → key frame");
            long keyframes = _rig.Keyframes;
            _rig.LossRate = 0.0;
            for (int t = 0; t < AckDelayTicks + 2; t++) _rig.Step();
            Assert.IsFalse(_rig.Builder.LastWasKeyframe, "deltas again once a key frame is acknowledged");
            Assert.Greater(_rig.Keyframes, keyframes);
        }

        [Test]
        public void SteadyState_BuildAndDecode_DoNotAllocate()
        {
            AllocationAssert.InitializeTypes(typeof(SnapshotBuilder).Assembly, typeof(SnapshotFrame).Assembly,
                typeof(MatchSimulation).Assembly, typeof(BotController).Assembly);
            var rig = SnapshotMatchRig.Create(ModeKind.Acorns, 44);
            rig.AckDelayTicks = AckDelayTicks;
            for (int t = 0; t < 600; t++) rig.Step();
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = 0; t < 150; t++) rig.Step();
            }, "snapshot build + decode allocates");

            var beast = SnapshotMatchRig.Create(ModeKind.LastBeast, 45);
            beast.AckDelayTicks = AckDelayTicks;
            for (int t = 0; t < 300; t++) beast.Step();
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = 0; t < 150 && !beast.IsEnded; t++) beast.Step();
            }, "Last Beast snapshot build + decode allocates");
        }

        private void RunFullMatch(ModeKind mode, int seed, int maxTicks)
        {
            _rig = SnapshotMatchRig.Create(mode, seed);
            _rig.AckDelayTicks = AckDelayTicks;
            _rig.OnDecoded = CheckView;
            _rig.RunToEnd(maxTicks);
            Assert.IsTrue(_rig.IsEnded, mode + " match ends");
            string report = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "SNAPSHOT BUDGET {0}: {1} clients, {2} ticks, average {3:F1} B, max {4} B, key frames {5}, deferred items {6}, hidden-target events {7}, segments checked {8}, fighters checked {9}",
                mode, _rig.Slots, _rig.Sim.State.Globals.Tick, _rig.AverageBytes, _rig.MaxBytes, _rig.Keyframes, _rig.DeferredItems,
                _hiddenTargetEvents, _checkedSegments, _checkedFighters);
            Debug.Log(report);
            TestContext.WriteLine(report);
            Assert.LessOrEqual(_rig.AverageBytes, AverageBudgetBytes, report);
            Assert.LessOrEqual(_rig.MaxBytes, MaxBudgetBytes, report);
            Assert.Greater(_checkedSegments, 0, "projectiles were checked");
        }

        private void CheckView(int slot, SnapshotFrame view, NetSnapshotEvent[] events, int eventCount)
        {
            MatchState state = _rig.Sim.State;
            MatchDefs defs = _rig.Defs;
            int tick = state.Globals.Tick;
            RecordVisibility(state, tick);
            int team = state.Fighters[slot].Team;
            float aoi = _rig.Builder.Settings.AoiRadiusFor(defs.Mode);
            Vec2 eye = state.Fighters[slot].Position;

            Assert.IsFalse(view.Fighters.Contains(slot), "own fighter is not in the foreign list");
            Assert.IsTrue(view.Own.Equals(state.Fighters[slot]), "own fighter exact");
            Assert.AreEqual(state.Mode.Team0Acorns, view.Mode.Team0Acorns);
            Assert.AreEqual(state.Mode.Team1Acorns, view.Mode.Team1Acorns);
            for (int i = 0; i < view.Fighters.Count; i++)
            {
                int s = view.Fighters.SlotAt(i);
                ref FighterState f = ref state.Fighters[s];
                bool ally = f.Team == team;
                if (!ally && !f.IsVisibleTo(team)) Assert.Fail("tick " + tick + ": slot " + slot + " got hidden fighter " + s);
                if (!ally && aoi > 0f && Vec2.Distance(f.Position, eye) > aoi + AoiTolerance)
                    Assert.Fail("tick " + tick + ": slot " + slot + " got fighter " + s + " beyond the area of interest");
                if (_rig.Builder.LastDeferredItems == 0)
                {
                    ref NetFighterView v = ref view.Fighters.ItemAt(i);
                    Assert.AreEqual(Quantize.ToPosition(f.Position.X), v.PositionX);
                    Assert.AreEqual(f.Health, v.Health);
                    _checkedFighters++;
                }
            }
            for (int i = 0; i < view.Areas.Count; i++)
            {
                int s = view.Areas.SlotAt(i);
                if (!state.Areas[s].Active || !state.Areas[s].IsVisibleTo(team))
                    Assert.Fail("tick " + tick + ": slot " + slot + " got hidden or stale area " + s);
            }
            for (int i = 0; i < view.Projectiles.Count; i++)
            {
                int s = view.Projectiles.SlotAt(i);
                ref NetProjectileSegment seg = ref view.Projectiles.ItemAt(i);
                ref ProjectileState p = ref state.Projectiles[s];
                if (seg.Ended || !p.Active || p.SpawnTick != seg.SpawnTick || _rig.Builder.LastDeferredItems != 0) continue;
                AbilityDef ability = defs.GetAbility(seg.AbilityRef);
                Vec2 dir = MathUtil.AngleToDir(Quantize.FromAngle(seg.Direction, Arena.Net.Protocol.NetProtocol.ProjectileAngleBits));
                Vec2 origin = new Vec2(Quantize.FromPosition(seg.OriginX), Quantize.FromPosition(seg.OriginY));
                Vec2 at = origin + dir * (ability.ProjectileSpeed * SimTime.Dt * (tick - seg.StartTick));
                if (Vec2.Distance(at, p.Position) > SegmentTolerance)
                    Assert.Fail("tick " + tick + ": segment of projectile " + s + " is off by " + Vec2.Distance(at, p.Position));
                _checkedSegments++;
            }
            for (int i = 0; i < eventCount; i++)
            {
                ref NetSnapshotEvent e = ref events[i];
                switch (e.Type)
                {
                    case SimEventType.Damaged:
                    case SimEventType.Healed:
                        if (!e.HasPoint)
                        {
                            Assert.AreEqual(-1, e.Target, "hidden target: no slot");
                            Assert.AreEqual(slot, e.Source, "hidden target: only the source's client");
                            _hiddenTargetEvents++;
                        }
                        else
                        {
                            int targetTeam = state.Fighters[e.Target].Team;
                            if (targetTeam != team && (SeenAt(e.Tick, e.Target) & (1u << team)) == 0u)
                                Assert.Fail("tick " + tick + ": slot " + slot + " got the point of hidden target " + e.Target);
                        }
                        break;
                    case SimEventType.UltReady:
                        Assert.AreEqual(slot, e.Source, "UltReady only to its own client");
                        break;
                    case SimEventType.AbilityFired:
                        Assert.AreEqual(AbilitySlot.Ult, MatchDefs.SlotOf(e.Value), "only ults");
                        if (state.Fighters[e.Source].Team != team && (SeenAt(e.Tick, e.Source) & (1u << team)) == 0u)
                            Assert.Fail("tick " + tick + ": ult of a hidden fighter");
                        break;
                    default:
                        Assert.Fail("unexpected event " + e.Type);
                        break;
                }
            }
        }

        private void RecordVisibility(MatchState state, int tick)
        {
            if (tick == _lastRecordedTick) return;
            _lastRecordedTick = tick;
            int h = tick & (HistoryTicks - 1);
            _historyTick[h] = tick;
            for (int i = 0; i < SimLimits.MaxFighters; i++) _seenBy[h, i] = state.Fighters[i].VisibleToTeams;
        }

        private uint SeenAt(int tick, int slot)
        {
            int h = tick & (HistoryTicks - 1);
            Assert.AreEqual(tick, _historyTick[h], "visibility history");
            return _seenBy[h, slot];
        }
    }
}
