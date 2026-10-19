using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Turns the match's directional light with the camera (Art-Spec §6.3): pitch from <see cref="ViewCatalog.LightPitch"/>, yaw =
    /// camera yaw + <see cref="ViewCatalog.LightYawFromCamera"/>. Team 1 looks from the other side (camera yaw 180°, D-016); a
    /// world-fixed light would leave its view of every fighter in the toon shade. Also turns the soft wall shadows on the ground
    /// (A.3b, D-125): they fall by <see cref="MapMeshStyle.ShadowFall"/> in the camera frame (to the screen right and towards the
    /// viewer), so both teams see them beside the walls, not hidden behind; and the shader-side grade of the environment
    /// (<see cref="MapMeshStyle.Grade"/>, A.3e). Applied by <c>MatchRunner</c> at match start and by the
    /// screenshot stand in the editor.
    /// </summary>
    public static class MatchLightRig
    {
        private static readonly int ShadowReachId = Shader.PropertyToID("_ArenaShadowReach");
        private static readonly int GradeId = Shader.PropertyToID("_ArenaGrade");

        public static Quaternion Rotation(float cameraYawDegrees, float pitchDegrees, float yawFromCameraDegrees) =>
            Quaternion.Euler(pitchDegrees, cameraYawDegrees + yawFromCameraDegrees, 0f);

        /// <summary>
        /// World XZ offset from a ground point towards its shadow caster at full caster height: minus the fall of the shadow
        /// (<paramref name="fall"/>: x = screen right, y = towards the camera, per unit height) times <paramref name="casterHeight"/>.
        /// </summary>
        public static Vector2 ShadowReach(float cameraYawDegrees, Vector2 fall, float casterHeight)
        {
            float yaw = cameraYawDegrees * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
            Vector2 fallWorld = right * fall.x - forward * fall.y;
            return -fallWorld * casterHeight;
        }

        /// <summary>No-op without a catalog; the light is turned only if there is one.</summary>
        public static void Apply(Light light, float cameraYawDegrees, ViewCatalog catalog)
        {
            if (catalog == null) return;
            Vector2 reach = ShadowReach(cameraYawDegrees, catalog.MapStyle.ShadowFall, catalog.WallHeight);
            Shader.SetGlobalVector(ShadowReachId, new Vector4(reach.x, reach.y, 0f, 0f));
            Vector3 grade = catalog.MapStyle.Grade;
            Shader.SetGlobalVector(GradeId, new Vector4(grade.x, grade.y, grade.z, 0f));
            if (light == null) return;
            light.transform.rotation = Rotation(cameraYawDegrees, catalog.LightPitch, catalog.LightYawFromCamera);
        }
    }
}
