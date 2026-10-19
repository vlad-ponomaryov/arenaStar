using System;
using UnityEngine;

namespace Arena.Editor.Art.Procedural
{
    /// <summary>
    /// One primitive of a sculpted group as a signed distance (negative inside): the same <see cref="PartShape"/> sizes as
    /// <see cref="ProcMesh"/>, placed by position, rotation and a per-axis scale (distance × smallest scale — a bound, exact for
    /// uniform scale). Exact or bounded formulas after Inigo Quilez (ellipsoid is the usual first-order approximation).
    /// </summary>
    public sealed class SdfPrimitive
    {
        public readonly PartShape Shape;
        public readonly SdfOp Op;
        public readonly Vector3 Size;
        public readonly float Radius;
        /// <summary>Linear vertex colour, alpha = gloss.</summary>
        public readonly Color32 Color;

        private readonly Matrix4x4 _worldToLocal;
        private readonly Matrix4x4 _localToWorld;
        private readonly Vector3 _invScale;
        private readonly float _minScale;
        private readonly Vector3 _scale;

        public SdfPrimitive(PartShape shape, SdfOp op, Vector3 position, Quaternion rotation, Vector3 scale, Vector3 size, float radius,
            Color32 color)
        {
            Shape = shape;
            Op = op;
            Size = size;
            Radius = radius;
            Color = color;
            _scale = new Vector3(Positive(scale.x), Positive(scale.y), Positive(scale.z));
            _invScale = new Vector3(1f / _scale.x, 1f / _scale.y, 1f / _scale.z);
            _minScale = Mathf.Min(_scale.x, Mathf.Min(_scale.y, _scale.z));
            _localToWorld = Matrix4x4.TRS(position, rotation, Vector3.one);
            _worldToLocal = _localToWorld.inverse;
        }

        public float Distance(Vector3 p)
        {
            Vector3 q = Vector3.Scale(_worldToLocal.MultiplyPoint3x4(p), _invScale);
            return LocalDistance(q) * _minScale;
        }

        /// <summary>World-space box around the primitive (no blend margin).</summary>
        public Bounds WorldBounds()
        {
            Vector3 min, max;
            switch (Shape)
            {
                case PartShape.Ellipsoid: min = -Size; max = Size; break;
                case PartShape.RoundCone:
                {
                    float r = Mathf.Max(Size.x, Size.z);
                    min = new Vector3(-r, -Size.x, -r);
                    max = new Vector3(r, Size.y + Size.z, r);
                    break;
                }
                case PartShape.RoundedBox: min = -Size; max = Size; break;
                case PartShape.Torus:
                {
                    float r = Size.x + Radius;
                    min = new Vector3(-r, -Radius, -r);
                    max = new Vector3(r, Radius, r);
                    break;
                }
                default: throw new ArgumentOutOfRangeException();
            }
            min = Vector3.Scale(min, _scale);
            max = Vector3.Scale(max, _scale);
            var bounds = new Bounds(_localToWorld.MultiplyPoint3x4(min), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                bounds.Encapsulate(_localToWorld.MultiplyPoint3x4(corner));
            }
            return bounds;
        }

        private float LocalDistance(Vector3 q)
        {
            switch (Shape)
            {
                case PartShape.Ellipsoid: return Ellipsoid(q, Size);
                case PartShape.RoundCone: return RoundCone(q, Size.x, Size.y, Size.z);
                case PartShape.RoundedBox: return RoundedBox(q, Size, Radius);
                case PartShape.Torus:
                {
                    var v = new Vector2(new Vector2(q.x, q.z).magnitude - Size.x, q.y);
                    return v.magnitude - Radius;
                }
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public static float Ellipsoid(Vector3 p, Vector3 r)
        {
            float k0 = new Vector3(p.x / r.x, p.y / r.y, p.z / r.z).magnitude;
            float k1 = new Vector3(p.x / (r.x * r.x), p.y / (r.y * r.y), p.z / (r.z * r.z)).magnitude;
            if (k1 < 1e-8f) return -Mathf.Min(r.x, Mathf.Min(r.y, r.z));
            return k0 * (k0 - 1f) / k1;
        }

        /// <summary>Round cone from the origin (radius r1) to +Y length (radius r2).</summary>
        public static float RoundCone(Vector3 p, float r1, float length, float r2)
        {
            var ba = new Vector3(0f, Mathf.Max(length, 1e-5f), 0f);
            float l2 = Vector3.Dot(ba, ba);
            float rr = r1 - r2;
            float a2 = l2 - rr * rr;
            float il2 = 1f / l2;
            float y = Vector3.Dot(p, ba);
            float z = y - l2;
            Vector3 xv = p * l2 - ba * y;
            float x2 = Vector3.Dot(xv, xv);
            float y2 = y * y * l2;
            float z2 = z * z * l2;
            float k = Mathf.Sign(rr) * rr * rr * x2;
            if (Mathf.Sign(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
            if (Mathf.Sign(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
            return (Mathf.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
        }

        public static float RoundedBox(Vector3 p, Vector3 halfExtents, float radius)
        {
            var q = new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z)) - (halfExtents - Vector3.one * radius);
            var outside = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f));
            return outside.magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - radius;
        }

        private static float Positive(float v) => v > 1e-4f ? v : 1f;
    }
}
