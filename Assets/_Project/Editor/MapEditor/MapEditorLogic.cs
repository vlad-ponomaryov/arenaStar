using System;
using System.Collections.Generic;
using Arena.Core.Map;
using Arena.Data.Conversion;

namespace Arena.Editor.MapEditor
{
    /// <summary>
    /// Pure functions of the map editor (S2.6): brush painting, resize, central mirroring, text rows ↔ grid, validation.
    /// <see cref="MapEditorWindow"/> only forwards mouse input here and draws the result. Covered by MapEditorLogicTests.
    /// </summary>
    public static class MapEditorLogic
    {
        /// <summary>
        /// Paints a square brush of <paramref name="brushSize"/> cells (odd; 1 or 3) centred on the cell, clipped by the map
        /// edges. Markers (spawns, hollow) are always painted as a single cell; painting a hollow removes the previous one.
        /// Returns the number of changed cells.
        /// </summary>
        public static int Paint(MapGrid grid, int column, int textRow, char value, int brushSize)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (!grid.Contains(column, textRow)) return 0;
            int changed = 0;
            if (IsMarker(value))
            {
                if (value == MapConverter.HollowChar)
                    for (int i = 0; i < grid.Cells.Length; i++)
                        if (grid.Cells[i] == MapConverter.HollowChar && i != textRow * grid.Width + column)
                        {
                            grid.Cells[i] = MapConverter.FloorChar;
                            changed++;
                        }
                return changed + SetCell(grid, column, textRow, value);
            }

            int radius = Math.Max(0, brushSize) / 2;
            for (int t = textRow - radius; t <= textRow + radius; t++)
                for (int c = column - radius; c <= column + radius; c++)
                    if (grid.Contains(c, t)) changed += SetCell(grid, c, t, value);
            return changed;
        }

        /// <summary>
        /// New grid of the given size; the content keeps its Core coordinates (anchored at the bottom-left cell, team 0
        /// side); new cells are floor, cells outside the new size are dropped.
        /// </summary>
        public static MapGrid Resize(MapGrid grid, int width, int height)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            var result = new MapGrid(width, height, MapConverter.FloorChar);
            int rows = Math.Min(height, grid.Height);
            int columns = Math.Min(width, grid.Width);
            for (int mapRow = 0; mapRow < rows; mapRow++)
                for (int c = 0; c < columns; c++)
                    result.Set(c, result.TextRowOfMapRow(mapRow), grid.Get(c, grid.TextRowOfMapRow(mapRow)));
            return result;
        }

        /// <summary>
        /// Makes the map centrally symmetric (GDD §5.5) by copying one half onto the other rotated by 180°:
        /// <paramref name="fromTop"/> = the top half (and the left part of an odd middle row) is the source, otherwise the
        /// bottom half (and the right part of the middle row). Spawns swap teams in the copy; a hollow is not copied
        /// (it must be the centre cell). The centre cell of an odd×odd map is left as is.
        /// </summary>
        public static void MirrorCentral(MapGrid grid, bool fromTop)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            int last = grid.Cells.Length - 1;
            // In text reading order the central mirror of index i is (N − 1 − i).
            for (int i = 0; i <= last; i++)
            {
                int mirror = last - i;
                bool isSource = fromTop ? i < mirror : i > mirror;
                if (isSource) grid.Cells[mirror] = MirrorChar(grid.Cells[i]);
            }
        }

        /// <summary>Character of the mirror cell: team spawns swap, the hollow becomes floor, tiles stay.</summary>
        public static char MirrorChar(char value)
        {
            if (value == MapConverter.Team0SpawnChar) return MapConverter.Team1SpawnChar;
            if (value == MapConverter.Team1SpawnChar) return MapConverter.Team0SpawnChar;
            if (value == MapConverter.HollowChar) return MapConverter.FloorChar;
            return value;
        }

        public static bool IsMarker(char value) =>
            value == MapConverter.Team0SpawnChar || value == MapConverter.Team1SpawnChar || value == MapConverter.HollowChar
            || value == MapConverter.SharedSpawnChar;

        /// <summary>Text rows (top first), the format of <c>MapConfig.Rows</c>.</summary>
        public static string[] ToRows(MapGrid grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            var rows = new string[grid.Height];
            for (int t = 0; t < grid.Height; t++) rows[t] = new string(grid.Cells, t * grid.Width, grid.Width);
            return rows;
        }

        /// <summary>Grid from text rows (top first); throws <see cref="ArgumentException"/> on empty or unequal rows.
        /// Characters are not checked here (see <see cref="Validate"/>).</summary>
        public static MapGrid FromRows(IReadOnlyList<string> rows)
        {
            if (rows == null || rows.Count == 0) throw new ArgumentException("No rows");
            int width = rows[0] == null ? 0 : rows[0].Length;
            if (width == 0) throw new ArgumentException("Row 0 is empty");
            var grid = new MapGrid(width, rows.Count, MapConverter.FloorChar);
            for (int t = 0; t < rows.Count; t++)
            {
                string row = rows[t];
                if (row == null || row.Length != width)
                    throw new ArgumentException("Text row " + t + " has length " + (row == null ? 0 : row.Length) + ", expected " + width);
                row.CopyTo(0, grid.Cells, t * width, width);
            }
            return grid;
        }

        /// <summary>Central symmetry through <see cref="MapConverter"/> + <see cref="MapSymmetry"/>; on false
        /// <paramref name="error"/> says why and <paramref name="mismatch"/> is the offending cell (Core coordinates) if any.</summary>
        public static bool CheckSymmetry(MapGrid grid, string id, out GridCell mismatch, out bool hasCell, out string error)
        {
            mismatch = default;
            hasCell = false;
            MapDef map;
            try
            {
                map = MapConverter.FromRows(id, ToRows(grid));
            }
            catch (ArgumentException e)
            {
                error = e.Message;
                return false;
            }
            if (MapSymmetry.Check(map, out mismatch))
            {
                error = null;
                return true;
            }
            hasCell = true;
            error = "Not centrally symmetric at cell " + mismatch;
            return false;
        }

        /// <summary>Everything the match start checks (<see cref="MapConverter.ToMapDef"/>): format, spawns per team,
        /// symmetry if required.</summary>
        public static bool Validate(MapGrid grid, string id, int spawnsPerTeam, bool requireSymmetry, out string error)
        {
            try
            {
                MapDef map = MapConverter.FromRows(id, ToRows(grid));
                MapConverter.ValidateRules(map, spawnsPerTeam, requireSymmetry);
                error = null;
                return true;
            }
            catch (ArgumentException e)
            {
                error = e.Message;
                return false;
            }
        }

        private static int SetCell(MapGrid grid, int column, int textRow, char value)
        {
            if (grid.Get(column, textRow) == value) return 0;
            grid.Set(column, textRow, value);
            return 1;
        }
    }
}
