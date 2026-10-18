namespace Arena.UI.Hud
{
    /// <summary>What the <see cref="ConnectionOverlay"/> shows (S3.11).</summary>
    public enum ConnectionOverlayState : byte
    {
        /// <summary>Everything fine: hidden.</summary>
        Ok = 0,

        /// <summary>No packets for a while or the connection is down: "connection lost — reconnecting… N s".</summary>
        Lost = 1,

        /// <summary>The server's bot plays the own fighter (§7): "a bot is playing your fighter".</summary>
        BotPlaying = 2,
    }
}
