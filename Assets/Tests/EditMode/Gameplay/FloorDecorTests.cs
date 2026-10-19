using System.Collections.Generic;
using Arena.Core.Map;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Art;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>Floor details as geometry (A.3e, D-130): placement, symmetry, budget.</summary>
    public sealed class FloorDecorTests
    {
        private static EnvDecorStyle CatalogStyle()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            Assert.IsNotNull(catalog);
            return catalog.DecorStyle;
        }

        private static IEnumerable<TileGrid> RealMaps()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:MapConfig"))
            {
                var config = AssetDatabase.LoadAssetAtPath<MapConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (config != null) yield return MapConverter.ToMapDef(config).InitialGrid;
            }
        }

        private static TileGrid Symmetric() => MapConverter.FromRows("m", new[]
        {
            "...........",
            ".##...\"\"\"..",
            ".##.......%",
            "....~~.....",
            "...........",
            ".....#.....",
            "...........",
            ".....~~....",
            "%.......##.",
            "..\"\"\"...##.",
            "..........."
        }).InitialGrid;

        [Test]
        public void Decor_IsOnlyOnFreeFloor_SymmetricAndInBudget_OnRealMaps()
        {
            EnvDecorStyle style = CatalogStyle();
            var items = new List<FloorDecorItem>();
            int maps = 0;
            foreach (TileGrid grid in RealMaps())
            {
                maps++;
                int triangles = FloorDecorPlanner.Plan(in grid, in style, items);
                Assert.Greater(items.Count, 0, "details planned");
                Assert.LessOrEqual(triangles, 5000, "brief §3.3 budget");
                int sum = 0;
                bool stones = false;
                foreach (FloorDecorItem item in items)
                {
                    sum += FloorDecorMeshBuilder.TrianglesOf(item.Kind);
                    stones |= item.Kind == FloorDecorKind.Stone;
                    Vector2 p = item.Position;
                    Assert.AreEqual(TileType.Floor, grid.Get(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)),
                        item.Kind + " at " + p + " is not on open floor");
                }
                Assert.AreEqual(triangles, sum);
                Assert.IsTrue(stones, "a stepping-stone path");
                AssertSymmetric(items, grid);
            }
            Assert.Greater(maps, 0);
        }

        [Test]
        public void Decor_IsDeterministic_AndTurnsWithTheMap()
        {
            EnvDecorStyle style = CatalogStyle();
            TileGrid grid = Symmetric();
            var a = new List<FloorDecorItem>();
            var b = new List<FloorDecorItem>();
            FloorDecorPlanner.Plan(in grid, in style, a);
            FloorDecorPlanner.Plan(in grid, in style, b);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i].Position, b[i].Position);
            AssertSymmetric(a, grid);
            foreach (FloorDecorItem item in a)
                Assert.IsTrue(FloorDecorPlanner.IsFree(in grid, item.Position, 0f), "on free floor: " + item.Position);
        }

        [Test]
        public void DecorMesh_HasColoursAndTheCountedTriangles()
        {
            EnvDecorStyle style = CatalogStyle();
            TileGrid grid = Symmetric();
            var items = new List<FloorDecorItem>();
            int triangles = FloorDecorPlanner.Plan(in grid, in style, items);
            Mesh mesh = FloorDecorMeshBuilder.Build(items, in style, "Decor");
            try
            {
                Assert.AreEqual(triangles, mesh.triangles.Length / 3);
                Assert.AreEqual(mesh.vertexCount, mesh.colors.Length);
                Assert.Less(mesh.bounds.max.y, 0.2f, "details lie flat on the sand");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static void AssertSymmetric(List<FloorDecorItem> items, TileGrid grid)
        {
            foreach (FloorDecorItem item in items)
            {
                Vector2 twin = FloorDecorPlanner.Mirror(item.Position, grid.Width, grid.Height);
                bool found = false;
                foreach (FloorDecorItem other in items)
                {
                    if (other.Kind != item.Kind || (other.Position - twin).sqrMagnitude > 1e-6f) continue;
                    found = Mathf.Abs(other.Size - item.Size) < 1e-5f;
                    if (found) break;
                }
                Assert.IsTrue(found, item.Kind + " at " + item.Position + " has no 180° twin");
            }
        }
    }
}
