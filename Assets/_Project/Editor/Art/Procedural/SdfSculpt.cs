using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.Editor.Art.Procedural
{
    /// <summary>
    /// A sculpted group (Art-Spec §6.2): primitives of one bone blended into one smooth surface — smooth union (polynomial smin,
    /// radius <see cref="Blend"/>) and smooth subtraction — polygonized by <see cref="SurfaceNets"/> with the grid step picked to fit
    /// a triangle budget. Vertex colour = the nearest volume primitive, then paint primitives over it (soft edge one grid step wide),
    /// darkened by a soft occlusion from the field (3 probes along the normal; creases up to <see cref="OcclusionStrength"/>).
    /// </summary>
    public sealed class SdfSculpt
    {
        public const float OcclusionStrength = 0.25f;
        private const float MinStep = 0.012f;
        private const int MaxBudgetIterations = 12;

        private readonly List<SdfPrimitive> _volumes = new List<SdfPrimitive>();
        private readonly List<SdfPrimitive> _cuts = new List<SdfPrimitive>();
        private readonly List<SdfPrimitive> _paints = new List<SdfPrimitive>();

        public float Blend { get; }

        /// <summary>Grid step of the last <see cref="Build"/>.</summary>
        public float LastStep { get; private set; }

        public SdfSculpt(float blend)
        {
            Blend = Mathf.Max(0f, blend);
        }

        public void Add(SdfPrimitive primitive)
        {
            switch (primitive.Op)
            {
                case SdfOp.Union: _volumes.Add(primitive); break;
                case SdfOp.Subtract: _cuts.Add(primitive); break;
                case SdfOp.Paint: _paints.Add(primitive); break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public float Distance(Vector3 p)
        {
            float d = float.MaxValue;
            for (int i = 0; i < _volumes.Count; i++)
            {
                float di = _volumes[i].Distance(p);
                d = i == 0 ? di : SmoothMin(d, di, Blend);
            }
            for (int i = 0; i < _cuts.Count; i++) d = -SmoothMin(-d, _cuts[i].Distance(p), Blend);
            return d;
        }

        /// <summary>Polynomial smooth minimum (Quilez): equals min away from the seam, rounds it within radius k.</summary>
        public static float SmoothMin(float a, float b, float k)
        {
            if (k <= 0f) return Mathf.Min(a, b);
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }

        public Bounds VolumeBounds()
        {
            if (_volumes.Count == 0) throw new InvalidOperationException("SdfSculpt: no volume primitives");
            Bounds b = _volumes[0].WorldBounds();
            for (int i = 1; i < _volumes.Count; i++) b.Encapsulate(_volumes[i].WorldBounds());
            return b;
        }

        /// <summary>
        /// Polygonizes into <paramref name="target"/> with the finest grid step whose triangle count fits <paramref name="budget"/>;
        /// returns the triangles added.
        /// </summary>
        public int Build(MeshBuilderBuffer target, int bone, int budget)
        {
            Bounds volume = VolumeBounds();
            var scratch = new MeshBuilderBuffer();
            Func<Vector3, float> field = Distance;
            // Surface area guess → first step; then refine by the actual count (triangles ∝ 1 / step²).
            float step = Mathf.Max(MinStep, Mathf.Sqrt(2f * SurfaceGuess(volume) / Mathf.Max(1, budget)));
            for (int i = 0; i < MaxBudgetIterations; i++)
            {
                scratch.Clear();
                Bounds box = volume;
                box.Expand(2f * (Blend + 2f * step));
                int tris = SurfaceNets.Polygonize(field, box, step, scratch, new Color32(255, 255, 255, 0), bone);
                if (tris <= budget)
                {
                    if (tris >= budget * 0.9f || step <= MinStep) break;
                    float finer = Mathf.Max(MinStep, step * Mathf.Sqrt(tris / (budget * 0.97f)));
                    if (finer >= step * 0.995f) break;
                    // Try finer; keep this result if the finer one overshoots.
                    var finerBuffer = new MeshBuilderBuffer();
                    Bounds finerBox = volume;
                    finerBox.Expand(2f * (Blend + 2f * finer));
                    int finerTris = SurfaceNets.Polygonize(field, finerBox, finer, finerBuffer, new Color32(255, 255, 255, 0), bone);
                    if (finerTris > budget) break;
                    scratch = finerBuffer;
                    step = finer;
                    continue;
                }
                step *= Mathf.Sqrt(tris / (budget * 0.97f)) * 1.01f;
            }
            LastStep = step;

            int added = scratch.TriangleCount;
            int offset = target.VertexCount;
            for (int v = 0; v < scratch.VertexCount; v++)
            {
                Vector3 p = scratch.Positions[v], n = scratch.Normals[v];
                target.AddVertex(p, n, Shade(Colour(p, step), Occlusion(p, n, step)), bone);
            }
            for (int t = 0; t < scratch.Triangles.Count; t++) target.Triangles.Add(scratch.Triangles[t] + offset);
            return added;
        }

        /// <summary>Colour of the nearest volume primitive, then paints over it (alpha = gloss).</summary>
        public Color32 Colour(Vector3 p, float edge)
        {
            Color32 c = _volumes[0].Color;
            float best = float.MaxValue;
            for (int i = 0; i < _volumes.Count; i++)
            {
                float d = _volumes[i].Distance(p);
                if (d < best) { best = d; c = _volumes[i].Color; }
            }
            for (int i = 0; i < _paints.Count; i++)
            {
                float d = _paints[i].Distance(p);
                float t = Mathf.Clamp01(0.5f - d / Mathf.Max(edge, 1e-4f));
                if (t > 0f) c = Color32.Lerp(c, _paints[i].Color, t);
            }
            return c;
        }

        /// <summary>0 = open, 1 = deep crease: how much the field closes in along the normal (3 probes).</summary>
        public float Occlusion(Vector3 p, Vector3 n, float step)
        {
            float delta = Mathf.Max(step * 1.5f, Blend * 0.5f);
            float occ = 0f, weight = 0.5f;
            for (int k = 1; k <= 3; k++)
            {
                float dist = k * delta;
                occ += weight * Mathf.Max(0f, dist - Distance(p + n * dist)) / dist;
                weight *= 0.5f;
            }
            return Mathf.Clamp01(occ * 2f);
        }

        private static Color32 Shade(Color32 c, float occlusion)
        {
            float k = 1f - OcclusionStrength * occlusion;
            return new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), c.a);
        }

        private static float SurfaceGuess(Bounds b)
        {
            // Ellipsoid of the box: area ≈ 4π((ab)^1.6 + (ac)^1.6 + (bc)^1.6)/3)^(1/1.6), half extents a, b, c.
            Vector3 e = b.extents;
            const float p = 1.6f;
            float s = (Mathf.Pow(e.x * e.y, p) + Mathf.Pow(e.x * e.z, p) + Mathf.Pow(e.y * e.z, p)) / 3f;
            return 4f * Mathf.PI * Mathf.Pow(s, 1f / p);
        }
    }
}
