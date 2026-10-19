using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.Editor.Art.Procedural
{
    /// <summary>
    /// Quadric edge-collapse simplification (Garland–Heckbert) of a closed triangle mesh in a <see cref="MeshBuilderBuffer"/>:
    /// cheapest edge first, the new vertex at the quadric optimum (or the better endpoint / midpoint), collapses that would fold a
    /// triangle, pinch the surface (link condition) or touch a non-manifold edge are skipped. Edges between differently coloured
    /// vertices cost extra (<see cref="ColorWeight"/>), so colour borders keep their vertices and stay crisp; a small length term
    /// (<see cref="LengthWeight"/>) keeps the triangles even. Used by <see cref="SdfSculpt"/>: sculpt fine, then fit the budget.
    /// </summary>
    public static class MeshDecimator
    {
        /// <summary>Extra cost of collapsing a colour border, × (max channel difference 0–1) × length².</summary>
        public const double ColorWeight = 0.08;
        /// <summary>Extra cost per length² of the collapsed edge (even triangle sizes).</summary>
        public const double LengthWeight = 0.004;
        /// <summary>Colour difference (0–1) above which the collapsed vertex stays at an endpoint (keeps the border in place).</summary>
        private const float ColorPinThreshold = 0.15f;
        /// <summary>A collapse may not turn a triangle's normal by more than this (cosine).</summary>
        private const float MinNormalDot = 0.25f;
        /// <summary>Triangle shape (4√3·area / Σ edge²: 1 = equilateral) a collapse may not go below.</summary>
        private const float MinQuality = 0.08f;

        private struct Entry
        {
            public double Cost;
            public int A, B, StampA, StampB;
            public Vector3 Target;
        }

        /// <summary>Simplifies <paramref name="mesh"/> in place to at most <paramref name="targetTriangles"/> (if the topology allows); returns the triangle count.</summary>
        public static int Simplify(MeshBuilderBuffer mesh, int targetTriangles)
        {
            int vCount = mesh.VertexCount;
            int tCount = mesh.TriangleCount;
            if (tCount <= targetTriangles) return tCount;

            var pos = mesh.Positions.ToArray();
            var tri = mesh.Triangles.ToArray();
            var triAlive = new bool[tCount];
            var vAlive = new bool[vCount];
            var stamp = new int[vCount];
            var q = new double[vCount * 10];
            var vTris = new List<int>[vCount];
            for (int v = 0; v < vCount; v++) vTris[v] = new List<int>(8);
            for (int t = 0; t < tCount; t++)
            {
                triAlive[t] = true;
                int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                vTris[a].Add(t);
                vTris[b].Add(t);
                vTris[c].Add(t);
                vAlive[a] = vAlive[b] = vAlive[c] = true;
                Vector3 n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                float len = n.magnitude;
                if (len < 1e-12f) continue;
                n /= len;
                double d = -Vector3.Dot(n, pos[a]);
                AddPlane(q, a, n, d);
                AddPlane(q, b, n, d);
                AddPlane(q, c, n, d);
            }

            var colors = mesh.Colors;
            // Vertices on a colour border stay where the fine sculpt put them: a collapse with one moves onto it.
            var border = new bool[vCount];
            for (int t = 0; t < tCount; t++)
            for (int e = 0; e < 3; e++)
            {
                int a = tri[3 * t + e], b = tri[3 * t + (e + 1) % 3];
                if (ColorDifference(colors[a], colors[b]) > ColorPinThreshold) border[a] = border[b] = true;
            }
            var heap = new List<Entry>(tCount * 2);
            var neighbours = new List<int>(16);
            for (int t = 0; t < tCount; t++)
            for (int e = 0; e < 3; e++)
            {
                int a = tri[3 * t + e], b = tri[3 * t + (e + 1) % 3];
                if (a < b) Push(heap, Evaluate(a, b, pos, q, colors, border, stamp));
            }

            int alive = tCount;
            var shared = new List<int>(4);
            var scratchA = new List<int>(16);
            var scratchB = new List<int>(16);
            while (alive > targetTriangles && heap.Count > 0)
            {
                Entry top = Pop(heap);
                int a = top.A, b = top.B;
                if (!vAlive[a] || !vAlive[b] || stamp[a] != top.StampA || stamp[b] != top.StampB) continue;

                // Manifold edge with exactly two triangles, and the link condition (only the two opposite vertices are common).
                shared.Clear();
                foreach (int t in vTris[a])
                    if (Contains(tri, t, b)) shared.Add(t);
                if (shared.Count != 2) continue;
                Ring(a, vTris, tri, scratchA);
                Ring(b, vTris, tri, scratchB);
                int common = 0;
                foreach (int n in scratchA)
                    if (scratchB.Contains(n)) common++;
                if (common != 2) continue;
                if (vTris[a].Count + vTris[b].Count <= 6) continue; // would collapse a tetrahedron-like cap

                Vector3 p = top.Target;
                if (Folds(a, b, p, vTris, tri, pos) || Folds(b, a, p, vTris, tri, pos)) continue;

                // Collapse b into a.
                foreach (int t in shared)
                {
                    triAlive[t] = false;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tri[3 * t + k];
                        if (v != a && v != b) vTris[v].Remove(t);
                    }
                    vTris[a].Remove(t);
                    alive--;
                }
                foreach (int t in vTris[b])
                {
                    if (!triAlive[t]) continue;
                    for (int k = 0; k < 3; k++)
                        if (tri[3 * t + k] == b) tri[3 * t + k] = a;
                    vTris[a].Add(t);
                }
                vTris[b].Clear();
                vAlive[b] = false;
                bool nearerB = Vector3.SqrMagnitude(p - pos[b]) < Vector3.SqrMagnitude(p - pos[a]);
                pos[a] = p;
                for (int k = 0; k < 10; k++) q[a * 10 + k] += q[b * 10 + k];
                stamp[a]++;
                border[a] |= border[b] || ColorDifference(colors[a], colors[b]) > ColorPinThreshold;
                if (nearerB) colors[a] = colors[b];

                Ring(a, vTris, tri, neighbours);
                foreach (int n in neighbours) Push(heap, Evaluate(Mathf.Min(a, n), Mathf.Max(a, n), pos, q, colors, border, stamp));
            }

            // Compact.
            var remap = new int[vCount];
            var newPos = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newColors = new List<Color32>();
            var newBones = new List<int>();
            for (int v = 0; v < vCount; v++)
            {
                remap[v] = -1;
                if (!vAlive[v] || vTris[v].Count == 0) continue;
                remap[v] = newPos.Count;
                newPos.Add(pos[v]);
                newNormals.Add(mesh.Normals[v]);
                newColors.Add(colors[v]);
                newBones.Add(mesh.Bones[v]);
            }
            var newTris = new List<int>(alive * 3);
            for (int t = 0; t < tCount; t++)
            {
                if (!triAlive[t]) continue;
                newTris.Add(remap[tri[3 * t]]);
                newTris.Add(remap[tri[3 * t + 1]]);
                newTris.Add(remap[tri[3 * t + 2]]);
            }
            mesh.Clear();
            mesh.Positions.AddRange(newPos);
            mesh.Normals.AddRange(newNormals);
            mesh.Colors.AddRange(newColors);
            mesh.Bones.AddRange(newBones);
            mesh.Triangles.AddRange(newTris);
            return mesh.TriangleCount;
        }

        private static Entry Evaluate(int a, int b, Vector3[] pos, double[] q, List<Color32> colors, bool[] border, int[] stamp)
        {
            var m = new double[10];
            for (int k = 0; k < 10; k++) m[k] = q[a * 10 + k] + q[b * 10 + k];
            Vector3 pa = pos[a], pb = pos[b], mid = (pa + pb) * 0.5f;
            float len2 = (pb - pa).sqrMagnitude;

            Vector3 best = mid;
            double bestErr = Error(m, mid);
            double ea = Error(m, pa), eb = Error(m, pb);
            if (ea < bestErr) { bestErr = ea; best = pa; }
            if (eb < bestErr) { bestErr = eb; best = pb; }
            if (Optimum(m, out Vector3 opt) && (opt - mid).sqrMagnitude <= len2)
            {
                double eo = Error(m, opt);
                if (eo < bestErr) { bestErr = eo; best = opt; }
            }
            // Across a colour border the vertex stays on one side of it (no smeared borders).
            float colour = ColorDifference(colors[a], colors[b]);
            if (border[a] != border[b]) best = border[a] ? pa : pb;
            else if (border[a] || colour > ColorPinThreshold) best = ea <= eb ? pa : pb;
            double cost = Math.Max(0.0, Error(m, best)) + len2 * (LengthWeight + ColorWeight * colour);
            return new Entry { Cost = cost, A = a, B = b, StampA = stamp[a], StampB = stamp[b], Target = best };
        }

        /// <summary>True if moving <paramref name="v"/> (collapsing with <paramref name="other"/>) to p folds or slivers a surviving triangle.</summary>
        private static bool Folds(int v, int other, Vector3 p, List<int>[] vTris, int[] tri, Vector3[] pos)
        {
            foreach (int t in vTris[v])
            {
                if (Contains(tri, t, other)) continue; // removed by the collapse
                Vector3 a = pos[tri[3 * t]], b = pos[tri[3 * t + 1]], c = pos[tri[3 * t + 2]];
                Vector3 before = Vector3.Cross(b - a, c - a);
                if (tri[3 * t] == v) a = p;
                else if (tri[3 * t + 1] == v) b = p;
                else c = p;
                Vector3 after = Vector3.Cross(b - a, c - a);
                float la = after.magnitude, lb = before.magnitude;
                if (la < 1e-12f) return true;
                if (lb > 1e-12f && Vector3.Dot(before, after) < MinNormalDot * la * lb) return true;
                float edges = (b - a).sqrMagnitude + (c - b).sqrMagnitude + (a - c).sqrMagnitude;
                if (2f * 1.7320508f * la / edges < MinQuality) return true; // area = la / 2
            }
            return false;
        }

        private static void Ring(int v, List<int>[] vTris, int[] tri, List<int> result)
        {
            result.Clear();
            foreach (int t in vTris[v])
                for (int k = 0; k < 3; k++)
                {
                    int n = tri[3 * t + k];
                    if (n != v && !result.Contains(n)) result.Add(n);
                }
        }

        private static bool Contains(int[] tri, int t, int v) => tri[3 * t] == v || tri[3 * t + 1] == v || tri[3 * t + 2] == v;

        private static float ColorDifference(Color32 x, Color32 y) =>
            Mathf.Max(Mathf.Abs(x.r - y.r), Mathf.Max(Mathf.Abs(x.g - y.g), Mathf.Abs(x.b - y.b))) / 255f;

        private static void AddPlane(double[] q, int v, Vector3 n, double d)
        {
            int o = v * 10;
            q[o] += n.x * n.x; q[o + 1] += n.x * n.y; q[o + 2] += n.x * n.z; q[o + 3] += n.x * d;
            q[o + 4] += n.y * n.y; q[o + 5] += n.y * n.z; q[o + 6] += n.y * d;
            q[o + 7] += n.z * n.z; q[o + 8] += n.z * d;
            q[o + 9] += d * d;
        }

        private static double Error(double[] m, Vector3 p)
        {
            double x = p.x, y = p.y, z = p.z;
            return m[0] * x * x + 2 * m[1] * x * y + 2 * m[2] * x * z + 2 * m[3] * x
                   + m[4] * y * y + 2 * m[5] * y * z + 2 * m[6] * y
                   + m[7] * z * z + 2 * m[8] * z + m[9];
        }

        private static bool Optimum(double[] m, out Vector3 p)
        {
            double a = m[0], b = m[1], c = m[2], d = m[4], e = m[5], f = m[7];
            double det = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
            p = Vector3.zero;
            if (Math.Abs(det) < 1e-12) return false;
            double r0 = -m[3], r1 = -m[6], r2 = -m[8];
            double x = (r0 * (d * f - e * e) - b * (r1 * f - e * r2) + c * (r1 * e - d * r2)) / det;
            double y = (a * (r1 * f - e * r2) - r0 * (b * f - e * c) + c * (b * r2 - r1 * c)) / det;
            double z = (a * (d * r2 - r1 * e) - b * (b * r2 - r1 * c) + r0 * (b * e - d * c)) / det;
            p = new Vector3((float)x, (float)y, (float)z);
            return true;
        }

        private static void Push(List<Entry> heap, Entry e)
        {
            heap.Add(e);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].Cost <= e.Cost) break;
                heap[i] = heap[parent];
                i = parent;
            }
            heap[i] = e;
        }

        private static Entry Pop(List<Entry> heap)
        {
            Entry top = heap[0];
            Entry last = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count == 0) return top;
            int i = 0;
            int count = heap.Count;
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= count) break;
                int r = l + 1;
                int child = r < count && heap[r].Cost < heap[l].Cost ? r : l;
                if (heap[child].Cost >= last.Cost) break;
                heap[i] = heap[child];
                i = child;
            }
            heap[i] = last;
            return top;
        }
    }
}
