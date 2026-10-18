using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>ClientStats</c> (unreliable, client → server, about once a second, Server-Spec §5.6, §11.2): the client's own
    /// prediction and presentation counters since <c>MatchStart</c>, cumulative — a lost report is covered by the next one; the
    /// server adds the growth since the previous report of the slot to its metrics. All fields varuint; the largest prediction
    /// error in thousandths of a cell. No identifiers, no device data (§6.4). Preallocated, no allocations.
    /// </summary>
    public sealed class ClientStatsMessage
    {
        /// <summary>Upper bound of every counter on read (a client cannot report more than ≈ 2³⁰).</summary>
        public const uint MaxCounter = 1u << 30;

        /// <summary>Reconciliation samples without an external cause (<c>PredictionErrorStats.Samples</c>).</summary>
        public long PredictionSamples;

        /// <summary>Of them, with an error above zero (corrections).</summary>
        public long PredictionCorrections;

        /// <summary>Of them, above <c>LargeCorrectionCells</c>.</summary>
        public long LargeCorrections;

        /// <summary>The largest prediction error, thousandths of a cell.</summary>
        public long MaxErrorMilliCells;

        /// <summary>Presented (rendered) ticks and those of them extrapolated (interpolation underrun).</summary>
        public long PresentedTicks;

        public long ExtrapolatedTicks;

        /// <summary>View jumps above the motion limit and those above one cell (<c>ClientNetStats</c>).</summary>
        public long Teleports;

        public long LargeTeleports;

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.ClientStats);
            WriteCounter(w, PredictionSamples);
            WriteCounter(w, PredictionCorrections);
            WriteCounter(w, LargeCorrections);
            WriteCounter(w, MaxErrorMilliCells);
            WriteCounter(w, PresentedTicks);
            WriteCounter(w, ExtrapolatedTicks);
            WriteCounter(w, Teleports);
            WriteCounter(w, LargeTeleports);
        }

        /// <summary>Reads the body after the kind byte. False — malformed or out of range (the report is dropped).</summary>
        public bool Read(BitReader r)
        {
            if (!ReadCounter(r, out PredictionSamples)) return false;
            if (!ReadCounter(r, out PredictionCorrections)) return false;
            if (!ReadCounter(r, out LargeCorrections)) return false;
            if (!ReadCounter(r, out MaxErrorMilliCells)) return false;
            if (!ReadCounter(r, out PresentedTicks)) return false;
            if (!ReadCounter(r, out ExtrapolatedTicks)) return false;
            if (!ReadCounter(r, out Teleports)) return false;
            if (!ReadCounter(r, out LargeTeleports)) return false;
            return PredictionCorrections <= PredictionSamples && LargeCorrections <= PredictionCorrections
                && ExtrapolatedTicks <= PresentedTicks && LargeTeleports <= Teleports;
        }

        private static void WriteCounter(BitWriter w, long value)
        {
            if (value < 0) value = 0;
            if (value > MaxCounter) value = MaxCounter;
            w.WriteVarUInt((uint)value);
        }

        private static bool ReadCounter(BitReader r, out long value)
        {
            uint v = r.ReadVarUInt();
            value = v;
            return !r.IsOverflow && v <= MaxCounter;
        }
    }
}
