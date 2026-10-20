using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>Damped oscillator of the bone springs (Art-Spec §3.1): semi-implicit Euler, stable for ω·dt &lt; 2.</summary>
    public static class SpringMath
    {
        /// <summary>
        /// Moves the spring toward <paramref name="target"/> (degrees). <paramref name="frequency"/> — natural frequency, Hz;
        /// <paramref name="damping"/> — damping ratio (1 = critical). The caller limits <paramref name="dt"/> (config MaxDt).
        /// </summary>
        public static void Step(ref SpringState s, float target, float frequency, float damping, float dt)
        {
            if (dt <= 0f) return;
            float omega = 2f * Mathf.PI * frequency;
            float accel = omega * omega * (target - s.Angle) - 2f * damping * omega * s.Velocity;
            s.Velocity += accel * dt;
            s.Angle += s.Velocity * dt;
        }

        /// <summary>Keeps the angle within ±<paramref name="maxDegrees"/>; at the limit the outward velocity is dropped.</summary>
        public static void Clamp(ref SpringState s, float maxDegrees)
        {
            if (s.Angle > maxDegrees)
            {
                s.Angle = maxDegrees;
                if (s.Velocity > 0f) s.Velocity = 0f;
            }
            else if (s.Angle < -maxDegrees)
            {
                s.Angle = -maxDegrees;
                if (s.Velocity < 0f) s.Velocity = 0f;
            }
        }
    }
}
