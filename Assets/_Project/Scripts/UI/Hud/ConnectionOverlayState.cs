namespace Arena.UI.Hud
{
    /// <summary>What the <see cref="ConnectionOverlay"/> shows (S3.11–S3.12).</summary>
    public enum ConnectionOverlayState : byte
    {
        /// <summary>Everything fine: hidden.</summary>
        Ok = 0,

        /// <summary>No packets for a while or the connection is down: "connection lost — reconnecting… N s".</summary>
        Lost = 1,

        /// <summary>The server's bot plays the own fighter (§7): "a bot is playing your fighter".</summary>
        BotPlaying = 2,

        /// <summary>The automatic attempts gave up (window over): "server unreachable" + "Back to the match".</summary>
        GaveUp = 3,

        /// <summary>The server answered <c>MatchOver</c>: "the match is already over" + "Menu".</summary>
        MatchOver = 4,
    }
}
