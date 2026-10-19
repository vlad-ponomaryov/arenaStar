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
            for (int variant = 0; variant < EnvMeshBuilder.BushVariantCount; variant++)
            {
                Mesh mesh = EnvMeshBuilder.BuildBush(variant);
                try
                {
                    int tris = mesh.triangles.Length / 3;
                    Assert.LessOrEqual(tris, EnvMeshBuilder.BushTriangleBudget, "bush " + variant);
                    Assert.Greater(tris, 100, "a full cap of blobs");
                    Vector3[] v = mesh.vertices;
                    Color[] c = mesh.colors;
                    Assert.AreEqual(v.Length, c.Length, "meshes under Arena/* shaders need vertex colours");
                    Bounds b = mesh.bounds;
                    Assert.Greater(b.size.x, 0.85f, "fills the cell (neighbours merge into one bush)");
                    Assert.Less(b.size.x, 1.3f);
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
                    Assert.Less(lowWeight, 0.05f, "no sway at the ground");
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
            Assert.AreEqual(EnvMeshBuilder.BushVariantCount, catalog.BushVariants.Length);
            foreach (Mesh m in catalog.BushVariants) Assert.IsNotNull(m);
            Assert.Less(catalog.WaterHeight, 0f, "a sunken pond");
            Assert.Greater(catalog.MapStyle.ShadowStrength, 0f);
        }
    }
}
