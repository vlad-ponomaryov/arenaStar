using System;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;

namespace Arena.Net.Server.Snapshots
{
    /// <summary>
    /// Builds the snapshot of every client of one match (Server-Spec §4.4, D-069). <see cref="Capture"/> runs once per server
    /// tick (projectile segments, event journal, changed cells, quantized views of fighters, areas, pickups and the mode);
    /// <see cref="Build"/> runs per client: the filter works <b>before</b> encoding — a fighter or mine the client's team does not
    /// see never enters its target view (anti-cheat against "seeing through bushes"); in Last Beast foreign fighters and
    /// projectiles farther than the area of interest are left out too. The target lists are ordered by priority (allies, then
    /// enemies by distance; projectiles, areas, pickups by distance), the encoder writes the delta to the newest acknowledged
    /// view within the byte budget. One thread, no allocations after construction.
    /// </summary>
    public sealed class SnapshotBuilder
    {
        private readonly MatchDefs _defs;
        private readonly SnapshotSettings _settings;
        private readonly float _aoiRadiusSq;
        private readonly ProjectileSegmentTracker _projectiles = new ProjectileSegmentTracker();
        private readonly EventJournal _journal = new EventJournal();
        private readonly SnapshotEncoder _encoder = new SnapshotEncoder();
        private readonly SnapshotFrame _target = new SnapshotFrame();
        private readonly NetSnapshotEvent[] _events = new NetSnapshotEvent[NetLimits.MaxSnapshotEvents];
        private readonly uint[] _changedCells = new uint[SnapshotFrame.CellWords];

        // Quantized per-tick views (team-independent part), by slot.
        private readonly NetFighterView[] _fighterViews = new NetFighterView[SimLimits.MaxFighters];
        private readonly NetAreaView[] _areaViews = new NetAreaView[SimLimits.MaxAreas];
        private readonly NetPickupView[] _pickupViews = new NetPickupView[SimLimits.MaxPickups];
        private NetModeView _mode;
        private int _capturedTick;
        private bool _captured;

        // Priority sorting scratch (slot, key) — sized for the largest list (projectiles).
        private readonly int[] _sortSlots = new int[SimLimits.MaxProjectiles];
        private readonly float[] _sortKeys = new float[SimLimits.MaxProjectiles];

        public SnapshotBuilder(MatchDefs defs, SnapshotSettings settings)
        {
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            float aoi = _settings.AoiRadiusFor(defs.Mode);
            _aoiRadiusSq = aoi > 0f ? aoi * aoi : 0f;
        }

        public SnapshotSettings Settings => _settings;

        /// <summary>The filtered true view of the last <see cref="Build"/> (before the budget cut) — tests.</summary>
        public SnapshotFrame LastTarget => _target;

        /// <summary>Events written by the last <see cref="Build"/> (newest first) — tests.</summary>
        public NetSnapshotEvent[] LastEvents => _events;
        public int LastEventCount { get; private set; }

        /// <summary>Items of the last <see cref="Build"/> that waited for the next snapshot (byte budget).</summary>
        public int LastDeferredItems => _encoder.LastDeferredItems;

        public bool LastWasKeyframe { get; private set; }

        public EventJournal Journal => _journal;

        public void Reset()
        {
            _projectiles.Reset();
            _journal.Reset();
            Array.Clear(_changedCells, 0, _changedCells.Length);
            _captured = false;
        }

        /// <summary>After every simulation tick (also before the first snapshot, with an empty event buffer).</summary>
        public void Capture(MatchState state, SimEventBuffer events)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            _capturedTick = state.Globals.Tick;
            _captured = true;
            _projectiles.Capture(state, _settings.ProjectileEndLingerTicks);
            if (events != null) _journal.Capture(state, events);
            CaptureCells(state);
            CaptureMode(state);
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++) _fighterViews[i] = ToView(in state.Fighters[i]);
            for (int i = 0; i < state.Areas.Length; i++)
                if (state.Areas[i].Active) _areaViews[i] = ToView(in state.Areas[i]);
            for (int i = 0; i < state.Pickups.Length; i++)
                if (state.Pickups[i].Active) _pickupViews[i] = ToView(in state.Pickups[i]);
        }

        /// <summary>
        /// Builds and encodes the snapshot of the client of <paramref name="view"/> for the captured tick into
        /// <paramref name="buffer"/> at <paramref name="offset"/> (at most <c>MaxSnapshotBytes</c>). <paramref name="header"/>
        /// gives the input acknowledgement, margin and bot flag; its tick and slot are set here. The produced view is stored in
        /// <paramref name="view"/>; after sending call <see cref="ClientView.MarkSent"/>. Returns the byte count (0 — did not fit).
        /// </summary>
        public int Build(MatchState state, ClientView view, SnapshotHeader header, byte[] buffer, int offset)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return Build(state, view, header, state.Globals.Tick, buffer, offset);
        }

        /// <summary>
        /// Same as <see cref="Build(MatchState, ClientView, SnapshotHeader, byte[], int)"/> with an explicit snapshot tick: in the
        /// countdown (§2.3) the captured state is that of tick 0 and <paramref name="serverTick"/> is the negative countdown tick.
        /// </summary>
        public int Build(MatchState state, ClientView view, SnapshotHeader header, int serverTick, byte[] buffer, int offset)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!_captured || state.Globals.Tick != _capturedTick) throw new InvalidOperationException("SnapshotBuilder: Capture the tick first");
            int tick = serverTick;
            header.ServerTick = tick;
            header.OwnSlot = view.Slot;
            BuildTarget(state, view.Slot, in header);

            SnapshotFrame baseline = view.ChooseBaseline(tick);
            SnapshotFrame result = view.BeginFrame(tick);
            int team = state.Fighters[view.Slot].Team;
            int maxEvents = _settings.MaxEventsPerSnapshot;
            LastEventCount = _journal.Collect(tick, _settings.EventWindowTicks, view.Slot, team, state.Fighters[view.Slot].Position,
                _aoiRadiusSq, _events, maxEvents);
            int bytes = _encoder.Encode(baseline, _target, _events, LastEventCount, result, buffer, offset, _settings.MaxSnapshotBytes);
            LastWasKeyframe = baseline == null;
            if (bytes > 0) view.CountBuilt(LastWasKeyframe);
            return bytes;
        }

        private void BuildTarget(MatchState state, int slot, in SnapshotHeader header)
        {
            SnapshotFrame t = _target;
            t.Clear();
            t.Header = header;
            t.Valid = true;
            t.Mode = _mode;
            ref FighterState own = ref state.Fighters[slot];
            t.Own = own;
            int team = own.Team;
            uint teamBit = (uint)team < 32u ? 1u << team : 0u;
            Vec2 eye = own.Position;

            StatusEffectState[] statuses = state.Statuses;
            for (int i = 0; i < statuses.Length && t.OwnStatusCount < t.OwnStatuses.Length; i++)
                if (statuses[i].Active && statuses[i].Target == slot) t.OwnStatuses[t.OwnStatusCount++] = statuses[i];

            // Fighters: allies (key −1, by slot), then visible enemies by distance.
            int n = 0;
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
            {
                if (i == slot) continue;
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active) continue;
                bool ally = f.Team == team;
                float d = Vec2.SqrDistance(f.Position, eye);
                if (!ally)
                {
                    if ((f.VisibleToTeams & teamBit) == 0u) continue;
                    if (_aoiRadiusSq > 0f && d > _aoiRadiusSq) continue;
                }
                Push(ref n, i, ally ? -1f : d);
            }
            SortByKey(n);
            uint shown = 1u << slot; // fighters this client may know about: itself, allies, visible enemies in the area
            for (int k = 0; k < n; k++)
            {
                int i = _sortSlots[k];
                shown |= 1u << i;
                ref FighterState f = ref state.Fighters[i];
                NetFighterView v = _fighterViews[i];
                if (f.Team == team)
                {
                    if (f.Invisible) v.Flags |= NetFighterFlags.Invisible;
                    if ((f.VisibleToTeams & ~teamBit) != 0u) v.Flags |= NetFighterFlags.SeenByEnemies;
                }
                if ((f.MarkedByTeamMask & teamBit) != 0u) v.Flags |= NetFighterFlags.MarkedByViewer;
                t.Fighters.Set(i, in v);
            }

            // Projectiles: everyone sees shots (D-024); AOI in Last Beast; nearest first.
            n = 0;
            for (int i = 0; i < _projectiles.SlotCount; i++)
            {
                if (!_projectiles.Has(i)) continue;
                float d = Vec2.SqrDistance(_projectiles.PointOf(i), eye);
                if (_aoiRadiusSq > 0f && d > _aoiRadiusSq) continue;
                Push(ref n, i, d);
            }
            SortByKey(n);
            for (int k = 0; k < n && !t.Projectiles.IsFull; k++)
            {
                NetProjectileSegment seg = _projectiles.Get(_sortSlots[k]);
                if (seg.Owner != slot) HideUnseenHits(ref seg, shown);
                t.Projectiles.Set(_sortSlots[k], in seg);
            }

            // Areas visible to the team (hidden mines, D-030), nearest first; foreign ones only inside the area of interest, and
            // their origin (where the thrower stood) only if it is inside too (F2, D-117).
            n = 0;
            AreaState[] areas = state.Areas;
            for (int i = 0; i < areas.Length; i++)
            {
                if (!areas[i].Active || (areas[i].VisibleToTeams & teamBit) == 0u) continue;
                float d = Vec2.SqrDistance(areas[i].Position, eye);
                if (_aoiRadiusSq > 0f && areas[i].Team != team && d > _aoiRadiusSq) continue;
                Push(ref n, i, d);
            }
            SortByKey(n);
            for (int k = 0; k < n && !t.Areas.IsFull; k++)
            {
                int i = _sortSlots[k];
                NetAreaView a = _areaViews[i];
                if (_aoiRadiusSq > 0f && areas[i].Team != team && Vec2.SqrDistance(areas[i].Origin, eye) > _aoiRadiusSq)
                {
                    a.OriginX = a.PositionX;
                    a.OriginY = a.PositionY;
                }
                t.Areas.Set(i, in a);
            }

            // Pickups, nearest first.
            n = 0;
            PickupState[] pickups = state.Pickups;
            for (int i = 0; i < pickups.Length; i++)
            {
                if (!pickups[i].Active) continue;
                Push(ref n, i, Vec2.SqrDistance(pickups[i].Position, eye));
            }
            SortByKey(n);
            for (int k = 0; k < n && !t.Pickups.IsFull; k++) t.Pickups.Set(_sortSlots[k], in _pickupViews[_sortSlots[k]]);

            Array.Copy(_changedCells, t.ChangedCells, _changedCells.Length);
        }

        /// <summary>
        /// A foreign projectile must not tell who it hit unseen (§4.4 item 9, F2): only the hit bits of fighters this client is
        /// shown (and the owner's own bit) stay, and the pierces spent on the others are given back.
        /// </summary>
        private static void HideUnseenHits(ref NetProjectileSegment seg, uint shown)
        {
            uint keep = shown;
            if ((uint)seg.Owner < 32u) keep |= 1u << seg.Owner;
            uint hidden = seg.HitMask & ~keep;
            if (hidden == 0u) return;
            seg.HitMask = (ushort)(seg.HitMask & keep);
            int count = 0;
            for (; hidden != 0u; hidden &= hidden - 1u) count++;
            seg.PierceLeft += count;
        }

        private void Push(ref int n, int slot, float key)
        {
            _sortSlots[n] = slot;
            _sortKeys[n] = key;
            n++;
        }

        /// <summary>Stable insertion sort of the first <paramref name="n"/> scratch entries by key (n is small).</summary>
        private void SortByKey(int n)
        {
            for (int i = 1; i < n; i++)
            {
                int s = _sortSlots[i];
                float key = _sortKeys[i];
                int j = i - 1;
                while (j >= 0 && _sortKeys[j] > key)
                {
                    _sortSlots[j + 1] = _sortSlots[j];
                    _sortKeys[j + 1] = _sortKeys[j];
                    j--;
                }
                _sortSlots[j + 1] = s;
                _sortKeys[j + 1] = key;
            }
        }

        private void CaptureCells(MatchState state)
        {
            Array.Clear(_changedCells, 0, _changedCells.Length);
            TileType[] map = _defs.Map.Tiles;
            int cells = state.Globals.MapWidth * state.Globals.MapHeight;
            if (cells > map.Length) cells = map.Length;
            if (cells > SimLimits.MaxMapCells) cells = SimLimits.MaxMapCells;
            TileType[] tiles = state.Tiles;
            for (int i = 0; i < cells; i++)
                if (tiles[i] != map[i]) _changedCells[i >> 5] |= 1u << (i & 31);
        }

        private void CaptureMode(MatchState state)
        {
            ref ModeState m = ref state.Mode;
            _mode = new NetModeView
            {
                Phase = m.Phase,
                CountdownTeam = m.CountdownTeam,
                CountdownEndTick = m.CountdownEndTick,
                EndTick = m.EndTick,
                WinnerTeam = m.WinnerTeam,
                Team0Acorns = m.Team0Acorns,
                Team1Acorns = m.Team1Acorns,
                FogStep = m.FogStep,
                NextFogStepTick = m.NextFogStepTick,
                SafeMinX = Quantize.ToPosition(m.SafeMinX),
                SafeMinY = Quantize.ToPosition(m.SafeMinY),
                SafeMaxX = Quantize.ToPosition(m.SafeMaxX),
                SafeMaxY = Quantize.ToPosition(m.SafeMaxY),
                AliveCount = m.AliveCount
            };
        }

        private NetFighterView ToView(in FighterState f)
        {
            NetFighterFlags flags = NetFighterFlags.None;
            if (f.InBush) flags |= NetFighterFlags.InBush;
            if (f.Stunned) flags |= NetFighterFlags.Stunned;
            if (f.SpeedMul < 1f) flags |= NetFighterFlags.Slowed;
            if (f.IsDashing) flags |= NetFighterFlags.Dashing;
            if (f.Active && UltSystem.IsCharged(_defs, in f)) flags |= NetFighterFlags.UltReady;
            return new NetFighterView
            {
                Alive = f.Alive,
                PositionX = Quantize.ToPosition(f.Position.X),
                PositionY = Quantize.ToPosition(f.Position.Y),
                VelocityX = Quantize.ToVelocity(f.Velocity.X),
                VelocityY = Quantize.ToVelocity(f.Velocity.Y),
                Facing = (byte)Quantize.ToDirectionAngle(f.Facing, NetProtocol.FacingAngleBits),
                Health = f.Health,
                MaxHealth = f.MaxHealth,
                Charges = f.Charges,
                Flags = flags,
                Acorns = f.CarriedAcorns,
                Berries = f.Berries
            };
        }

        private static NetAreaView ToView(in AreaState a) => new NetAreaView
        {
            Kind = a.Kind,
            AbilityRef = a.AbilityRef,
            Owner = a.Owner,
            Team = a.Team,
            PositionX = Quantize.ToPosition(a.Position.X),
            PositionY = Quantize.ToPosition(a.Position.Y),
            OriginX = Quantize.ToPosition(a.Origin.X),
            OriginY = Quantize.ToPosition(a.Origin.Y),
            Radius = Quantize.ToRadius(a.Radius),
            SpawnTick = a.SpawnTick,
            ActivateTick = a.ActivateTick,
            ExpireTick = a.ExpireTick,
            Triggered = a.Triggered
        };

        private static NetPickupView ToView(in PickupState p) => new NetPickupView
        {
            Kind = p.Kind,
            Value = p.Value,
            PositionX = Quantize.ToPosition(p.Position.X),
            PositionY = Quantize.ToPosition(p.Position.Y),
            PickableFromTick = p.PickableFromTick,
            SpawnTick = p.SpawnTick
        };
    }
}
