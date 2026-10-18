using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// An entry of a snapshot list (foreign fighter, projectile segment, area, pickup) coded field by field against its previous
    /// value (<see cref="DeltaCodec"/>); a new entry is coded against <c>default</c>. Ticks are relative to <c>baseTick</c>
    /// (the snapshot tick). Used through generic constraints on structs — no boxing.
    /// </summary>
    public interface ISnapshotEntry<T> where T : struct
    {
        /// <summary>Writes the fields of this entry that differ from <paramref name="prev"/>.</summary>
        void WriteDelta(BitWriter w, in T prev, int baseTick);

        /// <summary>Called on the previous value: reads a delta written against it and returns the new value.</summary>
        T ReadDelta(BitReader r, int baseTick);
    }
}
