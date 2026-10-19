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
                    string name = "bush " + mask + "/" + variant;
                    int tris = mesh.triangles.Length / 3;
                    Assert.LessOrEqual(tris, EnvMeshBuilder.BushTriangleBudget, name);
                    Assert.Greater(tris, 150, name + ": a full field of blades");
                    Vector3[] v = mesh.vertices;
                    Color[] c = mesh.colors;
                    Assert.AreEqual(v.Length, c.Length, "meshes under Arena/* shaders need vertex colours");
                    Assert.AreEqual(v.Length, mesh.tangents.Length, "tangent.xyz = blade normal (Arena/Bush)");
                    Bounds b = mesh.bounds;
                    Assert.Greater(b.size.x, 0.8f, name + ": fills the cell");
                    Assert.LessOrEqual(b.extents.x, 0.5f + EnvMeshBuilder.BushMaxOverhang + 1e-4f, name + ": overhang");
                    Assert.LessOrEqual(b.extents.z, 0.5f + EnvMeshBuilder.BushMaxOverhang + 1e-4f, name + ": overhang");
                    Assert.LessOrEqual(b.max.y, EnvMeshBuilder.BushMaxHeight + 1e-4f, name + ": height");
                    Assert.Greater(b.max.y, 0.85f, name + ": taller than a stone block's shoulder");
                    float lowWeight = 1f, highWeight = 0f, lowY = float.MaxValue, highY = float.MinValue;
                    float lowGreen = 0f, highGreen = 0f;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Assert.GreaterOrEqual(c[i].a, 0f);
                        Assert.LessOrEqual(c[i].a, 1f);
                        if (v[i].y < lowY) { lowY = v[i].y; lowWeight = c[i].a; lowGreen = c[i].g; }
                        if (v[i].y > highY) { highY = v[i].y; highWeight = c[i].a; highGreen = c[i].g; }
                    }
                    Assert.Less(lowWeight, 0.05f, "no sway at the ground");
                    Assert.Greater(highWeight, 0.8f, "the top sways");
                    Assert.Greater(highGreen, lowGreen, "the tips are lighter than the foot");
                    // Tips in the concept's emerald-teal (hue 150–165°), saturated.
                    float tipHue = 0f, tipSat = 0f;
                    int tipCount = 0;
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (c[i].a < 0.95f) continue;
                        Color.RGBToHSV(c[i].gamma, out float hh, out float ss, out _);
                        tipHue += hh;
                        tipSat += ss;
                        tipCount++;
                    }
                    Assert.Greater(tipCount, 20, name);
                    Assert.That(tipHue / tipCount * 360f, Is.InRange(148f, 167f), name + ": tip hue");
                    Assert.Greater(tipSat / tipCount, 0.75f, name + ": tip saturation");
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void BladesNextToASharedSide_AreTheSameInEveryVariantAndMask_SoNeighbouringClumpsHaveNoSeam()
        {
            int gx = EnvMeshBuilder.BushColumns, gz = EnvMeshBuilder.BushRows;
            float step = 1f / gx;
            // Reference: blades along each side when only that side is shared (variant 0); W 1, E 2, S 4, N 8.
            for (int side = 0; side < 4; side++)
            {
                int bit = 1 << side;
                BushBlade[] reference = EnvMeshBuilder.BushBladeLayout(bit, 0);
                int along = side < 2 ? gz : gx;
                for (int mask = 0; mask < EnvMeshBuilder.BushMaskCount; mask++)
                {
                    if ((mask & bit) == 0) continue;
                    for (int variant = 0; variant < EnvMeshBuilder.BushVariantCount; variant++)
                    {
                        BushBlade[] blades = EnvMeshBuilder.BushBladeLayout(mask, variant);
                        for (int k = 1; k < along - 1; k++)
                        {
                            int i = side == 0 ? 0 : side == 1 ? gx - 1 : k, j = side == 2 ? 0 : side == 3 ? gz - 1 : k;
                            BushBlade x = blades[j * gx + i], r = reference[j * gx + i];
                            string at = "side " + side + " mask " + mask + " v" + variant + " blade " + i + "," + j;
                            Assert.IsTrue(x.EdgeLocked, at);
                            Assert.Less((x.Root - r.Root).magnitude, 1e-5f, at + ": root");
                            Assert.AreEqual(r.Height, x.Height, 1e-5f, at + ": height");
                        }
                    }
                }
            }
            // Across a seam the blade rows keep the grid step: the east column of one cell to the west column of the next.
            BushBlade[] west = EnvMeshBuilder.BushBladeLayout(2, 0), east = EnvMeshBuilder.BushBladeLayout(1, 1);
            for (int j = 1; j < gz - 1; j++)
            {
                float gap = east[j * gx].Root.x + 1f - west[j * gx + gx - 1].Root.x;
                Assert.That(gap, Is.InRange(step * 0.5f, step * 1.5f), "row " + j);
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
            Assert.IsNotNull(catalog.Floor.GetTexture("_PatchMap"), "grass fringe at bushes on the sand floor");
            foreach (Mesh m in catalog.BushVariants) Assert.IsNotNull(m);
            Assert.Less(catalog.WaterHeight, 0f, "a sunken pond");
            Assert.Greater(catalog.MapStyle.ShadowStrength, 0f);
        }
    }
}
