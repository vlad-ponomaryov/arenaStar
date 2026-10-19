using System.Collections.Generic;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Art
{
    /// <summary>Topology checks shared by the procedural mesh tests.</summary>
    public static class MeshAssert
    {
        /// <summary>A closed surface: every edge (by vertex indices) belongs to exactly two triangles.</summary>
        public static void EveryEdgeHasTwoTriangles(IList<int> triangles, string label)
        {
            var counts = new Dictionary<long, int>();
            for (int t = 0; t < triangles.Count; t += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = triangles[t + e], b = triangles[t + (e + 1) % 3];
                if (a == b) continue;
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
            foreach (KeyValuePair<long, int> kv in counts)
                Assert.AreEqual(2, kv.Value, label + ": edge " + (kv.Key >> 32) + "-" + (kv.Key & 0xffffffff));
        }
    }
}
