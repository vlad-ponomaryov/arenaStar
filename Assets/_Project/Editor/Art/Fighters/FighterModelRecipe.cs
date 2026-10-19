using Arena.Gameplay.View;

namespace Arena.Editor.Art.Fighters
{
    /// <summary>
    /// Everything that makes one fighter's model (Art-Spec §6.2, D-108): bones, groups of parts, animation styles and springs, scale.
    /// Editor-only data, one file per fighter in <c>Recipes/</c>; a new fighter is a new recipe, not a new class of code.
    /// Model space: ground at y = 0, facing +Z, +X = the fighter's right; height ≈ 1.5 before <see cref="Scale"/>.
    /// </summary>
    public sealed class FighterModelRecipe
    {
        /// <summary><c>FighterConfig.Id</c> of the fighter this model is for.</summary>
        public string FighterId;
        public float Scale = 1f;
        /// <summary>All bones in <see cref="FighterBone"/> order (index = enum value).</summary>
        public FighterBoneSpec[] Bones;
        public FighterGroupSpec[] Groups;
        public FighterAttackStyle AttackStyle;
        public FighterAttackStyle UltStyle;
        public FighterTrickStyle TrickStyle;
        public FighterSpring[] Springs;
        /// <summary>
        /// Skinned FBX the body comes from (A-Theo3D), or null for a fully procedural model. Its mesh, vertex colours (sRGB →
        /// linear, alpha = gloss) and bone weights are copied (bones matched by name to <see cref="FighterBone"/>) and the heads of
        /// its bones replace the pivots in <see cref="Bones"/>; <see cref="Groups"/> are added on top. Used for the hero model too.
        /// </summary>
        public string SourceModel;
        /// <summary>Unskinned FBX of the held prop (origin = the grip), weighted to <see cref="FighterBone.Prop"/> at its pivot; or null.</summary>
        public string SourceProp;
    }
}
