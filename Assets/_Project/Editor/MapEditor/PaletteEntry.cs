using UnityEngine;

namespace Arena.Editor.MapEditor
{
    /// <summary>One brush of the map editor palette: legend character, label and preview colour.</summary>
    public readonly struct PaletteEntry
    {
        public readonly char Symbol;
        public readonly string Name;
        public readonly Color Color;

        public PaletteEntry(char symbol, string name, Color color)
        {
            Symbol = symbol;
            Name = name;
            Color = color;
        }
    }
}
