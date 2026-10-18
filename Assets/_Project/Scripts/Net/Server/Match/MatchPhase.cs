namespace Arena.Net.Server.Match
{
    /// <summary>Life cycle of a <see cref="MatchRoom"/> (Server-Spec §5.3).</summary>
    public enum MatchPhase : byte
    {
        /// <summary>Waiting for the players to connect (up to <c>JoinTimeoutSeconds</c>; who does not come — a bot).</summary>
        Joining = 0,

        /// <summary><c>MatchStart</c> sent, waiting for <c>ClientReady</c> (up to <c>LoadTimeoutSeconds</c>).</summary>
        Loading = 1,

        /// <summary>Ticks −CountdownTicks … −1: snapshots and inputs flow, the simulation stands (§2.3).</summary>
        Countdown = 2,

        /// <summary>From T = 0 every tick is <c>MatchSimulation.Tick</c>.</summary>
        Running = 3,

        /// <summary><c>MatchEnded</c> sent; the room stays <c>ResultLingerSeconds</c> for returning players.</summary>
        Ended = 4,

        /// <summary>Done; the host removes the room.</summary>
        Closed = 5,
    }
}
