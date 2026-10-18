using System;

namespace Arena.UI.Controls
{
    /// <summary>Thresholds of <see cref="StickGesture"/>, as fractions of the stick radius and seconds (from UiStyle).</summary>
    [Serializable]
    public struct StickGestureSettings
    {
        /// <summary>Offset below this fraction of the radius is "not dragged" (tap / no movement).</summary>
        public float DeadZone;

        /// <summary>After a drag, releasing closer than this fraction of the radius cancels the shot.</summary>
        public float CancelRadius;

        /// <summary>Longest press that still counts as a tap, seconds.</summary>
        public float TapMaxSeconds;

        public StickGestureSettings(float deadZone, float cancelRadius, float tapMaxSeconds)
        {
            DeadZone = deadZone;
            CancelRadius = cancelRadius;
            TapMaxSeconds = tapMaxSeconds;
        }
    }
}
