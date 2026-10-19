using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// One mesh of all floor details (A.3e, D-130) in world space: stones and pebbles = flattened domes (top fan + skirt to the sand,
    /// dark edge in the vertex colour), sprouts = three leaf diamonds rising from a point, flowers = five petal diamonds and a
    /// centre lying on the sand. Every face looks up enough to be seen by both team cameras (the floor material culls back faces).
    /// Vertex colours linear, alpha 0 (matte); uv0.x = 1 turns the floor texture off for these vertices.
    /// </summary>
    public static class FloorDecorMeshBuilder
    {
        private const int StoneSides = 8;
        private const int Leaves = 3;
        private const int Petals = 5;
        private const float GroundLift = 0.004f;

        public static int TrianglesOf(FloorDecorKind kind)
        {
            switch (kind)
            {
                case FloorDecorKind.Stone:
                case FloorDecorKind.Pebble: return StoneSides * 3;
                case FloorDecorKind.Sprout: return Leaves * 2;
                default: return Petals * 3;
            }
        }

        public static Mesh Build(List<FloorDecorItem> items, in EnvDecorStyle style, string name)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                FloorDecorItem item = items[i];
                switch (item.Kind)
                {
                    case FloorDecorKind.Stone:
                        AddDome(item, style.StoneHeight, style.StoneTop, style.StoneEdge, vertices, normals, colors, triangles);
                        break;
                    case FloorDecorKind.Pebble:
                        AddDome(item, item.Size * 0.6f, style.PebbleTop, style.PebbleEdge, vertices, normals, colors, triangles);
                        break;
                    case FloorDecorKind.Sprout:
                        AddSprout(item, style.SproutBase, style.SproutTip, vertices, normals, colors, triangles);
                        break;
                    default:
                        Color petal = style.FlowerPetals != null && style.FlowerPetals.Length > 0
                            ? style.FlowerPetals[Mathf.Abs(item.Variant) % style.FlowerPetals.Length] : Color.white;
                        AddFlower(item, petal, style.FlowerCentre, vertices, normals, colors, triangles);
                        break;
                }
            }
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            var uv = new Vector2[vertices.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(1f, 0f);
            mesh.uv = uv;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddDome(FloorDecorItem item, float height, Color top, Color edge, List<Vector3> v, List<Vector3> n,
            List<Color> c, List<int> t)
        {
            Quaternion yaw = Quaternion.Euler(0f, item.YawDegrees, 0f);
            var origin = new Vector3(item.Position.x, 0f, item.Position.y);
            Color topLin = top.linear, edgeLin = edge.linear, midLin = Color.Lerp(top, edge, 0.3f).linear;
            int centre = v.Count;
            v.Add(origin + Vector3.up * (height + GroundLift));
            n.Add(Vector3.up);
            c.Add(Alpha0(topLin));
            uint h = (uint)item.Variant * 2654435761u;
            for (int j = 0; j < StoneSides; j++)
            {
                h = h * 1664525u + 1013904223u;
                float wobble = 1f + (((h >> 8) & 0xFF) / 255f - 0.5f) * 0.14f;
                float angle = (j + (((h >> 16) & 0xFF) / 255f - 0.5f) * 0.2f) * Mathf.PI * 2f / StoneSides;
                var dir = new Vector3(Mathf.Cos(angle) * item.Size * wobble, 0f, Mathf.Sin(angle) * item.Size * item.Aspect * wobble);
                Vector3 outward = yaw * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                v.Add(origin + yaw * (dir * 0.7f) + Vector3.up * (height * 0.8f + GroundLift));
                n.Add((Vector3.up + outward * 0.6f).normalized);
                c.Add(Alpha0(midLin));
                v.Add(origin + yaw * dir + Vector3.up * GroundLift);
                n.Add((Vector3.up * 0.4f + outward).normalized);
                c.Add(Alpha0(edgeLin));
            }
            for (int j = 0; j < StoneSides; j++)
            {
                int a = centre + 1 + j * 2, b = centre + 1 + (j + 1) % StoneSides * 2;
                AddTri(v, t, centre, b, a, Vector3.up);
                Vector3 outward = v[a] + v[b] - 2f * v[centre];
                outward.y = 0f;
                AddTri(v, t, a, b, b + 1, outward + Vector3.up * 0.2f);
                AddTri(v, t, a, b + 1, a + 1, outward + Vector3.up * 0.2f);
            }
        }

        private static void AddSprout(FloorDecorItem item, Color baseColor, Color tipColor, List<Vector3> v, List<Vector3> n,
            List<Color> c, List<int> t)
        {
            var origin = new Vector3(item.Position.x, GroundLift, item.Position.y);
            float len = item.Size;
            Color baseLin = baseColor.linear, tipLin = tipColor.linear, midLin = Color.Lerp(baseColor, tipColor, 0.5f).linear;
            for (int k = 0; k < Leaves; k++)
            {
                float yaw = (item.YawDegrees + k * 360f / Leaves + (k == 1 ? 17f : 0f)) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                Vector3 side = Vector3.Cross(Vector3.up, d) * (len * 0.22f);
                float l = len * (k == 0 ? 1.1f : 0.9f);
                Vector3 tip = origin + d * (l * 0.75f) + Vector3.up * (l * 0.6f);
                Vector3 mid = origin + d * (l * 0.4f) + Vector3.up * (l * 0.3f);
                Vector3 facing = (Vector3.up - d * 0.6f).normalized;
                int i0 = v.Count;
                v.Add(origin); v.Add(mid + side); v.Add(tip); v.Add(mid - side);
                Vector3 soft = (facing + Vector3.up).normalized;
                n.Add(soft); n.Add(soft); n.Add(soft); n.Add(soft);
                c.Add(Alpha0(baseLin)); c.Add(Alpha0(midLin)); c.Add(Alpha0(tipLin)); c.Add(Alpha0(midLin));
                AddTri(v, t, i0, i0 + 1, i0 + 2, facing);
                AddTri(v, t, i0, i0 + 2, i0 + 3, facing);
            }
        }

        private static void AddFlower(FloorDecorItem item, Color petal, Color centreColor, List<Vector3> v, List<Vector3> n,
            List<Color> c, List<int> t)
        {
            var origin = new Vector3(item.Position.x, GroundLift + 0.012f, item.Position.y);
            float s = item.Size;
            Color petalLin = petal.linear, petalInner = Color.Lerp(petal, centreColor, 0.25f).linear, centreLin = centreColor.linear;
            for (int k = 0; k < Petals; k++)
            {
                float yaw = (item.YawDegrees + k * 360f / Petals) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                Vector3 side = Vector3.Cross(Vector3.up, d) * (s * 0.32f);
                int i0 = v.Count;
                v.Add(origin + d * (s * 0.2f));
                v.Add(origin + d * (s * 0.6f) + side + Vector3.up * 0.006f);
                v.Add(origin + d * s + Vector3.up * 0.01f);
                v.Add(origin + d * (s * 0.6f) - side + Vector3.up * 0.006f);
                for (int q = 0; q < 4; q++) n.Add(Vector3.up);
                c.Add(Alpha0(petalInner)); c.Add(Alpha0(petalLin)); c.Add(Alpha0(petalLin)); c.Add(Alpha0(petalLin));
                AddTri(v, t, i0, i0 + 1, i0 + 2, Vector3.up);
                AddTri(v, t, i0, i0 + 2, i0 + 3, Vector3.up);
            }
            int centre = v.Count;
            v.Add(origin + Vector3.up * 0.016f);
            n.Add(Vector3.up);
            c.Add(Alpha0(centreLin));
            for (int k = 0; k < Petals; k++)
            {
                float yaw = (item.YawDegrees + (k + 0.5f) * 360f / Petals) * Mathf.Deg2Rad;
                v.Add(origin + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * (s * 0.28f) + Vector3.up * 0.012f);
                n.Add(Vector3.up);
                c.Add(Alpha0(centreLin));
            }
            for (int k = 0; k < Petals; k++)
                AddTri(v, t, centre, centre + 1 + k, centre + 1 + (k + 1) % Petals, Vector3.up);
        }

        /// <summary>Adds a triangle wound so that its face normal points along <paramref name="towards"/>.</summary>
        private static void AddTri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 towards)
        {
            Vector3 normal = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(normal, towards) < 0f)
            {
                int swap = b;
                b = c;
                c = swap;
            }
            t.Add(a);
            t.Add(b);
            t.Add(c);
        }

        private static Color Alpha0(Color c) => new Color(c.r, c.g, c.b, 0f);
    }
}
