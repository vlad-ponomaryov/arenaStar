using System;
using UnityEngine;

namespace Arena.Data.Audio
{
    /// <summary>
    /// One sound of a fighter (step, shot, hit…): clip variants (a random one, not the same twice in a row), volume, pitch
    /// spread, cooldown per fighter, priority for the voice limit, channel. No clips = silent.
    /// </summary>
    [Serializable]
    public sealed class SfxCue
    {
        [Tooltip("Variants; one is picked at random, never the same twice in a row.")]
        public AudioClip[] Clips = new AudioClip[0];

        [Tooltip("Volume of the clip, 0–1 (before the channel and the player's setting).")]
        [Range(0f, 1f)] public float Volume;

        [Tooltip("Random pitch spread ± this (0.05 = ±5 %).")]
        [Range(0f, 0.5f)] public float PitchJitter;

        [Tooltip("The same fighter plays this cue at most once per this many seconds.")]
        [Min(0f)] public float Cooldown;

        [Tooltip("Voice limit: when all sources are busy, a sound replaces the oldest one of a lower or equal priority.")]
        public int Priority;

        public SfxChannel Channel = SfxChannel.Sfx;

        [Tooltip("Only for the local player's own fighter (voices of pain, for example).")]
        public bool LocalOnly;

        public bool HasClips => Clips != null && Clips.Length > 0;
    }
}
