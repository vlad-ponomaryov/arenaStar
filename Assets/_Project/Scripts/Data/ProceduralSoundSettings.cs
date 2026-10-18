using System;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// One procedural sound (D-028): a sine whose frequency glides from <see cref="StartFrequency"/> to
    /// <see cref="EndFrequency"/>, mixed with white noise, with a fast attack and a linear fade. An optional second tone
    /// (<see cref="SecondToneFrequency"/> &gt; 0) replaces the first one in the second half (a two-note signal).
    /// </summary>
    [Serializable]
    public struct ProceduralSoundSettings
    {
        [Tooltip("Frequency at the start, Hz.")]
        [Min(0f)] public float StartFrequency;

        [Tooltip("Frequency at the end, Hz (equal to the start = a steady tone).")]
        [Min(0f)] public float EndFrequency;

        [Tooltip("Frequency of the second note in the second half, Hz; 0 = one note.")]
        [Min(0f)] public float SecondToneFrequency;

        [Tooltip("Length, seconds.")]
        [Min(0.005f)] public float Seconds;

        [Tooltip("Peak volume 0..1.")]
        [Range(0f, 1f)] public float Volume;

        [Tooltip("Share of white noise in the mix 0..1 (0 = pure tone).")]
        [Range(0f, 1f)] public float NoiseMix;

        public ProceduralSoundSettings(float startFrequency, float endFrequency, float secondToneFrequency, float seconds,
            float volume, float noiseMix)
        {
            StartFrequency = startFrequency;
            EndFrequency = endFrequency;
            SecondToneFrequency = secondToneFrequency;
            Seconds = seconds;
            Volume = volume;
            NoiseMix = noiseMix;
        }
    }
}
