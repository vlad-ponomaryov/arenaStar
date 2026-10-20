using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>
    /// Offset of one bone from its rest pose, summed over the animation layers (brief A-TheoAnim §2). Position is in the bone's
    /// rest frame (final = rest + restRotation × <see cref="Position"/>: a prop slides along its own axis); rotation is in the
    /// parent's frame (final = Euler(<see cref="Euler"/>) × restRotation: two arms holding one prop, turned by the same pitch about
    /// the body's X axis, keep their grip whatever their rest poses); scale — see <see cref="ComposeScale"/>. Euler angles (degrees) instead of a quaternion so weighted layers simply add.
    /// </summary>
    public struct BonePose
    {
        public Vector3 Position;
        /// <summary>Rotation offset, degrees (X = pitch forward, Y = yaw, Z = roll to the left).</summary>
        public Vector3 Euler;
        /// <summary>Scale offset as a fraction of the rest scale (0 = rest, −1 = vanished); see <see cref="ComposeScale"/>.</summary>
        public Vector3 Scale;

        public Quaternion RotationOffset => Quaternion.Euler(Euler);

        /// <summary>
        /// Final local scale: rest × (1 + offset) per axis; an axis whose rest scale is 0 (eyelids: 0 = open) takes the offset
        /// itself, so a lid offset of 1 means "closed".
        /// </summary>
        public static Vector3 ComposeScale(Vector3 rest, Vector3 offset) =>
            new Vector3(ComposeAxis(rest.x, offset.x), ComposeAxis(rest.y, offset.y), ComposeAxis(rest.z, offset.z));

        private static float ComposeAxis(float rest, float offset) => rest == 0f ? offset : rest * (1f + offset);
    }
}
