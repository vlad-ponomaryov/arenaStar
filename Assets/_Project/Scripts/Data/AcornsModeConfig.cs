using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// The Acorns mode (GDD §5.4, D-025) in designer units: seconds and cells. Converted to <c>AcornsModeDef</c> by
    /// ConfigConverter. Values are written by DataSeeder (D-010).
    /// </summary>
    [CreateAssetMenu(fileName = "Acorns", menuName = "Arena/Acorns Mode")]
    public sealed class AcornsModeConfig : ScriptableObject
    {
        [Header("Acorns from the hollow")]
        [Tooltip("A normal acorn falls every … seconds.")]
        [Min(0.1f)] public float SpawnIntervalSeconds;

        [Tooltip("A golden acorn falls in addition every … seconds.")]
        [Min(0.1f)] public float GoldenIntervalSeconds;

        [Tooltip("A golden acorn counts as … acorns.")]
        [Min(1)] public int GoldenValue;

        [Tooltip("New acorns land within this radius (cells) of the hollow.")]
        [Min(0f)] public float SpawnScatterRadius;

        [Header("Win")]
        [Min(1)] public int TargetAcorns;

        [Tooltip("Hold countdown, seconds.")]
        [Min(0.1f)] public float HoldSeconds;

        [Tooltip("Match length, seconds (2:30 = 150).")]
        [Min(1f)] public float DurationSeconds;

        [Header("Pickup and drop")]
        [Tooltip("Acorn radius, cells: picked up when the body touches it.")]
        [Min(0f)] public float PickupRadius;

        [Tooltip("Acorns of a defeated fighter land within this radius, cells.")]
        [Min(0f)] public float DropScatterRadius;

        [Tooltip("Dropped acorns cannot be picked up for … seconds.")]
        [Min(0f)] public float PickupDelaySeconds;

        [Tooltip("Random landing points tried before falling back to the centre (walls, water).")]
        [Min(1)] public int ScatterAttempts;

        [Header("MVP score weights")]
        public float MvpKillWeight;
        public float MvpDamageWeight;
        public float MvpHealWeight;
        public float MvpAcornWeight;
        public float MvpDeathWeight;
    }
}
