namespace Arena.Core.Map
{
    /// <summary>
    /// Read/write view of a tile array (row-major, index = row × width + column). Cells outside the map read as
    /// <see cref="TileType.Wall"/>, so the map edge needs no special case. A struct over the state's own array —
    /// creating one allocates nothing; writes go straight into <c>MatchState.Tiles</c>.
    /// </summary>
    public readonly struct TileGrid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly TileType[] Tiles;

        public TileGrid(int width, int height, TileType[] tiles)
        {
            Width = width;
            Height = height;
            Tiles = tiles;
        }

        public bool InBounds(int column, int row) => (uint)column < (uint)Width && (uint)row < (uint)Height;

        public int IndexOf(int column, int row) => row * Width + column;

        public GridCell CellAt(int index) => new GridCell(index % Width, index / Width);

        public TileType Get(int column, int row) => InBounds(column, row) ? Tiles[row * Width + column] : TileType.Wall;

        public TileType Get(GridCell cell) => Get(cell.Column, cell.Row);

        public void Set(int column, int row, TileType type)
        {
            if (InBounds(column, row)) Tiles[row * Width + column] = type;
        }

        public bool BlocksMovement(int column, int row) => TileRules.BlocksMovement(Get(column, row));

        public bool BlocksProjectiles(int column, int row) => TileRules.BlocksProjectiles(Get(column, row));
    }
}
