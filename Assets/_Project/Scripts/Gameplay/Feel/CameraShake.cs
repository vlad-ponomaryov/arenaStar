using UnityEngine;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Camera shake of an ult hit (GDD §5.3): a ground-plane offset oscillating at <c>frequency</c>, fading linearly to zero over
    /// the duration. A new, stronger shake replaces a weaker one. Plain C#; the camera controller adds <see cref="Offset"/>.
    /// </summary>
    public sealed class CameraShake
    {
        private const float TwoPi = Mathf.PI * 2f;

        // Different phase speeds on X and Z so the offset does not move along one line (ratio of two coprime-ish numbers).
        private const float SecondAxisRatio = 1.37f;

        private float _amplitude;
        private float _duration;
        private float _frequency;
        private float _elapsed;

        public bool IsActive => _elapsed < _duration;

        /// <summary>Current offset in world units (zero when idle).</summary>
        public Vector3 Offset { get; private set; }

        public void Trigger(float amplitude, float seconds, float frequency)
        {
            if (amplitude <= 0f || seconds <= 0f) return;
            if (IsActive && CurrentAmplitude() > amplitude) return;
            _amplitude = amplitude;
            _duration = seconds;
            _frequency = frequency;
            _elapsed = 0f;
            Offset = Vector3.zero;
        }

        /// <param name="deltaTime">Unscaled frame time, seconds.</param>
        public void Update(float deltaTime)
        {
            if (!IsActive)
            {
                Offset = Vector3.zero;
                return;
            }
            _elapsed += deltaTime;
            if (_elapsed >= _duration)
            {
                Offset = Vector3.zero;
                return;
            }
            float a = CurrentAmplitude();
            float phase = _elapsed * _frequency * TwoPi;
            Offset = new Vector3(Mathf.Sin(phase) * a, 0f, Mathf.Sin(phase * SecondAxisRatio) * a);
        }

        public void Reset()
        {
            _elapsed = _duration = 0f;
            Offset = Vector3.zero;
        }

        private float CurrentAmplitude() => _duration <= 0f ? 0f : _amplitude * (1f - _elapsed / _duration);
    }
}
