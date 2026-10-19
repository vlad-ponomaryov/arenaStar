using System;
using System.Collections.Generic;
using Arena.Editor.Art;
using Arena.Editor.Art.Fighters;
using Arena.Editor.Art.Procedural;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Art
{
    /// <summary>
    /// Procedural fighter models (Art-Spec §6.2, §8): one skinned renderer on the shared body material, ≤ 4000 triangles, one bone
    /// per vertex, rig with rest pose, recipe budgets. Prefabs are the assets made by <c>ViewAssetBuilder.BuildAll</c>.
    /// </summary>
    public sealed class FighterModelTests
    {
        private static ViewCatalog Catalog() => AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);

        private static IEnumerable<FighterModelRecipe> Recipes() => FighterModelBuilder.Recipes();

        [Test]
        public void EveryRecipe_HasAPrefabInTheCatalog()
        {
            ViewCatalog catalog = Catalog();
            foreach (FighterModelRecipe recipe in Recipes())
            {
                FighterView prefab = catalog.FighterPrefab(recipe.FighterId);
                Assert.AreNotSame(catalog.FighterViewPrefab, prefab, recipe.FighterId + " uses the fallback capsule");
                Assert.AreEqual(FighterModelBuilder.PrefabPath(recipe.FighterId), AssetDatabase.GetAssetPath(prefab));
            }
            Assert.IsNotNull(catalog.FighterPrefab("pix").Rig, "Pix is the reference model (A.1b)");
        }

        [Test]
        public void EveryRecipe_HasAHeroModel_FinerThanTheMatchOne()
        {
            ViewCatalog catalog = Catalog();
            foreach (FighterModelRecipe recipe in Recipes())
            {
                string id = recipe.FighterId;
                FighterView hero = catalog.FighterHeroPrefab(id);
                Assert.AreEqual(FighterModelBuilder.PrefabPath(id, FighterModelQuality.Hero), AssetDatabase.GetAssetPath(hero), id);
                SkinnedMeshRenderer[] smrs = hero.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.AreEqual(1, smrs.Length, id);
                Assert.AreSame(catalog.FighterBody, smrs[0].sharedMaterial, id);
                Mesh mesh = smrs[0].sharedMesh;
                Assert.AreEqual(FighterModelBuilder.MeshPath(id, FighterModelQuality.Hero), AssetDatabase.GetAssetPath(mesh), id);
                Assert.That(mesh.triangles.Length / 3, Is.InRange(15000, 30000), id + ": showcase LOD (D-122)");
                Assert.AreEqual(Enum.GetValues(typeof(FighterBone)).Length, smrs[0].bones.Length, id);
                Assert.IsNotNull(hero.Rig, id);
                Assert.AreEqual(catalog.FighterPrefab(id).Rig.TopHeight, hero.Rig.TopHeight, 0.03f, id + ": same size as the match model");
            }
            Assert.AreSame(catalog.FighterViewPrefab, catalog.FighterHeroPrefab("no-such-fighter"), "fallback");
        }

        [Test]
        public void Prefabs_HaveOneSkinnedRenderer_OneMaterial_AndValidSkin()
        {
            ViewCatalog catalog = Catalog();
            int boneCount = Enum.GetValues(typeof(FighterBone)).Length;
            foreach (FighterModelRecipe recipe in Recipes())
            {
                FighterView view = catalog.FighterPrefab(recipe.FighterId);
                string id = recipe.FighterId;
                SkinnedMeshRenderer[] smrs = view.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.AreEqual(1, smrs.Length, id);
                SkinnedMeshRenderer smr = smrs[0];
                Assert.AreSame(smr.transform, view.Body, id + ": Body is the skinned renderer");
                Assert.AreEqual(1, smr.sharedMaterials.Length, id);
                Assert.AreSame(catalog.FighterBody, smr.sharedMaterial, id);
                Assert.AreEqual(SkinQuality.Bone1, smr.quality, id);
                Assert.IsFalse(smr.updateWhenOffscreen, id);
                Assert.AreEqual(0, view.GetComponentsInChildren<Collider>(true).Length, id);
                Assert.IsNotNull(view.Ring, id);
                Assert.IsNotNull(view.Shadow, id);

                Mesh mesh = smr.sharedMesh;
                Assert.AreEqual(FighterModelBuilder.MeshPath(id), AssetDatabase.GetAssetPath(mesh), id);
                Assert.LessOrEqual(mesh.triangles.Length / 3, FighterModelBuilder.MaxTriangles, id);
                Assert.AreEqual(mesh.vertexCount, mesh.colors32.Length, id + ": vertex colours");
                Assert.AreEqual(boneCount, smr.bones.Length, id);
                Assert.AreEqual(boneCount, mesh.bindposes.Length, id);
                BoneWeight[] weights = mesh.boneWeights;
                Assert.AreEqual(mesh.vertexCount, weights.Length, id);
                foreach (BoneWeight w in weights)
                {
                    Assert.AreEqual(1f, w.weight0, id + ": one bone per vertex, weight 1");
                    Assert.AreEqual(0f, w.weight1, id);
                    Assert.That(w.boneIndex0, Is.InRange(0, boneCount - 1), id);
                }

                FighterRig rig = view.Rig;
                Assert.IsNotNull(rig, id);
                Assert.AreEqual(boneCount, rig.Bones.Length, id);
                for (int i = 0; i < boneCount; i++)
                {
                    Assert.AreSame(smr.bones[i], rig.Bones[i], id + " bone " + i);
                    Assert.AreEqual(((FighterBone)i).ToString(), rig.Bones[i].name, id);
                }
                Assert.AreEqual(Vector3.zero, rig.RestScales[(int)FighterBone.LidL], id + ": eyes open at rest");
                Assert.AreEqual(Vector3.zero, rig.RestScales[(int)FighterBone.LidR], id);
                Assert.AreEqual(Vector3.one, rig.RestScales[(int)FighterBone.Head], id);
                Assert.That(rig.TopHeight, Is.InRange(1.2f, 2.2f), id);
                Assert.IsNotNull(rig.Springs, id);
            }
        }

        [Test]
        public void Geometry_KeepsEveryGroupInItsBudget_AndTheHeadWithinSpec()
        {
            foreach (FighterModelRecipe recipe in Recipes())
            {
                var stats = new List<FighterModelBuilder.GroupStats>();
                MeshBuilderBuffer geometry = FighterModelBuilder.BuildGeometry(recipe, stats);
                Assert.LessOrEqual(geometry.TriangleCount, FighterModelBuilder.MaxTriangles, recipe.FighterId);
                int head = 0;
                for (int i = 0; i < stats.Count; i++)
                {
                    Assert.LessOrEqual(stats[i].Triangles, stats[i].Budget, recipe.FighterId + " " + stats[i].Name);
                    Assert.Greater(stats[i].Triangles, 0, recipe.FighterId + " " + stats[i].Name);
                    if (recipe.Groups[i].Bone == FighterBone.Head) head += stats[i].Triangles;
                }
                Assert.LessOrEqual(head, 1400, recipe.FighterId + ": head budget (Art-Spec §6.2)");
            }
        }

        [Test]
        public void Pix_IsAToySizedFox_SeenFromAbove()
        {
            FighterModelRecipe pix = Array.Find(FighterModelBuilder.Recipes(), r => r.FighterId == "pix");
            Assert.IsNotNull(pix);
            MeshBuilderBuffer geometry = FighterModelBuilder.BuildGeometry(pix);
            float top = float.MinValue, headBottom = float.MaxValue, headTop = float.MinValue, minX = 0f, maxX = 0f;
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                Vector3 p = geometry.Positions[i];
                var bone = (FighterBone)geometry.Bones[i];
                top = Mathf.Max(top, p.y);
                if (bone == FighterBone.Head && Mathf.Abs(p.x) < 0.08f) // the face column, without the ears
                {
                    headBottom = Mathf.Min(headBottom, p.y);
                    headTop = Mathf.Max(headTop, p.y);
                }
                if (bone == FighterBone.Head || bone == FighterBone.Body)
                {
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                }
            }
            float height = headTop; // standing height without the ears
            Assert.That(height, Is.InRange(1.3f, 1.6f), "Pix is ≈ 1.5 tall (Art-Spec §1.4)");
            Assert.That((headTop - headBottom) / height, Is.InRange(0.33f, 0.45f),
                "head ≈ 0.35–0.4 of the height (Art-Spec §1.1.3); the measure includes ≈ 0.03 of chin tucked into the torso");
            Assert.Greater(top, height + 0.15f, "ears stick out above the head");
            // Width from above ≈ 2 × hitbox radius 0.45 (± 10 %), not counting the tail and the blaster.
            Assert.That(maxX - minX, Is.InRange(0.9f * 0.9f, 0.9f * 1.1f));
        }
    }
}
