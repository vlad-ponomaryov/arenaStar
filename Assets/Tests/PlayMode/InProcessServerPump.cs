using Arena.Net.Server.Lobby;
using Arena.Net.Transport;
using UnityEngine;

namespace Arena.Tests.PlayMode
{
    /// <summary>Runs a <see cref="ServerNode"/> in the play-mode process (Server-Spec §11.2): one update per frame, before the
    /// client session (real time, real UDP on 127.0.0.1). Survives scene loads.</summary>
    [DefaultExecutionOrder(-2000)]
    public sealed class InProcessServerPump : MonoBehaviour
    {
        public ServerNode Node;

        private void Awake() => DontDestroyOnLoad(gameObject);

        private void Update()
        {
            if (Node != null) Node.Update(NetClock.NowMicros());
        }
    }
}
