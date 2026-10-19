using System.Collections.Generic;
using Arena.Editor.Art.Procedural;
using UnityEngine;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// Mesh buffer of the snow theme props (D-140, D-142: <see cref="SnowPropMeshes"/>, <see cref="SnowFrameMeshes"/>) with per-vertex
    /// gloss and primitive helpers; colours are sRGB until <see cref="Create"/>, which writes them linear with gloss in alpha and
    /// uv0.x = 1 (no texture detail, Arena/ToonEnv).
    /// </summary>
    internal sealed class SnowMesh
    {
        public const int Segments = 14;
        public const int SmallSegments = 8;
        public const int ArcSteps = 2;
        public const float Matte = 0f;
        public const float Glossy = 1f;
        private const float FootHeight = 0.18f;
        private const float FootShade = 0.78f;

        private readonly MeshBuilderBuffer _buffer = new MeshBuilderBuffer();
        private readonly List<float> _gloss = new List<float>();

        public int TriangleCount => _buffer.TriangleCount;

        private void Track(float gloss)
        {
            while (_gloss.Count < _buffer.VertexCount) _gloss.Add(gloss);
        }

        public void Sphere(Vector3 centre, float radius, Color color, float gloss) =>
            Ellipsoid(centre, new Vector3(radius, radius, radius), color, gloss);

        public void Ellipsoid(Vector3 centre, Vector3 radii, Color color, float gloss) =>
            Ellipsoid(centre, radii, color, gloss, radii.x < 0.1f ? SmallSegments : Segments);

        public void Ellipsoid(Vector3 centre, Vector3 radii, Color color, float gloss, int segments)
        {
            ProcMesh.Ellipsoid(_buffer, Matrix4x4.Translate(centre), radii, segments, color, 0);
            Track(gloss);
        }

        public void Box(Vector3 centre, Vector3 halfExtents, float radius, Color color) =>
            Box(Matrix4x4.Translate(centre), halfExtents, radius, color);

        public void Box(Matrix4x4 matrix, Vector3 halfExtents, float radius, Color color)
        {
            ProcMesh.RoundedBox(_buffer, matrix, halfExtents, radius, ArcSteps, color, 0);
            Track(Matte);
        }

        /// <summary>A thin box from <paramref name="a"/> to <paramref name="b"/> (wire, log, rung).</summary>
        public void Segment(Vector3 a, Vector3 b, float halfWidth, Color color)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-8f) return;
            Matrix4x4 matrix = Matrix4x4.TRS((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.right, d.normalized), Vector3.one);
            Box(matrix, new Vector3(d.magnitude * 0.5f, halfWidth, halfWidth), halfWidth * 0.4f, color);
        }

        public void Cylinder(Vector3 bottom, float radius, float height, Color color) =>
            CylinderAlong(bottom, Vector3.up, radius, height, color, Segments);

        public void CylinderAlong(Vector3 bottom, Vector3 axis, float radius, float length, Color color, int segments)
        {
            Vector2[] profile = { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(radius, length), new Vector2(0f, length) };
            Vector2[] normals = { Vector2.down, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.up };
            Matrix4x4 matrix = Matrix4x4.TRS(bottom, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one);
            ProcMesh.Lathe(_buffer, matrix, profile, normals, segments, color, 0);
            Track(Matte);
        }

        public void Cone(Vector3 bottom, float radius, float height, Color color) => Cone(bottom, radius, height, color, Segments + 4);

        public void Cone(Vector3 bottom, float radius, float height, Color color, int segments)
        {
            Vector2 side = new Vector2(height, radius).normalized;
            Vector2[] profile = { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(0f, height) };
            Vector2[] normals = { Vector2.down, side, Vector2.up };
            ProcMesh.Lathe(_buffer, Matrix4x4.Translate(bottom), profile, normals, segments, color, 0);
            Track(Matte);
        }

        public void ConeAlong(Vector3 bottom, Vector3 axis, float radius, float length, Color color)
        {
            Vector2 side = new Vector2(length, radius).normalized;
            Vector2[] profile = { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(0f, length) };
            Vector2[] normals = { Vector2.down, side, Vector2.up };
            Matrix4x4 matrix = Matrix4x4.TRS(bottom, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one);
            ProcMesh.Lathe(_buffer, matrix, profile, normals, SmallSegments, color, 0);
            Track(Matte);
        }

        public void Torus(Vector3 centre, float ringRadius, float tubeRadius, Color color)
        {
            ProcMesh.Torus(_buffer, Matrix4x4.Translate(centre), ringRadius, tubeRadius, Segments + 6, SmallSegments, color, 0);
            Track(Matte);
        }

        /// <summary>Vertices of colour <paramref name="only"/> below <paramref name="height"/> blend towards <paramref name="shade"/>.</summary>
        public void ShadeTowards(float height, Color shade, Color only)
        {
            Color32 match = only;
            for (int i = 0; i < _buffer.VertexCount; i++)
            {
                Color32 c = _buffer.Colors[i];
                if (c.r != match.r || c.g != match.g || c.b != match.b) continue;
                float t = 1f - Mathf.Clamp01(_buffer.Positions[i].y / height);
                _buffer.Colors[i] = Color.Lerp(only, shade, t);
            }
        }

        /// <summary>Linear colours, gloss in alpha, uv0.x = 1; <paramref name="shadeFoot"/> darkens the vertices near the ground.</summary>
        public Mesh Create(string name, bool shadeFoot)
        {
            var mesh = new Mesh { name = name };
            _buffer.WriteTo(mesh);
            var colors = new List<Color>(_buffer.VertexCount);
            var uvs = new List<Vector2>(_buffer.VertexCount);
            for (int i = 0; i < _buffer.VertexCount; i++)
            {
                Color c = _buffer.Colors[i];
                if (shadeFoot && _buffer.Positions[i].y < FootHeight)
                    c = Color.Lerp(c * FootShade, c, _buffer.Positions[i].y / FootHeight);
                Color linear = c.linear;
                linear.a = i < _gloss.Count ? _gloss[i] : Matte;
                colors.Add(linear);
                uvs.Add(new Vector2(1f, 0f));
            }
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
