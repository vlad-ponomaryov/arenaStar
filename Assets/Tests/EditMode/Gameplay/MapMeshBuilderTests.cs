using Arena.Core.Map;
using Arena.Data.Conversion;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>Map meshes (A.3a, Art-Spec §2.1): chamfered walls with top/side colours, floor occlusion near walls, meadow frame.</summary>
    public sealed class MapMeshBuilderTests
    {
        private const float Height = 1.2f;
        private const float Bevel = 0.08f;

        private static MapMeshStyle Style() => new MapMeshStyle
        {
            WallBevel = Bevel,
            WallTop = new Color(0.95f, 0.89f, 0.76f, 1f),
            WallBevelHighlight = 0.4f,
            WallSide = new Color(0.73f, 0.54f, 0.37f, 1f),
            WallFootShade = 0.7f,
            WallCellVariation = 0f,
            FloorSubdivisions = 2,
            FloorWallOcclusion = 0.5f,
            FloorOcclusionRadius = 0.75f,
            FloorOcclusionColor = new Color(0.4f, 0.5f, 0.45f, 1f),
            MeadowMargin = 10f,
            MeadowFade = 4f,
            MeadowEdge = new Color(0.7f, 0.75f, 0.8f, 1f),
            MeadowInner = new Color(0.8f, 0.85f, 0.8f, 1f),
            MeadowInnerWidth = 0.5f
        };

        private static TileGrid Grid(params string[] rows) => MapConverter.FromRows("m", rows).InitialGrid;

        private static Mesh Walls(in TileGrid grid)
        {
            MapMeshStyle style = Style();
            return MapMeshBuilder.BuildBlocks(in grid, TileType.Wall, Height, in style, "walls");
        }

        /// <summary>Sum of the XZ-projected areas of triangles whose face normal has y above <paramref name="minNormalY"/>.</summary>
        private static float UpArea(Mesh m, float minNormalY = 0.01f)
        {
            Vector3[] v = m.vertices;
            int[] t = m.triangles;
            float area = 0f;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (n.normalized.y > minNormalY) area += n.y * 0.5f;
            }
            return area;
        }

        [Test]
        public void SingleWall_IsChamferedBlock_AtTheRightPlace()
        {
            Mesh mesh = Walls(Grid("...", ".#.", "..."));
            try
            {
                Bounds b = mesh.bounds;
                Assert.Less(Vector3.Distance(new Vector3(1.5f, 0.6f, 1.5f), b.center), 1e-5f, b.center.ToString());
                Assert.Less(Vector3.Distance(new Vector3(1f, Height, 1f), b.size), 1e-5f, b.size.ToString());
                Assert.AreEqual(1f, UpArea(mesh), 1e-4f, "top + chamfer cover the cell exactly once");

                Vector3[] v = mesh.vertices;
                Vector3[] n = mesh.normals;
                int bevelFaces = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    bool onEdge = Mathf.Abs(v[i].x - 1f) < 1e-5f || Mathf.Abs(v[i].x - 2f) < 1e-5f ||
                                  Mathf.Abs(v[i].z - 1f) < 1e-5f || Mathf.Abs(v[i].z - 2f) < 1e-5f;
                    if (onEdge) Assert.LessOrEqual(v[i].y, Height - Bevel + 1e-5f, "the cell edge is lowered by the chamfer at " + v[i]);
                    if (Mathf.Abs(v[i].y - Height) < 1e-5f)
                    {
                        Assert.GreaterOrEqual(v[i].x, 1f + Bevel - 1e-5f);
                        Assert.LessOrEqual(v[i].x, 2f - Bevel + 1e-5f);
                    }
                    if (n[i].y > 0.6f && n[i].y < 0.8f) bevelFaces++;
                    Assert.AreEqual(1f, n[i].magnitude, 1e-4f);
                }
                Assert.Greater(bevelFaces, 0, "45° chamfer faces exist");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Walls_TopIsLightStone_SidesWarmWood_DarkerAtTheFoot()
        {
            MapMeshStyle style = Style();
            Mesh mesh = Walls(Grid("...", ".#.", "..."));
            try
            {
                Vector3[] v = mesh.vertices;
                Vector3[] n = mesh.normals;
                Color[] c = mesh.colors;
                Assert.AreEqual(v.Length, c.Length, "vertex colours on every vertex (Arena/* shaders)");
                Color top = style.WallTop.linear, side = style.WallSide.linear;
                Color rim = Color.Lerp(style.WallTop, Color.white, style.WallBevelHighlight).linear;
                int tops = 0, rims = 0, upper = 0, feet = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    Assert.AreEqual(0f, c[i].a, 1e-5f, "environment is matte (alpha = gloss)");
                    if (n[i].y > 0.99f)
                    {
                        AssertColor(top, c[i], "top");
                        tops++;
                    }
                    else if (n[i].y > 0.5f)
                    {
                        AssertColor(rim, c[i], "chamfer");
                        Assert.Greater(c[i].r + c[i].g + c[i].b, top.r + top.g + top.b, "the chamfer is a light rim");
                        rims++;
                    }
                    else if (Mathf.Abs(n[i].y) < 1e-4f)
                    {
                        if (v[i].y > 0.5f)
                        {
                            AssertColor(side, c[i], "side");
                            upper++;
                        }
                        else
                        {
                            float k = style.WallFootShade;
                            AssertColor(new Color(style.WallSide.r * k, style.WallSide.g * k, style.WallSide.b * k, 1f).linear, c[i], "foot");
                            feet++;
                        }
                    }
                }
                Assert.Greater(tops, 0);
                Assert.Greater(rims, 0);
                Assert.Greater(upper, 0);
                Assert.Greater(feet, 0);
                float topLum = top.r + top.g + top.b, sideLum = side.r + side.g + side.b;
                Assert.Greater(topLum, sideLum * 1.5f, "the top is clearly lighter than the sides (volume reads)");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SharedSides_AreSkipped_MapEdgeSides_AreBuilt()
        {
            // Two walls along the south-west map edge: the shared side between them has no face; the map edge has sides
            // (the meadow is visible around the arena).
            Mesh mesh = Walls(Grid("...", "...", "##."));
            Mesh water = MapMeshBuilder.BuildSheet(Grid("...", "...", "##."), TileType.Water, 0.02f, "water");
            try
            {
                Vector3[] v = mesh.vertices;
                Vector3[] n = mesh.normals;
                int[] t = mesh.triangles;
                bool south = false, west = false;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    Vector3 fn = n[t[i]];
                    bool vertical = Mathf.Abs(fn.y) < 1e-4f;
                    if (!vertical) continue;
                    if (Mathf.Abs(a.x - 1f) < 1e-5f && Mathf.Abs(b.x - 1f) < 1e-5f && Mathf.Abs(c.x - 1f) < 1e-5f)
                        Assert.Fail("side face between two walls at x = 1");
                    if (fn.z < -0.99f) south = true;
                    if (fn.x < -0.99f) west = true;
                }
                Assert.IsTrue(south, "south side on the map edge");
                Assert.IsTrue(west, "west side on the map edge");
                Assert.AreEqual(2f, UpArea(mesh), 1e-4f, "two tops cover two cells without gaps or overlaps");
                Assert.AreEqual(0, water.vertexCount, "no water cells");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(water);
            }
        }

        [Test]
        public void InnerCorner_IsMitred_WithoutGaps()
        {
            // L-shaped wall: the inner (concave) corner at (1, 1) must close without holes; the full 3-cell area is covered once.
            Mesh mesh = Walls(Grid("...", "#..", "##."));
            try
            {
                Assert.AreEqual(3f, UpArea(mesh), 1e-4f);
                Vector3[] v = mesh.vertices;
                bool cornerLow = false;
                for (int i = 0; i < v.Length; i++)
                    if (Mathf.Abs(v[i].x - 1f) < 1e-5f && Mathf.Abs(v[i].z - 1f) < 1e-5f && Mathf.Abs(v[i].y - (Height - Bevel)) < 1e-5f)
                        cornerLow = true;
                Assert.IsTrue(cornerLow, "the inner corner point sits at the chamfer foot");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Floor_SkipsWallCells_AndIsDarkerNextToWalls()
        {
            MapMeshStyle style = Style();
            TileGrid grid = Grid(".....", ".....", "#....");
            Mesh floor = MapMeshBuilder.BuildFloor(in grid, TileType.Wall, in style, "floor");
            try
            {
                Assert.AreEqual(14f, UpArea(floor), 1e-4f, "15 cells minus the wall cell");
                Vector3[] v = floor.vertices;
                Color[] c = floor.colors;
                Color near = Color.white, far = Color.white;
                for (int i = 0; i < v.Length; i++)
                {
                    Assert.AreEqual(0f, v[i].y, 1e-6f);
                    if (Vector3.Distance(v[i], new Vector3(1f, 0f, 0.5f)) < 1e-5f) near = c[i];   // touches the wall's east side
                    if (Vector3.Distance(v[i], new Vector3(4f, 0f, 2f)) < 1e-5f) far = c[i];
                }
                Assert.AreEqual(Color.white.r, far.r, 1e-5f, "far from walls: no occlusion");
                Assert.Less(near.g, far.g - 0.2f, "right at a wall: clearly darker");
                Color expected = Color.Lerp(Color.white, style.FloorOcclusionColor.linear, style.FloorWallOcclusion);
                AssertColor(expected, near, "occlusion at the wall");
            }
            finally
            {
                Object.DestroyImmediate(floor);
            }
        }

        [Test]
        public void Meadow_FramesTheArena_AndFadesToTheEdgeColour()
        {
            MapMeshStyle style = Style();
            Mesh meadow = MapMeshBuilder.BuildMeadow(6, 4, in style, "meadow");
            try
            {
                float m = style.MeadowMargin;
                Assert.AreEqual((6f + 2f * m) * (4f + 2f * m) - 24f, UpArea(meadow), 1e-2f, "frame only, the arena itself is not covered");
                Vector3[] v = meadow.vertices;
                Color[] c = meadow.colors;
                bool outer = false, inner = false;
                for (int i = 0; i < v.Length; i++)
                {
                    if (Mathf.Abs(v[i].x + m) < 1e-4f && Mathf.Abs(v[i].z + m) < 1e-4f)
                    {
                        AssertColor(style.MeadowEdge.linear, c[i], "outer edge");
                        outer = true;
                    }
                    if (Mathf.Abs(v[i].x) < 1e-4f && Mathf.Abs(v[i].z) < 1e-4f)
                    {
                        AssertColor(style.MeadowInner.linear, c[i], "arena corner");
                        inner = true;
                    }
                }
                Assert.IsTrue(outer && inner);
            }
            finally
            {
                Object.DestroyImmediate(meadow);
            }
        }

        [Test]
        public void TopFaces_PointUp()
        {
            Mesh mesh = MapMeshBuilder.BuildSheet(Grid("~"), TileType.Water, 0.02f, "water");
            try
            {
                Vector3[] v = mesh.vertices;
                int[] t = mesh.triangles;
                Vector3 n = Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]);
                Assert.Greater(n.y, 0f, "clockwise seen from above = front face up");
                Assert.AreEqual(v.Length, mesh.colors.Length);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static void AssertColor(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, what + " r");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, what + " g");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, what + " b");
        }
    }
}
