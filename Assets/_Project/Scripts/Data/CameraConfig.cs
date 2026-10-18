using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Match camera (GDD §5.5, D-016): fixed pitch, centred across the map, follows the local fighter along the long axis,
    /// clamped by the map edges; rotated 180° for team 1. Values are written by DataSeeder (D-010).
    /// </summary>
    [CreateAssetMenu(fileName = "CameraConfig", menuName = "Arena/Camera Config")]
    public sealed class CameraConfig : ScriptableObject
    {
        [Tooltip("Angle between the view direction and the ground, degrees (~55).")]
        [Range(10f, 90f)] public float PitchDegrees;

        [Tooltip("Distance from the focus point on the ground to the camera, cells.")]
        [Min(1f)] public float Distance;

        [Tooltip("Vertical field of view, degrees.")]
        [Range(10f, 120f)] public float FieldOfView;

        [Tooltip("Focus is shifted this many cells ahead of the fighter (towards the enemy side), so more of the arena ahead is visible.")]
        public float ForwardOffset;

        [Tooltip("Closest distance, cells, of the focus point to the near and far map edges along the long axis.")]
        [Min(0f)] public float EdgeInset;

        [Tooltip("Follow smoothing: higher = snappier (1/s). 0 = no smoothing.")]
        [Min(0f)] public float FollowSharpness;
    }
}
