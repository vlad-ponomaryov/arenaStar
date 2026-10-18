using UnityEngine;

namespace Arena.Online
{
    /// <summary>
    /// Log of the online client (Server-Spec §12.3): one line per state change of the session (search, connect, match start
    /// and end, errors) with the <c>[Net]</c> prefix; nothing per frame or per packet. Expected network failures are warnings —
    /// a lost server is not a bug of the game. No addresses beyond the server's, no identifiers (§6.4).
    /// </summary>
    public static class UnityNetLog
    {
        public const string Prefix = "[Net] ";

        public static void Info(string message) => Debug.Log(Prefix + message);

        public static void Warn(string message) => Debug.LogWarning(Prefix + message);
    }
}
