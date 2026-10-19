using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.Editor.Art.Procedural
{
    /// <summary>
    /// A sculpted group (Art-Spec §6.2): primitives of one bone blended into one smooth surface — smooth union (polynomial smin,
    /// radius <see cref="Blend"/>) and smooth subtraction — polygonized finely by <see cref="SurfaceNets"/> and simplified by <see cref="MeshDecimator"/> to
    /// a triangle budget. Vertex colour = the nearest volume primitive, then paint primitives over it (soft edge <see cref="PaintEdge"/>),
    /// darkened by a soft occlusion from the field (3 probes along the normal; creases up to <see cref="OcclusionStrength"/>).
    /// </summary>
    public sealed class SdfSculpt
    {
        public const float OcclusionStrength = 0.25f;
        private const float MinStep = 0.012f;
        private const int MaxBudgetIterations = 12;
        /// <summary>The fine sculpt has ≈ this many times the budget's triangles before the simplification.</summary>
        public const int Oversample = 8;
        /// <summary>Width of a paint border, units: crisp at the gameplay distance, no stair-steps (A.1b2).</summary>
        public const float PaintEdge = 0.05f;

        private readonly List<SdfPrimitive> _volumes = new List<SdfPrimitive>();
        private readonly List<SdfPrimitive> _cuts = new List<SdfPrimitive>();
        private readonly List<SdfPrimitive> _paints = new List<SdfPrimitive>();

        public float Blend { get; }

        /// <summary>Grid step of the last <see cref="Build"/> (the fine sculpt).</summary>
        public float LastStep { get; private set; }

        /// <summary>Mean edge length of the last <see cref="Build"/> after the simplification, as a grid step.</summary>
        public float EffectiveStep { get; private set; }

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
        /// Polygonizes into <paramref name="target"/> within <paramref name="budget"/> triangles; returns the triangles added. The
        /// surface is sampled ≈ <see cref="Oversample"/> times finer than the budget, coloured, simplified by
        /// <see cref="MeshDecimator"/> (the triangles go where the curvature and the colour borders are), then every vertex is put
        /// back on the surface with the field gradient as its normal (smooth shading, no facets). Falls back to the plain grid fit
        /// if the simplification cannot reach the budget.
        /// </summary>
        public int Build(MeshBuilderBuffer target, int bone, int budget)
        {
            Bounds volume = VolumeBounds();
            Func<Vector3, float> field = Distance;
            int fineTarget = budget * Oversample;
            float step = Mathf.Max(MinStep, Mathf.Sqrt(2f * SurfaceGuess(volume) / Mathf.Max(1, fineTarget)));
            var fine = new MeshBuilderBuffer();
            int fineTris = 0;
            for (int i = 0; i < 3; i++)
            {
                fine.Clear();
                fineTris = Polygonize(field, volume, step, fine, bone);
                float ratio = fineTris / (float)fineTarget;
                if (ratio > 0.75f && ratio < 1.4f) break;
                step = Mathf.Max(MinStep, step * Mathf.Sqrt(ratio));
            }
            for (int v = 0; v < fine.VertexCount; v++) fine.Colors[v] = Colour(fine.Positions[v], PaintEdge);
            int tris = MeshDecimator.Simplify(fine, budget);
            MeshBuilderBuffer result = fine;
            if (tris > budget)
            {
                step = BuildOnGrid(field, volume, budget, bone, out result);
                LastStep = step;
                EffectiveStep = step;
            }
            else
            {
                LastStep = step;
                EffectiveStep = step * Mathf.Sqrt(fineTris / (float)Mathf.Max(1, tris));
            }

            int added = result.TriangleCount;
            int offset = target.VertexCount;
            for (int v = 0; v < result.VertexCount; v++)
            {
                Vector3 p = ProjectToSurface(result.Positions[v], LastStep);
                Vector3 n = SurfaceNets.Gradient(field, p, LastStep);
                target.AddVertex(p, n, Shade(Colour(p, PaintEdge), Occlusion(p, n, EffectiveStep)), bone);
            }
            for (int t = 0; t < result.Triangles.Count; t++) target.Triangles.Add(result.Triangles[t] + offset);
            return added;
        }

        /// <summary>A few Newton steps along the gradient onto the zero level, never further than <paramref name="maxMove"/>.</summary>
        public Vector3 ProjectToSurface(Vector3 p, float maxMove)
        {
            Vector3 start = p;
            for (int i = 0; i < 3; i++)
            {
                float d = Distance(p);
                if (Mathf.Abs(d) < 1e-5f) break;
                p -= SurfaceNets.Gradient(Distance, p, maxMove) * d;
            }
            Vector3 move = p - start;
            return move.sqrMagnitude > maxMove * maxMove ? start + move.normalized * maxMove : p;
        }

        private int Polygonize(Func<Vector3, float> field, Bounds volume, float step, MeshBuilderBuffer buffer, int bone)
        {
            Bounds box = volume;
            box.Expand(2f * (Blend + 2f * step));
            return SurfaceNets.Polygonize(field, box, step, buffer, new Color32(255, 255, 255, 0), bone);
        }

        /// <summary>The A.1b fit: the finest grid step whose Surface Nets triangle count fits the budget.</summary>
        private float BuildOnGrid(Func<Vector3, float> field, Bounds volume, int budget, int bone, out MeshBuilderBuffer result)
        {
            var scratch = new MeshBuilderBuffer();
            float step = Mathf.Max(MinStep, Mathf.Sqrt(2f * SurfaceGuess(volume) / Mathf.Max(1, budget)));
            for (int i = 0; i < MaxBudgetIterations; i++)
            {
                scratch.Clear();
                int tris = Polygonize(field, volume, step, scratch, bone);
                if (tris <= budget) break;
                step *= Mathf.Sqrt(tris / (budget * 0.97f)) * 1.01f;
            }
            result = scratch;
            return step;
        }

        /// <summary>
        /// Colour of the nearest volume primitive (blended with the ones within <paramref name="edge"/> of it, so borders between
        /// volumes are as soft as paint borders), then paints over it (alpha = gloss).
        /// </summary>
        public Color32 Colour(Vector3 p, float edge)
        {
            edge = Mathf.Max(edge, 1e-4f);
            float best = float.MaxValue;
            for (int i = 0; i < _volumes.Count; i++) best = Mathf.Min(best, _volumes[i].Distance(p));
            float r = 0f, g = 0f, b = 0f, a = 0f, sum = 0f;
            for (int i = 0; i < _volumes.Count; i++)
            {
                float w = Mathf.Clamp01(1f - (_volumes[i].Distance(p) - best) / edge);
                if (w <= 0f) continue;
                w *= w;
                Color32 ci = _volumes[i].Color;
                r += ci.r * w; g += ci.g * w; b += ci.b * w; a += ci.a * w;
                sum += w;
            }
            var c = new Color32((byte)Mathf.RoundToInt(r / sum), (byte)Mathf.RoundToInt(g / sum), (byte)Mathf.RoundToInt(b / sum),
                (byte)Mathf.RoundToInt(a / sum));
            for (int i = 0; i < _paints.Count; i++)
            {
                float d = _paints[i].Distance(p);
                float t = Mathf.Clamp01(0.5f - d / edge);
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
