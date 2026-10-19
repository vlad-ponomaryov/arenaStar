using System;

namespace Arena.UI.Controls
{
    /// <summary>Thresholds of <see cref="StickGesture"/>, as fractions of the stick radius (from UiStyle).</summary>
    [Serializable]
    public struct StickGestureSettings
    {
        /// <summary>Movement stick: an offset below this fraction of the radius is no movement.</summary>
        public float DeadZone;

        /// <summary>After a drag, releasing closer than this fraction of the radius cancels the shot.</summary>
        public float CancelRadius;

        /// <summary>
        /// Aim sticks: a press whose finger never moved farther than this fraction of the radius is a tap (auto-aim), however
        /// long it was held (D-129); beyond it — manual aim.
        /// </summary>
        public float TapZone;

        public StickGestureSettings(float deadZone, float cancelRadius, float tapZone)
        {
            DeadZone = deadZone;
            CancelRadius = cancelRadius;
            TapZone = tapZone;
        }
    }
}
