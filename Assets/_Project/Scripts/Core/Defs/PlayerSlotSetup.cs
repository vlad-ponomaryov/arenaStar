using Arena.Core.Math;

namespace Arena.Core.Defs
{
    /// <summary>One fighter slot of a match: who plays it (fighter, level), for which team, where it spawns.</summary>
    public struct PlayerSlotSetup
    {
        /// <summary>Index in <see cref="MatchDefs.Fighters"/>.</summary>
        public int FighterIndex;

        /// <summary>Team 0..<c>SimLimits.MaxTeams</c>−1 (3×3: team 0 bottom, team 1 top — D-009).</summary>
        public int Team;

        /// <summary>Fighter level, 1 and up (D-012: 1 in the prototype).</summary>
        public int Level;

        /// <summary>Spawn and respawn position (MatchSetupFactory takes it from the map spawns).</summary>
        public Vec2 SpawnPosition;

        public PlayerSlotSetup(int fighterIndex, int team, int level, Vec2 spawnPosition)
        {
            FighterIndex = fighterIndex;
            Team = team;
            Level = level;
            SpawnPosition = spawnPosition;
        }
    }
}
