using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Gameplay.Input;
using Arena.Gameplay.Match;
using Arena.Net.Client;
using Arena.Net.Transport;

namespace Arena.Online
{
    /// <summary>
    /// Online <see cref="IMatchDriver"/> (Server-Spec §12.2, S3.10b) over a <see cref="MatchClientSession"/>: the ticks come from
    /// the prediction clock (§2.3), every <see cref="Step"/> takes the local input, applies the aim magnet (setting, as offline),
    /// predicts and sends it, and exposes the display state of that prediction tick (<see cref="PresentationState"/>: own fighter
    /// from the shadow, others interpolated, their tick fields shifted) — the views, telegraphs and the HUD read it exactly like
    /// the offline state. <c>MatchEnded</c> fills the statistics and the mode result, so the results screen works unchanged.
    /// The network is polled earlier in the frame by <see cref="OnlineSession"/>. No allocations per frame.
    /// </summary>
    public sealed class OnlineMatchDriver : IMatchDriver
    {
        private readonly MatchClientSession _session;
        private readonly Func<long> _clock;
        private readonly bool _aimMagnet;
        private readonly float _aimMagnetDegrees;
        private readonly SimEventBuffer _noEvents = new SimEventBuffer();
        private long _frameMicros;
        private bool _built;

        /// <param name="session">A session that has received <c>MatchStart</c>.</param>
        /// <param name="aimMagnet">The aim magnet setting of the player.</param>
        /// <param name="clock">Microsecond clock (real time by default; tests may pass their own).</param>
        public OnlineMatchDriver(MatchClientSession session, bool aimMagnet, float aimMagnetDegrees, Func<long> clock = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            if (!session.HasStart || session.Presentation == null)
                throw new InvalidOperationException("OnlineMatchDriver: the session has no MatchStart");
            _aimMagnet = aimMagnet;
            _aimMagnetDegrees = aimMagnetDegrees;
            _clock = clock ?? NetClock.NowMicros;
            LocalSlot = session.OwnSlot;
            LocalTeam = session.Setup.Slots[LocalSlot].Team;
        }

        public MatchClientSession Session => _session;

        public MatchDefs Defs => _session.Defs;
        public MatchSetup Setup => _session.Setup;
        public MatchState State => _session.Presentation.State;

        /// <summary>Events of the last display tick; empty when the last step had no data to build a tick from.</summary>
        public SimEventBuffer Events => _built ? _session.Presentation.Events : _noEvents;

        public int LocalSlot { get; }
        public int LocalTeam { get; }
        public bool IsEnded => _session.HasEnded && _session.Presentation.Ended;
        public float Alpha => _session.Clock.Alpha;

        /// <summary>Display ticks built so far (tests, the dev overlay).</summary>
        public long BuiltTicks { get; private set; }

        public int BeginFrame(float deltaTime)
        {
            _frameMicros = _clock();
            return _session.BeginFrame(_frameMicros);
        }

        public void Step(LocalInputCollector input)
        {
            PlayerInput raw = ApplyAimMagnet(input.ConsumeForTick());
            PresentationState p = _session.Presentation;
            int before = p.State.Globals.Tick;
            bool had = p.HasState;
            _session.Step(in raw, _frameMicros);
            _built = p.HasState && (!had || p.State.Globals.Tick != before);
            if (_built) BuiltTicks++;
        }

        /// <summary>Manual attack/ult aim turns to a visible enemy within a few degrees (setting, GDD §5.1) — on the shown state.</summary>
        private PlayerInput ApplyAimMagnet(PlayerInput input)
        {
            if (!_aimMagnet || input.Has(InputButtons.AutoAim) || input.Aim.IsZero) return input;
            bool attack = input.Has(InputButtons.Attack);
            if (!attack && !input.Has(InputButtons.Ult)) return input;
            MatchState state = State;
            FighterDef def = Defs.Fighters[state.Fighters[LocalSlot].DefIndex];
            float range = attack || def.Ult == null || def.Ult.Range <= 0f ? def.AttackRange : def.Ult.Range;
            input.Aim = AimMagnet.Apply(state, LocalSlot, input.Aim, range, _aimMagnetDegrees);
            return input;
        }
    }
}
