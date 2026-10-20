using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data;
using Arena.Data.Audio;
using Arena.Gameplay.Feel;
using Arena.Gameplay.Settings;
using UnityEngine;

namespace Arena.Gameplay.Audio
{
    /// <summary>
    /// Audio of one match (brief A-TheoSfx): the <see cref="SfxPlayer"/> pool (volume from <see cref="GameSettings.SoundPercent"/>),
    /// the procedural sounds of D-028 played through it (fighters without a sound set, the "ult ready" signal) and
    /// <see cref="FighterSfx"/> for fighters with a <see cref="FighterSoundSet"/>. Created by the match runner; without an
    /// <see cref="AudioConfig"/> in the database the match is silent.
    /// </summary>
    public sealed class MatchAudio
    {
        public SfxPlayer Player { get; }
        public ProceduralSounds Procedural { get; }
        public FighterSfx Fighters { get; }

        private MatchAudio(SfxPlayer player, ProceduralSounds procedural, FighterSfx fighters)
        {
            Player = player;
            Procedural = procedural;
            Fighters = fighters;
        }

        /// <param name="viewRotated">The camera looks at the map from the other side (team 1: the screen's right is −X).</param>
        public static MatchAudio Create(Transform parent, GameDatabase database, MatchDefs defs, MatchSetup setup, GameSettings settings,
            int localSlot, int localTeam, bool viewRotated)
        {
            AudioConfig config = database.Audio;
            float volume = settings != null ? settings.SoundVolume : 1f;
            if (config == null)
            {
                Debug.LogWarning("MatchAudio: GameDatabase.Audio is not set — the match is silent");
                return new MatchAudio(null, new ProceduralSounds(null, database.Feel, 0), null);
            }

            var player = new SfxPlayer(parent, config, volume);
            var procedural = new ProceduralSounds(player, database.Feel, config.ProceduralPriority);
            var sets = new FighterSoundSet[setup.SlotCount];
            for (int i = 0; i < sets.Length; i++)
                sets[i] = config.FindFighterSounds(defs.Fighters[setup.Slots[i].FighterIndex].Id);
            var fighters = new FighterSfx(player, config, sets, localSlot, localTeam, setup.Seed);
            fighters.SetScreenRight(viewRotated ? -Vec2.Right : Vec2.Right);
            return new MatchAudio(player, procedural, fighters);
        }

        /// <summary>After every tick.</summary>
        public void Process(SimEventBuffer events, MatchState state, float now) => Fighters?.Process(events, state, now);
    }
}
