using Arena.Core.Math;
using UnityEngine;

namespace Arena.Gameplay.Cameras
{
    /// <summary>
    /// Pure math of the match camera (D-016): focus is the map centre across, the target along the long axis (map Y = world Z)
    /// shifted forward towards the enemy side and clamped by the edges; team 1 looks from the other side (yaw 180°).
    /// Free-for-all maps (Last Beast, S3.14) are followed along both axes (<see cref="FocusFree"/>), never rotated.
    /// </summary>
    public static class CameraMath
    {
        public const float RotatedYaw = 180f;

        /// <param name="mapSize">Map width (world X) and height (world Z) in cells; the map starts at the origin (D-009).</param>
        public static Vector3 Focus(Vector3 target, Vector2 mapSize, bool rotated, float forwardOffset, float edgeInset)
        {
            float z = target.z + (rotated ? -forwardOffset : forwardOffset);
            float lo = edgeInset;
            float hi = mapSize.y - edgeInset;
            z = lo > hi ? mapSize.y * 0.5f : MathUtil.Clamp(z, lo, hi);
            return new Vector3(mapSize.x * 0.5f, 0f, z);
        }

        /// <summary>
        /// Focus that follows the target along both axes (a big free-for-all map): world X clamped <paramref name="sideInset"/>
        /// from the side edges, world Z shifted by <paramref name="forwardOffset"/> and clamped <paramref name="edgeInset"/> from
        /// the near and far edges; an axis shorter than twice its inset stays at the map centre.
        /// </summary>
        public static Vector3 FocusFree(Vector3 target, Vector2 mapSize, float forwardOffset, float edgeInset, float sideInset)
        {
            float x = sideInset * 2f > mapSize.x ? mapSize.x * 0.5f : MathUtil.Clamp(target.x, sideInset, mapSize.x - sideInset);
            float z = edgeInset * 2f > mapSize.y ? mapSize.y * 0.5f : MathUtil.Clamp(target.z + forwardOffset, edgeInset, mapSize.y - edgeInset);
            return new Vector3(x, 0f, z);
        }

        public static Quaternion Rotation(float pitchDegrees, bool rotated) =>
            Quaternion.Euler(pitchDegrees, rotated ? RotatedYaw : 0f, 0f);

        /// <summary>Camera position looking at <paramref name="focus"/> from <paramref name="distance"/> along the view direction.</summary>
        public static Vector3 Position(Vector3 focus, Quaternion rotation, float distance) =>
            focus - rotation * Vector3.forward * distance;

        /// <summary>Frame-rate independent exponential smoothing factor; sharpness ≤ 0 means no smoothing (1).</summary>
        public static float DampFactor(float sharpness, float deltaTime) =>
            sharpness <= 0f ? 1f : 1f - Mathf.Exp(-sharpness * deltaTime);
    }
}
