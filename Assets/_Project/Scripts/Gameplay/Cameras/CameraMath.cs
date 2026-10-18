using Arena.Core.Defs;
using Arena.Core.Math;
using UnityEngine;

namespace Arena.Gameplay.Cameras
{
    /// <summary>
    /// Pure math of the match camera (D-016): focus is the map centre across, the target along the long axis (map Y = world Z)
    /// shifted forward towards the enemy side and clamped by the edges; team 1 looks from the other side (yaw 180°).
    /// </summary>
    public static class CameraMath
    {
        public const float RotatedYaw = 180f;

        public static Vector3 Focus(Vector3 target, ArenaBounds bounds, bool rotated, float forwardOffset, float edgeInset)
        {
            float z = target.z + (rotated ? -forwardOffset : forwardOffset);
            float lo = bounds.Min.Y + edgeInset;
            float hi = bounds.Max.Y - edgeInset;
            z = lo > hi ? (bounds.Min.Y + bounds.Max.Y) * 0.5f : MathUtil.Clamp(z, lo, hi);
            return new Vector3(bounds.Center.X, 0f, z);
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
