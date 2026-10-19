using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Turns the match's directional light with the camera (Art-Spec §6.3): pitch from <see cref="ViewCatalog.LightPitch"/>, yaw =
    /// camera yaw + <see cref="ViewCatalog.LightYawFromCamera"/>. Team 1 looks from the other side (camera yaw 180°, D-016); a
    /// world-fixed light would leave its view of every fighter in the toon shade. Applied by <c>MatchRunner</c> at match start and
    /// by the screenshot stand in the editor.
    /// </summary>
    public static class MatchLightRig
    {
        public static Quaternion Rotation(float cameraYawDegrees, float pitchDegrees, float yawFromCameraDegrees) =>
            Quaternion.Euler(pitchDegrees, cameraYawDegrees + yawFromCameraDegrees, 0f);

        /// <summary>No-op without a light or catalog.</summary>
        public static void Apply(Light light, float cameraYawDegrees, ViewCatalog catalog)
        {
            if (light == null || catalog == null) return;
            light.transform.rotation = Rotation(cameraYawDegrees, catalog.LightPitch, catalog.LightYawFromCamera);
        }
    }
}
