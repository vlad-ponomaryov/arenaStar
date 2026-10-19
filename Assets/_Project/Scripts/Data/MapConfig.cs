using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// A map as text rows (GDD §5.5), one character per cell; legend in <c>MapConverter</c>:
    /// <c>.</c> floor, <c>#</c> wall, <c>%</c> breakable wall, <c>"</c> bush, <c>~</c> water, <c>$</c> berry box,
    /// <c>A</c> / <c>B</c> spawn of team 0 / team 1 (floor), <c>S</c> shared spawn of free-for-all modes (floor), <c>H</c> hollow (floor).
    /// <see cref="Rows"/>[0] is the TOP row (team 1 side, largest Y); the last row is the bottom (team 0, row 0, D-009) —
    /// the text reads like the map seen by team 0. Converted by <c>MapConverter</c>; written by DataSeeder / the map editor.
    /// </summary>
    [CreateAssetMenu(fileName = "Map", menuName = "Arena/Map")]
    public sealed class MapConfig : ScriptableObject
    {
        public string Id;

        [Tooltip("Rows top (team 1) to bottom (team 0), all of equal length.")]
        public string[] Rows = new string[0];

        [Tooltip("Spawns each of the two teams must have (3 for 3×3 modes).")]
        [Min(0)] public int RequiredSpawnsPerTeam;

        [Tooltip("Shared spawns (S) the map must have: 10 for the Last Beast, 0 for team maps.")]
        [Min(0)] public int RequiredSharedSpawns;

        [Tooltip("Team maps must be centrally symmetric (GDD §5.5).")]
        public bool RequireCentralSymmetry;

        [Tooltip("Decor layer (D-140): rows like Rows, one symbol per cell = which prop the view draws there (legend in MapDecor); " +
                 "empty = the look by the cell type. View only: the simulation and the content pack never read it.")]
        public string[] Decor = new string[0];

        [Tooltip("Look of this map (materials, colours, decor props); empty = the shared look of the view catalog.")]
        public MapThemeConfig Theme;
    }
}
