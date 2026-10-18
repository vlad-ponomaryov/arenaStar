namespace Arena.Core.Map
{
    /// <summary>
    /// Spawn cell of a team. The k-th spawn of a team is used by the k-th fighter of that team. A shared spawn
    /// (<see cref="SharedTeam"/>, map character <c>S</c>) belongs to no team: free-for-all modes hand them out by a seeded
    /// permutation (Last Beast, <c>MatchSetupBuilder.CreateFreeForAll</c>).
    /// </summary>
    public struct MapSpawn
    {
        /// <summary>Team of a shared spawn (free-for-all modes).</summary>
        public const int SharedTeam = -1;

        public int Team;
        public GridCell Cell;

        public MapSpawn(int team, GridCell cell)
        {
            Team = team;
            Cell = cell;
        }
    }
}
