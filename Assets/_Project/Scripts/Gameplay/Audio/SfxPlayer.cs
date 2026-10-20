using System;
using Arena.Data.Audio;
using UnityEngine;

namespace Arena.Gameplay.Audio
{
    /// <summary>
    /// Thin sound player of the match (brief A-TheoSfx, grown from the single source of D-028): a pool of 2D
    /// <see cref="AudioSource"/>s created once (<see cref="AudioConfig.MaxVoices"/>), the voice limit of
    /// <see cref="SfxVoiceTable"/>, output to the mixer group of the channel. Volume = clip volume × channel volume ×
    /// <see cref="MasterVolume"/> (the player's setting). Positional sounds come with volume and pan already applied
    /// (<see cref="SfxSpatial"/>). No allocations per sound.
    /// </summary>
    public sealed class SfxPlayer : ISfxOutput
    {
        private readonly AudioConfig _config;
        private readonly AudioSource[] _sources;
        private readonly SfxVoiceTable _voices;

        /// <summary>The player's sound setting, 0–1.</summary>
        public float MasterVolume { get; set; }

        /// <summary>Counters for tests and debug.</summary>
        public int Played { get; private set; }
        public int Dropped { get; private set; }

        public SfxVoiceTable Voices => _voices;

        public SfxPlayer(Transform parent, AudioConfig config, float masterVolume)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            MasterVolume = Mathf.Clamp01(masterVolume);
            int count = Mathf.Max(1, config.MaxVoices);
            _voices = new SfxVoiceTable(count);
            _sources = new AudioSource[count];
            var host = new GameObject("Sfx");
            host.transform.SetParent(parent, false);
            for (int i = 0; i < count; i++)
            {
                AudioSource s = host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                _sources[i] = s;
            }
        }

        public bool Play(AudioClip clip, SfxChannel channel, float volume, float pitch, float pan, int priority)
        {
            if (clip == null) return false;
            float v = volume * _config.ChannelVolume(channel) * MasterVolume;
            if (v <= 0f) return false;
            float p = pitch > 0f ? pitch : 1f;
            int i = _voices.Acquire(priority, Time.unscaledTime, clip.length / p);
            if (i < 0)
            {
                Dropped++;
                return false;
            }
            AudioSource s = _sources[i];
            s.Stop();
            s.clip = clip;
            s.volume = Mathf.Clamp01(v);
            s.pitch = p;
            s.panStereo = Mathf.Clamp(pan, -1f, 1f);
            s.outputAudioMixerGroup = _config.Group(channel);
            s.Play();
            Played++;
            return true;
        }

        public void StopAll()
        {
            for (int i = 0; i < _sources.Length; i++)
                if (_sources[i] != null) _sources[i].Stop();
            _voices.Reset();
        }
    }
}
