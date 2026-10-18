namespace Arena.Core.Map
{
    /// <summary>Spawn cell of a team. The k-th spawn of a team is used by the k-th fighter of that team.</summary>
    public struct MapSpawn
    {
        public int Team;
        public GridCell Cell;

        public MapSpawn(int team, GridCell cell)
        {
            Team = team;
            Cell = cell;
        }
    }
}
