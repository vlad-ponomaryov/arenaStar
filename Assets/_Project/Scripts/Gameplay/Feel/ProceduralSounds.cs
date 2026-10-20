using System;
using Arena.Data;
using Arena.Data.Audio;
using Arena.Gameplay.Audio;
using UnityEngine;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Short procedural sounds of the prototype (D-028): clips are generated once at match start with
    /// <see cref="AudioClip.Create(string,int,int,int,bool)"/> from <see cref="FeelConfig"/> (hit, ult, ult ready) and played
    /// through the match <see cref="ISfxOutput"/> (<see cref="SfxPlayer"/>: voice limit, volume setting). No asset files, original
    /// content. Hits are rate-limited (<see cref="FeelConfig.HitSoundMinInterval"/>). Fighters with their own sound set
    /// (<see cref="FighterSfx"/>) skip these (see <see cref="HitFeedback"/>).
    /// </summary>
    public sealed class ProceduralSounds
    {
        // Attack of the envelope (fraction of the length): avoids a click at the start.
        private const float AttackFraction = 0.08f;
        private const float TwoPi = Mathf.PI * 2f;
        private const int NoiseSeed = 12345;

        private readonly ISfxOutput _output;
        private readonly int _priority;
        private readonly float _hitMinInterval;
        private float _nextHitTime;

        public AudioClip Hit { get; }
        public AudioClip Ult { get; }
        public AudioClip UltReady { get; }

        /// <param name="output">Null = silent.</param>
        /// <param name="priority">Voice-limit priority of these sounds.</param>
        public ProceduralSounds(ISfxOutput output, FeelConfig feel, int priority)
        {
            if (feel == null) throw new ArgumentNullException(nameof(feel));
            _output = output;
            _priority = priority;
            _hitMinInterval = feel.HitSoundMinInterval;
            Hit = CreateClip("Hit", feel.HitSound, feel.SampleRate);
            Ult = CreateClip("Ult", feel.UltSound, feel.SampleRate);
            UltReady = CreateClip("UltReady", feel.UltReadySound, feel.SampleRate);
        }

        /// <param name="now">Unscaled time, seconds.</param>
        public void PlayHit(float now)
        {
            if (now < _nextHitTime) return;
            _nextHitTime = now + _hitMinInterval;
            Play(Hit);
        }

        public void PlayUlt() => Play(Ult);

        public void PlayUltReady() => Play(UltReady);

        private void Play(AudioClip clip)
        {
            if (_output != null && clip != null) _output.Play(clip, SfxChannel.Sfx, 1f, 1f, 0f, _priority);
        }

        public static AudioClip CreateClip(string name, ProceduralSoundSettings s, int sampleRate)
        {
            float[] samples = Generate(s, sampleRate);
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>Mono samples −1..1 of the sound: gliding sine (+ optional second note) mixed with noise, fast attack, linear fade.</summary>
        public static float[] Generate(ProceduralSoundSettings s, int sampleRate)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(s.Seconds * sampleRate));
            var samples = new float[count];
            var noise = new System.Random(NoiseSeed);
            float phase = 0f;
            int attack = Mathf.Max(1, Mathf.RoundToInt(count * AttackFraction));
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                float freq = s.SecondToneFrequency > 0f && t >= 0.5f ? s.SecondToneFrequency : Mathf.Lerp(s.StartFrequency, s.EndFrequency, t);
                phase += TwoPi * freq / sampleRate;
                if (phase > TwoPi) phase -= TwoPi;
                float tone = Mathf.Sin(phase);
                float white = (float)(noise.NextDouble() * 2.0 - 1.0);
                float env = i < attack ? (float)i / attack : 1f - (float)(i - attack) / (count - attack);
                samples[i] = Mathf.Clamp((tone * (1f - s.NoiseMix) + white * s.NoiseMix) * env * s.Volume, -1f, 1f);
            }
            return samples;
        }
    }
}
