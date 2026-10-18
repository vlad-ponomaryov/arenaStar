namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Ult hit stop (GDD §5.3: 40 ms), visual only (D-015): while active, the runner does not move the views (fighters,
    /// projectiles keep their pose); the simulation keeps ticking and the views catch up afterwards. Plain C#, real time.
    /// </summary>
    public sealed class HitStop
    {
        private float _remaining;

        public bool IsActive => _remaining > 0f;

        /// <summary>Starts (or extends to) a stop of <paramref name="seconds"/>; a shorter request does not cut a running one.</summary>
        public void Trigger(float seconds)
        {
            if (seconds > _remaining) _remaining = seconds;
        }

        /// <param name="deltaTime">Unscaled frame time, seconds.</param>
        public void Update(float deltaTime)
        {
            if (_remaining <= 0f) return;
            _remaining -= deltaTime;
            if (_remaining < 0f) _remaining = 0f;
        }

        public void Reset() => _remaining = 0f;
    }
}
