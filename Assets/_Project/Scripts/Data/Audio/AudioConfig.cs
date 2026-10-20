using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Arena.Data.Audio
{
    /// <summary>
    /// Match audio (brief A-TheoSfx step 2): the mixer and its groups (Master → Sfx, Voice; created by Editor code), the voice
    /// limit, channel volumes, how other fighters' sounds fade with distance from the local fighter and pan left/right, and the
    /// fighters' sound sets. The player's volume setting multiplies everything. Values are written by DataSeeder.
    /// </summary>
    [CreateAssetMenu(fileName = "Audio", menuName = "Arena/Audio Config")]
    public sealed class AudioConfig : ScriptableObject
    {
        [Header("Mixer")]
        [Tooltip("Mixer of the game (Master → Sfx, Voice). Empty = sources play straight to the listener.")]
        public AudioMixer Mixer;
        public AudioMixerGroup SfxGroup;
        public AudioMixerGroup VoiceGroup;

        [Header("Voices")]
        [Tooltip("Audio sources in the pool = sounds at once; more → the oldest of a lower or equal priority is replaced.")]
        [Min(1)] public int MaxVoices;

        [Tooltip("Volume of the effects channel, 0–1.")]
        [Range(0f, 1f)] public float SfxVolume;

        [Tooltip("Volume of the voice channel, 0–1.")]
        [Range(0f, 1f)] public float VoiceVolume;

        [Header("Other fighters (positional)")]
        [Tooltip("Full volume up to this distance from the local fighter, world units.")]
        [Min(0f)] public float FullVolumeDistance;

        [Tooltip("Silent from this distance on, world units (linear fade in between).")]
        [Min(0.01f)] public float SilentDistance;

        [Tooltip("Stereo pan of a sound PanDistance to the side on screen (0 = no pan, 1 = hard left/right).")]
        [Range(0f, 1f)] public float PanStrength;

        [Tooltip("Horizontal distance on screen, world units, that gives the full PanStrength.")]
        [Min(0.01f)] public float PanDistance;

        [Header("Procedural sounds (D-028: hit, ult, ult ready of fighters without a sound set)")]
        public int ProceduralPriority;

        [Header("Fighters")]
        public List<FighterSoundSet> FighterSounds = new List<FighterSoundSet>();

        /// <summary>Sound set of a fighter id, or null. No allocations.</summary>
        public FighterSoundSet FindFighterSounds(string fighterId)
        {
            if (FighterSounds == null || string.IsNullOrEmpty(fighterId)) return null;
            for (int i = 0; i < FighterSounds.Count; i++)
                if (FighterSounds[i] != null && string.Equals(FighterSounds[i].FighterId, fighterId, StringComparison.Ordinal))
                    return FighterSounds[i];
            return null;
        }

        /// <summary>Channel volume 0–1.</summary>
        public float ChannelVolume(SfxChannel channel) => channel == SfxChannel.Voice ? VoiceVolume : SfxVolume;

        /// <summary>Mixer group of a channel (null = none).</summary>
        public AudioMixerGroup Group(SfxChannel channel) => channel == SfxChannel.Voice ? VoiceGroup : SfxGroup;
    }
}
