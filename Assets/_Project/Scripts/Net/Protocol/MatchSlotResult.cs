namespace Arena.Net.Protocol
{
    /// <summary>Result of one slot in <see cref="MatchEndedMessage"/> / <see cref="EliminatedMessage"/>: <c>FighterStats</c> + AFK flag.</summary>
    public struct MatchSlotResult
    {
        public int DamageDealt;
        public int HealingDone;
        public int Kills;
        public int Deaths;
        public int AcornsCollected;

        /// <summary>Final place (Last Beast), 0 — no places in the mode.</summary>
        public int Place;

        /// <summary>The slot was taken over for being AFK at some point (rewards of stage 4, §7).</summary>
        public bool Afk;

        public void Write(Serialization.BitWriter w)
        {
            w.WriteVarUInt((uint)DamageDealt);
            w.WriteVarUInt((uint)HealingDone);
            w.WriteVarUInt((uint)Kills);
            w.WriteVarUInt((uint)Deaths);
            w.WriteVarUInt((uint)AcornsCollected);
            w.WriteVarUInt((uint)Place);
            w.WriteBool(Afk);
        }

        public void Read(Serialization.BitReader r)
        {
            DamageDealt = (int)r.ReadVarUInt();
            HealingDone = (int)r.ReadVarUInt();
            Kills = (int)r.ReadVarUInt();
            Deaths = (int)r.ReadVarUInt();
            AcornsCollected = (int)r.ReadVarUInt();
            Place = (int)r.ReadVarUInt();
            Afk = r.ReadBool();
        }
    }
}
