using System;
using UnityEngine;

namespace Arena.Editor.Art.Procedural
{
    /// <summary>
    /// Triangle primitives for the procedural models (Art-Spec §6.2): smooth normals, one vertex colour, one bone. Every primitive
    /// is built in its local space and placed by a matrix (normals by its inverse transpose, so non-uniform scale stays correct).
    /// <c>segments</c> is the number of steps around (10–14 for the body, fewer for small parts).
    /// </summary>
    public static class ProcMesh
    {
        private const float PoleEpsilon = 1e-5f;

        /// <summary>Any <see cref="PartShape"/> (see its sizes).</summary>
        public static void Add(MeshBuilderBuffer buffer, PartShape shape, Vector3 size, float radius, Matrix4x4 matrix, int segments,
            Color32 color, int bone)
        {
            switch (shape)
            {
                case PartShape.Ellipsoid: Ellipsoid(buffer, matrix, size, segments, color, bone); break;
                case PartShape.RoundCone: RoundCone(buffer, matrix, size.x, size.y, size.z, segments, color, bone); break;
                case PartShape.RoundedBox: RoundedBox(buffer, matrix, size, radius, Mathf.Max(1, segments / 5), color, bone); break;
                case PartShape.Torus: Torus(buffer, matrix, size.x, radius, segments, Mathf.Max(3, segments / 3), color, bone); break;
                default: throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
            }
        }

        /// <summary>UV sphere scaled to the radii; segments / 2 bands from pole to pole (poles are single vertices).</summary>
        public static void Ellipsoid(MeshBuilderBuffer buffer, Matrix4x4 matrix, Vector3 radii, int segments, Color32 color, int bone)
        {
            int bands = Mathf.Max(2, segments / 2);
            var profile = new Vector2[bands + 1];
            var normals = new Vector2[bands + 1];
            for (int i = 0; i <= bands; i++)
            {
                float phi = -Mathf.PI * 0.5f + Mathf.PI * i / bands;
                profile[i] = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
                normals[i] = profile[i];
            }
            profile[0].x = 0f;
            profile[bands].x = 0f;
            Lathe(buffer, matrix * Matrix4x4.Scale(radii), profile, normals, segments, color, bone);
        }

        /// <summary>
        /// Two spheres (bottom radius at the origin, top radius at +Y length) joined by their common tangent cone; a capsule if the
        /// radii are equal.
        /// </summary>
        public static void RoundCone(MeshBuilderBuffer buffer, Matrix4x4 matrix, float bottomRadius, float length, float topRadius,
            int segments, Color32 color, int bone)
        {
            float theta = Mathf.Asin(Mathf.Clamp((bottomRadius - topRadius) / Mathf.Max(length, PoleEpsilon), -1f, 1f));
            int steps = Mathf.Clamp(segments / 5, 1, 3); // arc steps per end cap
            var profile = new Vector2[steps * 2 + 2];
            var normals = new Vector2[profile.Length];
            for (int i = 0; i <= steps; i++)
            {
                float phi = Mathf.Lerp(-Mathf.PI * 0.5f, theta, (float)i / steps);
                normals[i] = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
                profile[i] = normals[i] * bottomRadius;
            }
            for (int i = 0; i <= steps; i++)
            {
                float phi = Mathf.Lerp(theta, Mathf.PI * 0.5f, (float)i / steps);
                var n = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
                normals[steps + 1 + i] = n;
                profile[steps + 1 + i] = n * topRadius + new Vector2(0f, length);
            }
            profile[0].x = 0f;
            profile[profile.Length - 1].x = 0f;
            Lathe(buffer, matrix, profile, normals, segments, color, bone);
        }

        /// <summary>
        /// Surface of revolution around local Y: profile points (radius, height) from bottom to top with their 2D normals
        /// (outward radius, up); a point with radius 0 is a pole (one vertex, a fan).
        /// </summary>
        public static void Lathe(MeshBuilderBuffer buffer, Matrix4x4 matrix, Vector2[] profile, Vector2[] normals, int segments,
            Color32 color, int bone)
        {
            segments = Mathf.Max(3, segments);
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            var starts = new int[profile.Length];
            var poles = new bool[profile.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                Vector2 p = profile[i], n = normals[i];
                starts[i] = buffer.VertexCount;
                poles[i] = p.x < PoleEpsilon;
                if (poles[i])
                {
                    buffer.AddVertex(matrix.MultiplyPoint3x4(new Vector3(0f, p.y, 0f)),
                        normalMatrix.MultiplyVector(new Vector3(0f, Mathf.Sign(n.y), 0f)), color, bone);
                    continue;
                }
                for (int s = 0; s < segments; s++)
                {
                    float a = 2f * Mathf.PI * s / segments;
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    buffer.AddVertex(matrix.MultiplyPoint3x4(new Vector3(p.x * c, p.y, p.x * sn)),
                        normalMatrix.MultiplyVector(new Vector3(n.x * c, n.y, n.x * sn)), color, bone);
                }
            }
            for (int i = 0; i + 1 < profile.Length; i++)
            {
                int a = starts[i], b = starts[i + 1];
                if (poles[i] && poles[i + 1]) continue;
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    if (poles[i]) buffer.AddTriangle(a, b + s, b + s1);
                    else if (poles[i + 1]) buffer.AddTriangle(a + s, a + s1, b);
                    else buffer.AddQuad(a + s, a + s1, b + s1, b + s);
                }
            }
        }

        /// <summary>
        /// Box with rounded edges and corners (half extents, corner radius); each face covers 45° of the arcs on its sides,
        /// <paramref name="arcSteps"/> steps each, so neighbouring faces meet in shared positions and normals.
        /// </summary>
        public static void RoundedBox(MeshBuilderBuffer buffer, Matrix4x4 matrix, Vector3 halfExtents, float radius, int arcSteps,
            Color32 color, int bone)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(halfExtents.x, Mathf.Min(halfExtents.y, halfExtents.z)));
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            Vector3 inner = halfExtents - Vector3.one * radius;
            int k = Mathf.Max(1, arcSteps);
            int n = 2 * (k + 1);
            var side = new float[n];  // −1 / +1: which edge of the inner box
            var slope = new float[n]; // tangent of the arc angle towards that edge
            for (int j = 0; j <= k; j++)
            {
                float tan = Mathf.Tan(Mathf.PI * 0.25f * (1f - (float)j / k));
                side[j] = -1f;
                slope[j] = -tan;
                side[n - 1 - j] = 1f;
                slope[n - 1 - j] = tan;
            }
            for (int face = 0; face < 6; face++)
            {
                int axis = face / 2;
                float sign = face % 2 == 0 ? 1f : -1f;
                int ua = (axis + 1) % 3, va = (axis + 2) % 3;
                int start = buffer.VertexCount;
                for (int iv = 0; iv < n; iv++)
                for (int iu = 0; iu < n; iu++)
                {
                    Vector3 dir = Vector3.zero, basePt = Vector3.zero;
                    dir[axis] = sign;
                    basePt[axis] = sign * inner[axis];
                    dir[ua] = slope[iu];
                    basePt[ua] = side[iu] * inner[ua];
                    dir[va] = slope[iv];
                    basePt[va] = side[iv] * inner[va];
                    Vector3 normal = dir.normalized;
                    buffer.AddVertex(matrix.MultiplyPoint3x4(basePt + normal * radius), normalMatrix.MultiplyVector(normal), color, bone);
                }
                for (int iv = 0; iv + 1 < n; iv++)
                for (int iu = 0; iu + 1 < n; iu++)
                {
                    int a = start + iv * n + iu;
                    buffer.AddQuad(a, a + 1, a + 1 + n, a + n);
                }
            }
        }

        /// <summary>Ring in the local XZ plane around Y.</summary>
        public static void Torus(MeshBuilderBuffer buffer, Matrix4x4 matrix, float ringRadius, float tubeRadius, int segments,
            int tubeSegments, Color32 color, int bone)
        {
            segments = Mathf.Max(3, segments);
            tubeSegments = Mathf.Max(3, tubeSegments);
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            int start = buffer.VertexCount;
            for (int s = 0; s < segments; s++)
            {
                float a = 2f * Mathf.PI * s / segments;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int t = 0; t < tubeSegments; t++)
                {
                    float b = 2f * Mathf.PI * t / tubeSegments;
                    Vector3 normal = radial * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    buffer.AddVertex(matrix.MultiplyPoint3x4(radial * ringRadius + normal * tubeRadius),
                        normalMatrix.MultiplyVector(normal), color, bone);
                }
            }
            for (int s = 0; s < segments; s++)
            for (int t = 0; t < tubeSegments; t++)
            {
                int s1 = (s + 1) % segments, t1 = (t + 1) % tubeSegments;
                buffer.AddQuad(start + s * tubeSegments + t, start + s1 * tubeSegments + t, start + s1 * tubeSegments + t1,
                    start + s * tubeSegments + t1);
            }
        }
    }
}
