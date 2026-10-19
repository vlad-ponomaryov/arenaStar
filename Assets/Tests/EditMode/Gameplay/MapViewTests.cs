using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Data.Conversion;
using Arena.Editor.Art;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>MapView with the A.3b props (D-024, D-125): bush see-through swap, bush variants, crate shadow cleared on break.</summary>
    public sealed class MapViewTests
    {
        [Test]
        public void Bushes_TurnSeeThroughAroundTheHiddenFighter_AndBrokenCrateLeavesTheShadowMask()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            Assert.IsNotNull(catalog);
            TileGrid grid = MapConverter.FromRows("m", new[] { "\"\"....", "......", "%....\"" }).InitialGrid;
            var root = new GameObject("MapViewTests");
            try
            {
                MapView view = MapView.Create(root.transform, catalog, in grid);
                Assert.AreEqual(3, view.BushCount);
                Assert.AreEqual(1, view.BreakableCount);
                for (int i = 0; i < view.BushCount; i++) Assert.AreEqual(catalog.Bush, view.BushMaterial(i));

                // The local fighter hides in the top-left bush: the two near bushes turn see-through, the far one stays.
                view.UpdateSeeThrough(new Vec2(0.5f, 2.5f), true);
                int seeThrough = 0;
                for (int i = 0; i < view.BushCount; i++)
                    if (view.BushMaterial(i) == catalog.BushSeeThrough) seeThrough++;
                Assert.AreEqual(2, seeThrough);
                view.UpdateSeeThrough(new Vec2(0.5f, 2.5f), false);
                for (int i = 0; i < view.BushCount; i++) Assert.AreEqual(catalog.Bush, view.BushMaterial(i));

                // The crate (cell 0, 0) casts a shadow until it breaks.
                MapShadowMask mask = view.ShadowMask;
                Assert.IsNotNull(mask);
                int k = mask.TexelsPerCell, pad = MapShadowMask.PaddingCells;
                int texel = (pad * k + k / 2) * mask.Width + pad * k + k / 2;
                Assert.Greater(mask.Texture.GetPixelData<byte>(0)[texel], 0);
                view.OnTileBroken(grid.IndexOf(0, 0));
                Assert.IsFalse(view.IsBreakableShown(grid.IndexOf(0, 0)));
                Assert.AreEqual(0, mask.Texture.GetPixelData<byte>(0)[texel]);
                mask.Destroy();
            }
            finally
            {
                Object.DestroyImmediate(root);
                MapShadowMask.Clear();
            }
        }
    }
}
