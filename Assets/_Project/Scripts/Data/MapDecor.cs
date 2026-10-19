namespace Arena.Data
{
    /// <summary>
    /// Legend of the decor layer <see cref="MapConfig.Decor"/> (D-140, Map-SnowValley-LB §5 п.2): which prop the view draws on a cell.
    /// View only — the cell's rules come from <see cref="MapConfig.Rows"/>. <c>.</c> = the look by the cell type.
    /// On a wall <c>#</c>: <see cref="Tree"/>, <see cref="Fort"/> (fort corner / pier), <see cref="Lantern"/> (the same with a lantern,
    /// D-142), <see cref="Drift"/>, <see cref="Sled"/>;
    /// on a breakable wall <c>%</c>: <see cref="Snowman"/>, <see cref="Fort"/> (snow bricks); on a berry box <c>$</c>:
    /// <see cref="BigGift"/>; any other cell: only <c>.</c>.
    /// </summary>
    public static class MapDecor
    {
        public const char None = '.';
        public const char Tree = 'T';
        public const char Fort = 'f';
        public const char Lantern = 'L';
        public const char Drift = 'd';
        public const char Sled = 's';
        public const char Snowman = 'm';
        public const char BigGift = 'G';

        private const char WallChar = '#';
        private const char BreakableChar = '%';
        private const char BoxChar = '$';

        /// <summary>True if <paramref name="decor"/> may stand on a cell written as <paramref name="tile"/> in the map rows.</summary>
        public static bool IsAllowed(char tile, char decor)
        {
            if (decor == None) return true;
            switch (tile)
            {
                case WallChar: return decor == Tree || decor == Fort || decor == Lantern || decor == Drift || decor == Sled;
                case BreakableChar: return decor == Snowman || decor == Fort;
                case BoxChar: return decor == BigGift;
                default: return false;
            }
        }

        /// <summary>Null if the map has no decor or a valid one (same size as the rows, every symbol allowed on its cell), else the
        /// first problem.</summary>
        public static string Validate(MapConfig map)
        {
            if (map == null) return "no map";
            if (map.Decor == null || map.Decor.Length == 0) return null;
            if (map.Rows == null || map.Decor.Length != map.Rows.Length)
                return "decor has " + map.Decor.Length + " rows, the map " + (map.Rows == null ? 0 : map.Rows.Length);
            for (int r = 0; r < map.Rows.Length; r++)
            {
                string row = map.Rows[r], decor = map.Decor[r];
                if (decor == null || row == null || decor.Length != row.Length) return "decor row " + r + " has another length";
                for (int c = 0; c < row.Length; c++)
                    if (!IsAllowed(row[c], decor[c]))
                        return "decor '" + decor[c] + "' is not allowed on '" + row[c] + "' at text row " + r + ", column " + c;
            }
            return null;
        }

        /// <summary>Decor symbol of cell (<paramref name="column"/>, <paramref name="row"/>) in map coordinates (row 0 = bottom, the
        /// last text row), or <see cref="None"/> when the map has no (valid-sized) decor there.</summary>
        public static char At(string[] decor, int column, int row)
        {
            if (decor == null || decor.Length == 0) return None;
            int textRow = decor.Length - 1 - row;
            if (textRow < 0 || textRow >= decor.Length) return None;
            string line = decor[textRow];
            return line != null && column >= 0 && column < line.Length ? line[column] : None;
        }
    }
}
