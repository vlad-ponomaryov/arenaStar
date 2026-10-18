namespace Arena.Net.Transport
{
    /// <summary>
    /// Source of unpredictable 64-bit values: connection ids, nonces, cookie keys, path challenges. Real endpoints use
    /// <see cref="CryptoNetRandom"/>; tests use <see cref="DeterministicNetRandom"/> so that runs repeat exactly.
    /// </summary>
    public interface INetRandom
    {
        ulong NextULong();
    }
}
