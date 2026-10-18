using System;
using Arena.Core.Math;

namespace Arena.Core.Map
{
    /// <summary>Integer cell coordinates (D-009): column = X, row = Y; row 0 is the bottom (team 0) edge.</summary>
    public struct GridCell : IEquatable<GridCell>
    {
        public int Column;
        public int Row;

        public GridCell(int column, int row)
        {
            Column = column;
            Row = row;
        }

        /// <summary>World centre of the cell: (c + 0.5, r + 0.5).</summary>
        public Vec2 Center => new Vec2(Column + 0.5f, Row + 0.5f);

        /// <summary>Cell containing a point.</summary>
        public static GridCell Of(Vec2 p) => new GridCell((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

        public bool Equals(GridCell o) => Column == o.Column && Row == o.Row;
        public override bool Equals(object obj) => obj is GridCell other && Equals(other);
        public override int GetHashCode() => (Column * 397) ^ Row;
        public override string ToString() => "(" + Column + ", " + Row + ")";
        public static bool operator ==(GridCell a, GridCell b) => a.Equals(b);
        public static bool operator !=(GridCell a, GridCell b) => !a.Equals(b);
    }
}
