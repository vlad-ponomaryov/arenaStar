using System.Collections.Generic;
using Arena.Editor.Art.Procedural;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Art
{
    /// <summary>Primitive meshes of the procedural models (Art-Spec §6.2): on the same surface as the distance field, outward, closed.</summary>
    public sealed class ProcMeshTests
    {
        private static readonly Color32 White = new Color32(255, 255, 255, 0);

        private static IEnumerable<(PartShape shape, Vector3 size, float radius)> Shapes()
        {
            yield return (PartShape.Ellipsoid, new Vector3(0.3f, 0.2f, 0.25f), 0f);
            yield return (PartShape.RoundCone, new Vector3(0.15f, 0.4f, 0.05f), 0f);
            yield return (PartShape.RoundCone, new Vector3(0.1f, 0.3f, 0.1f), 0f);
            yield return (PartShape.RoundedBox, new Vector3(0.2f, 0.1f, 0.3f), 0.05f);
            yield return (PartShape.Torus, new Vector3(0.3f, 0f, 0f), 0.05f);
        }

        [Test]
        public void Vertices_LieOnTheDistanceFieldSurface_WithUnitOutwardNormals()
        {
            Quaternion rot = Quaternion.Euler(20f, 35f, -10f);
            var pos = new Vector3(0.3f, 1f, -0.2f);
            foreach (var (shape, size, radius) in Shapes())
            {
                var buffer = new MeshBuilderBuffer();
                ProcMesh.Add(buffer, shape, size, radius, Matrix4x4.TRS(pos, rot, Vector3.one), 12, White, 0);
                var sdf = new SdfPrimitive(shape, SdfOp.Union, pos, rot, Vector3.one, size, radius, White);
                Assert.Greater(buffer.TriangleCount, 0, shape.ToString());
                for (int i = 0; i < buffer.VertexCount; i++)
                {
                    Assert.AreEqual(0f, sdf.Distance(buffer.Positions[i]), 2e-3f, shape + " vertex " + i);
                    Assert.AreEqual(1f, buffer.Normals[i].magnitude, 1e-3f, shape + " normal " + i);
                    Vector3 g = SurfaceNets.Gradient(sdf.Distance, buffer.Positions[i], 0.01f);
                    Assert.Greater(Vector3.Dot(g, buffer.Normals[i]), 0.9f, shape + " normal direction " + i);
                }
            }
        }

        [Test]
        public void Triangles_FaceOutwards()
        {
            foreach (var (shape, size, radius) in Shapes())
            {
                var buffer = new MeshBuilderBuffer();
                ProcMesh.Add(buffer, shape, size, radius, Matrix4x4.identity, 12, White, 0);
                var sdf = new SdfPrimitive(shape, SdfOp.Union, Vector3.zero, Quaternion.identity, Vector3.one, size, radius, White);
                for (int t = 0; t < buffer.Triangles.Count; t += 3)
                {
                    Vector3 a = buffer.Positions[buffer.Triangles[t]], b = buffer.Positions[buffer.Triangles[t + 1]],
                        c = buffer.Positions[buffer.Triangles[t + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    if (face.sqrMagnitude < 1e-12f) continue;
                    Vector3 g = SurfaceNets.Gradient(sdf.Distance, (a + b + c) / 3f, 0.01f);
                    Assert.Greater(Vector3.Dot(face.normalized, g), 0f, shape + " triangle " + t / 3);
                }
            }
        }

        [Test]
        public void LatheShapesAndTorus_AreClosed()
        {
            foreach (var (shape, size, radius) in Shapes())
            {
                if (shape == PartShape.RoundedBox) continue; // faces meet in duplicated (equal) vertices
                var buffer = new MeshBuilderBuffer();
                ProcMesh.Add(buffer, shape, size, radius, Matrix4x4.identity, 10, White, 0);
                MeshAssert.EveryEdgeHasTwoTriangles(buffer.Triangles, shape.ToString());
            }
        }

        [Test]
        public void Color_AndBone_AreWrittenToEveryVertex()
        {
            var buffer = new MeshBuilderBuffer();
            var c = new Color32(10, 20, 30, 40);
            ProcMesh.Ellipsoid(buffer, Matrix4x4.identity, Vector3.one, 8, c, 7);
            for (int i = 0; i < buffer.VertexCount; i++)
            {
                Assert.AreEqual(c, buffer.Colors[i]);
                Assert.AreEqual(7, buffer.Bones[i]);
            }
        }
    }
}
