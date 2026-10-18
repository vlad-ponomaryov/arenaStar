using Arena.Core.Map;
using Arena.Data.Conversion;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class MapMeshBuilderTests
    {
        private static int Quads(Mesh m) => m.vertexCount / 4;

        [Test]
        public void SingleWall_IsTopPlusFourSides_AtTheRightPlace()
        {
            TileGrid grid = MapConverter.FromRows("m", new[] { "...", ".#.", "..." }).InitialGrid;
            Mesh mesh = MapMeshBuilder.BuildBlocks(in grid, TileType.Wall, 1.2f, "walls");
            try
            {
                Assert.AreEqual(5, Quads(mesh));
                Assert.AreEqual(30, mesh.triangles.Length);
                Bounds b = mesh.bounds;
                Assert.Less(Vector3.Distance(new Vector3(1.5f, 0.6f, 1.5f), b.center), 1e-5f, b.center.ToString());
                Assert.Less(Vector3.Distance(new Vector3(1f, 1.2f, 1f), b.size), 1e-5f, b.size.ToString());
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SharedSides_AndMapEdgeSides_AreSkipped()
        {
            TileGrid grid = MapConverter.FromRows("m", new[] { "...", "...", "##." }).InitialGrid;
            Mesh mesh = MapMeshBuilder.BuildBlocks(in grid, TileType.Wall, 1f, "walls");
            Mesh water = MapMeshBuilder.BuildSheet(in grid, TileType.Water, 0.02f, "water");
            try
            {
                // 2 tops + north of both + east of the right one (west and south are the map edge, the middle is shared).
                Assert.AreEqual(5, Quads(mesh));
                Assert.AreEqual(0, water.vertexCount, "no water cells");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(water);
            }
        }

        [Test]
        public void TopFaces_PointUp()
        {
            TileGrid grid = MapConverter.FromRows("m", new[] { "~" }).InitialGrid;
            Mesh mesh = MapMeshBuilder.BuildSheet(in grid, TileType.Water, 0.02f, "water");
            try
            {
                Vector3[] v = mesh.vertices;
                int[] t = mesh.triangles;
                Vector3 n = Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]);
                Assert.Greater(n.y, 0f, "clockwise seen from above = front face up");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
