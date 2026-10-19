using Arena.Core.Map;
using Arena.Data.Conversion;
using Arena.Gameplay.View;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>Soft ground shadows and contact occlusion of <see cref="MapShadowMask"/> (A.3e, D-130).</summary>
    public sealed class MapShadowMaskTests
    {
        private static MapMeshStyle Style() => new MapMeshStyle
        {
            ShadowTexelsPerCell = 8,
            ShadowStrength = 0.9f,
            ShadowSoftness = 0.15f,
            ShadowFall = new Vector2(0.55f, 0.4f),
            BushShadowHeight = 0.55f,
            CrateShadowHeight = 0.8f,
            BoxShadowHeight = 0.5f,
            PropShadowHeight = 0.65f,
            ObstaclePropRadius = 0.4f,
            FootprintInset = 0.06f,
            FootprintCorner = 0.15f,
            ShadowBlur = 0.12f,
            ContactAoBlur = 0.2f,
            ContactAoStrength = 0.7f,
            BushAoWeight = 0.6f
        };

        // Row 0 is the bottom row: wall, crate, bush, berry box far apart.
        private static TileGrid Grid() => MapConverter.FromRows("m", new[]
        {
            "...........",
            ".#..%..\"..$",
            "...........",
            "...........",
            "..........."
        }).InitialGrid;

        private static (int x, int y) Texel(MapShadowMask mask, float worldX, float worldY)
        {
            int k = mask.TexelsPerCell, pad = MapShadowMask.PaddingCells;
            return (Mathf.FloorToInt((worldX + pad) * k), Mathf.FloorToInt((worldY + pad) * k));
        }

        [Test]
        public void Build_IsDeterministic()
        {
            MapMeshStyle style = Style();
            TileGrid grid = Grid();
            byte[] a = MapShadowMask.Build(in grid, in style, out int w, out int h);
            byte[] b = MapShadowMask.Build(in grid, in style, out int w2, out int h2);
            Assert.AreEqual(w, w2);
            Assert.AreEqual(h, h2);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void ContactOcclusion_AroundEveryObstacleAndBush_ZeroFarAway()
        {
            MapMeshStyle style = Style();
            TileGrid grid = Grid();
            var mask = new MapShadowMask(in grid, in style);
            try
            {
                // Cells (column, row 3 from the top = row 3 in world): wall 1, crate 4, bush 7, berry box 10.
                int[] columns = { 1, 4, 7, 10 };
                foreach (int c in columns)
                {
                    // Just outside the south side of the footprint (0.1 cell beyond the cell edge).
                    (int x, int y) = Texel(mask, c + 0.5f, 3f - 0.1f);
                    Assert.Greater(mask.OcclusionAt(x, y), 10, "contact AO beside the caster in column " + c);
                    (int cx, int cy) = Texel(mask, c + 0.5f, 3.5f);
                    Assert.Greater(mask.HeightAt(cx, cy), 0, "caster height in column " + c);
                }
                (int fx, int fy) = Texel(mask, 5.5f, 0.5f);
                Assert.AreEqual(0, mask.OcclusionAt(fx, fy), "open floor far from casters");
                Assert.AreEqual(0, mask.HeightAt(fx, fy));
            }
            finally
            {
                mask.Destroy();
            }
        }

        [Test]
        public void ShadowEdge_IsSoft()
        {
            MapMeshStyle style = Style();
            TileGrid grid = Grid();
            var mask = new MapShadowMask(in grid, in style);
            try
            {
                // Walk east across the wall's east edge (x = 2 − inset): some texels are between open and full height.
                int partial = 0;
                (int x0, int y) = Texel(mask, 1.5f, 3.5f);
                (int x1, _) = Texel(mask, 2.5f, 3.5f);
                for (int x = x0; x <= x1; x++)
                {
                    byte v = mask.HeightAt(x, y);
                    if (v > 20 && v < 235) partial++;
                }
                Assert.GreaterOrEqual(partial, 2, "blurred edge spans at least two texels (≥ 0.25 cell)");
            }
            finally
            {
                mask.Destroy();
            }
        }

        [Test]
        public void ClearCell_ClearsBothChannels_WithoutAllocations()
        {
            MapMeshStyle style = Style();
            TileGrid grid = MapConverter.FromRows("m", new[]
            {
                ".........",
                ".%.....%.",
                "........."
            }).InitialGrid;
            var mask = new MapShadowMask(in grid, in style);
            try
            {
                mask.ClearCell(7, 1); // warm-up (first-use costs of the texture API)
                (int x, int y) = Texel(mask, 1.5f, 1.5f);
                (int ax, int ay) = Texel(mask, 1.5f, 0.9f);
                Assert.Greater(mask.HeightAt(x, y), 0);
                Assert.Greater(mask.OcclusionAt(ax, ay), 0);
                AllocationAssert.NoGcAllocations(() => mask.ClearCell(1, 1));
                Assert.AreEqual(0, mask.HeightAt(x, y), "height cleared");
                Assert.AreEqual(0, mask.OcclusionAt(ax, ay), "contact AO cleared around the broken crate");
                byte[] raw = mask.Texture.GetPixelData<byte>(0).ToArray();
                Assert.AreEqual(0, raw[(y * mask.Width + x) * 2], "texture updated");
            }
            finally
            {
                mask.Destroy();
            }
        }

        [Test]
        public void RoundProps_CastADiscFootprint()
        {
            MapMeshStyle style = Style();
            TileGrid grid = MapConverter.FromRows("m", new[] { "...", ".#.", "..." }).InitialGrid;
            var round = new bool[grid.Width * grid.Height];
            round[grid.IndexOf(1, 1)] = true;
            var mask = new MapShadowMask(in grid, in style, round);
            try
            {
                (int cx, int cy) = Texel(mask, 1.5f, 1.5f);
                (int kx, int ky) = Texel(mask, 1.06f, 1.06f);
                Assert.AreEqual(Mathf.RoundToInt(style.PropShadowHeight * 255f), mask.HeightAt(cx, cy), 2, "prop height in the middle");
                Assert.Less(mask.HeightAt(kx, ky), 40, "the corner of the cell is outside the disc");
            }
            finally
            {
                mask.Destroy();
            }
        }
    }
}
