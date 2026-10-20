using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters
{
    /// <summary>
    /// Rest pose of one bone of a recipe on top of the bind pose of the model (A-TheoPolish: the arms bring the blaster forward):
    /// the bone's rotation in model space (Euler degrees, ground at y = 0, facing +Z) and a shift of its pivot (model space, before
    /// the recipe scale). The animator adds its offsets to this rest pose (<see cref="FighterRig.RestRotations"/>).
    /// </summary>
    public readonly struct FighterBonePose
    {
        public readonly FighterBone Bone;
        public readonly Vector3 Euler;
        public readonly Vector3 Offset;

        public FighterBonePose(FighterBone bone, Vector3 euler, Vector3 offset = default)
        {
            Bone = bone;
            Euler = euler;
            Offset = offset;
        }
    }
}
