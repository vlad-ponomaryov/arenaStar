using System.Collections.Generic;
using Arena.Core.Map;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Builds combined meshes of the static map parts at match start (one mesh per material, D-014): all walls as boxes
    /// (top + the sides that do not touch another wall), all water cells as one flat sheet. Coordinates per D-009:
    /// cell (c, r) spans world x ∈ [c, c+1], z ∈ [r, r+1]. Allocates only here (load time), never per frame.
    /// </summary>
    public static class MapMeshBuilder
    {
        /// <summary>Boxes of height <paramref name="height"/> over every cell of <paramref name="type"/>.</summary>
        public static Mesh BuildBlocks(in TileGrid grid, TileType type, float height, string name)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != type) continue;
                    float x0 = c, x1 = c + 1f, z0 = r, z1 = r + 1f;
                    AddQuad(verts, normals, tris, new Vector3(x0, height, z0), new Vector3(x0, height, z1),
                        new Vector3(x1, height, z1), new Vector3(x1, height, z0), Vector3.up);
                    if (grid.InBounds(c, r - 1) && grid.Get(c, r - 1) != type)
                        AddQuad(verts, normals, tris, new Vector3(x0, 0f, z0), new Vector3(x0, height, z0),
                            new Vector3(x1, height, z0), new Vector3(x1, 0f, z0), Vector3.back);
                    if (grid.InBounds(c, r + 1) && grid.Get(c, r + 1) != type)
                        AddQuad(verts, normals, tris, new Vector3(x1, 0f, z1), new Vector3(x1, height, z1),
                            new Vector3(x0, height, z1), new Vector3(x0, 0f, z1), Vector3.forward);
                    if (grid.InBounds(c - 1, r) && grid.Get(c - 1, r) != type)
                        AddQuad(verts, normals, tris, new Vector3(x0, 0f, z1), new Vector3(x0, height, z1),
                            new Vector3(x0, height, z0), new Vector3(x0, 0f, z0), Vector3.left);
                    if (grid.InBounds(c + 1, r) && grid.Get(c + 1, r) != type)
                        AddQuad(verts, normals, tris, new Vector3(x1, 0f, z0), new Vector3(x1, height, z0),
                            new Vector3(x1, height, z1), new Vector3(x1, 0f, z1), Vector3.right);
                }
            }
            return Create(name, verts, normals, tris);
        }

        /// <summary>One upward quad per cell of <paramref name="type"/> at <paramref name="height"/> (water sheet).</summary>
        public static Mesh BuildSheet(in TileGrid grid, TileType type, float height, string name)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != type) continue;
                    float x0 = c, x1 = c + 1f, z0 = r, z1 = r + 1f;
                    AddQuad(verts, normals, tris, new Vector3(x0, height, z0), new Vector3(x0, height, z1),
                        new Vector3(x1, height, z1), new Vector3(x1, height, z0), Vector3.up);
                }
            }
            return Create(name, verts, normals, tris);
        }

        /// <summary>Quad a-b-c-d listed clockwise as seen from the side the normal points to (Unity front face).</summary>
        private static void AddQuad(List<Vector3> verts, List<Vector3> normals, List<int> tris, Vector3 a, Vector3 b, Vector3 c,
            Vector3 d, Vector3 normal)
        {
            int i = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            verts.Add(d);
            for (int k = 0; k < 4; k++) normals.Add(normal);
            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 3);
        }

        private static Mesh Create(string name, List<Vector3> verts, List<Vector3> normals, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
