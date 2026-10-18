using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Gameplay.Settings;
using Arena.Net.Client;
using UnityEngine;

namespace Arena.Online
{
    /// <summary>
    /// Bootstrap of the Match scene for online play (Server-Spec §12.2, S3.10b): runs before <see cref="MatchRunner"/> (Awake,
    /// <c>DefaultExecutionOrder</c> like the HUD, D-049). If the scene was loaded for an online match, it takes the session from
    /// <see cref="OnlineSession"/>, gives the runner an <see cref="OnlineMatchDriver"/> and answers <c>ClientReady</c> (the scene is
    /// loaded — the server starts the countdown). Otherwise it does nothing and the runner plays offline as before.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class OnlineMatchBootstrap : MonoBehaviour
    {
        [SerializeField] private MatchRunner _runner;

        /// <summary>The driver given to the runner, or null (offline).</summary>
        public OnlineMatchDriver Driver { get; private set; }

        private void Awake()
        {
            MatchLaunchParams launch = SceneFlow.Current;
            if (launch == null || !launch.Online) return;
            if (_runner == null)
            {
                Debug.LogError("OnlineMatchBootstrap: runner is not set", this);
                return;
            }
            OnlineSession online = OnlineSession.Instance;
            MatchClientSession session = online != null ? online.TakeMatch() : null;
            if (session == null)
            {
                UnityNetLog.Warn("online match scene without a started session: playing offline");
                return;
            }
            var feel = _runner.Database != null ? _runner.Database.Feel : null;
            GameSettings settings = PlayerSettingsStore.SharedFor(feel).Current;
            Driver = new OnlineMatchDriver(session, settings.AimMagnet, feel != null ? feel.AimMagnetDegrees : 0f);
            _runner.SetDriver(Driver);
            session.SetReady();
        }
    }
}
