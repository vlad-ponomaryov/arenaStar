using UnityEngine;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Short vibrations (GDD §5.3) on Android through <c>android.os.Vibrator</c> with <c>VibrationEffect.createOneShot</c>
    /// (minSdk 26, amplitude = setting strength). Java objects (vibrator, one effect per duration) are created once and cached;
    /// they are rebuilt only when the strength changes. In the editor and on other platforms — no-op. At most one vibration per
    /// <c>minInterval</c>. The fallback <see cref="Handheld.Vibrate"/> also makes Unity add the VIBRATE permission.
    /// </summary>
    public sealed class Haptics
    {
        private const int MaxAmplitude = 255;

        private readonly float _minInterval;
        private float _strength;
        private float _nextAllowedTime;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _vibrator;
        private AndroidJavaClass _effectClass;
        private readonly System.Collections.Generic.Dictionary<int, AndroidJavaObject> _effects =
            new System.Collections.Generic.Dictionary<int, AndroidJavaObject>();
        private bool _failed;
#endif

        /// <param name="strength">0..1 (0 = off).</param>
        public Haptics(float strength, float minInterval)
        {
            _minInterval = minInterval;
            SetStrength(strength);
        }

        public float Strength => _strength;

        /// <summary>Count of vibrations requested and allowed (tests and debug; also counted in the editor).</summary>
        public int PlayedCount { get; private set; }

        public void SetStrength(float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (strength == _strength) return;
            _strength = strength;
#if UNITY_ANDROID && !UNITY_EDITOR
            foreach (AndroidJavaObject e in _effects.Values) e?.Dispose();
            _effects.Clear();
#endif
        }

        /// <param name="milliseconds">Length of the buzz.</param>
        /// <param name="now">Unscaled time, seconds.</param>
        public void Vibrate(int milliseconds, float now)
        {
            if (_strength <= 0f || milliseconds <= 0 || now < _nextAllowedTime) return;
            _nextAllowedTime = now + _minInterval;
            PlayedCount++;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_failed) return;
            try
            {
                if (_vibrator == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    _effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                }
                if (_vibrator == null)
                {
                    _failed = true;
                    return;
                }
                if (!_effects.TryGetValue(milliseconds, out AndroidJavaObject effect))
                {
                    int amplitude = Mathf.Clamp(Mathf.RoundToInt(_strength * MaxAmplitude), 1, MaxAmplitude);
                    effect = _effectClass.CallStatic<AndroidJavaObject>("createOneShot", (long)milliseconds, amplitude);
                    _effects[milliseconds] = effect;
                }
                _vibrator.Call("vibrate", effect);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Debug.LogWarning("Haptics: vibration is not available, falling back: " + e.Message);
                Handheld.Vibrate();
            }
#endif
        }
    }
}
