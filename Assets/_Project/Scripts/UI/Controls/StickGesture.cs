using UnityEngine;

namespace Arena.UI.Controls
{
    /// <summary>
    /// Pure classification of a stick press (GDD §5.1): tap → auto-aim; drag beyond the dead zone → manual aim, fired on
    /// release; drag and return into the centre → cancel. Offsets are normalized to the stick radius (length ≤ 1).
    /// No Unity callbacks — tested in EditMode; <see cref="VirtualStick"/> feeds it.
    /// </summary>
    public struct StickGesture
    {
        private float _startTime;
        private float _peakLength;
        private Vector2 _current;

        public bool IsActive { get; private set; }

        /// <summary>Latest normalized offset of the finger from the stick centre.</summary>
        public Vector2 Current => _current;

        /// <summary>True once the finger has left the dead zone during this press.</summary>
        public bool Dragged(in StickGestureSettings s) => IsActive && _peakLength >= s.DeadZone;

        public void Begin(float time)
        {
            IsActive = true;
            _startTime = time;
            _peakLength = 0f;
            _current = Vector2.zero;
        }

        public void Move(Vector2 normalizedOffset)
        {
            if (!IsActive) return;
            _current = Vector2.ClampMagnitude(normalizedOffset, 1f);
            float len = _current.magnitude;
            if (len > _peakLength) _peakLength = len;
        }

        /// <summary>Manual aim to show while held (aim line): zero before the drag and inside the cancel zone.</summary>
        public Vector2 CurrentAim(in StickGestureSettings s) =>
            Dragged(s) && _current.magnitude >= s.CancelRadius ? _current : Vector2.zero;

        /// <param name="aim">Normalized aim for <see cref="StickGestureResult.Manual"/>, otherwise zero.</param>
        public StickGestureResult End(float time, in StickGestureSettings s, out Vector2 aim)
        {
            aim = Vector2.zero;
            if (!IsActive) return StickGestureResult.None;
            IsActive = false;

            if (_peakLength < s.DeadZone)
                return time - _startTime <= s.TapMaxSeconds ? StickGestureResult.Tap : StickGestureResult.None;
            if (_current.magnitude < s.CancelRadius) return StickGestureResult.Cancel;
            aim = _current;
            return StickGestureResult.Manual;
        }

        /// <summary>Aborts the press without a result (pointer lost, HUD hidden).</summary>
        public void Abort()
        {
            IsActive = false;
            _current = Vector2.zero;
        }
    }
}
