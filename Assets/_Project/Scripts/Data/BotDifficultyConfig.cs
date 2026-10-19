using Arena.Bots;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// One bot skill level (GDD §7 table) in designer units. Converted to <c>Arena.Bots.Defs.BotDifficultyDef</c> by
    /// ConfigConverter. Values are written by DataSeeder (D-010).
    /// </summary>
    [CreateAssetMenu(fileName = "BotDifficulty", menuName = "Arena/Bot Difficulty")]
    public sealed class BotDifficultyConfig : ScriptableObject
    {
        public BotDifficulty Difficulty;

        [Tooltip("Aim spread: each shot deviates by a random angle within ± this many degrees.")]
        [Range(0f, 180f)] public float AimSpreadDegrees;

        [Tooltip("Seconds from first seeing an enemy until the bot may shoot at it.")]
        [Min(0f)] public float ReactionSeconds;

        [Header("Ult, trick, mistakes (chances per decision)")]
        [Tooltip("Chance to use a charged ult when it is worth it (2+ enemies in its zone or it finishes the target).")]
        [Range(0f, 1f)] public float UltUseChance;

        [Tooltip("Chance to waste a charged ult on a target in its zone when it is not worth it.")]
        [Range(0f, 1f)] public float UltMisuseChance;

        [Tooltip("Chance to use the trick when the fighter's bot rule fits.")]
        [Range(0f, 1f)] public float TrickUseChance;

        [Tooltip("Chance to use the trick off the mark while an enemy is perceived.")]
        [Range(0f, 1f)] public float TrickMisuseChance;

        [Tooltip("Chance to shoot into a random bush in range with nobody seen there.")]
        [Range(0f, 1f)] public float EmptyBushShotChance;

        [Tooltip("The retreat health threshold is shifted per life by a random amount within ± this.")]
        [Range(0f, 1f)] public float RetreatThresholdJitter;

        [Header("Skill (F1: Normal)")]
        [Tooltip("Lead on moving targets for projectile attacks and ults: fraction of the target velocity × flight time.")]
        [Range(0f, 1f)] public float LeadFraction;

        [Tooltip("Focus the weak: target = lowest 'distance + this × health fraction' (cells); 0 — the nearest enemy.")]
        [Min(0f)] public float FocusHealthWeightCells;

        [Tooltip("Chance per decision to sidestep enemy projectiles until the next decision.")]
        [Range(0f, 1f)] public float DodgeChance;

        [Tooltip("A projectile that reaches the bot within this many seconds is a threat.")]
        [Min(0f)] public float DodgeLookaheadSeconds;

        [Tooltip("…when it passes closer than its radius + the bot radius + this margin (cells).")]
        [Min(0f)] public float DodgeMarginCells;

        [Tooltip("Seconds between two shots of the bot at least (D-131: a weak bot shoots less often); the shared bot behaviour minimum applies when larger.")]
        [Min(0f)] public float ShotIntervalSeconds;
    }
}
