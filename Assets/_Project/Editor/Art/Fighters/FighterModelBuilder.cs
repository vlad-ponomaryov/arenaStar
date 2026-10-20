using System;
using System.Collections.Generic;
using Arena.Editor.Art.Fighters.Recipes;
using Arena.Editor.Art.Procedural;
using Arena.Editor.Setup;
using Arena.Gameplay.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art.Fighters
{
    /// <summary>
    /// Builds a fighter model from its recipe (Art-Spec §6.2): the source FBX if any (A-Theo3D: its weights, up to
    /// <see cref="SourceSkinQuality"/>) and every group into <b>one</b> mesh (procedural parts: one bone per vertex, weight 1),
    /// one <see cref="SkinnedMeshRenderer"/> on the shared body material, the bone hierarchy, <see cref="FighterRig"/> (rest pose,
    /// styles, springs) and the usual team ring and blob shadow. Mesh → <c>Art/Meshes/Fighters/&lt;id&gt;.asset</c>, prefab →
    /// <c>Art/Prefabs/Fighters/Fighter_&lt;id&gt;.prefab</c> (GUIDs kept on re-runs). Called from <see cref="ViewAssetBuilder.BuildAll"/>.
    /// </summary>
    public static class FighterModelBuilder
    {
        public const string MeshFolder = ViewAssetBuilder.MeshesFolder + "/Fighters";
        public const string PrefabFolder = ViewAssetBuilder.PrefabsFolder + "/Fighters";
        /// <summary>GDD §8.4: 3–5 thousand triangles per fighter; Art-Spec §6.2 — at most 4000.</summary>
        public const int MaxTriangles = 4000;
        /// <summary>Budget of a held prop that comes as its own FBX (<see cref="FighterModelRecipe.SourceProp"/>), on top of <see cref="MaxTriangles"/>.</summary>
        public const int MaxPropTriangles = 1200;
        /// <summary>Most bone influences of a source-model vertex the renderer blends (the Blender export uses ≤ 3).</summary>
        public const SkinQuality SourceSkinQuality = SkinQuality.Bone4;
        private const float BoundsMargin = 1.3f;
        /// <summary>Hero LOD (D-122): every group's budget times this (sculpted groups re-fit their sculpt to it).</summary>
        public const int HeroBudgetScale = 6;
        /// <summary>Hero LOD: steps around the primitive parts times this.</summary>
        public const float HeroSegmentScale = 2.5f;
        public const string HeroSuffix = "_hero";

        /// <summary>Recipes of the fighters that have a model (the rest use the fallback capsule).</summary>
        public static FighterModelRecipe[] Recipes() => new[] { TheoRecipe.Create() };

        /// <summary>Triangle limit of the recipe's match model: the body budget plus the prop budget when the prop is its own FBX.</summary>
        public static int TriangleLimit(FighterModelRecipe recipe) => MaxTriangles + (recipe.SourceProp != null ? MaxPropTriangles : 0);

        public static string MeshPath(string fighterId, FighterModelQuality quality = FighterModelQuality.Game) =>
            MeshFolder + "/" + fighterId + Suffix(quality) + ".asset";

        public static string PrefabPath(string fighterId, FighterModelQuality quality = FighterModelQuality.Game) =>
            PrefabFolder + "/Fighter_" + fighterId + Suffix(quality) + ".prefab";

        private static string Suffix(FighterModelQuality quality) => quality == FighterModelQuality.Hero ? HeroSuffix : "";

        /// <summary>Builds every recipe; the entries for <see cref="ViewCatalog.FighterVisuals"/>.</summary>
        public static FighterVisual[] BuildAll(Material body, Material ring, Material shadow, Material ringEdge = null)
        {
            ProjectConfigurator.EnsureFolder(MeshFolder);
            ProjectConfigurator.EnsureFolder(PrefabFolder);
            FighterModelRecipe[] recipes = Recipes();
            var visuals = new FighterVisual[recipes.Length];
            for (int i = 0; i < recipes.Length; i++)
                visuals[i] = new FighterVisual
                {
                    FighterId = recipes[i].FighterId,
                    Prefab = BuildPrefab(recipes[i], body, ring, shadow, FighterModelQuality.Game, ringEdge),
                    HeroPrefab = BuildPrefab(recipes[i], body, ring, shadow, FighterModelQuality.Hero, ringEdge)
                };
            return visuals;
        }

        /// <summary>Triangles of one group after the build (budget check, log).</summary>
        public struct GroupStats
        {
            public string Name;
            public int Triangles;
            public int Budget;
            public float Step;
        }

        /// <summary>
        /// All groups of the recipe into one buffer (scaled model space, bone index = <see cref="FighterBone"/>); pure, no assets.
        /// </summary>
        public static MeshBuilderBuffer BuildGeometry(FighterModelRecipe recipe, List<GroupStats> stats = null,
            FighterModelQuality quality = FighterModelQuality.Game, List<BoneWeight> weights = null, List<Vector2> uvs = null)
        {
            Validate(recipe);
            ApplySourcePivots(recipe);
            var buffer = new MeshBuilderBuffer();
            var sourceWeights = new List<BoneWeight>();
            var sourceUvs = new List<Vector2>();
            if (recipe.SourceModel != null)
            {
                string model = quality == FighterModelQuality.Hero && recipe.SourceModelHero != null ? recipe.SourceModelHero : recipe.SourceModel;
                FighterModelSource.AppendModel(model, buffer, sourceWeights, sourceUvs);
                stats?.Add(new GroupStats
                {
                    Name = "model", Triangles = buffer.TriangleCount,
                    Budget = quality == FighterModelQuality.Hero ? MaxTriangles * HeroBudgetScale : MaxTriangles
                });
            }
            if (recipe.SourceProp != null)
            {
                int before = buffer.TriangleCount;
                FighterModelSource.AppendProp(recipe.SourceProp, recipe.Bones[(int)FighterBone.Prop].Pivot, FighterBone.Prop, buffer, sourceWeights,
                    sourceUvs);
                stats?.Add(new GroupStats { Name = "prop", Triangles = buffer.TriangleCount - before, Budget = MaxPropTriangles });
            }
            foreach (FighterGroupSpec group in recipe.Groups)
            {
                int bone = (int)group.Bone;
                int before = buffer.TriangleCount;
                float step = 0f;
                bool hero = quality == FighterModelQuality.Hero;
                int budget = hero ? group.Budget * HeroBudgetScale : group.Budget;
                if (group.Mode == FighterGroupMode.Sculpt)
                {
                    var sculpt = new SdfSculpt(group.Blend);
                    foreach (FighterPartSpec part in group.Parts) sculpt.Add(part.ToSdf());
                    sculpt.Build(buffer, bone, budget);
                    step = sculpt.LastStep;
                }
                else
                {
                    foreach (FighterPartSpec part in group.Parts)
                    {
                        if (part.Op != SdfOp.Union) continue;
                        int segments = part.Segments > 0 ? part.Segments : group.Segments;
                        if (hero) segments = Mathf.RoundToInt(segments * HeroSegmentScale);
                        ProcMesh.Add(buffer, part.Shape, part.Size, part.Radius, part.Matrix(), segments, part.VertexColor(), bone);
                    }
                }
                stats?.Add(new GroupStats { Name = group.Name, Triangles = buffer.TriangleCount - before, Budget = budget, Step = step });
            }
            if (weights != null)
            {
                weights.Clear();
                weights.AddRange(sourceWeights);
                for (int i = sourceWeights.Count; i < buffer.VertexCount; i++)
                    weights.Add(new BoneWeight { boneIndex0 = buffer.Bones[i], weight0 = 1f });
            }
            if (uvs != null)
            {
                uvs.Clear();
                uvs.AddRange(sourceUvs);
                for (int i = sourceUvs.Count; i < buffer.VertexCount; i++) uvs.Add(FighterModelSource.NoUv);
            }
            if (!Mathf.Approximately(recipe.Scale, 1f))
                for (int i = 0; i < buffer.Positions.Count; i++) buffer.Positions[i] *= recipe.Scale;
            return buffer;
        }

        /// <summary>Replaces the recipe's pivots with the heads of the bones its source model has (idempotent).</summary>
        public static void ApplySourcePivots(FighterModelRecipe recipe)
        {
            if (recipe.SourceModel == null) return;
            Vector3?[] pivots = FighterModelSource.BonePivots(recipe.SourceModel);
            for (int i = 0; i < recipe.Bones.Length; i++)
                if (pivots[i].HasValue) recipe.Bones[i] = new FighterBoneSpec(recipe.Bones[i].Bone, recipe.Bones[i].Parent, pivots[i].Value);
        }

        /// <summary>Name of the empty transform at the end of the prop's barrel (<see cref="FighterRig.Muzzle"/>).</summary>
        public const string MuzzleName = "Muzzle";

        /// <summary>
        /// Turns and shifts the bones to the recipe's rest pose (<see cref="FighterModelRecipe.RestPose"/>) after the bind poses are
        /// taken, parents first, so the skinned mesh shows the pose and <see cref="FighterRig"/> stores it as the rest.
        /// </summary>
        public static void ApplyRestPose(FighterModelRecipe recipe, Transform root, Transform[] bones)
        {
            if (recipe.RestPose == null || recipe.RestPose.Length == 0) return;
            var poses = (FighterBonePose[])recipe.RestPose.Clone();
            Array.Sort(poses, (a, b) => ((int)a.Bone).CompareTo((int)b.Bone));
            for (int i = 0; i < poses.Length; i++)
            {
                if (i > 0 && poses[i].Bone == poses[i - 1].Bone)
                    throw new ArgumentException("FighterModelRecipe " + recipe.FighterId + ": two rest poses of " + poses[i].Bone);
                Transform bone = bones[(int)poses[i].Bone];
                bone.rotation = root.rotation * Quaternion.Euler(poses[i].Euler);
                bone.position += root.TransformVector(poses[i].Offset * recipe.Scale);
            }
        }

        /// <summary>Rest pivot of a bone in scaled model space.</summary>
        public static Vector3 Pivot(FighterModelRecipe recipe, FighterBone bone) => recipe.Bones[(int)bone].Pivot * recipe.Scale;

        public static FighterView BuildPrefab(FighterModelRecipe recipe, Material body, Material ring, Material shadow,
            FighterModelQuality quality = FighterModelQuality.Game, Material ringEdge = null)
        {
            var stats = new List<GroupStats>();
            var weights = new List<BoneWeight>();
            var uvs = new List<Vector2>();
            MeshBuilderBuffer geometry = BuildGeometry(recipe, stats, quality, weights, uvs);
            Mesh mesh = SaveMesh(recipe, geometry, weights, recipe.SourceModel != null ? uvs : null, quality);
            LogStats(recipe, stats, geometry, quality);

            var root = new GameObject("Fighter_" + recipe.FighterId + Suffix(quality));
            try
            {
                (Renderer ringRenderer, Renderer shadowRenderer) = ViewAssetBuilder.AddRingAndShadow(root.transform, ring, shadow);
                if (ringEdge != null) ViewAssetBuilder.AddRingEdge(root.transform, ringEdge);

                int boneCount = recipe.Bones.Length;
                var bones = new Transform[boneCount];
                for (int i = 0; i < boneCount; i++)
                {
                    FighterBoneSpec spec = recipe.Bones[i];
                    var go = new GameObject(spec.Bone.ToString());
                    Transform parent = spec.Bone == spec.Parent ? root.transform : bones[(int)spec.Parent];
                    go.transform.SetParent(parent, false);
                    Vector3 parentPivot = spec.Bone == spec.Parent ? Vector3.zero : Pivot(recipe, spec.Parent);
                    go.transform.localPosition = Pivot(recipe, spec.Bone) - parentPivot;
                    bones[i] = go.transform;
                }

                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                var smr = model.AddComponent<SkinnedMeshRenderer>();
                var bindposes = new Matrix4x4[boneCount];
                for (int i = 0; i < boneCount; i++) bindposes[i] = bones[i].worldToLocalMatrix * model.transform.localToWorldMatrix;
                mesh.bindposes = bindposes;
                EditorUtility.SetDirty(mesh);
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = bones[(int)FighterBone.Root];
                smr.quality = recipe.SourceModel != null ? SourceSkinQuality : SkinQuality.Bone1;
                smr.updateWhenOffscreen = false;
                Bounds local = mesh.bounds; // root bone sits at the model origin
                local.extents *= BoundsMargin;
                smr.localBounds = local;
                smr.sharedMaterial = body;
                smr.shadowCastingMode = ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.lightProbeUsage = LightProbeUsage.Off;
                smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                smr.skinnedMotionVectors = false;
                if (recipe.BaseMap != null)
                {
                    var map = AssetDatabase.LoadAssetAtPath<Texture2D>(recipe.BaseMap);
                    if (map == null) throw new InvalidOperationException("FighterModelBuilder: no texture at " + recipe.BaseMap);
                    model.AddComponent<FighterSkin>().BaseMap = map;
                }

                // Eyelids are modelled closed and hidden at rest (scale 0 = open eyes).
                bones[(int)FighterBone.LidL].localScale = Vector3.zero;
                bones[(int)FighterBone.LidR].localScale = Vector3.zero;
                ApplyRestPose(recipe, root.transform, bones);
                if (recipe.RestPose != null && recipe.RestPose.Length > 0)
                {
                    // The posed prop reaches further than the bind pose: bounds over both.
                    var posed = new Mesh();
                    smr.BakeMesh(posed, true);
                    Bounds both = mesh.bounds;
                    both.Encapsulate(posed.bounds);
                    Object.DestroyImmediate(posed);
                    both.extents *= BoundsMargin;
                    smr.localBounds = both;
                }

                var rig = root.AddComponent<FighterRig>();
                rig.Bones = bones;
                if (recipe.SourceProp != null)
                {
                    var muzzle = new GameObject(MuzzleName).transform;
                    muzzle.SetParent(bones[(int)FighterBone.Prop], false);
                    muzzle.localPosition = recipe.PropMuzzle * recipe.Scale;
                    rig.Muzzle = muzzle;
                }
                rig.RestPositions = new Vector3[boneCount];
                rig.RestRotations = new Quaternion[boneCount];
                rig.RestScales = new Vector3[boneCount];
                for (int i = 0; i < boneCount; i++)
                {
                    rig.RestPositions[i] = bones[i].localPosition;
                    rig.RestRotations[i] = bones[i].localRotation;
                    rig.RestScales[i] = bones[i].localScale;
                }
                rig.TopHeight = mesh.bounds.max.y;
                rig.AttackStyle = recipe.AttackStyle;
                rig.UltStyle = recipe.UltStyle;
                rig.TrickStyle = recipe.TrickStyle;
                rig.Springs = recipe.Springs ?? Array.Empty<FighterSpring>();

                var view = root.AddComponent<FighterView>();
                var so = new SerializedObject(view);
                so.FindProperty("_body").objectReferenceValue = model.transform;
                so.FindProperty("_ring").objectReferenceValue = ringRenderer;
                so.FindProperty("_shadow").objectReferenceValue = shadowRenderer;
                so.FindProperty("_rig").objectReferenceValue = rig;
                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(recipe.FighterId, quality), out bool ok);
                if (!ok || prefab == null)
                    throw new InvalidOperationException("FighterModelBuilder: failed to save " + PrefabPath(recipe.FighterId, quality));
                return prefab.GetComponent<FighterView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Mesh SaveMesh(FighterModelRecipe recipe, MeshBuilderBuffer geometry, List<BoneWeight> weights, List<Vector2> uvs,
            FighterModelQuality quality)
        {
            string path = MeshPath(recipe.FighterId, quality);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            geometry.WriteTo(mesh);
            mesh.name = recipe.FighterId + Suffix(quality);
            mesh.boneWeights = weights.ToArray();
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.UploadMeshData(false);
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void Validate(FighterModelRecipe recipe)
        {
            if (string.IsNullOrEmpty(recipe.FighterId)) throw new ArgumentException("FighterModelRecipe: no fighter id");
            int count = Enum.GetValues(typeof(FighterBone)).Length;
            if (recipe.Bones == null || recipe.Bones.Length != count)
                throw new ArgumentException("FighterModelRecipe " + recipe.FighterId + ": needs all " + count + " bones");
            for (int i = 0; i < count; i++)
            {
                FighterBoneSpec b = recipe.Bones[i];
                if ((int)b.Bone != i) throw new ArgumentException("FighterModelRecipe " + recipe.FighterId + ": bone " + i + " is " + b.Bone);
                if (b.Bone != b.Parent && (int)b.Parent >= i)
                    throw new ArgumentException("FighterModelRecipe " + recipe.FighterId + ": parent of " + b.Bone + " must come first");
            }
        }

        private static void LogStats(FighterModelRecipe recipe, List<GroupStats> stats, MeshBuilderBuffer geometry, FighterModelQuality quality)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("FighterModelBuilder: ").Append(recipe.FighterId).Append(Suffix(quality)).Append(' ').Append(geometry.TriangleCount).Append(" tris, ")
                .Append(geometry.VertexCount).Append(" verts;");
            foreach (GroupStats s in stats)
            {
                sb.Append(' ').Append(s.Name).Append(' ').Append(s.Triangles).Append('/').Append(s.Budget);
                if (s.Step > 0f) sb.Append(" (step ").Append(s.Step.ToString("0.000")).Append(')');
                sb.Append(';');
            }
            Debug.Log(sb.ToString());
        }
    }
}
