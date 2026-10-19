using System;
using UnityEngine;

namespace Arena.Editor.Art.Procedural
{
    /// <summary>
    /// Naive Surface Nets over a signed distance field (negative inside): one vertex per grid cell the surface crosses (the mean of
    /// its edge crossings, then pulled onto the surface along the gradient), one quad per crossed grid edge. Normals are the field
    /// gradient (smooth, no seams). The box must leave at least one empty cell around the surface.
    /// </summary>
    public static class SurfaceNets
    {
        private const int ProjectionIterations = 2;

        private static readonly int[,] CornerOffsets =
        {
            { 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 }, { 0, 0, 1 }, { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 1 }
        };

        /// <summary>Cell edges as corner pairs (corner index bits: x = 1, y = 2, z = 4).</summary>
        private static readonly int[,] Edges =
        {
            { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };

        /// <summary>Adds the surface inside <paramref name="box"/> sampled every <paramref name="step"/>; returns the triangles added.</summary>
        public static int Polygonize(Func<Vector3, float> field, Bounds box, float step, MeshBuilderBuffer buffer, Color32 color, int bone)
        {
            if (step <= 0f) throw new ArgumentOutOfRangeException(nameof(step));
            Vector3 origin = box.min;
            int nx = Mathf.CeilToInt(box.size.x / step) + 1;
            int ny = Mathf.CeilToInt(box.size.y / step) + 1;
            int nz = Mathf.CeilToInt(box.size.z / step) + 1;
            var values = new float[nx * ny * nz];
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
                values[Index(x, y, z, nx, ny)] = field(origin + new Vector3(x, y, z) * step);

            int trianglesBefore = buffer.TriangleCount;
            var cellVertex = new int[(nx - 1) * (ny - 1) * (nz - 1)];
            var corner = new float[8];
            for (int z = 0; z < nz - 1; z++)
            for (int y = 0; y < ny - 1; y++)
            for (int x = 0; x < nx - 1; x++)
            {
                int cell = Index(x, y, z, nx - 1, ny - 1);
                cellVertex[cell] = -1;
                int inside = 0;
                for (int c = 0; c < 8; c++)
                {
                    corner[c] = values[Index(x + CornerOffsets[c, 0], y + CornerOffsets[c, 1], z + CornerOffsets[c, 2], nx, ny)];
                    if (corner[c] < 0f) inside++;
                }
                if (inside == 0 || inside == 8) continue;

                Vector3 sum = Vector3.zero;
                int crossings = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = Edges[e, 0], b = Edges[e, 1];
                    if (corner[a] < 0f == corner[b] < 0f) continue;
                    float t = corner[a] / (corner[a] - corner[b]);
                    Vector3 pa = CornerPoint(a), pb = CornerPoint(b);
                    sum += Vector3.Lerp(pa, pb, t);
                    crossings++;
                }
                Vector3 cellMin = origin + new Vector3(x, y, z) * step;
                Vector3 p = cellMin + sum / crossings * step;
                p = Project(field, p, step, cellMin);
                cellVertex[cell] = buffer.AddVertex(p, Gradient(field, p, step), color, bone);
            }

            // One quad per grid edge with a sign change, joining the four cells around the edge.
            for (int z = 1; z < nz - 1; z++)
            for (int y = 1; y < ny - 1; y++)
            for (int x = 1; x < nx - 1; x++)
            {
                float v0 = values[Index(x, y, z, nx, ny)];
                for (int axis = 0; axis < 3; axis++)
                {
                    int ex = x + (axis == 0 ? 1 : 0), ey = y + (axis == 1 ? 1 : 0), ez = z + (axis == 2 ? 1 : 0);
                    if (ex >= nx || ey >= ny || ez >= nz) continue;
                    float v1 = values[Index(ex, ey, ez, nx, ny)];
                    if (v0 < 0f == v1 < 0f) continue;
                    // Cells sharing the edge: offsets −1/0 along the two other axes.
                    int ua = (axis + 1) % 3, va = (axis + 2) % 3;
                    int c00 = CellAt(cellVertex, x, y, z, ua, 1, va, 1, nx, ny);
                    int c10 = CellAt(cellVertex, x, y, z, ua, 0, va, 1, nx, ny);
                    int c11 = CellAt(cellVertex, x, y, z, ua, 0, va, 0, nx, ny);
                    int c01 = CellAt(cellVertex, x, y, z, ua, 1, va, 0, nx, ny);
                    if (c00 < 0 || c10 < 0 || c11 < 0 || c01 < 0) continue;
                    buffer.AddQuad(c00, c10, c11, c01);
                }
            }
            return buffer.TriangleCount - trianglesBefore;
        }

        /// <summary>Unit gradient of the field by central differences (outward normal).</summary>
        public static Vector3 Gradient(Func<Vector3, float> field, Vector3 p, float step)
        {
            float h = step * 0.25f;
            var g = new Vector3(
                field(p + new Vector3(h, 0f, 0f)) - field(p - new Vector3(h, 0f, 0f)),
                field(p + new Vector3(0f, h, 0f)) - field(p - new Vector3(0f, h, 0f)),
                field(p + new Vector3(0f, 0f, h)) - field(p - new Vector3(0f, 0f, h)));
            float m = g.magnitude;
            return m > 1e-9f ? g / m : Vector3.up;
        }

        private static Vector3 Project(Func<Vector3, float> field, Vector3 p, float step, Vector3 cellMin)
        {
            for (int i = 0; i < ProjectionIterations; i++)
            {
                float d = field(p);
                Vector3 n = Gradient(field, p, step);
                p -= n * d;
            }
            // Stay in (or just around) the own cell: keeps the quads from folding over.
            Vector3 lo = cellMin - Vector3.one * (step * 0.25f), hi = cellMin + Vector3.one * (step * 1.25f);
            return new Vector3(Mathf.Clamp(p.x, lo.x, hi.x), Mathf.Clamp(p.y, lo.y, hi.y), Mathf.Clamp(p.z, lo.z, hi.z));
        }

        private static Vector3 CornerPoint(int c) => new Vector3(CornerOffsets[c, 0], CornerOffsets[c, 1], CornerOffsets[c, 2]);

        private static int CellAt(int[] cellVertex, int x, int y, int z, int ua, int du, int va, int dv, int nx, int ny)
        {
            int cx = x, cy = y, cz = z;
            if (ua == 0) cx -= du; else if (ua == 1) cy -= du; else cz -= du;
            if (va == 0) cx -= dv; else if (va == 1) cy -= dv; else cz -= dv;
            return cellVertex[Index(cx, cy, cz, nx - 1, ny - 1)];
        }

        private static int Index(int x, int y, int z, int sx, int sy) => (z * sy + y) * sx + x;
    }
}
