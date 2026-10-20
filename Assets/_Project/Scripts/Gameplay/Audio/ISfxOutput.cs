using Arena.Data.Audio;
using UnityEngine;

namespace Arena.Gameplay.Audio
{
    /// <summary>Where match sounds go: <see cref="SfxPlayer"/> in the game, a recorder in tests.</summary>
    public interface ISfxOutput
    {
        /// <summary>Plays <paramref name="clip"/> once. <paramref name="volume"/> 0–1 before the channel and the player's
        /// setting, <paramref name="pan"/> −1 (left) … 1 (right). False if it was not played (silent, voice limit).</summary>
        bool Play(AudioClip clip, SfxChannel channel, float volume, float pitch, float pan, int priority);
    }
}
