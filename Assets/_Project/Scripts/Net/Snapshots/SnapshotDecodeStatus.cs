namespace Arena.Net.Snapshots
{
    /// <summary>Result of <see cref="SnapshotDecoder.Decode"/>.</summary>
    public enum SnapshotDecodeStatus : byte
    {
        Ok = 0,

        /// <summary>The baseline view is not in the ring: drop the snapshot and ask for a key frame in the input (§4.4).</summary>
        MissingBaseline = 1,

        /// <summary>This tick was decoded already.</summary>
        Duplicate = 2,

        /// <summary>Truncated or invalid data: dropped, the ring slot is left invalid.</summary>
        Malformed = 3
    }
}
