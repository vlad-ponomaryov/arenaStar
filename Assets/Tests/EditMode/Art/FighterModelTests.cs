using System;
using System.Collections.Generic;
using Arena.Editor.Art;
using Arena.Editor.Art.Fighters;
using Arena.Editor.Art.Fighters.Recipes;
using Arena.Editor.Art.Procedural;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Art
{
    /// <summary>
    /// Fighter models (Art-Spec §6.2, §8): one skinned renderer on the shared body material, ≤ 4000 triangles (+ 1200 for an FBX
    /// prop), one bone per vertex for procedural recipes, ≤ 2 normalised weights for FBX ones (A-Theo3D), rig with rest pose, recipe
    /// budgets. Prefabs are the assets made by <c>ViewAssetBuilder.BuildAll</c>.
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
                if (recipe.SourceModel == null || recipe.SourceModelHero != null)
                    Assert.That(mesh.triangles.Length / 3, Is.InRange(15000, 30000), id + ": showcase LOD (D-122)");
                else // no finer FBX yet: the hero model is the same source
                    Assert.AreEqual(catalog.FighterPrefab(id).Body.GetComponent<SkinnedMeshRenderer>().sharedMesh.triangles.Length,
                        mesh.triangles.Length, id);
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
                bool fbx = recipe.SourceModel != null;
                Assert.AreEqual(fbx ? FighterModelBuilder.SourceSkinQuality : SkinQuality.Bone1, smr.quality, id);
                Assert.IsFalse(smr.updateWhenOffscreen, id);
                Assert.AreEqual(0, view.GetComponentsInChildren<Collider>(true).Length, id);
                Assert.IsNotNull(view.Ring, id);
                Assert.IsNotNull(view.Shadow, id);

                Mesh mesh = smr.sharedMesh;
                Assert.AreEqual(FighterModelBuilder.MeshPath(id), AssetDatabase.GetAssetPath(mesh), id);
                Assert.LessOrEqual(mesh.triangles.Length / 3, FighterModelBuilder.TriangleLimit(recipe), id);
                Assert.AreEqual(mesh.vertexCount, mesh.colors32.Length, id + ": vertex colours");
                Assert.AreEqual(boneCount, smr.bones.Length, id);
                Assert.AreEqual(boneCount, mesh.bindposes.Length, id);
                BoneWeight[] weights = mesh.boneWeights;
                Assert.AreEqual(mesh.vertexCount, weights.Length, id);
                foreach (BoneWeight w in weights)
                {
                    if (fbx)
                    {
                        Assert.AreEqual(1f, w.weight0 + w.weight1 + w.weight2 + w.weight3, 1e-3f, id + ": weights sum to 1");
                        Assert.That(w.boneIndex1, Is.InRange(0, boneCount - 1), id);
                    }
                    else
                    {
                        Assert.AreEqual(1f, w.weight0, id + ": one bone per vertex, weight 1");
                        Assert.AreEqual(0f, w.weight1, id);
                    }
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
                Assert.LessOrEqual(geometry.TriangleCount, FighterModelBuilder.TriangleLimit(recipe), recipe.FighterId);
                int head = 0;
                int firstGroup = stats.Count - recipe.Groups.Length; // source model / prop stats come first
                for (int i = 0; i < stats.Count; i++)
                {
                    Assert.LessOrEqual(stats[i].Triangles, stats[i].Budget, recipe.FighterId + " " + stats[i].Name);
                    Assert.Greater(stats[i].Triangles, 0, recipe.FighterId + " " + stats[i].Name);
                    if (i >= firstGroup && recipe.Groups[i - firstGroup].Bone == FighterBone.Head) head += stats[i].Triangles;
                }
                Assert.LessOrEqual(head, 1400, recipe.FighterId + ": head budget (Art-Spec §6.2)");
            }
        }

        [Test]
        public void Pix_IsAToySizedFox_SeenFromAbove()
        {
            FighterModelRecipe pix = PixRecipe.Create(); // the procedural fallback of the "pix" view (A-Theo3D)
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

        [Test]
        public void Theo_SourceModel_UsesOnlyFighterBones_AndItsBudget()
        {
            FighterModelRecipe theo = TheoRecipe.Create();
            foreach (string name in FighterModelSource.SkinBoneNames(theo.SourceModel))
                Assert.IsTrue(Enum.TryParse(name, false, out FighterBone _), "FBX bone " + name + " is not a FighterBone");
            var stats = new List<FighterModelBuilder.GroupStats>();
            FighterModelBuilder.BuildGeometry(theo, stats);
            Assert.AreEqual("model", stats[0].Name);
            Assert.That(stats[0].Triangles, Is.InRange(1000, FighterModelBuilder.MaxTriangles), "body");
            Assert.AreEqual("prop", stats[1].Name);
            Assert.That(stats[1].Triangles, Is.InRange(100, FighterModelBuilder.MaxPropTriangles), "blaster");
        }

        [Test]
        public void Theo_StandsUpright_FacesForward_AndHoldsTheBlasterInTheRightHand()
        {
            FighterModelRecipe theo = TheoRecipe.Create();
            var weights = new List<BoneWeight>();
            MeshBuilderBuffer geometry = FighterModelBuilder.BuildGeometry(theo, null, FighterModelQuality.Game, weights);
            Assert.AreEqual(geometry.VertexCount, weights.Count);
            Vector3 head = FighterModelBuilder.Pivot(theo, FighterBone.Head), body = FighterModelBuilder.Pivot(theo, FighterBone.Body);
            Vector3 armR = FighterModelBuilder.Pivot(theo, FighterBone.ArmR), prop = FighterModelBuilder.Pivot(theo, FighterBone.Prop);
            Assert.Greater(head.y, body.y, "head above the body");
            Assert.Greater(armR.x, 0.1f, "right arm on +X");
            Assert.Greater(prop.x, 0.2f, "blaster grip at the right hand");
            Assert.Less(prop.y, armR.y, "the hand hangs below the shoulder");

            float minY = float.MaxValue, maxY = float.MinValue, propFront = float.MinValue, eyeFront = float.MinValue;
            Vector3 pmin = Vector3.one * 9f, pmax = -Vector3.one * 9f;
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                Vector3 p = geometry.Positions[i];
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
                var bone = (FighterBone)geometry.Bones[i];
                if (bone == FighterBone.Prop) { propFront = Mathf.Max(propFront, p.z); pmin = Vector3.Min(pmin, p); pmax = Vector3.Max(pmax, p); }
                if (bone == FighterBone.Head) eyeFront = Mathf.Max(eyeFront, p.z);
            }
            Assert.AreEqual(0f, minY, 0.03f, "feet on the ground: " + minY + ".." + maxY + " prop " + pmin + " " + pmax);
            Assert.AreEqual(TheoRecipe.Scale, maxY, 0.05f, "≈ 1.5 tall, as the other fighters");
            Assert.Greater(propFront, prop.z + 0.5f, "the barrel points forward (+Z)");
            Assert.Greater(eyeFront, head.z + 0.1f, "the face (painted eyes) is on the +Z side of the head");
        }

        [Test]
        public void Theo_VertexColours_AreLinear_WithGlossInAlpha()
        {
            ViewCatalog catalog = Catalog();
            Mesh mesh = catalog.FighterPrefab(TheoRecipe.FighterId).Body.GetComponent<SkinnedMeshRenderer>().sharedMesh;
            SkinnedMeshRenderer source = AssetDatabase.LoadAssetAtPath<GameObject>(TheoRecipe.ModelPath).GetComponentInChildren<SkinnedMeshRenderer>();
            Color[] srgb = source.sharedMesh.colors;
            Color32[] baked = mesh.colors32;
            Assert.LessOrEqual(srgb.Length, baked.Length);
            for (int i = 0; i < srgb.Length; i += 17)
            {
                Color32 expected = FighterModelSource.Linear(srgb[i]);
                Assert.AreEqual(expected.r, baked[i].r, 1, "vertex " + i + " linearised");
                Assert.AreEqual(expected.a, baked[i].a, 1, "vertex " + i + " gloss");
            }
            var gloss = new HashSet<byte>();
            foreach (Color32 c in baked) gloss.Add(c.a);
            Assert.GreaterOrEqual(gloss.Count, 4, "gloss varies by material (skin, cloth, plates, metal)");
            Color32 red = FighterModelSource.Linear(new Color(0xB3 / 255f, 0x14 / 255f, 0x1C / 255f));
            Assert.That(red.r, Is.InRange(110, 120), "#B3 → linear ≈ 0.45");
        }

        [Test]
        public void Theo_IsTextured_OnTheGameAndHeroModels_TheBlasterIsNot()
        {
            ViewCatalog catalog = Catalog();
            var map = AssetDatabase.LoadAssetAtPath<Texture2D>(TheoRecipe.BaseMapPath);
            Assert.IsNotNull(map, TheoRecipe.BaseMapPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TheoRecipe.BaseMapPath);
            Assert.IsTrue(importer.sRGBTexture, "colour is sRGB");
            Assert.IsTrue(importer.mipmapEnabled);
            Assert.IsFalse(importer.alphaIsTransparency, "alpha is gloss");
            Assert.AreEqual(TextureImporterAlphaSource.FromInput, importer.alphaSource);
            Assert.LessOrEqual(map.width, FighterModelImporter.TextureMaxSize);
            foreach (FighterView view in new[] { catalog.FighterPrefab(TheoRecipe.FighterId), catalog.FighterHeroPrefab(TheoRecipe.FighterId) })
            {
                var skin = view.Body.GetComponent<FighterSkin>();
                Assert.IsNotNull(skin, view.name);
                Assert.AreSame(map, skin.BaseMap, view.name);
                Mesh mesh = view.Body.GetComponent<SkinnedMeshRenderer>().sharedMesh;
                var uvs = new List<Vector2>();
                mesh.GetUVs(0, uvs);
                Assert.AreEqual(mesh.vertexCount, uvs.Count, view.name + ": UVs");
                BoneWeight[] weights = mesh.boneWeights;
                int textured = 0;
                for (int i = 0; i < uvs.Count; i++)
                {
                    bool prop = weights[i].boneIndex0 == (int)FighterBone.Prop && weights[i].weight0 > 0.999f;
                    if (uvs[i].x >= 0f)
                    {
                        textured++;
                        Assert.That(uvs[i].x, Is.InRange(0f, 1f), view.name);
                        Assert.That(uvs[i].y, Is.InRange(0f, 1f), view.name);
                        Assert.IsFalse(prop, view.name + ": the blaster keeps its vertex colours");
                    }
                }
                Assert.Greater(textured, mesh.vertexCount / 2, view.name + ": the body is textured");
            }
            Assert.IsNull(PixRecipe.Create().BaseMap, "procedural recipes stay untextured (_BaseMapOn = 0, white map)");
        }
    }
}
