using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>
    /// Feeds the match into the fighter animations (brief A-TheoAnim §2): tick events → <see cref="IFighterAnimSink.OnShoot"/> /
    /// <see cref="IFighterAnimSink.OnHurt"/>; per rendered frame the measured speed, status, facing, spawn / death / match end, then
    /// <see cref="FighterAnimator.Evaluate"/> + <see cref="FighterAnimator.Apply"/>. Owned by the match runner, one sink per slot,
    /// created at match start; no allocations afterwards. Frozen together with the views during hit stop (the runner does not
    /// render then, D-015).
    /// </summary>
    public sealed class FighterAnimDriver
    {
        private const float Epsilon = 1e-6f;

        private readonly FighterAnimConfig _config;
        private readonly IFighterAnimSink[] _sinks;
        private readonly FighterAnimator[] _animators;
        private readonly FighterRig[] _rigs;
        private readonly Renderer[] _renderers;
        private readonly Vector3[] _lastPosition;
        private readonly bool[] _hasLastPosition;
        private readonly bool[] _wasAlive;
        private readonly bool[] _wasShown;
        private readonly bool[] _dying;
        private bool _ended;

        public FighterAnimDriver(FighterAnimConfig config, int capacity)
        {
            _config = config;
            _sinks = new IFighterAnimSink[capacity];
            _animators = new FighterAnimator[capacity];
            _rigs = new FighterRig[capacity];
            _renderers = new Renderer[capacity];
            _lastPosition = new Vector3[capacity];
            _hasLastPosition = new bool[capacity];
            _wasAlive = new bool[capacity];
            _wasShown = new bool[capacity];
            _dying = new bool[capacity];
            CullOffscreen = config != null && config.CullOffscreen;
        }

        /// <summary>Skip writing bones of fighters whose body renderer is off screen (from the config; tests in batch mode turn it off).</summary>
        public bool CullOffscreen { get; set; }

        /// <summary>Bone writes so far (all slots) — a cheap probe for smoke tests.</summary>
        public int AppliedCount { get; private set; }

        public int Capacity => _sinks.Length;

        public FighterAnimator Animator(int slot) => _animators[slot];

        /// <summary>Procedural animator for a fighter view with a rig; a view without a rig (capsule) gets none.</summary>
        public void Bind(int slot, FighterRig rig, Renderer bodyRenderer)
        {
            if (rig == null || _config == null) return;
            var animator = new FighterAnimator(_config, slot);
            animator.Bind(rig);
            _animators[slot] = animator;
            _sinks[slot] = animator;
            _rigs[slot] = rig;
            _renderers[slot] = bodyRenderer;
        }

        /// <summary>Any sink for a slot (tests, a future Animator-based path); it is told events but not evaluated.</summary>
        public void SetSink(int slot, IFighterAnimSink sink)
        {
            _sinks[slot] = sink;
            _animators[slot] = sink as FighterAnimator;
        }

        /// <summary>Events of one tick: abilities fired → clips, damage → hurt (direction toward the source, heavy = ult).</summary>
        public void ProcessEvents(SimEventBuffer events, MatchState state)
        {
            for (int i = 0; i < events.Count; i++)
            {
                ref readonly SimEvent e = ref events[i];
                switch (e.Type)
                {
                    case SimEventType.AbilityFired:
                    {
                        IFighterAnimSink sink = SinkOf(e.Source);
                        if (sink != null && e.Value >= 0) sink.OnShoot(MatchDefs.SlotOf(e.Value), ToWorld(e.Direction));
                        break;
                    }
                    case SimEventType.Damaged:
                    {
                        IFighterAnimSink sink = SinkOf(e.Target);
                        if (sink == null || e.Value <= 0) break;
                        bool heavy = e.AbilityRef >= 0 && MatchDefs.SlotOf(e.AbilityRef) == AbilitySlot.Ult;
                        sink.OnHurt(HitFrom(in e, state), heavy);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Spawn / death / reveal bookkeeping of one slot for this frame. Returns true while a fighter that died in view still
        /// plays its defeat clip: the runner keeps the view shown (without the health bar) until it ends.
        /// </summary>
        public bool UpdateLife(int slot, bool alive, bool shown)
        {
            IFighterAnimSink sink = _sinks[slot];
            if (alive)
            {
                _dying[slot] = false;
                if (!_wasAlive[slot])
                {
                    sink?.OnSpawn();
                    _hasLastPosition[slot] = false;
                }
                else if (shown && !_wasShown[slot])
                {
                    // Not evaluated while hidden: start clean when it shows up again.
                    sink?.ResetAll();
                    _hasLastPosition[slot] = false;
                }
                _wasAlive[slot] = true;
                _wasShown[slot] = shown;
                return false;
            }

            if (_wasAlive[slot])
            {
                _wasAlive[slot] = false;
                _dying[slot] = _wasShown[slot] && sink != null;
                if (_dying[slot]) sink.OnDeath();
            }
            _wasShown[slot] = false;
            FighterAnimator animator = _animators[slot];
            if (_dying[slot] && (animator == null || animator.DefeatFinished)) _dying[slot] = false;
            return _dying[slot];
        }

        /// <summary>Once per match: winners hop, the others stay idle.</summary>
        public void CheckMatchEnd(MatchState state, int fighterCount)
        {
            if (_ended || !state.Mode.IsEnded) return;
            _ended = true;
            int winner = state.Mode.WinnerTeam;
            for (int i = 0; i < fighterCount && i < _sinks.Length; i++)
                _sinks[i]?.OnMatchEnd(state.Fighters[i].Team == winner);
        }

        /// <summary>
        /// One rendered frame of a slot, after the root was placed: speed from the interpolated position, status, facing, then
        /// evaluate and (unless culled) write the bones. <paramref name="dt"/> — frame seconds.
        /// </summary>
        public void Render(int slot, Vector3 worldPosition, Vector3 facing, bool stunned, bool dashing, float dt)
        {
            IFighterAnimSink sink = _sinks[slot];
            if (sink == null) return;

            if (dt > 0f)
            {
                Vector3 delta = _hasLastPosition[slot] ? worldPosition - _lastPosition[slot] : Vector3.zero;
                delta.y = 0f;
                float distance = delta.magnitude;
                Vector3 dir = distance > Epsilon ? delta / distance : Vector3.zero;
                sink.OnMove(distance / dt, dir, dt);
            }
            _lastPosition[slot] = worldPosition;
            _hasLastPosition[slot] = true;

            sink.SetStatus(stunned, dashing);
            sink.SetLook(facing);

            FighterAnimator animator = _animators[slot];
            FighterRig rig = _rigs[slot];
            if (animator == null) return;
            animator.Evaluate(dt);
            if (rig == null) return;
            Renderer r = _renderers[slot];
            if (CullOffscreen && r != null && !r.isVisible) return;
            animator.Apply(rig);
            AppliedCount++;
        }

        private IFighterAnimSink SinkOf(int slot) => slot >= 0 && slot < _sinks.Length ? _sinks[slot] : null;

        /// <summary>World direction from the hit fighter toward the source; the hit's own direction reversed if the source is unknown.</summary>
        private static Vector3 HitFrom(in SimEvent e, MatchState state)
        {
            int target = e.Target;
            int source = e.Source;
            if (source >= 0 && source != target && source < state.Fighters.Length && target < state.Fighters.Length)
            {
                Vec2 d = state.Fighters[source].Position - state.Fighters[target].Position;
                if (!d.IsZero) return ToWorld(d);
            }
            return -ToWorld(e.Direction);
        }

        private static Vector3 ToWorld(Vec2 v) => new Vector3(v.X, 0f, v.Y);
    }
}
