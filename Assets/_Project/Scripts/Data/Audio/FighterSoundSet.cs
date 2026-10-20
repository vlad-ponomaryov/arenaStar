using UnityEngine;

namespace Arena.Data.Audio
{
    /// <summary>
    /// Sounds of one fighter, matched by <see cref="FighterId"/> (brief A-TheoSfx): steps (by distance run), attack shot, hit of
    /// its projectile on an enemy, ult, taking damage, wordless voices (effort at the ult, pain, win, lose). View only.
    /// Values are written by DataSeeder (AudioDataSeeder).
    /// </summary>
    [CreateAssetMenu(fileName = "FighterSounds", menuName = "Arena/Fighter Sound Set")]
    public sealed class FighterSoundSet : ScriptableObject
    {
        /// <summary>Number of <see cref="FighterSoundKind"/> values.</summary>
        public const int KindCount = 9;

        [Tooltip("Fighter id (FighterConfig.Id) these sounds belong to.")]
        public string FighterId;

        [Header("Steps")]
        [Tooltip("One step sound per this many world units run.")]
        [Min(0.05f)] public float StepDistance;

        [Tooltip("A jump longer than this between two ticks (respawn, teleport) does not count as running, world units.")]
        [Min(0f)] public float StepTeleportDistance;

        public SfxCue Step = new SfxCue();
        public SfxCue Shot = new SfxCue();
        public SfxCue Hit = new SfxCue();
        public SfxCue Ult = new SfxCue();
        public SfxCue Hurt = new SfxCue();

        [Header("Voice (wordless)")]
        public SfxCue VoEffort = new SfxCue();
        public SfxCue VoHurt = new SfxCue();
        public SfxCue VoWin = new SfxCue();
        public SfxCue VoLose = new SfxCue();

        /// <summary>Cue of <paramref name="kind"/>.</summary>
        public SfxCue Cue(FighterSoundKind kind)
        {
            switch (kind)
            {
                case FighterSoundKind.Step: return Step;
                case FighterSoundKind.Shot: return Shot;
                case FighterSoundKind.Hit: return Hit;
                case FighterSoundKind.Ult: return Ult;
                case FighterSoundKind.Hurt: return Hurt;
                case FighterSoundKind.VoEffort: return VoEffort;
                case FighterSoundKind.VoHurt: return VoHurt;
                case FighterSoundKind.VoWin: return VoWin;
                default: return VoLose;
            }
        }
    }
}
