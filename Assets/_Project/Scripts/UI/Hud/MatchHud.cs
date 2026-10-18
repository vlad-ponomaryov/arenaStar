using Arena.Core.Math;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Data.Localization;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Settings;
using Arena.Gameplay.Input;
using Arena.Gameplay.Match;
using Arena.Online;
using Arena.UI.Controls;
using Arena.UI.Results;
using Arena.UI.Style;
using UnityEngine;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Scene bootstrap of the match HUD: builds the controls from <see cref="UiStyle"/> and every frame forwards the sticks
    /// into the runner's <see cref="LocalInputCollector"/> (touch channel, view space). Runs before <see cref="MatchRunner"/>
    /// so the values of this frame reach this frame's ticks. Lives in Arena.UI because UI depends on Gameplay, not back.
    /// The ult stick works only while the ult is charged (GDD §5.1); ability state is shown by <see cref="AbilityHudView"/>.
    /// The Acorns score bar (<see cref="ScoreBarView"/>) and the Last Beast panel (<see cref="LastBeastHudView"/>, S3.14) are on
    /// the same canvas; when the match ends the controls are hidden
    /// and the <see cref="ResultsScreen"/> appears ("Play again" → <see cref="SceneFlow.Restart"/>, "Menu" → <see cref="SceneFlow.ToMenu"/>).
    /// S2.10: the layout follows the "mirrored" setting, the charged ult stick pulses (<see cref="FeelConfig"/>), an FPS counter
    /// sits in the top-left corner; the aim line knows whether the ult stick is held (its range).
    /// S3.10b: online in a Development build — the dev network overlay (<see cref="NetOverlayView"/>, <c>NetConfig.DevOverlay</c>).
    /// S3.11: online — the ping badge (<see cref="PingIndicator"/>) and the connection banner (<see cref="ConnectionOverlay"/>);
    /// the results show the own place in Last Beast, also early, when the own fighter is out (<c>Eliminated</c>); "Play again"
    /// queues again (<see cref="OnlineSession.SearchAgain"/>) and goes to the menu, which shows the matchmaking screen.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MatchHud : MonoBehaviour
    {
        [SerializeField] private MatchRunner _runner;
        [SerializeField] private UiStyle _style;

        private MatchHudControls _controls;
        private AbilityHudView _abilityView;
        private ScoreBarView _scoreBar;
        private LastBeastHudView _lastBeast;
        private ResultsScreen _results;
        private FpsCounter _fps;
        private FeelConfig _feel;
        private NetOverlayView _netOverlay;
        private bool _netOverlayChecked;
        private OnlineMatchDriver _online;
        private PingIndicator _ping;
        private ConnectionOverlay _connection;

        public MatchRunner Runner => _runner;
        public UiStyle Style => _style;
        public MatchHudControls Controls => _controls;
        public ScoreBarView ScoreBar => _scoreBar;

        /// <summary>Last Beast panel: fighters left, fog timer, own berries (S3.14).</summary>
        public LastBeastHudView LastBeast => _lastBeast;
        public ResultsScreen Results => _results;
        public FpsCounter Fps => _fps;

        /// <summary>Dev network overlay of an online match in a Development build, or null.</summary>
        public NetOverlayView NetOverlay => _netOverlay;

        /// <summary>Ping badge of an online match, or null.</summary>
        public PingIndicator Ping => _ping;

        /// <summary>Connection banner of an online match, or null.</summary>
        public ConnectionOverlay Connection => _connection;

        private void Start()
        {
            if (_runner == null || _style == null)
            {
                Debug.LogError("MatchHud: runner or style is not set", this);
                enabled = false;
                return;
            }
            if (_runner.Database != null && _runner.Database.Strings != null) Loc.Init(_runner.Database.Strings);
            _feel = _runner.Database != null ? _runner.Database.Feel : null;
            _controls = MatchHudBuilder.Build(transform, _style, PlayerSettingsStore.SharedFor(_feel).Current.MirroredLayout);
            _abilityView = new AbilityHudView(_controls, _style);
            _scoreBar = ScoreBarView.Build(_controls.Canvas.transform, _style);
            _lastBeast = LastBeastHudView.Build(_controls.Canvas.transform, _style);
            _results = ResultsScreen.Build(transform, _style, PlayAgain, SceneFlow.ToMenu);
            if (_feel != null) _fps = new FpsCounter(_controls.Canvas.transform, _style, _feel.FpsUpdateSeconds);
        }

        private void Update()
        {
            if (_controls == null) return;
            _fps?.Tick(Time.unscaledDeltaTime);
            IMatchDriver driver = _runner.Driver;
            if (driver != null && !_netOverlayChecked) CreateOnlineWidgets(driver);
            _netOverlay?.Tick(Time.unscaledDeltaTime);
            _ping?.Tick(Time.unscaledDeltaTime);
            _connection?.Tick();
            if (driver != null)
            {
                _scoreBar.Apply(driver.Defs, driver.State, _runner.LocalTeam);
                _lastBeast.Apply(driver.Defs, driver.State, _runner.LocalSlot);
            }
            int eliminatedPlace = EliminatedPlace(driver);
            bool eliminated = eliminatedPlace > 0;
            if (_runner.IsMatchEnded || eliminated)
            {
                if (!_results.IsShown)
                {
                    SetControlsVisible(false);
                    _results.Show(driver.Defs, driver.State, _runner.LocalTeam, _runner.LocalSlot, eliminatedPlace);
                }
                _results.Tick();
                return;
            }
            LocalInputCollector input = _runner.InputCollector;
            AbilityHudInfo abilities = driver != null ? AbilityHudInfo.From(driver.Defs, driver.State, _runner.LocalSlot) : default;
            if (driver != null) _abilityView.Apply(in abilities);
            bool ultActive = abilities.UltCharged;
            if (_feel != null) _abilityView.AnimateUltGlow(ultActive, Time.unscaledTime, _feel.UltGlowFrequency, _feel.UltGlowScale);

            input.SetMove(InputChannel.Touch, ToVec2(_controls.MoveStick.Value));

            bool aimingAttack = _controls.AttackStick.IsHeld;
            Vector2 aim = aimingAttack ? _controls.AttackStick.Aim
                : ultActive && _controls.UltStick.IsHeld ? _controls.UltStick.Aim : Vector2.zero;
            input.SetAim(InputChannel.Touch, ToVec2(aim), !aimingAttack);

            if (_controls.AttackStick.TryConsumeRelease(out StickGestureResult attack, out Vector2 attackAim))
            {
                if (attack == StickGestureResult.Tap) input.PressAttack(Vec2.Zero, true);
                else if (attack == StickGestureResult.Manual) input.PressAttack(ToVec2(attackAim), false);
            }
            if (_controls.UltStick.TryConsumeRelease(out StickGestureResult ult, out Vector2 ultAim) && ultActive)
            {
                if (ult == StickGestureResult.Tap) input.PressUlt(Vec2.Zero, true);
                else if (ult == StickGestureResult.Manual) input.PressUlt(ToVec2(ultAim), false);
            }
            if (_controls.TrickButton.ConsumePress()) input.PressTrick();
        }

        /// <summary>
        /// Place of the own fighter that is out of a free-for-all match still running (the personal result is shown at once):
        /// online from <c>Eliminated</c>, offline (Last Beast training, S3.14) from its <c>FighterStats.Place</c>; 0 otherwise.
        /// </summary>
        private int EliminatedPlace(IMatchDriver driver)
        {
            if (_online != null) return _online.IsEliminated ? _online.EliminatedPlace : 0;
            if (driver == null || !driver.Defs.NoRespawn || driver.IsEnded) return 0;
            int slot = _runner.LocalSlot;
            return (uint)slot < (uint)driver.State.Stats.Length ? driver.State.Stats[slot].Place : 0;
        }

        /// <summary>The runner gets its driver after this Start: the online widgets are made on the first frame with a driver.</summary>
        private void CreateOnlineWidgets(IMatchDriver driver)
        {
            _netOverlayChecked = true;
            _online = driver as OnlineMatchDriver;
            var net = _runner.Database != null ? _runner.Database.Net : null;
            if (_online == null || net == null) return;
            Transform canvas = _controls.Canvas.transform;
            _ping = PingIndicator.Build(canvas, _style, _online, NetConfigConverter.ToLobbySettings(net), net.HudPingRefreshSeconds);
            _connection = ConnectionOverlay.Build(canvas, _style, _online, net.ConnectionLostSeconds, net.ReconnectWindowSeconds);
            if (net.DevOverlay && Debug.isDebugBuild)
                _netOverlay = NetOverlayView.Build(canvas, _style, _online, net.DevOverlayRefreshSeconds);
        }

        /// <summary>"Play again": offline — the same match again; online — back into the queue (the menu shows the search).</summary>
        private void PlayAgain()
        {
            OnlineSession session = OnlineSession.Instance;
            if (_online != null && session != null && session.SearchAgain())
            {
                SceneFlow.ToMenu();
                return;
            }
            SceneFlow.Restart();
        }

        private void SetControlsVisible(bool visible)
        {
            _controls.MoveStick.gameObject.SetActive(visible);
            _controls.AttackStick.gameObject.SetActive(visible);
            _controls.UltStick.gameObject.SetActive(visible);
            _controls.TrickButton.gameObject.SetActive(visible);
        }

        private static Vec2 ToVec2(Vector2 v) => new Vec2(v.x, v.y);
    }
}
