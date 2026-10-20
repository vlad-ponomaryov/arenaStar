using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>
    /// Bone offsets of one fighter for one frame, indexed by <see cref="FighterBone"/>. Created once per fighter; the layers of
    /// <see cref="PuppetPose"/> add into it with a weight.
    /// </summary>
    public sealed class PoseBuffer
    {
        public const int BoneCount = (int)FighterBone.LidR + 1;

        public readonly BonePose[] Bones = new BonePose[BoneCount];

        public BonePose this[FighterBone bone] => Bones[(int)bone];

        public void Clear()
        {
            for (int i = 0; i < Bones.Length; i++) Bones[i] = default;
        }

        public void CopyFrom(PoseBuffer other)
        {
            for (int i = 0; i < Bones.Length; i++) Bones[i] = other.Bones[i];
        }

        public void AddPosition(FighterBone bone, Vector3 offset, float weight) => Bones[(int)bone].Position += offset * weight;

        public void AddEuler(FighterBone bone, Vector3 degrees, float weight) => Bones[(int)bone].Euler += degrees * weight;

        public void AddScale(FighterBone bone, Vector3 offset, float weight) => Bones[(int)bone].Scale += offset * weight;

        /// <summary>Uniform scale offset of at least <paramref name="value"/> (eyelid closure: the strongest reason wins).</summary>
        public void MaxUniformScale(FighterBone bone, float value)
        {
            ref BonePose p = ref Bones[(int)bone];
            p.Scale = new Vector3(Mathf.Max(p.Scale.x, value), Mathf.Max(p.Scale.y, value), Mathf.Max(p.Scale.z, value));
        }
    }
}
