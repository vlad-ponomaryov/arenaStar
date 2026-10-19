using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Bones of a procedural fighter model and their rest pose, plus the per-fighter animation data (styles, springs) that the
    /// model builder writes from the Editor-only recipe (Art-Spec §3.2, D-109). Pure data: the animator reads it and moves the bones.
    /// </summary>
    public sealed class FighterRig : MonoBehaviour
    {
        /// <summary>Indexed by <see cref="FighterBone"/>; also the bone array of the skinned mesh.</summary>
        public Transform[] Bones;
        public Vector3[] RestPositions;
        public Quaternion[] RestRotations;
        public Vector3[] RestScales;

        /// <summary>Top of the model (ear tips) above the ground, units: where the health bar goes.</summary>
        public float TopHeight;

        public FighterAttackStyle AttackStyle;
        public FighterAttackStyle UltStyle;
        public FighterTrickStyle TrickStyle;
        public FighterSpring[] Springs;

        public Transform Bone(FighterBone bone) => Bones[(int)bone];

        /// <summary>Puts every bone back to its rest pose (eyelids open, prop shown).</summary>
        public void ResetPose()
        {
            for (int i = 0; i < Bones.Length; i++)
            {
                Transform t = Bones[i];
                if (t == null) continue;
                t.localPosition = RestPositions[i];
                t.localRotation = RestRotations[i];
                t.localScale = RestScales[i];
            }
        }
    }
}
