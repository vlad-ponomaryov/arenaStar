using Arena.Editor.Art.Procedural;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Art
{
    /// <summary>Surface Nets and the sculpted groups (Art-Spec §6.2, §8): accuracy, closed surface, normals, triangle budget.</summary>
    public sealed class SurfaceNetsTests
    {
        private static readonly Color32 White = new Color32(255, 255, 255, 0);

        [Test]
        public void Sphere_IsWithinOneStep_ClosedAndOutward()
        {
            const float r = 0.5f, step = 0.05f;
            var buffer = new MeshBuilderBuffer();
            int tris = SurfaceNets.Polygonize(p => p.magnitude - r, new Bounds(Vector3.zero, Vector3.one * 1.4f), step, buffer, White, 3);
            Assert.Greater(tris, 100);
            Assert.AreEqual(tris, buffer.TriangleCount);
            for (int i = 0; i < buffer.VertexCount; i++)
            {
                Vector3 v = buffer.Positions[i];
                Assert.LessOrEqual(Mathf.Abs(v.magnitude - r), step, "vertex " + i);
                Assert.AreEqual(1f, buffer.Normals[i].magnitude, 1e-3f, "normal " + i);
                Assert.Greater(Vector3.Dot(buffer.Normals[i], v.normalized), 0.98f, "normal " + i + " is radial");
                Assert.AreEqual(3, buffer.Bones[i]);
            }
            for (int t = 0; t < buffer.Triangles.Count; t += 3)
            {
                Vector3 a = buffer.Positions[buffer.Triangles[t]], b = buffer.Positions[buffer.Triangles[t + 1]],
                    c = buffer.Positions[buffer.Triangles[t + 2]];
                Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c), 0f, "triangle " + t / 3 + " faces out");
            }
            MeshAssert.EveryEdgeHasTwoTriangles(buffer.Triangles, "sphere");
        }

        [Test]
        public void SmoothMin_RoundsOnlyNearTheSeam()
        {
            Assert.AreEqual(1f, SdfSculpt.SmoothMin(1f, 3f, 0.1f), 1e-6f);
            Assert.AreEqual(1f - 0.1f * 0.25f, SdfSculpt.SmoothMin(1f, 1f, 0.1f), 1e-6f);
            Assert.AreEqual(1f, SdfSculpt.SmoothMin(1f, 1f, 0f), 1e-6f);
        }

        [Test]
        public void Sculpt_FitsTheBudget_IsClosed_AndPaints()
        {
            var sculpt = new SdfSculpt(0.08f);
            var red = new Color32(200, 0, 0, 0);
            var blue = new Color32(0, 0, 200, 0);
            var green = new Color32(0, 200, 0, 0);
            sculpt.Add(new SdfPrimitive(PartShape.Ellipsoid, SdfOp.Union, Vector3.zero, Quaternion.identity, Vector3.one,
                Vector3.one * 0.3f, 0f, red));
            sculpt.Add(new SdfPrimitive(PartShape.Ellipsoid, SdfOp.Union, new Vector3(0.4f, 0f, 0f), Quaternion.identity, Vector3.one,
                Vector3.one * 0.2f, 0f, blue));
            sculpt.Add(new SdfPrimitive(PartShape.Ellipsoid, SdfOp.Paint, new Vector3(0f, 0.3f, 0f), Quaternion.identity, Vector3.one,
                Vector3.one * 0.1f, 0f, green));
            var buffer = new MeshBuilderBuffer();
            const int budget = 800;
            int tris = sculpt.Build(buffer, 1, budget);
            Assert.LessOrEqual(tris, budget);
            Assert.GreaterOrEqual(tris, budget * 0.6f, "the step is refined towards the budget");
            MeshAssert.EveryEdgeHasTwoTriangles(buffer.Triangles, "two blobs");

            Assert.AreEqual(200, sculpt.Colour(new Vector3(-0.3f, 0f, 0f), 0.02f).r, "nearest volume");
            Assert.AreEqual(200, sculpt.Colour(new Vector3(0.6f, 0f, 0f), 0.02f).b, "nearest volume");
            Assert.AreEqual(200, sculpt.Colour(new Vector3(0f, 0.3f, 0f), 0.02f).g, "paint on top");
            Assert.AreEqual(0f, sculpt.Occlusion(new Vector3(-0.3f, 0f, 0f), Vector3.left, 0.03f), 1e-3f, "open side");
            Assert.Greater(sculpt.Occlusion(new Vector3(0.27f, 0.16f, 0f), Vector3.up, 0.03f), 0f, "crease between the blobs");
        }

        [Test]
        public void Cut_CarvesTheSurface()
        {
            var sculpt = new SdfSculpt(0.02f);
            sculpt.Add(new SdfPrimitive(PartShape.Ellipsoid, SdfOp.Union, Vector3.zero, Quaternion.identity, Vector3.one,
                Vector3.one * 0.3f, 0f, White));
            sculpt.Add(new SdfPrimitive(PartShape.Ellipsoid, SdfOp.Subtract, new Vector3(0f, 0f, 0.3f), Quaternion.identity, Vector3.one,
                Vector3.one * 0.1f, 0f, White));
            Assert.Greater(sculpt.Distance(new Vector3(0f, 0f, 0.29f)), 0f, "inside the cut is outside the surface");
            Assert.Less(sculpt.Distance(Vector3.zero), 0f);
        }

        [Test]
        public void Decimator_FitsTheBudget_KeepsTheSphereClosedAndRound()
        {
            const float r = 0.5f;
            var buffer = new MeshBuilderBuffer();
            int fine = SurfaceNets.Polygonize(p => p.magnitude - r, new Bounds(Vector3.zero, Vector3.one * 1.3f), 0.025f, buffer, White, 0);
            const int budget = 600;
            Assert.Greater(fine, budget * 4);
            int tris = MeshDecimator.Simplify(buffer, budget);
            Assert.LessOrEqual(tris, budget);
            Assert.Greater(tris, budget * 0.9f);
            Assert.AreEqual(tris, buffer.TriangleCount);
            MeshAssert.EveryEdgeHasTwoTriangles(buffer.Triangles, "decimated sphere");
            for (int i = 0; i < buffer.VertexCount; i++)
                Assert.AreEqual(r, buffer.Positions[i].magnitude, 0.02f, "vertex " + i + " stays on the sphere");
            for (int t = 0; t < buffer.Triangles.Count; t += 3)
            {
                Vector3 a = buffer.Positions[buffer.Triangles[t]], b = buffer.Positions[buffer.Triangles[t + 1]],
                    c = buffer.Positions[buffer.Triangles[t + 2]];
                Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c), 0f, "triangle " + t / 3 + " faces out");
            }
        }

        [Test]
        public void Decimator_KeepsColourBorders()
        {
            const float r = 0.5f;
            var buffer = new MeshBuilderBuffer();
            SurfaceNets.Polygonize(p => p.magnitude - r, new Bounds(Vector3.zero, Vector3.one * 1.3f), 0.025f, buffer, White, 0);
            var red = new Color32(255, 0, 0, 0);
            for (int i = 0; i < buffer.VertexCount; i++) buffer.Colors[i] = buffer.Positions[i].y > 0f ? red : White;
            MeshDecimator.Simplify(buffer, 400);
            // Every triangle crossing the equator keeps a vertex within ≈ 2 fine steps of it (edges here are ≈ 0.12): the border is not
            // smeared over big triangles.
            for (int t = 0; t < buffer.Triangles.Count; t += 3)
            {
                float lo = float.MaxValue, hi = float.MinValue, nearest = float.MaxValue;
                for (int k = 0; k < 3; k++)
                {
                    float y = buffer.Positions[buffer.Triangles[t + k]].y;
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                    nearest = Mathf.Min(nearest, Mathf.Abs(y));
                }
                if (lo < 0f && hi > 0f) Assert.Less(nearest, 0.07f, "triangle " + t / 3 + " y " + lo + ".." + hi + " " + buffer.Positions[buffer.Triangles[t]].ToString("F3") + buffer.Positions[buffer.Triangles[t + 1]].ToString("F3") + buffer.Positions[buffer.Triangles[t + 2]].ToString("F3"));
            }
        }
    }
}
