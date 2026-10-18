using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;

namespace Arena.Net.Client
{
    /// <summary>
    /// Builds the start state of the client's shadow simulation from a decoded snapshot view (Server-Spec §2.5 "Состав тени"):
    /// the own fighter and its statuses exactly; own projectiles from their segments and own areas (plus enemy fields that block
    /// projectiles — domes) from the area list; broken cells and the mode block; other fighters as shown — S3.7a: at their
    /// positions in this snapshot (interpolated positions come in S3.7b, D-096); fighters missing from the snapshot (hidden) are
    /// inactive; no foreign projectiles, pickups or boxes. Finally <c>Predicting</c> / <c>PredictedSlot</c> are set (a
    /// <c>CopyFrom</c> or <c>Clear</c> resets them). No allocations.
    /// </summary>
    public static class ShadowStateBuilder
    {
        public static void Build(MatchState state, SnapshotFrame frame, MatchDefs defs, MatchSetup setup, int ownSlot, int baseTick)
        {
            state.Clear();
            ref MatchGlobals g = ref state.Globals;
            TileSystem.Load(state, defs.Map);
            g.Tick = baseTick;
            g.FighterCount = setup.SlotCount;
            g.Seed = setup.Seed;
            int cells = g.MapWidth * g.MapHeight;
            for (int c = 0; c < cells; c++)
                if (frame.IsCellChanged(c)) state.Tiles[c] = TileType.Floor;

            state.Mode = ToMode(in frame.Mode);

            int ownTeam = setup.Slots[ownSlot].Team;
            for (int i = 0; i < setup.SlotCount; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (i == ownSlot)
                {
                    f = frame.Own;
                    continue;
                }
                PlayerSlotSetup slot = setup.Slots[i];
                f = default;
                f.DefIndex = slot.FighterIndex;
                f.Team = slot.Team;
                f.Level = slot.Level;
                f.SpeedMul = 1f;
                f.DamageTakenMul = 1f;
                f.DashAbilityRef = -1;
                int index = frame.Fighters.IndexOf(i);
                if (index < 0) continue; // hidden from our team: not a target in the shadow
                ref NetFighterView v = ref frame.Fighters.ItemAt(index);
                f.Active = true;
                f.Alive = v.Alive;
                f.Position = new Vec2(Quantize.FromPosition(v.PositionX), Quantize.FromPosition(v.PositionY));
                f.Velocity = new Vec2(Quantize.FromVelocity(v.VelocityX), Quantize.FromVelocity(v.VelocityY));
                f.Facing = MathUtil.AngleToDir(Quantize.FromAngle(v.Facing, NetProtocol.FacingAngleBits));
                f.Health = v.Health;
                f.MaxHealth = v.MaxHealth;
                f.Charges = v.Charges;
                f.CarriedAcorns = v.Acorns;
                f.Berries = v.Berries;
                f.InBush = (v.Flags & NetFighterFlags.InBush) != 0;
                f.Invisible = (v.Flags & NetFighterFlags.Invisible) != 0;
                f.Stunned = (v.Flags & NetFighterFlags.Stunned) != 0;
                f.VisibleToTeams = TeamBit(ownTeam) | TeamBit(slot.Team);
                if ((v.Flags & NetFighterFlags.MarkedByViewer) != 0) f.MarkedByTeamMask = TeamBit(ownTeam);
            }

            int n = frame.OwnStatusCount;
            if (n > state.Statuses.Length) n = state.Statuses.Length;
            for (int i = 0; i < n; i++) state.Statuses[i] = frame.OwnStatuses[i];

            AddProjectiles(state, frame, defs, ownSlot, baseTick);
            AddAreas(state, frame, defs, ownSlot);

            g.Predicting = true;
            g.PredictedSlot = ownSlot;
        }

        private static void AddProjectiles(MatchState state, SnapshotFrame frame, MatchDefs defs, int ownSlot, int tick)
        {
            SnapshotEntityList<NetProjectileSegment> list = frame.Projectiles;
            int ownTeam = frame.Own.Team;
            for (int k = 0; k < list.Count; k++)
            {
                ref NetProjectileSegment s = ref list.ItemAt(k);
                if (s.Owner != ownSlot || s.Ended) continue;
                AbilityDef ability = defs.GetAbility(s.AbilityRef);
                if (ability == null) continue;
                int slot = list.SlotAt(k);
                if ((uint)slot >= (uint)state.Projectiles.Length) continue;
                float speed = ability.ProjectileSpeed;
                Vec2 dir = MathUtil.AngleToDir(Quantize.FromAngle(s.Direction, NetProtocol.ProjectileAngleBits));
                var origin = new Vec2(Quantize.FromPosition(s.OriginX), Quantize.FromPosition(s.OriginY));
                float sinceStart = speed * (tick - s.StartTick) / SimLimits.TickRate;
                // A first segment starts one step after the spawn (the spawn tick already moved it); a bounce restarts the range.
                float flown = s.AfterBounce ? sinceStart : speed * (tick - s.SpawnTick) / SimLimits.TickRate;
                float range = s.AfterBounce ? ability.BounceRange : ability.Range;
                float remaining = range - flown;
                if (remaining <= MathUtil.DistanceEpsilon) continue;

                ref ProjectileState p = ref state.Projectiles[slot];
                p = default;
                p.Active = true;
                p.Owner = s.Owner;
                p.Team = ownTeam;
                p.SpawnTick = s.SpawnTick;
                p.Position = origin + dir * sinceStart;
                p.Dir = dir;
                p.Speed = speed;
                p.Radius = ability.ProjectileRadius;
                p.RemainingDistance = remaining;
                p.AbilityRef = s.AbilityRef;
                p.PierceLeft = s.PierceLeft;
                p.BounceLeft = s.BounceLeft;
                p.HitMask = s.HitMask;
                p.DamagePercent = EffectContext.FullDamagePercent;
            }
        }

        private static void AddAreas(MatchState state, SnapshotFrame frame, MatchDefs defs, int ownSlot)
        {
            SnapshotEntityList<NetAreaView> list = frame.Areas;
            for (int k = 0; k < list.Count; k++)
            {
                ref NetAreaView v = ref list.ItemAt(k);
                AbilityDef ability = defs.GetAbility(v.AbilityRef);
                bool own = v.Owner == ownSlot;
                bool blocks = ability != null && ability.BlocksEnemyProjectiles && v.Kind == AreaKind.Field;
                if (!own && !blocks) continue;
                int slot = list.SlotAt(k);
                if ((uint)slot >= (uint)state.Areas.Length) continue;
                ref AreaState a = ref state.Areas[slot];
                a = default;
                a.Active = true;
                a.Triggered = v.Triggered;
                a.Kind = v.Kind;
                a.Owner = v.Owner;
                a.Team = v.Team;
                a.Position = new Vec2(Quantize.FromPosition(v.PositionX), Quantize.FromPosition(v.PositionY));
                a.Origin = new Vec2(Quantize.FromPosition(v.OriginX), Quantize.FromPosition(v.OriginY));
                a.SpawnTick = v.SpawnTick;
                a.Radius = Quantize.FromRadius(v.Radius);
                a.ActivateTick = v.ActivateTick;
                a.ExpireTick = v.ExpireTick;
                a.AbilityRef = v.AbilityRef;
                a.VisibleToTeams = ~0u;
                a.DamagePercent = EffectContext.FullDamagePercent;
                if (ability != null && a.Kind == AreaKind.Field && ability.AreaPulseTicks > 0)
                {
                    // Pulses only carry own non-damage effects in the shadow; their timing is rebuilt from the activation tick.
                    int done = frame.Tick > a.ActivateTick ? (frame.Tick - a.ActivateTick) / ability.AreaPulseTicks : 0;
                    a.PulseIndex = done;
                    a.NextPulseTick = a.ActivateTick + (done + 1) * ability.AreaPulseTicks;
                }
            }
        }

        private static ModeState ToMode(in NetModeView v) => new ModeState
        {
            Phase = v.Phase,
            CountdownTeam = v.CountdownTeam,
            CountdownEndTick = v.CountdownEndTick,
            EndTick = v.EndTick,
            WinnerTeam = v.WinnerTeam,
            Team0Acorns = v.Team0Acorns,
            Team1Acorns = v.Team1Acorns,
            FogStep = v.FogStep,
            NextFogStepTick = v.NextFogStepTick,
            SafeMinX = Quantize.FromPosition(v.SafeMinX),
            SafeMinY = Quantize.FromPosition(v.SafeMinY),
            SafeMaxX = Quantize.FromPosition(v.SafeMaxX),
            SafeMaxY = Quantize.FromPosition(v.SafeMaxY),
            AliveCount = v.AliveCount
        };

        private static uint TeamBit(int team) => (uint)team < 32u ? 1u << team : 0u;
    }
}
