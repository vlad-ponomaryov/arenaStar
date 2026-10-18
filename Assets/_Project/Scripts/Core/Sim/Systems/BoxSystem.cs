using System;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Berry boxes of the Last Beast mode (Server-Spec §9.2): <see cref="Load"/> creates a <see cref="BoxState"/> on every
    /// <see cref="TileType.BerryBox"/> tile at reset; deliveries damage them through one entry per shape — a projectile stopped
    /// by the box cell (<see cref="DamageCell"/>), a cone (<see cref="DamageCone"/>), a circle or a burst
    /// (<see cref="DamageCircle"/>). The amount is what the ability's enemy damage effects would deal (level, berries, shot
    /// boost and pulse share; PROTO, D-089). At 0 the tile becomes floor (<see cref="SimEventType.TileBroken"/>, as a broken
    /// wall) and <see cref="LastBeastModeDef.BerriesPerBox"/> berries drop. In other modes there are no boxes (a berry box
    /// tile stays an unbreakable wall) and every call does nothing. No allocations.
    /// </summary>
    public static class BoxSystem
    {
        /// <summary>Creates the boxes of the map (Last Beast only); the map was validated against <see cref="SimLimits.MaxBoxes"/>.</summary>
        public static void Load(MatchState state, MatchDefs defs)
        {
            Array.Clear(state.Boxes, 0, state.Boxes.Length);
            if (defs.Mode != ModeKind.LastBeast) return;
            TileType[] tiles = state.Tiles;
            int cells = state.Globals.MapWidth * state.Globals.MapHeight;
            int n = 0;
            for (int i = 0; i < cells && n < state.Boxes.Length; i++)
            {
                if (tiles[i] != TileType.BerryBox) continue;
                state.Boxes[n++] = new BoxState { Active = true, Cell = i, Health = defs.LastBeast.BoxHealth };
            }
        }

        /// <summary>Number of boxes still standing.</summary>
        public static int ActiveCount(MatchState state)
        {
            int n = 0;
            for (int i = 0; i < state.Boxes.Length; i++)
                if (state.Boxes[i].Active) n++;
            return n;
        }

        /// <summary>Slot of the standing box on cell (column, row), or −1.</summary>
        public static int Find(MatchState state, int column, int row)
        {
            TileGrid grid = state.Grid;
            if (!grid.InBounds(column, row)) return -1;
            int cell = grid.IndexOf(column, row);
            for (int i = 0; i < state.Boxes.Length; i++)
                if (state.Boxes[i].Active && state.Boxes[i].Cell == cell) return i;
            return -1;
        }

        /// <summary>
        /// Damage the ability's enemy damage effects deal in <paramref name="ctx"/> (base value × level × source berries ×
        /// shot percent, this pulse's share). 0 if it deals none.
        /// </summary>
        public static int AbilityDamage(MatchState state, MatchDefs defs, in EffectContext ctx)
        {
            if (ctx.Ability == null) return 0;
            EffectDef[] effects = ctx.Ability.Effects;
            if (effects == null) return 0;
            int level = ctx.Source >= 0 ? state.Fighters[ctx.Source].Level : 1;
            float berries = LastBeastRules.DamageMultiplier(state, defs, ctx.Source);
            int total = 0;
            for (int i = 0; i < effects.Length; i++)
            {
                EffectDef e = effects[i];
                if (e == null || e.Kind != EffectKind.Damage || (e.Filter & TargetFilter.Enemy) == 0) continue;
                int amount = defs.Rules.ScaleByLevel(e.Value, level);
                if (berries != 1f) amount = MathUtil.RoundToInt(amount * berries);
                total += ctx.Portion(ctx.ScaleDamage(amount));
            }
            return total;
        }

        /// <summary>The ability hit the cell (a projectile stopped by it): damages the box there, if any.</summary>
        public static void DamageCell(MatchState state, MatchDefs defs, SimEventBuffer events, in EffectContext ctx, int column, int row)
        {
            if (defs.Mode != ModeKind.LastBeast) return;
            int box = Find(state, column, row);
            if (box < 0) return;
            Damage(state, defs, events, box, ctx.Source, AbilityDamage(state, defs, in ctx));
        }

        /// <summary>Every box whose cell the circle overlaps (self areas, bursts, mines, field pulses).</summary>
        public static void DamageCircle(MatchState state, MatchDefs defs, SimEventBuffer events, in EffectContext ctx, Vec2 centre, float radius)
        {
            if (defs.Mode != ModeKind.LastBeast) return;
            int amount = -1;
            int width = state.Globals.MapWidth;
            for (int i = 0; i < state.Boxes.Length; i++)
            {
                ref BoxState b = ref state.Boxes[i];
                if (!b.Active || !GridCollision.CircleOverlapsCell(centre, radius, b.Cell % width, b.Cell / width)) continue;
                if (amount < 0) amount = AbilityDamage(state, defs, in ctx);
                if (amount <= 0) return;
                Damage(state, defs, events, i, ctx.Source, amount);
            }
        }

        /// <summary>
        /// Every box whose cell is within <paramref name="range"/> of <paramref name="origin"/>, within the cone around the unit
        /// <paramref name="dir"/> (by its centre) and not behind another projectile-blocking tile.
        /// </summary>
        public static void DamageCone(MatchState state, MatchDefs defs, SimEventBuffer events, in EffectContext ctx, Vec2 origin, Vec2 dir,
            float range, float cosHalfAngle, bool ignoresWalls)
        {
            if (defs.Mode != ModeKind.LastBeast) return;
            int amount = -1;
            int width = state.Globals.MapWidth;
            TileGrid grid = state.Grid;
            for (int i = 0; i < state.Boxes.Length; i++)
            {
                ref BoxState b = ref state.Boxes[i];
                if (!b.Active) continue;
                int column = b.Cell % width, row = b.Cell / width;
                if (!GridCollision.CircleOverlapsCell(origin, range, column, row)) continue;
                Vec2 to = new GridCell(column, row).Center - origin;
                if (!to.IsZero && Vec2.Dot(to.Normalized, dir) < cosHalfAngle) continue;
                if (!ignoresWalls && !ReachesCell(in grid, origin, column, row)) continue;
                if (amount < 0) amount = AbilityDamage(state, defs, in ctx);
                if (amount <= 0) return;
                Damage(state, defs, events, i, ctx.Source, amount);
            }
        }

        /// <summary>Removes durability from box <paramref name="box"/>; at 0 the tile becomes floor and berries drop.</summary>
        public static void Damage(MatchState state, MatchDefs defs, SimEventBuffer events, int box, int source, int amount)
        {
            ref BoxState b = ref state.Boxes[box];
            if (!b.Active || amount <= 0) return;
            int dealt = amount < b.Health ? amount : b.Health;
            b.Health -= dealt;
            TileGrid grid = state.Grid;
            GridCell cell = grid.CellAt(b.Cell);
            int tick = state.Globals.Tick;
            events.Add(new SimEvent(SimEventType.BoxDamaged, tick, source, box, dealt, cell.Center, Vec2.Zero));
            if (b.Health > 0) return;

            b.Active = false;
            grid.Set(cell.Column, cell.Row, TileType.Floor);
            events.Add(new SimEvent(SimEventType.TileBroken, tick, source, -1, b.Cell, cell.Center, Vec2.Zero));
            LastBeastModeDef lb = defs.LastBeast;
            int now = tick + 1;
            for (int k = 0; k < lb.BerriesPerBox; k++)
                PickupSystem.Spawn(state, events, PickupKind.Berry, 1, cell.Center, k == 0 ? 0f : lb.BerryDropScatterRadius,
                    lb.ScatterAttempts, now, now);
        }

        /// <summary>No projectile-blocking tile between <paramref name="origin"/> and the nearest point of the cell (the cell itself excluded).</summary>
        private static bool ReachesCell(in TileGrid grid, Vec2 origin, int column, int row)
        {
            var nearest = new Vec2(MathUtil.Clamp(origin.X, column, column + 1f), MathUtil.Clamp(origin.Y, row, row + 1f));
            Vec2 d = nearest - origin;
            float length = d.Length - MathUtil.DistanceEpsilon;
            if (length <= 0f) return true;
            return !GridCollision.Raycast(in grid, origin, d.Normalized, length, out _, out _);
        }
    }
}
