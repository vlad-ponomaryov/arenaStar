using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Timed effects in fixed slots (<see cref="SimLimits.MaxStatusEffects"/>, D-004) and their aggregates in
    /// <see cref="FighterState"/>: <c>SpeedMul</c> / <c>DamageTakenMul</c> (the strongest slow / reduction applies),
    /// <c>Stunned</c>, <c>Invisible</c>, <c>MarkedByTeamMask</c>. Aggregates are recomputed from the entries (never
    /// accumulated incrementally), so they are always consistent with the snapshot.
    /// Timing: a status applied during tick T affects the rest of tick T and the ticks T+1 … T+duration in full
    /// (<c>ExpireTick = T + 1 + duration</c>); <see cref="Run"/> at the start of a tick drops expired entries.
    /// </summary>
    public static class StatusSystem
    {
        public static void Run(MatchState state)
        {
            int tick = state.Globals.Tick;
            StatusEffectState[] items = state.Statuses;
            for (int i = 0; i < items.Length; i++)
                if (items[i].Active && tick >= items[i].ExpireTick) items[i].Active = false;
            RecomputeAll(state);
        }

        /// <summary>
        /// Applies or refreshes a status (same target, kind and source → one entry; the later expiry wins). Returns false
        /// if the target is not alive, the duration is not positive, or all slots are busy (counted, no exception).
        /// </summary>
        public static bool Apply(MatchState state, SimEventBuffer events, int target, int source, int sourceTeam,
            EffectKind kind, int value, int durationTicks)
        {
            ref FighterState t = ref state.Fighters[target];
            if (!t.Active || !t.Alive || durationTicks <= 0) return false;

            int tick = state.Globals.Tick;
            int expire = tick + 1 + durationTicks;
            StatusEffectState[] items = state.Statuses;
            int slot = -1;
            for (int i = 0; i < items.Length; i++)
            {
                ref StatusEffectState s = ref items[i];
                if (!s.Active)
                {
                    if (slot < 0) slot = i;
                    continue;
                }
                if (s.Target == target && s.Kind == kind && s.Source == source)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                state.Globals.DroppedStatuses++;
                return false;
            }

            ref StatusEffectState e = ref items[slot];
            if (e.Active)
            {
                if (expire > e.ExpireTick) e.ExpireTick = expire;
            }
            else
            {
                e = default;
                e.Active = true;
                e.Target = target;
                e.Source = source;
                e.Kind = kind;
                e.ExpireTick = expire;
            }
            e.SourceTeam = sourceTeam;
            e.Value = value;

            Recompute(state, target);
            events.Add(new SimEvent(SimEventType.StatusApplied, tick, source, target, (int)kind, t.Position, Vec2.Zero));
            return true;
        }

        /// <summary>Removes every status of <paramref name="kind"/> on the target (invisibility ends when attacking, D-024).</summary>
        public static void RemoveKind(MatchState state, int target, EffectKind kind)
        {
            StatusEffectState[] items = state.Statuses;
            bool removed = false;
            for (int i = 0; i < items.Length; i++)
            {
                ref StatusEffectState s = ref items[i];
                if (!s.Active || s.Target != target || s.Kind != kind) continue;
                s.Active = false;
                removed = true;
            }
            if (removed) Recompute(state, target);
        }

        /// <summary>Removes all statuses on the target (defeat) and resets its aggregates.</summary>
        public static void ClearTarget(MatchState state, int target)
        {
            StatusEffectState[] items = state.Statuses;
            for (int i = 0; i < items.Length; i++)
                if (items[i].Active && items[i].Target == target) items[i].Active = false;
            ResetAggregates(ref state.Fighters[target]);
        }

        /// <summary>Number of active statuses of <paramref name="kind"/> on the target (tests, views).</summary>
        public static int Count(MatchState state, int target, EffectKind kind)
        {
            int n = 0;
            StatusEffectState[] items = state.Statuses;
            for (int i = 0; i < items.Length; i++)
                if (items[i].Active && items[i].Target == target && items[i].Kind == kind) n++;
            return n;
        }

        /// <summary>Neutral aggregates: no slow, no reduction, not stunned, visible, unmarked.</summary>
        public static void ResetAggregates(ref FighterState f)
        {
            f.SpeedMul = 1f;
            f.DamageTakenMul = 1f;
            f.Stunned = false;
            f.Invisible = false;
            f.MarkedByTeamMask = 0u;
        }

        /// <summary>Recomputes the aggregates of one fighter from the active entries.</summary>
        public static void Recompute(MatchState state, int target)
        {
            ref FighterState f = ref state.Fighters[target];
            ResetAggregates(ref f);
            StatusEffectState[] items = state.Statuses;
            for (int i = 0; i < items.Length; i++)
                if (items[i].Active && items[i].Target == target) Accumulate(ref f, in items[i]);
        }

        private static void RecomputeAll(MatchState state)
        {
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++) ResetAggregates(ref state.Fighters[i]);
            StatusEffectState[] items = state.Statuses;
            for (int i = 0; i < items.Length; i++)
            {
                if (!items[i].Active || items[i].Target >= count) continue;
                Accumulate(ref state.Fighters[items[i].Target], in items[i]);
            }
        }

        private static void Accumulate(ref FighterState f, in StatusEffectState s)
        {
            switch (s.Kind)
            {
                case EffectKind.Stun:
                    f.Stunned = true;
                    break;
                case EffectKind.Slow:
                    f.SpeedMul = MathUtil.Min(f.SpeedMul, 1f - MathUtil.FromPercent(s.Value));
                    break;
                case EffectKind.DamageReduction:
                    f.DamageTakenMul = MathUtil.Min(f.DamageTakenMul, 1f - MathUtil.FromPercent(s.Value));
                    break;
                case EffectKind.Invisibility:
                    f.Invisible = true;
                    break;
                case EffectKind.Mark:
                    if (s.SourceTeam >= 0 && s.SourceTeam < SimLimits.MaxTeams) f.MarkedByTeamMask |= 1u << s.SourceTeam;
                    break;
            }
        }
    }
}
