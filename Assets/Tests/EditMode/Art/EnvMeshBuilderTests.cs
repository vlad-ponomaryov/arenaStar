using Arena.Editor.Art;
using Arena.Editor.Art.MapProps;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Art
{
    /// <summary>Map props of A.3b (Art-Spec §2.1, §7, D-125): bush budget and sway weights, crate and basket, their materials.</summary>
    public sealed class EnvMeshBuilderTests
    {
        [Test]
        public void Bushes_FitTheBudget_HaveVertexColours_AndSwayOnlyAtTheTop()
        {
            for (int mask = 0; mask < EnvMeshBuilder.BushMaskCount; mask++)
            for (int variant = 0; variant < EnvMeshBuilder.BushVariantCount; variant++)
            {
                Mesh mesh = EnvMeshBuilder.BuildBush(mask, variant);
                try
                {
                    int tris = mesh.triangles.Length / 3;
                    Assert.LessOrEqual(tris, EnvMeshBuilder.BushTriangleBudget, "bush " + mask + "/" + variant);
                    Assert.Greater(tris, 100, "a full field of grass tips");
                    Vector3[] v = mesh.vertices;
                    Color[] c = mesh.colors;
                    Assert.AreEqual(v.Length, c.Length, "meshes under Arena/* shaders need vertex colours");
                    Bounds b = mesh.bounds;
                    Assert.Greater(b.size.x, 0.85f, "fills the cell (neighbours merge into one bush)");
                    Assert.LessOrEqual(b.extents.x, 0.5f + 0.031f, "stays in its cell (foot spread only)");
                    Assert.Greater(b.max.y, 0.7f);
                    float lowWeight = 1f, highWeight = 0f, lowY = float.MaxValue, highY = float.MinValue;
                    float lowGreen = 0f, highGreen = 0f;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Assert.GreaterOrEqual(c[i].a, 0f);
                        Assert.LessOrEqual(c[i].a, 1f);
                        if (v[i].y < lowY) { lowY = v[i].y; lowWeight = c[i].a; lowGreen = c[i].g; }
                        if (v[i].y > highY) { highY = v[i].y; highWeight = c[i].a; highGreen = c[i].g; }
                    }
                    if (mask != EnvMeshBuilder.BushMaskCount - 1) Assert.Less(lowWeight, 0.05f, "no sway at the ground");
                    else Assert.Greater(lowY, 0.3f, "a bush closed on all sides has no skirt");
                    Assert.Greater(highWeight, 0.8f, "the top sways");
                    Assert.Greater(highGreen, lowGreen, "the top is lighter than the foot");
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void NeighbouringBushes_ShareTheirEdge_SoTheyMergeWithoutAGap()
        {
            // East side of a cell with an east neighbour (mask E = 2) vs the west side of the cell with a west neighbour (W = 1),
            // in different variants: the top vertices on the shared edge must coincide (heights are variant independent).
            Mesh west = EnvMeshBuilder.BuildBush(2, 0);
            Mesh east = EnvMeshBuilder.BuildBush(1, 1);
            try
            {
                var edgeA = new System.Collections.Generic.SortedDictionary<float, float>();
                foreach (Vector3 v in west.vertices) if (Mathf.Abs(v.x - 0.5f) < 1e-4f && v.y > 0.1f) edgeA[Mathf.Round(v.z * 1000f)] = v.y;
                int matched = 0;
                foreach (Vector3 v in east.vertices)
                {
                    if (Mathf.Abs(v.x + 0.5f) > 1e-4f || v.y <= 0.1f) continue;
                    Assert.IsTrue(edgeA.TryGetValue(Mathf.Round(v.z * 1000f), out float y), "vertex at z " + v.z + " has a twin");
                    Assert.AreEqual(y, v.y, 1e-4f, "same height on the shared edge");
                    matched++;
                }
                Assert.Greater(matched, 5);
                foreach (Vector3 v in west.vertices) Assert.Greater(v.x, -0.5f + 1e-3f, "open west side is inset");
            }
            finally
            {
                Object.DestroyImmediate(west);
                Object.DestroyImmediate(east);
            }
        }

        [Test]
        public void ObstacleProps_StandInTheCell_Untextured_WithColours()
        {
            Mesh[] props = EnvMeshBuilder.BuildObstacleProps();
            try
            {
                Assert.AreEqual(EnvMeshBuilder.ObstaclePropCount, props.Length);
                foreach (Mesh m in props)
                {
                    Assert.AreEqual(m.vertexCount, m.colors.Length, m.name);
                    Assert.AreEqual(m.vertexCount, m.uv.Length, m.name + ": uv0.x = 1 (vertex colours only)");
                    foreach (Vector2 uv in m.uv) Assert.AreEqual(1f, uv.x, 1e-5f, m.name);
                    Assert.GreaterOrEqual(m.bounds.min.y, -1e-4f, m.name + " stands on the ground");
                    Assert.LessOrEqual(m.bounds.extents.x, 0.5f, m.name + " stays in its cell");
                    Assert.LessOrEqual(m.bounds.extents.z, 0.5f, m.name);
                    Assert.Greater(m.bounds.extents.x, 0.4f, m.name + " fills most of the blocked cell");
                    Assert.Greater(m.bounds.size.y, 0.7f, m.name + " reads as an obstacle");
                    Assert.LessOrEqual(m.triangles.Length / 3, 400, m.name);
                }
            }
            finally
            {
                foreach (Mesh m in props) Object.DestroyImmediate(m);
            }
        }

        [Test]
        public void Crate_And_Basket_StandInTheCell_WithColoursAndGlossyBerries()
        {
            Mesh crate = EnvMeshBuilder.BuildCrate();
            Mesh basket = EnvMeshBuilder.BuildBasket();
            try
            {
                foreach (Mesh m in new[] { crate, basket })
                {
                    Assert.AreEqual(m.vertexCount, m.colors.Length, m.name);
                    Assert.AreEqual(m.vertexCount, m.normals.Length, m.name);
                    Assert.GreaterOrEqual(m.bounds.min.y, -1e-4f, m.name + " stands on the ground");
                    Assert.LessOrEqual(m.bounds.extents.x, 0.5f, m.name + " stays in its cell");
                    Assert.LessOrEqual(m.bounds.extents.z, 0.5f, m.name);
                }
                Assert.Greater(crate.bounds.size.y, 0.9f, "a crate is about wall height");
                bool glossy = false;
                foreach (Color c in basket.colors) glossy |= c.a > 0.9f;
                Assert.IsTrue(glossy, "berries are glossy (vertex alpha)");
                Assert.AreEqual(basket.vertexCount, basket.uv.Length, "uv0.x marks untextured berries");
                foreach (Color c in crate.colors) Assert.AreEqual(0f, c.a, 1e-5f, "the crate is matte");
            }
            finally
            {
                Object.DestroyImmediate(crate);
                Object.DestroyImmediate(basket);
            }
        }

        [Test]
        public void MapPropMaterials_UseArenaShaders_AndSeeThroughBushIsTransparent()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            Assert.IsNotNull(catalog);
            Assert.AreEqual(ViewAssetBuilder.BushShader, catalog.Bush.shader.name);
            Assert.AreEqual(ViewAssetBuilder.BushShader, catalog.BushSeeThrough.shader.name);
            Assert.AreEqual(ViewAssetBuilder.WaterShader, catalog.Water.shader.name);
            Assert.AreEqual(ViewAssetBuilder.ToonEnvShader, catalog.WallBreakable.shader.name);
            Assert.AreEqual(ViewAssetBuilder.ToonEnvShader, catalog.BerryBox.shader.name);
            Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, catalog.BushSeeThrough.renderQueue);
            Assert.AreEqual(0f, catalog.BushSeeThrough.GetFloat("_ZWrite"));
            Assert.Less(catalog.Bush.renderQueue, (int)UnityEngine.Rendering.RenderQueue.Transparent);
            Assert.AreEqual(1f, catalog.Bush.GetFloat("_ZWrite"));
            Assert.IsNotNull(catalog.Water.GetTexture("_RippleMap"));
            Assert.IsNotNull(catalog.Wall.GetTexture("_SideMap"), "planks on the wall sides");
            Assert.AreEqual(0f, catalog.Wall.GetFloat("_ReceiveMapShadow"), "walls do not shadow themselves");
            Assert.AreEqual(1f, catalog.Floor.GetFloat("_ReceiveMapShadow"));
            Assert.AreEqual(EnvMeshBuilder.BushMaskCount * EnvMeshBuilder.BushVariantCount, catalog.BushVariants.Length);
            Assert.AreEqual(EnvMeshBuilder.BushVariantCount, catalog.BushVariantsPerMask);
            Assert.AreEqual(EnvMeshBuilder.ObstaclePropCount, catalog.ObstacleProps.Length);
            Assert.AreEqual(ViewAssetBuilder.ToonEnvShader, catalog.ObstacleProp.shader.name);
            Assert.IsNotNull(catalog.Floor.GetTexture("_PatchMap"), "grass patches on the sand floor");
            foreach (Mesh m in catalog.BushVariants) Assert.IsNotNull(m);
            Assert.Less(catalog.WaterHeight, 0f, "a sunken pond");
            Assert.Greater(catalog.MapStyle.ShadowStrength, 0f);
        }
    }
}
