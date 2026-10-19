using Arena.Core.Input;
using Arena.Core.Math;

namespace Arena.Gameplay.Input
{
    /// <summary>
    /// Turns per-frame local input (virtual sticks, keyboard/mouse) into one <see cref="PlayerInput"/> per simulation tick
    /// (D-006, D-017). Directions arrive in view space (x right, y up on screen) and are rotated into simulation space:
    /// for team 1 the camera is turned 180° (D-016), so the input is turned the same way.
    ///
    /// One-shot buttons (attack, ult, trick) are latched until the next tick and appear in exactly one input. A latched press
    /// is delayed by one tick if the previous tick already carried the same button, so the simulation always sees a rising
    /// edge. Attack and ult carry their own aim (the stick is already released when the tick runs); if both are latched,
    /// the attack goes first and the ult in the next tick (a <see cref="PlayerInput"/> has one aim).
    /// Plain C#, no allocations after construction.
    /// </summary>
    public sealed class LocalInputCollector
    {
        private const int ChannelCount = 2;

        private readonly Vec2[] _move = new Vec2[ChannelCount];
        private readonly Vec2[] _aim = new Vec2[ChannelCount];
        private readonly bool[] _aimUlt = new bool[ChannelCount];
        private readonly bool[] _preview = new bool[ChannelCount];
        private readonly bool[] _previewUlt = new bool[ChannelCount];

        private bool _attackLatched;
        private bool _attackAuto;
        private Vec2 _attackAim;
        private bool _ultLatched;
        private bool _ultAuto;
        private Vec2 _ultAim;
        private bool _trickLatched;
        private InputButtons _lastButtons;

        /// <summary>True for team 1: view directions are rotated by 180° (D-016).</summary>
        public bool ViewRotated { get; set; }

        /// <summary>Current movement in simulation space (touch first, then keyboard).</summary>
        public Vec2 Move => Pick(_move);

        /// <summary>Current aim being held (stick dragged / mouse button held) in simulation space, zero if none.</summary>
        public Vec2 Aim => Pick(_aim);

        /// <summary>The aim being held (<see cref="Aim"/>) is for the ult, not the attack (the aim line uses the ult range).</summary>
        public bool AimIsUlt => _aim[(int)InputChannel.Touch].IsZero ? _aimUlt[(int)InputChannel.KeyboardMouse] : _aimUlt[(int)InputChannel.Touch];

        /// <summary>
        /// An aim stick is held without a drag (D-129): its release will fire the auto-aim, the views preview the target it picks.
        /// </summary>
        public bool AutoAimPreview => _preview[(int)InputChannel.Touch] || _preview[(int)InputChannel.KeyboardMouse];

        /// <summary>The held <see cref="AutoAimPreview"/> is for the ult, not the attack.</summary>
        public bool AutoAimPreviewIsUlt => _preview[(int)InputChannel.Touch] ? _previewUlt[(int)InputChannel.Touch] : _previewUlt[(int)InputChannel.KeyboardMouse];

        /// <summary>View-space direction → simulation-space direction.</summary>
        public Vec2 ViewToSim(Vec2 view) => ViewRotated ? -view : view;

        /// <summary>Simulation-space direction → view-space direction (inverse of <see cref="ViewToSim"/>).</summary>
        public Vec2 SimToView(Vec2 sim) => ViewRotated ? -sim : sim;

        public void SetMove(InputChannel channel, Vec2 viewMove) => _move[(int)channel] = Vec2.ClampLength(ViewToSim(viewMove), 1f);

        public void SetAim(InputChannel channel, Vec2 viewAim, bool ult = false)
        {
            _aim[(int)channel] = Vec2.ClampLength(ViewToSim(viewAim), 1f);
            _aimUlt[(int)channel] = ult;
        }

        /// <summary>Sets whether an aim stick of <paramref name="channel"/> is held still (auto-aim preview, D-129).</summary>
        public void SetAutoAimPreview(InputChannel channel, bool held, bool ult = false)
        {
            _preview[(int)channel] = held;
            _previewUlt[(int)channel] = held && ult;
        }

        /// <summary>Same as <see cref="SetAim"/> for a direction that is already in simulation space (mouse ray on the ground).</summary>
        public void SetAimSim(InputChannel channel, Vec2 simAim, bool ult = false)
        {
            _aim[(int)channel] = Vec2.ClampLength(simAim, 1f);
            _aimUlt[(int)channel] = ult;
        }

        /// <summary>Attack released: manual aim (view space, length = fraction of range) or auto-aim (tap).</summary>
        public void PressAttack(Vec2 viewAim, bool autoAim) => PressAttackSim(ViewToSim(viewAim), autoAim);

        public void PressAttackSim(Vec2 simAim, bool autoAim)
        {
            _attackLatched = true;
            _attackAuto = autoAim;
            _attackAim = autoAim ? Vec2.Zero : Vec2.ClampLength(simAim, 1f);
        }

        public void PressUlt(Vec2 viewAim, bool autoAim) => PressUltSim(ViewToSim(viewAim), autoAim);

        public void PressUltSim(Vec2 simAim, bool autoAim)
        {
            _ultLatched = true;
            _ultAuto = autoAim;
            _ultAim = autoAim ? Vec2.Zero : Vec2.ClampLength(simAim, 1f);
        }

        public void PressTrick() => _trickLatched = true;

        /// <summary>Input for the tick about to run; consumes the latched presses that it carries.</summary>
        public PlayerInput ConsumeForTick()
        {
            var buttons = InputButtons.None;
            Vec2 aim = Aim;

            if (_attackLatched && (_lastButtons & InputButtons.Attack) == 0)
            {
                buttons |= InputButtons.Attack;
                if (_attackAuto) buttons |= InputButtons.AutoAim;
                aim = _attackAim;
                _attackLatched = false;
            }
            else if (_ultLatched && (_lastButtons & InputButtons.Ult) == 0)
            {
                buttons |= InputButtons.Ult;
                if (_ultAuto) buttons |= InputButtons.AutoAim;
                aim = _ultAim;
                _ultLatched = false;
            }

            if (_trickLatched && (_lastButtons & InputButtons.Trick) == 0)
            {
                buttons |= InputButtons.Trick;
                _trickLatched = false;
            }

            _lastButtons = buttons;
            return new PlayerInput(Move, aim, buttons);
        }

        /// <summary>Drops all held values and latched presses (match restart, pause).</summary>
        public void Clear()
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                _move[i] = Vec2.Zero;
                _aim[i] = Vec2.Zero;
                _aimUlt[i] = false;
                _preview[i] = false;
                _previewUlt[i] = false;
            }
            _attackLatched = _ultLatched = _trickLatched = false;
            _attackAuto = _ultAuto = false;
            _attackAim = _ultAim = Vec2.Zero;
            _lastButtons = InputButtons.None;
        }

        private static Vec2 Pick(Vec2[] channels)
        {
            Vec2 touch = channels[(int)InputChannel.Touch];
            return touch.IsZero ? channels[(int)InputChannel.KeyboardMouse] : touch;
        }
    }
}
