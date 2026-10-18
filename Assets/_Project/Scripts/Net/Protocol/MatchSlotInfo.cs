namespace Arena.Net.Protocol
{
    /// <summary>One slot of <see cref="MatchStartMessage"/>: the static part of a fighter the snapshots do not repeat (§4.4).</summary>
    public struct MatchSlotInfo
    {
        public int FighterIndex;
        public int Team;
        public int Level;

        /// <summary>Spawn point, cells (exact float — the client builds the same <c>MatchSetup</c>).</summary>
        public float SpawnX;
        public float SpawnY;

        /// <summary>The slot is a bot; sent only when the server shows bots (<c>ShowBots</c>, D-087), otherwise false.</summary>
        public bool IsBot;
    }
}
