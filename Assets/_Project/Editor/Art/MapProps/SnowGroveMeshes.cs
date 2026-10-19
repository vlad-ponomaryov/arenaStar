using System.Collections.Generic;
using UnityEngine;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// Bush cells of the snow theme as a young spruce grove (D-142, user feedback 1: "кусты похожи на траву — зимой травы нет"): four
    /// small snowy spruces per cell on a lattice that tiles across bush cells (a dense thicket over several cells), shorter and inset
    /// on open sides (the mass reads as a mound), over a low dark core that hides the floor. One mesh per bush-neighbour mask
    /// (W 1, E 2, S 4, N 8) and variant, laid out like <c>ViewCatalog.BushVariants</c> (MapView picks <c>mask × perMask + hash</c>).
    /// Drawn by <c>Arena/Bush</c>: normal = tangent = the cone normal, linear vertex colours, alpha = a small sway weight.
    /// A spruce: two needle tiers (dark foot, green body) with a zig-zag snow drape on their shoulders; the upper tier ends in snow.
    /// </summary>
    public static class SnowGroveMeshes
    {
        public const int TriangleBudget = EnvMeshBuilder.BushTriangleBudget;
        private const int Segments = 7;
        private const float Lattice = 0.25f;           // trees at (±0.25, ±0.25) of the cell
        private const float Jitter = 0.05f;
        private const float OpenInset = 0.07f;
        private const float HeightInner = 1.08f;
        private const float HeightOpen = 0.84f;
        private const float HeightCorner = 0.7f;
        private const float HeightJitter = 0.12f;
        private const float RadiusPerHeight = 0.3f;
        private const float LowerBase = 0.06f;
        private const float LowerHeight = 0.64f;
        private const float UpperBase = 0.42f;
        private const float UpperHeight = 0.58f;
        private const float UpperRadius = 0.7f;
        private const float LowerSnowFrom = 0.34f;
        private const float UpperSnowFrom = 0.5f;
        private const float SnowZigZag = 0.08f;
        private const float SwayTop = 0.25f;
        private const float CoreHeight = 0.12f;
        private const float CoreInset = 0.12f;

        private static readonly Color Foot = new Color32(0x1A, 0x3C, 0x2A, 0xFF);
        private static readonly Color Body = new Color32(0x2E, 0x66, 0x40, 0xFF);
        private static readonly Color BodyLight = new Color32(0x3E, 0x7C, 0x50, 0xFF);
        private static readonly Color Snow = new Color32(0xF6, 0xFA, 0xFE, 0xFF);
        private static readonly Color Core = new Color32(0x18, 0x34, 0x26, 0xFF);

        public static Mesh[] BuildAll(int variants)
        {
            var meshes = new Mesh[EnvMeshBuilder.BushMaskCount * variants];
            for (int mask = 0; mask < EnvMeshBuilder.BushMaskCount; mask++)
                for (int v = 0; v < variants; v++)
                    meshes[mask * variants + v] = Build(mask, v);
            return meshes;
        }

        public static Mesh Build(int mask, int variant)
        {
            bool w = (mask & 1) != 0, e = (mask & 2) != 0, s = (mask & 4) != 0, n = (mask & 8) != 0;
            var b = new Data();
            int k = 0;
            for (int j = 0; j < 2; j++)
            {
                for (int i = 0; i < 2; i++, k++)
                {
                    bool openX = i == 0 ? !w : !e, openZ = j == 0 ? !s : !n;
                    float x = (i == 0 ? -Lattice : Lattice) + (Hash01(mask, variant, k, 0) - 0.5f) * 2f * Jitter;
                    float z = (j == 0 ? -Lattice : Lattice) + (Hash01(mask, variant, k, 1) - 0.5f) * 2f * Jitter;
                    if (openX) x += i == 0 ? OpenInset : -OpenInset;
                    if (openZ) z += j == 0 ? OpenInset : -OpenInset;
                    float h = openX && openZ ? HeightCorner : openX || openZ ? HeightOpen : HeightInner;
                    h *= 1f + (Hash01(mask, variant, k, 2) - 0.5f) * 2f * HeightJitter;
                    AddSpruce(b, new Vector3(x, 0f, z), h, Hash01(mask, variant, k, 3) * Mathf.PI * 2f, Hash01(mask, variant, k, 4));
                }
            }
            AddCore(b, mask);
            return b.Create("Grove_m" + mask + "_v" + variant);
        }

        private static void AddSpruce(Data b, Vector3 root, float height, float phase, float tone)
        {
            float radius = height * RadiusPerHeight;
            Color body = Color.Lerp(Body, BodyLight, tone);
            AddTier(b, root + Vector3.up * (LowerBase * height), radius, LowerHeight * height, LowerSnowFrom, Foot, body, phase, height);
            AddTier(b, root + Vector3.up * (UpperBase * height), radius * UpperRadius, UpperHeight * height, UpperSnowFrom, body, BodyLight, phase + 0.4f,
                height);
        }

        /// <summary>A needle cone: base ring (foot colour) → zig-zag snow line (body colour, duplicated as snow) → apex (snow).</summary>
        private static void AddTier(Data b, Vector3 bottom, float radius, float height, float snowFrom, Color foot, Color body, float phase, float treeHeight)
        {
            Vector2 slope = new Vector2(height, radius).normalized;
            var baseRing = new int[Segments];
            var greenRing = new int[Segments];
            var snowRing = new int[Segments];
            for (int s = 0; s < Segments; s++)
            {
                float a = phase + s * Mathf.PI * 2f / Segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 normal = (dir * slope.x + Vector3.up * slope.y).normalized;
                float t = snowFrom + ((s & 1) == 0 ? SnowZigZag : -SnowZigZag);
                Vector3 p0 = bottom + dir * radius;
                Vector3 p1 = bottom + dir * (radius * (1f - t)) + Vector3.up * (height * t);
                baseRing[s] = b.Add(p0, normal, foot, Sway(p0.y, treeHeight));
                greenRing[s] = b.Add(p1, normal, body, Sway(p1.y, treeHeight));
                snowRing[s] = b.Add(p1, normal, Snow, Sway(p1.y, treeHeight));
            }
            Vector3 apex = bottom + Vector3.up * height;
            int top = b.Add(apex, Vector3.up, Snow, Sway(apex.y, treeHeight));
            for (int s = 0; s < Segments; s++)
            {
                int s1 = (s + 1) % Segments;
                b.Tri(baseRing[s], baseRing[s1], greenRing[s1]);
                b.Tri(baseRing[s], greenRing[s1], greenRing[s]);
                b.Tri(snowRing[s], snowRing[s1], top);
            }
        }

        private static float Sway(float y, float height) => SwayTop * Mathf.Clamp01(y / Mathf.Max(height, 0.01f));

        /// <summary>Low dark box under the spruces (top and the open sides), like the meadow bush core.</summary>
        private static void AddCore(Data b, int mask)
        {
            float x0 = (mask & 1) != 0 ? -0.5f : -0.5f + CoreInset, x1 = (mask & 2) != 0 ? 0.5f : 0.5f - CoreInset;
            float z0 = (mask & 4) != 0 ? -0.5f : -0.5f + CoreInset, z1 = (mask & 8) != 0 ? 0.5f : 0.5f - CoreInset;
            float y = CoreHeight;
            b.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.up);
            if ((mask & 4) == 0) b.Quad(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(x1, y, z0), new Vector3(x0, y, z0), Vector3.back);
            if ((mask & 8) == 0) b.Quad(new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), new Vector3(x0, y, z1), new Vector3(x1, y, z1), Vector3.forward);
            if ((mask & 1) == 0) b.Quad(new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), new Vector3(x0, y, z0), new Vector3(x0, y, z1), Vector3.left);
            if ((mask & 2) == 0) b.Quad(new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.right);
        }

        private static float Hash01(int mask, int variant, int tree, int channel)
        {
            unchecked
            {
                uint h = (uint)mask * 374761393u + (uint)variant * 668265263u + (uint)tree * 2246822519u + (uint)channel * 3266489917u + 0x5bd1e995u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Vertex lists for <c>Arena/Bush</c>: positions, normals (also the tangent = "blade" normal), linear colours with sway alpha.</summary>
        private sealed class Data
        {
            private readonly List<Vector3> _verts = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector4> _tangents = new List<Vector4>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<int> _tris = new List<int>();

            public int Add(Vector3 p, Vector3 normal, Color srgb, float sway)
            {
                _verts.Add(p);
                _normals.Add(normal);
                _tangents.Add(new Vector4(normal.x, normal.y, normal.z, 1f));
                Color linear = srgb.linear;
                linear.a = sway;
                _colors.Add(linear);
                return _verts.Count - 1;
            }

            /// <summary>Triangle facing the side its vertex normals point to.</summary>
            public void Tri(int a, int b, int c)
            {
                Vector3 face = Vector3.Cross(_verts[b] - _verts[a], _verts[c] - _verts[a]);
                Vector3 n = _normals[a] + _normals[b] + _normals[c];
                if (Vector3.Dot(face, n) < 0f) { _tris.Add(a); _tris.Add(c); _tris.Add(b); }
                else { _tris.Add(a); _tris.Add(b); _tris.Add(c); }
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                Color top = Core, foot = Core * 0.7f;
                bool up = normal == Vector3.up;
                int ia = Add(a, normal, up ? top : foot, 0f), ib = Add(b, normal, up ? top : foot, 0f);
                int ic = Add(c, normal, top, 0f), id = Add(d, normal, top, 0f);
                Tri(ia, ib, ic);
                Tri(ia, ic, id);
            }

            public Mesh Create(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_verts);
                mesh.SetNormals(_normals);
                mesh.SetTangents(_tangents);
                mesh.SetColors(_colors);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
