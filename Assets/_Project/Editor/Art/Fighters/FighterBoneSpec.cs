using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters
{
    /// <summary>One bone of a recipe: its parent and its pivot in model space (ground at y = 0, facing +Z), before the recipe scale.</summary>
    public readonly struct FighterBoneSpec
    {
        public readonly FighterBone Bone;
        public readonly FighterBone Parent;
        public readonly Vector3 Pivot;

        public FighterBoneSpec(FighterBone bone, FighterBone parent, Vector3 pivot)
        {
            Bone = bone;
            Parent = parent;
            Pivot = pivot;
        }
    }
}
