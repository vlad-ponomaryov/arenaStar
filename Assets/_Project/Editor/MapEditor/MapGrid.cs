using System;

namespace Arena.Editor.MapEditor
{
    /// <summary>
    /// Editable map in text form: one legend character per cell (see <c>MapConverter</c>), stored row-major by TEXT row —
    /// text row 0 is the top (team 1 side), like <c>MapConfig.Rows</c>. Map row r (Core, 0 = bottom) is text row H − 1 − r.
    /// </summary>
    public sealed class MapGrid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly char[] Cells;

        public MapGrid(int width, int height, char fill)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Map size must be positive: " + width + "×" + height);
            Width = width;
            Height = height;
            Cells = new char[width * height];
            for (int i = 0; i < Cells.Length; i++) Cells[i] = fill;
        }

        public bool Contains(int column, int textRow) =>
            column >= 0 && column < Width && textRow >= 0 && textRow < Height;

        public char Get(int column, int textRow) => Cells[textRow * Width + column];

        public void Set(int column, int textRow, char value) => Cells[textRow * Width + column] = value;

        /// <summary>Text row of a Core map row (0 = bottom).</summary>
        public int TextRowOfMapRow(int mapRow) => Height - 1 - mapRow;
    }
}
