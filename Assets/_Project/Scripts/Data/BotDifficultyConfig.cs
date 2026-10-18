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
    }
}
