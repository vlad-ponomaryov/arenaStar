using System;
using System.Collections.Generic;
using Arena.Editor.Art.Procedural;
using Arena.Gameplay.View;
using UnityEditor;
using UnityEngine;

namespace Arena.Editor.Art.Fighters
{
    /// <summary>
    /// Reads the FBX sources of a recipe (A-Theo3D, <see cref="FighterModelRecipe.SourceModel"/> / <see cref="FighterModelRecipe.SourceProp"/>)
    /// into the builder's buffers, in model space before the recipe scale: the rest pose of the skinned body (bones matched by name
    /// to <see cref="FighterBone"/>, up to 4 weights each, normalised), vertex colours sRGB → linear with alpha (gloss) kept, and
    /// the heads of the source bones as pivots. The FBX import settings are <see cref="FighterModelImporter"/>'s.
    /// </summary>
    public static class FighterModelSource
    {
        /// <summary>
        /// Blender front (−Y) exported with <c>axis_forward=-Z</c> lands facing −Z in Unity (right hand on −X); the recipes face +Z
        /// with the right hand on +X, so every source is turned half a circle about the vertical. Applied to the world matrices
        /// of the imported objects: the root's own transform is part of the model (an FBX of one object is that object).
        /// </summary>
        public static readonly Matrix4x4 SourceToModel = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));

        /// <summary>Heads of the source bones in model space (index = <see cref="FighterBone"/>); null where the FBX has no such bone.</summary>
        public static Vector3?[] BonePivots(string path)
        {
            GameObject root = Load(path);
            var pivots = new Vector3?[Enum.GetValues(typeof(FighterBone)).Length];
            Matrix4x4 toModel = SourceToModel;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform || !TryBone(t.name, out FighterBone bone)) continue;
                if (pivots[(int)bone].HasValue) throw new InvalidOperationException("FighterModelSource: " + path + " has two bones " + bone);
                pivots[(int)bone] = toModel.MultiplyPoint3x4(t.position);
            }
            return pivots;
        }

        /// <summary>Names of the bones the skinned body is weighted to (for the tests: each must be a <see cref="FighterBone"/>).</summary>
        public static string[] SkinBoneNames(string path)
        {
            SkinnedMeshRenderer smr = Skin(path);
            var names = new string[smr.bones.Length];
            for (int i = 0; i < names.Length; i++) names[i] = smr.bones[i] != null ? smr.bones[i].name : "";
            return names;
        }

        /// <summary>Appends the skinned body at rest; <paramref name="weights"/> gets one entry per added vertex.</summary>
        public static void AppendModel(string path, MeshBuilderBuffer buffer, List<BoneWeight> weights)
        {
            GameObject root = Load(path);
            SkinnedMeshRenderer smr = Skin(path);
            Mesh mesh = smr.sharedMesh;
            Transform[] bones = smr.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            Matrix4x4 toModel = SourceToModel;
            var skin = new Matrix4x4[bones.Length];
            var boneIds = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || !TryBone(bones[i].name, out FighterBone bone))
                    throw new InvalidOperationException("FighterModelSource: " + path + " bone " + (bones[i] != null ? bones[i].name : "null") +
                                                        " is not a FighterBone");
                boneIds[i] = (int)bone;
                skin[i] = toModel * bones[i].localToWorldMatrix * bindposes[i];
            }

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Color[] colors = mesh.colors;
            BoneWeight[] source = mesh.boneWeights;
            if (colors.Length != positions.Length) throw new InvalidOperationException("FighterModelSource: " + path + " has no vertex colours");
            if (source.Length != positions.Length) throw new InvalidOperationException("FighterModelSource: " + path + " has no bone weights");
            int offset = buffer.VertexCount;
            for (int v = 0; v < positions.Length; v++)
            {
                BoneWeight w = source[v];
                float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                if (sum <= 1e-6f) throw new InvalidOperationException("FighterModelSource: " + path + " vertex " + v + " has no weight");
                Matrix4x4 m = Blend(skin, w, sum);
                var mapped = new BoneWeight
                {
                    boneIndex0 = boneIds[w.boneIndex0], weight0 = w.weight0 / sum,
                    boneIndex1 = w.weight1 > 0f ? boneIds[w.boneIndex1] : 0, weight1 = w.weight1 / sum,
                    boneIndex2 = w.weight2 > 0f ? boneIds[w.boneIndex2] : 0, weight2 = w.weight2 / sum,
                    boneIndex3 = w.weight3 > 0f ? boneIds[w.boneIndex3] : 0, weight3 = w.weight3 / sum
                };
                buffer.AddVertex(m.MultiplyPoint3x4(positions[v]), m.MultiplyVector(normals[v]), Linear(colors[v]), mapped.boneIndex0);
                weights.Add(mapped);
            }
            AppendTriangles(buffer, mesh.triangles, offset);
        }

        /// <summary>Appends the unskinned prop with its origin at <paramref name="pivot"/>, all on <paramref name="bone"/> (weight 1).</summary>
        public static void AppendProp(string path, Vector3 pivot, FighterBone bone, MeshBuilderBuffer buffer, List<BoneWeight> weights)
        {
            GameObject root = Load(path);
            MeshFilter filter = root.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("FighterModelSource: " + path + " has no mesh");
            Mesh mesh = filter.sharedMesh;
            Matrix4x4 m = Matrix4x4.Translate(pivot) * SourceToModel * filter.transform.localToWorldMatrix;
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Color[] colors = mesh.colors;
            if (colors.Length != positions.Length) throw new InvalidOperationException("FighterModelSource: " + path + " has no vertex colours");
            int offset = buffer.VertexCount;
            for (int v = 0; v < positions.Length; v++)
            {
                buffer.AddVertex(m.MultiplyPoint3x4(positions[v]), m.MultiplyVector(normals[v]), Linear(colors[v]), (int)bone);
                weights.Add(new BoneWeight { boneIndex0 = (int)bone, weight0 = 1f });
            }
            AppendTriangles(buffer, mesh.triangles, offset);
        }

        /// <summary>sRGB vertex colour (as Blender's FBX export writes it) → linear; alpha is linear gloss already.</summary>
        public static Color32 Linear(Color srgb) =>
            new Color(Mathf.GammaToLinearSpace(srgb.r), Mathf.GammaToLinearSpace(srgb.g), Mathf.GammaToLinearSpace(srgb.b), Mathf.Clamp01(srgb.a));

        private static bool TryBone(string name, out FighterBone bone) =>
            Enum.TryParse(name, false, out bone) && Enum.IsDefined(typeof(FighterBone), bone) && bone.ToString() == name;

        private static Matrix4x4 Blend(Matrix4x4[] skin, BoneWeight w, float sum)
        {
            Matrix4x4 m = Scaled(skin[w.boneIndex0], w.weight0 / sum);
            if (w.weight1 > 0f) m = Add(m, Scaled(skin[w.boneIndex1], w.weight1 / sum));
            if (w.weight2 > 0f) m = Add(m, Scaled(skin[w.boneIndex2], w.weight2 / sum));
            if (w.weight3 > 0f) m = Add(m, Scaled(skin[w.boneIndex3], w.weight3 / sum));
            return m;
        }

        private static Matrix4x4 Scaled(Matrix4x4 m, float k)
        {
            var r = new Matrix4x4();
            for (int i = 0; i < 16; i++) r[i] = m[i] * k;
            return r;
        }

        private static Matrix4x4 Add(Matrix4x4 a, Matrix4x4 b)
        {
            var r = new Matrix4x4();
            for (int i = 0; i < 16; i++) r[i] = a[i] + b[i];
            return r;
        }

        private static void AppendTriangles(MeshBuilderBuffer buffer, int[] triangles, int offset)
        {
            // The FBX winding is kept as is (the recalculated normals agree with it).
            for (int i = 0; i < triangles.Length; i++) buffer.Triangles.Add(triangles[i] + offset);
        }

        private static GameObject Load(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) throw new InvalidOperationException("FighterModelSource: no model at " + path);
            return root;
        }

        private static SkinnedMeshRenderer Skin(string path)
        {
            SkinnedMeshRenderer[] smrs = Load(path).GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length != 1 || smrs[0].sharedMesh == null)
                throw new InvalidOperationException("FighterModelSource: " + path + " needs exactly one skinned mesh, has " + smrs.Length);
            return smrs[0];
        }
    }
}
