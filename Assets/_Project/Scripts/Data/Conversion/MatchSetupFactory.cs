using System;
using Arena.Core.Defs;
using Arena.Core.Math;

namespace Arena.Data.Conversion
{
    /// <summary>
    /// Builds the prototype <see cref="MatchSetup"/> from <see cref="MatchRunnerConfig"/>: slots [0, TeamSize) are team 0
    /// (bottom), the rest team 1 (top). Temporary spawns are rows near the short edges of <see cref="ArenaBounds"/>,
    /// centrally symmetric (D-009), until map spawn points (S2.5). Called once per match start.
    /// </summary>
    public static class MatchSetupFactory
    {
        public static MatchSetup CreateDefault(MatchRunnerConfig config, MatchDefs defs)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (config.SlotFighterIds == null || config.SlotFighterIds.Count == 0)
                throw new ArgumentException("MatchRunnerConfig.SlotFighterIds is empty");
            if (config.TeamSize <= 0) throw new ArgumentException("MatchRunnerConfig.TeamSize must be > 0");

            int count = config.SlotFighterIds.Count;
            var setup = new MatchSetup
            {
                Seed = config.Seed, Slots = new PlayerSlotSetup[count], UnlockAllAbilities = config.UnlockAllAbilities
            };
            for (int i = 0; i < count; i++)
            {
                string id = config.SlotFighterIds[i];
                int fighterIndex = defs.IndexOfFighter(id);
                if (fighterIndex < 0) throw new ArgumentException("MatchRunnerConfig: unknown fighter id '" + id + "' in slot " + i);
                int team = i < config.TeamSize ? 0 : 1;
                int indexInTeam = team == 0 ? i : i - config.TeamSize;
                int teamCount = team == 0 ? MathUtil.Min(count, config.TeamSize) : count - config.TeamSize;
                setup.Slots[i] = new PlayerSlotSetup(fighterIndex, team, config.FighterLevel,
                    SpawnPoint(defs.Bounds, team, indexInTeam, teamCount, config.SpawnEdgeInset, config.SpawnSpacing));
            }
            setup.Validate(defs);
            if (config.LocalSlot < 0 || config.LocalSlot >= count)
                throw new ArgumentException("MatchRunnerConfig.LocalSlot " + config.LocalSlot + " out of range");
            return setup;
        }

        /// <summary>Spawn of the <paramref name="indexInTeam"/>-th fighter; team 1 mirrors team 0 through the arena centre.</summary>
        public static Vec2 SpawnPoint(ArenaBounds bounds, int team, int indexInTeam, int teamCount, float edgeInset, float spacing)
        {
            float offset = (indexInTeam - (teamCount - 1) * 0.5f) * spacing;
            Vec2 c = bounds.Center;
            return team == 0
                ? new Vec2(c.X + offset, bounds.Min.Y + edgeInset)
                : new Vec2(c.X - offset, bounds.Max.Y - edgeInset);
        }
    }
}
