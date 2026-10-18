using System;
using Arena.Core.Modes;

namespace Arena.Net.Content
{
    /// <summary>Maps a mode is played on: indices into <see cref="ContentBundle.Maps"/> (Server-Spec §5.4).</summary>
    public sealed class ContentModeMaps
    {
        public ModeKind Mode;

        /// <summary>Indices into <see cref="ContentBundle.Maps"/>, in the order of <c>GameDatabase</c>.</summary>
        public int[] MapIndices = new int[0];

        public void Validate(int mapCount)
        {
            if (MapIndices == null || MapIndices.Length == 0) throw new ArgumentException("ContentModeMaps " + Mode + ": no maps");
            for (int i = 0; i < MapIndices.Length; i++)
                if (MapIndices[i] < 0 || MapIndices[i] >= mapCount)
                    throw new ArgumentException("ContentModeMaps " + Mode + ": map index " + MapIndices[i] + " out of range");
        }
    }
}
