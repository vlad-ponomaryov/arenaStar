using System;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;

namespace Arena.Net.Client
{
    /// <summary>
    /// The display state of an online match, built once per prediction tick (Server-Spec §12.2, S3.7b) — what views, HUD and a
    /// client-side bot read instead of an offline <see cref="MatchSimulation"/> state:
    /// <list type="bullet">
    /// <item><c>Globals.Tick</c> = Tpred + 1 after predicting tick Tpred — like an offline state after its tick, so tick fields
    /// and events relate to it the same way (a snapshot frame of server tick T is the state before simulating T); the own fighter, its statuses, own projectiles and areas come from the
    /// shadow (the own fighter at predicted position + correction offset); HP and max HP of the own fighter — from the snapshot at
    /// the render tick (the bar changes with the projectile that arrives on screen);</item>
    /// <item>foreign fighters — interpolated between the snapshots around the render tick (§2.6), extrapolated by velocity for at
    /// most <c>MaxExtrapolationMs</c> and eased back over <c>CorrectionSmoothingSeconds</c>; a fighter missing from the snapshot
    /// keeps its last place with <c>VisibleToTeams</c> without our team (views hide it);</item>
    /// <item>foreign projectiles from their segments at the render tick, foreign areas, pickups, broken cells and the mode block from
    /// the frame at the render tick; their tick fields are shifted by <see cref="Shift"/> = (Tpred + 1) − round(renderTick), so views that
    /// compare them with <c>Globals.Tick</c> (telegraphs, lob arcs) show foreign things in their time and own ones at once. The
    /// shift an entity was first shown with is kept for its lifetime (<see cref="DisplaySlotTable"/>);</item>
    /// <item>projectiles and areas get display slots by key (<see cref="DisplaySlotTable"/>), stable across re-simulations;</item>
    /// <item>events — own <c>AbilityFired</c> from the front edge of the prediction (never repeated on re-simulation) and the server's
    /// view events by the <see cref="EventScheduler"/>; <c>TileBroken</c> is derived from the broken cells as the render tick reaches
    /// them;</item>
    /// <item><c>MatchEnded</c> writes the slot statistics and the result into <c>Stats</c> / <c>Mode</c> (<see cref="ApplyEnded"/>).</item>
    /// </list>
    /// Not known on the client and left empty: boxes (their cells are tiles), statuses of others, <c>TeamRevealUntilTick</c>
    /// (the reveal already shows in <c>VisibleToTeams</c>). No allocations after construction.
    /// </summary>
    public sealed class PresentationState
    {
        private const float TickSeconds = 1f / SimLimits.TickRate;

        private readonly MatchDefs _defs;
        private readonly MatchSetup _setup;
        private readonly int _ownSlot;
        private readonly int _ownTeam;
        private readonly ClientMatchSettings _settings;
        private readonly DisplaySlotTable _projectileSlots = new DisplaySlotTable(SimLimits.MaxProjectiles);
        private readonly DisplaySlotTable _areaSlots = new DisplaySlotTable(SimLimits.MaxAreas);
        private readonly bool[] _pickupKnown = new bool[SimLimits.MaxPickups];
        private readonly int[] _pickupSpawn = new int[SimLimits.MaxPickups];
        private readonly int[] _pickupShift = new int[SimLimits.MaxPickups];
        private readonly Vec2[] _offset = new Vec2[SimLimits.MaxFighters];
        private readonly Vec2[] _raw = new Vec2[SimLimits.MaxFighters];
        private readonly bool[] _rawValid = new bool[SimLimits.MaxFighters];
        private readonly bool[] _extrapolated = new bool[SimLimits.MaxFighters];
        private readonly Vec2[] _shown = new Vec2[SimLimits.MaxFighters];
        private readonly bool[] _shownVisible = new bool[SimLimits.MaxFighters];
        private bool _ownWasPredicted;
        private long _ownExternalSnaps;
        private int _lastOwnEventTick = int.MinValue;
        private MatchEndedMessage _ended;

        public PresentationState(MatchDefs defs, MatchSetup setup, int ownSlot, ClientMatchSettings settings)
        {
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if ((uint)ownSlot >= (uint)setup.SlotCount) throw new ArgumentOutOfRangeException(nameof(ownSlot));
            _ownSlot = ownSlot;
            _ownTeam = setup.Slots[ownSlot].Team;
            TileSystem.Load(State, defs.Map);
            State.Globals.FighterCount = setup.SlotCount;
            for (int i = 0; i < setup.SlotCount; i++)
            {
                ref FighterState f = ref State.Fighters[i];
                ShadowStateBuilder.ResetOther(ref f, setup.Slots[i]);
                f.Active = true;
                f.Alive = true;
                f.Position = setup.Slots[i].SpawnPosition;
                f.VisibleToTeams = TeamBit(setup.Slots[i].Team);
            }
        }

        public MatchState State { get; } = new MatchState();

        /// <summary>Events of the last built display tick.</summary>
        public SimEventBuffer Events { get; } = new SimEventBuffer();

        /// <summary>At least one display tick was built.</summary>
        public bool HasState { get; private set; }

        /// <summary>(Tpred + 1) − round(renderTick) of the last build: how far the own fighter is ahead of the shown world, ticks.</summary>
        public int Shift { get; private set; }

        /// <summary>Rounded render tick of the last build (server time of the shown world).</summary>
        public int RenderTick { get; private set; }

        public bool Ended => _ended != null;

        public long ProjectileSlotOverflows => _projectileSlots.Overflows;
        public long AreaSlotOverflows => _areaSlots.Overflows;

        /// <summary>
        /// Builds the display state of prediction tick <paramref name="predictionTick"/> from the shadow, the sampled
        /// <paramref name="interp"/> (call <see cref="InterpolationBuffer.Sample"/> first) and the due events.
        /// <paramref name="ownEvents"/> — events of the prediction tick that just ran, or null. False without data yet.
        /// </summary>
        public bool Build(int predictionTick, Predictor predictor, InterpolationBuffer interp, EventScheduler scheduler,
            SimEventBuffer ownEvents, ClientNetStats stats)
        {
            SnapshotFrame from = interp.From;
            if (from == null || predictor == null || !predictor.Ready) return false;
            Events.Clear();
            RenderTick = interp.RenderTickRounded;
            int displayTick = predictionTick + 1;
            Shift = displayTick - RenderTick;
            State.Globals.Tick = displayTick;
            State.Globals.FighterCount = _setup.SlotCount;
            bool predicting = !predictor.BotControlled;

            BuildFighters(predictor, interp, predicting, stats);
            BuildStatuses(predictor, from, predicting);
            BuildProjectiles(predictor, interp, predicting);
            BuildAreas(predictor, from, predicting);
            BuildPickups(from);
            BuildTiles(from, predictionTick);
            BuildMode(from);

            scheduler.Release(RenderTick, Shift, predictionTick, State, _ownTeam, Events);
            if (ownEvents != null && predicting)
            {
                int newest = _lastOwnEventTick;
                for (int i = 0; i < ownEvents.Count; i++)
                {
                    ref readonly SimEvent e = ref ownEvents[i];
                    if (e.Type != SimEventType.AbilityFired || e.Source != _ownSlot || e.Tick <= _lastOwnEventTick) continue;
                    Events.Add(in e);
                    if (e.Tick > newest) newest = e.Tick;
                }
                _lastOwnEventTick = newest;
            }
            stats?.AddPresentedTick(interp.Extrapolating, interp.DelayTicks);
            HasState = true;
            return true;
        }

        /// <summary>The match result (<c>MatchEnded</c>): statistics of the slots and the mode result, kept for later builds.</summary>
        public void ApplyEnded(MatchEndedMessage ended)
        {
            _ended = ended ?? throw new ArgumentNullException(nameof(ended));
            WriteEnded();
        }

        private void WriteEnded()
        {
            MatchEndedMessage m = _ended;
            ref ModeState mode = ref State.Mode;
            mode.Phase = ModePhase.Ended;
            mode.WinnerTeam = m.WinnerTeam;
            mode.EndTick = m.EndTick + Shift;
            int n = m.SlotCount < State.Stats.Length ? m.SlotCount : State.Stats.Length;
            for (int i = 0; i < n; i++)
            {
                MatchSlotResult r = m.Slots[i];
                ref FighterStats s = ref State.Stats[i];
                s.DamageDealt = r.DamageDealt;
                s.HealingDone = r.HealingDone;
                s.Kills = r.Kills;
                s.Deaths = r.Deaths;
                s.AcornsCollected = r.AcornsCollected;
                s.Place = r.Place;
            }
        }

        private void BuildFighters(Predictor predictor, InterpolationBuffer interp, bool predicting, ClientNetStats stats)
        {
            SnapshotFrame from = interp.From, to = interp.To;
            float alpha = interp.Alpha;
            float snap = _settings.SnapDistanceCells;
            float tau = _settings.CorrectionSmoothingSeconds;
            float decay = tau > 0f ? MathF.Exp(-TickSeconds / tau) : 0f;
            int count = _setup.SlotCount;
            for (int i = 0; i < count; i++)
            {
                ref FighterState d = ref State.Fighters[i];
                PlayerSlotSetup slot = _setup.Slots[i];
                bool continuous = true;
                if (i == _ownSlot)
                {
                    if (predicting)
                    {
                        // A push caught up by the smoothed offset, or placed at once, is not a teleport (§11.1).
                        if (predictor.ExternalCorrection || predictor.ExternalSnaps != _ownExternalSnaps) continuous = false;
                        _ownExternalSnaps = predictor.ExternalSnaps;
                        d = predictor.Own;
                        d.Position = predictor.DisplayPosition;
                        d.Health = from.Own.Health;
                        d.MaxHealth = from.Own.MaxHealth;
                    }
                    else
                    {
                        d = from.Own;
                        if (to != null && to.Own.Alive == from.Own.Alive && Vec2.Distance(from.Own.Position, to.Own.Position) <= snap)
                            d.Position = Vec2.Lerp(from.Own.Position, to.Own.Position, alpha);
                        else if (to == null && interp.Extrapolating && d.Alive)
                            d.Position += d.Velocity * (interp.ExtrapolationTicks * TickSeconds);
                        continuous = RawStep(i, d.Position, snap);
                    }
                    if (predicting != _ownWasPredicted) continuous = false; // the source of the own fighter switched
                    _ownWasPredicted = predicting;
                }
                else
                {
                    int ia = from.Fighters.IndexOf(i);
                    if (ia < 0)
                    {
                        // Hidden from our team: last known place, not visible to us (views hide it).
                        d.VisibleToTeams = TeamBit(slot.Team);
                        _offset[i] = Vec2.Zero;
                        _extrapolated[i] = false;
                        _rawValid[i] = false;
                    }
                    else
                    {
                        ref NetFighterView a = ref from.Fighters.ItemAt(ia);
                        ShadowStateBuilder.ResetOther(ref d, slot);
                        ShadowStateBuilder.FillOther(ref d, in a, slot.Team, _ownTeam);
                        if ((a.Flags & NetFighterFlags.Dashing) != 0) d.DashTicksLeft = 1;
                        Vec2 raw = d.Position;
                        bool extrapolated = false;
                        int ib = to != null ? to.Fighters.IndexOf(i) : -1;
                        if (ib >= 0)
                        {
                            ref NetFighterView b = ref to.Fighters.ItemAt(ib);
                            var pb = new Vec2(Quantize.FromPosition(b.PositionX), Quantize.FromPosition(b.PositionY));
                            if (b.Alive == a.Alive && Vec2.Distance(raw, pb) <= snap) raw = Vec2.Lerp(raw, pb, alpha);
                            if (alpha >= 0.5f) d.Facing = MathUtil.AngleToDir(Quantize.FromAngle(b.Facing, NetProtocol.FacingAngleBits));
                        }
                        else if (to == null && interp.Extrapolating && a.Alive)
                        {
                            raw += d.Velocity * (interp.ExtrapolationTicks * TickSeconds);
                            extrapolated = true;
                        }
                        continuous = RawStep(i, raw, snap);
                        if (_extrapolated[i] && !extrapolated && _shownVisible[i] && continuous)
                        {
                            // Data came after a run of extrapolation: ease back from where the fighter is shown.
                            Vec2 back = _shown[i] - raw;
                            _offset[i] = back.Length > snap ? Vec2.Zero : back;
                        }
                        else if (!continuous) _offset[i] = Vec2.Zero;
                        else _offset[i] *= decay;
                        _extrapolated[i] = extrapolated;
                        d.Position = raw + _offset[i];
                    }
                }

                bool visible = d.Active && d.Alive && (i == _ownSlot || d.IsVisibleTo(_ownTeam));
                if (stats != null && visible && _shownVisible[i] && continuous && !d.IsDashing && d.KnockbackTicks <= 0
                    && (uint)d.DefIndex < (uint)_defs.Fighters.Length)
                {
                    float speed = _defs.Fighters[d.DefIndex].MoveSpeed * (d.SpeedMul > 1f ? d.SpeedMul : 1f);
                    stats.AddMotion(Vec2.Distance(d.Position, _shown[i]), ClientNetStats.MotionLimit(speed), i, State.Globals.Tick);
                }
                _shown[i] = d.Position;
                _shownVisible[i] = visible;
            }
        }

        /// <summary>
        /// Remembers the un-smoothed place of a fighter; false when it jumped more than <paramref name="snap"/> since the last
        /// display tick (a respawn or appearance between snapshots — data, not a smoothing error).
        /// </summary>
        private bool RawStep(int i, Vec2 raw, float snap)
        {
            bool continuous = _rawValid[i] && Vec2.Distance(raw, _raw[i]) <= snap;
            _raw[i] = raw;
            _rawValid[i] = true;
            return continuous;
        }

        private void BuildStatuses(Predictor predictor, SnapshotFrame from, bool predicting)
        {
            StatusEffectState[] statuses = State.Statuses;
            Array.Clear(statuses, 0, statuses.Length);
            if (predicting)
            {
                // Only own statuses live in the shadow.
                Array.Copy(predictor.Shadow.State.Statuses, statuses, statuses.Length);
                return;
            }
            int n = from.OwnStatusCount < statuses.Length ? from.OwnStatusCount : statuses.Length;
            for (int i = 0; i < n; i++) statuses[i] = from.OwnStatuses[i];
        }

        private void BuildProjectiles(Predictor predictor, InterpolationBuffer interp, bool predicting)
        {
            ProjectileState[] items = State.Projectiles;
            Array.Clear(items, 0, items.Length);
            _projectileSlots.Begin();
            if (predicting)
            {
                ProjectileState[] own = predictor.Shadow.State.Projectiles;
                for (int k = 0; k < own.Length; k++)
                {
                    ref ProjectileState p = ref own[k];
                    if (!p.Active || p.Owner != _ownSlot) continue;
                    int slot = _projectileSlots.Acquire(DisplaySlotTable.MakeKey(p.Owner, p.SpawnTick, p.AbilityRef), k, 0, out _);
                    if (slot >= 0) items[slot] = p;
                }
            }
            SnapshotFrame from = interp.From, to = interp.To;
            SnapshotFrame source = to ?? from;
            double rt = interp.RenderTick;
            SnapshotEntityList<NetProjectileSegment> list = source.Projectiles;
            for (int k = 0; k < list.Count; k++)
            {
                int serverSlot = list.SlotAt(k);
                AddSegment(serverSlot, in list.ItemAt(k), from, source, rt, predicting);
            }
            if (to != null)
            {
                // Segments already gone from the newer frame but still flying at the render tick.
                SnapshotEntityList<NetProjectileSegment> older = from.Projectiles;
                for (int k = 0; k < older.Count; k++)
                {
                    int serverSlot = older.SlotAt(k);
                    if (list.Contains(serverSlot)) continue;
                    AddSegment(serverSlot, in older.ItemAt(k), from, from, rt, predicting);
                }
            }
            _projectileSlots.End();
        }

        private void AddSegment(int serverSlot, in NetProjectileSegment segment, SnapshotFrame from, SnapshotFrame source,
            double rt, bool predicting)
        {
            NetProjectileSegment s = segment;
            if (predicting && s.Owner == _ownSlot) return;
            if (s.SpawnTick > rt) return;
            if (s.StartTick > rt && source != from)
            {
                // A bounce after the render tick: the part shown now is the older segment of the same projectile.
                int j = from.Projectiles.IndexOf(serverSlot);
                if (j >= 0)
                {
                    ref NetProjectileSegment old = ref from.Projectiles.ItemAt(j);
                    if (old.SpawnTick == s.SpawnTick && old.StartTick <= rt) s = old;
                }
            }
            if (s.Ended && rt >= s.EndTick) return;
            AbilityDef ability = _defs.GetAbility(s.AbilityRef);
            if (ability == null || (uint)s.Owner >= (uint)_setup.SlotCount) return;
            int slot = _projectileSlots.Acquire(DisplaySlotTable.MakeKey(s.Owner, s.SpawnTick, s.AbilityRef), serverSlot, Shift,
                out int birthShift);
            if (slot < 0) return;
            float speed = ability.ProjectileSpeed;
            Vec2 dir = MathUtil.AngleToDir(Quantize.FromAngle(s.Direction, NetProtocol.ProjectileAngleBits));
            var origin = new Vec2(Quantize.FromPosition(s.OriginX), Quantize.FromPosition(s.OriginY));
            float travel = speed * (float)(rt - s.StartTick) / SimLimits.TickRate;
            if (s.Ended)
            {
                float toEnd = Vec2.Distance(origin, new Vec2(Quantize.FromPosition(s.EndX), Quantize.FromPosition(s.EndY)));
                if (travel > toEnd) travel = toEnd;
            }
            float flown = s.AfterBounce ? travel : speed * (float)(rt - s.SpawnTick) / SimLimits.TickRate;
            float remaining = (s.AfterBounce ? ability.BounceRange : ability.Range) - flown;

            ref ProjectileState p = ref State.Projectiles[slot];
            p.Active = true;
            p.Owner = s.Owner;
            p.Team = _setup.Slots[s.Owner].Team;
            p.SpawnTick = s.SpawnTick + birthShift;
            p.Position = origin + dir * travel;
            p.Dir = dir;
            p.Speed = speed;
            p.Radius = ability.ProjectileRadius;
            p.RemainingDistance = remaining > 0f ? remaining : 0f;
            p.AbilityRef = s.AbilityRef;
            p.PierceLeft = s.PierceLeft;
            p.BounceLeft = s.BounceLeft;
            p.HitMask = s.HitMask;
            p.DamagePercent = EffectContext.FullDamagePercent;
        }

        private void BuildAreas(Predictor predictor, SnapshotFrame from, bool predicting)
        {
            AreaState[] items = State.Areas;
            Array.Clear(items, 0, items.Length);
            _areaSlots.Begin();
            if (predicting)
            {
                AreaState[] own = predictor.Shadow.State.Areas;
                for (int k = 0; k < own.Length; k++)
                {
                    ref AreaState a = ref own[k];
                    if (!a.Active || a.Owner != _ownSlot) continue;
                    int slot = _areaSlots.Acquire(DisplaySlotTable.MakeKey(a.Owner, a.SpawnTick, a.AbilityRef), k, 0, out _);
                    if (slot >= 0) items[slot] = a;
                }
            }
            SnapshotEntityList<NetAreaView> list = from.Areas;
            for (int k = 0; k < list.Count; k++)
            {
                ref NetAreaView v = ref list.ItemAt(k);
                if (predicting && v.Owner == _ownSlot) continue;
                int slot = _areaSlots.Acquire(DisplaySlotTable.MakeKey(v.Owner, v.SpawnTick, v.AbilityRef), list.SlotAt(k), Shift,
                    out int birthShift);
                if (slot >= 0) ShadowStateBuilder.FillArea(ref items[slot], in v, birthShift);
            }
            _areaSlots.End();
        }

        private void BuildPickups(SnapshotFrame from)
        {
            PickupState[] items = State.Pickups;
            Array.Clear(items, 0, items.Length);
            SnapshotEntityList<NetPickupView> list = from.Pickups;
            for (int i = 0; i < _pickupKnown.Length; i++)
                if (_pickupKnown[i] && !list.Contains(i)) _pickupKnown[i] = false;
            for (int k = 0; k < list.Count; k++)
            {
                int slot = list.SlotAt(k);
                if ((uint)slot >= (uint)items.Length) continue;
                ref NetPickupView v = ref list.ItemAt(k);
                if (!_pickupKnown[slot] || _pickupSpawn[slot] != v.SpawnTick)
                {
                    _pickupKnown[slot] = true;
                    _pickupSpawn[slot] = v.SpawnTick;
                    _pickupShift[slot] = Shift;
                }
                int shift = _pickupShift[slot];
                ref PickupState p = ref items[slot];
                p.Active = true;
                p.Kind = v.Kind;
                p.Value = v.Value;
                p.Position = new Vec2(Quantize.FromPosition(v.PositionX), Quantize.FromPosition(v.PositionY));
                p.PickableFromTick = v.PickableFromTick + shift;
                p.SpawnTick = v.SpawnTick + shift;
            }
        }

        private void BuildTiles(SnapshotFrame from, int tick)
        {
            int width = State.Globals.MapWidth;
            int cells = width * State.Globals.MapHeight;
            uint[] words = from.ChangedCells;
            int wordCount = (cells + 31) >> 5;
            if (wordCount > words.Length) wordCount = words.Length;
            for (int w = 0; w < wordCount; w++)
            {
                uint bits = words[w];
                if (bits == 0u) continue;
                for (int b = 0; b < 32; b++)
                {
                    if ((bits & (1u << b)) == 0u) continue;
                    int c = (w << 5) + b;
                    if (c >= cells || State.Tiles[c] == TileType.Floor) continue;
                    State.Tiles[c] = TileType.Floor;
                    var cell = new GridCell(c % width, c / width);
                    Events.Add(new SimEvent(SimEventType.TileBroken, tick, -1, -1, c, cell.Center, Vec2.Zero));
                }
            }
        }

        private void BuildMode(SnapshotFrame from)
        {
            ModeState mode = ShadowStateBuilder.ToMode(in from.Mode);
            if (mode.CountdownEndTick != 0) mode.CountdownEndTick += Shift;
            if (mode.EndTick != 0) mode.EndTick += Shift;
            if (mode.NextFogStepTick != 0) mode.NextFogStepTick += Shift;
            State.Mode = mode;
            if (_ended != null) WriteEnded();
        }

        private static uint TeamBit(int team) => (uint)team < 32u ? 1u << team : 0u;
    }
}
