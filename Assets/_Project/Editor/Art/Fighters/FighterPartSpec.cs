using System;
using Arena.Editor.Art.Procedural;
using UnityEngine;

namespace Arena.Editor.Art.Fighters
{
    /// <summary>
    /// One primitive of a fighter recipe (Art-Spec §6.2), in model space (ground at y = 0, facing +Z, +X = the fighter's right):
    /// shape and sizes (<see cref="PartShape"/>), placement, colour (sRGB hex, baked as linear vertex colour), gloss (vertex alpha)
    /// and, in sculpted groups, its operation (volume, cut, paint). Built with the factories and fluent modifiers below.
    /// </summary>
    public sealed class FighterPartSpec
    {
        public PartShape Shape;
        public SdfOp Op = SdfOp.Union;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Size;
        public float Radius;
        public Vector3 Scale = Vector3.one;
        public string Color;
        public float Gloss;
        /// <summary>Steps around; 0 = the group's.</summary>
        public int Segments;

        public static FighterPartSpec Ellipsoid(Vector3 center, Vector3 radii, string hex) =>
            new FighterPartSpec { Shape = PartShape.Ellipsoid, Position = center, Size = radii, Color = hex };

        public static FighterPartSpec Sphere(Vector3 center, float radius, string hex) => Ellipsoid(center, Vector3.one * radius, hex);

        /// <summary>
        /// Round cone (capsule if the radii are equal) from one point to another; its local Z faces forward as much as possible,
        /// so <see cref="Squash"/> on Z flattens it front to back (ears).
        /// </summary>
        public static FighterPartSpec Cone(Vector3 from, Vector3 to, float fromRadius, float toRadius, string hex)
        {
            Vector3 dir = to - from;
            float length = dir.magnitude;
            if (length < 1e-5f) throw new ArgumentException("FighterPartSpec.Cone: zero length");
            dir /= length;
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, dir);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(Vector3.right, dir);
            return new FighterPartSpec
            {
                Shape = PartShape.RoundCone, Position = from, Rotation = Quaternion.LookRotation(forward.normalized, dir),
                Size = new Vector3(fromRadius, length, toRadius), Color = hex
            };
        }

        public static FighterPartSpec Box(Vector3 center, Vector3 halfExtents, float cornerRadius, string hex) =>
            new FighterPartSpec { Shape = PartShape.RoundedBox, Position = center, Size = halfExtents, Radius = cornerRadius, Color = hex };

        /// <summary>Ring around <paramref name="axis"/> (a torus in the plane perpendicular to it).</summary>
        public static FighterPartSpec Ring(Vector3 center, Vector3 axis, float ringRadius, float tubeRadius, string hex) =>
            new FighterPartSpec
            {
                Shape = PartShape.Torus, Position = center, Rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized),
                Size = new Vector3(ringRadius, 0f, 0f), Radius = tubeRadius, Color = hex
            };

        /// <summary>Extra rotation applied after the shape's own (Euler degrees, model space).</summary>
        public FighterPartSpec Rotated(Vector3 euler)
        {
            Rotation = Quaternion.Euler(euler) * Rotation;
            return this;
        }

        /// <summary>Sets the whole rotation.</summary>
        public FighterPartSpec Oriented(Quaternion rotation)
        {
            Rotation = rotation;
            return this;
        }

        /// <summary>Per-axis scale in the shape's local frame.</summary>
        public FighterPartSpec Squash(Vector3 scale)
        {
            Scale = scale;
            return this;
        }

        public FighterPartSpec Glossy(float gloss)
        {
            Gloss = gloss;
            return this;
        }

        public FighterPartSpec WithSegments(int segments)
        {
            Segments = segments;
            return this;
        }

        /// <summary>Only recolours the sculpted surface inside it.</summary>
        public FighterPartSpec AsPaint()
        {
            Op = SdfOp.Paint;
            return this;
        }

        /// <summary>Carves the sculpted surface.</summary>
        public FighterPartSpec AsCut()
        {
            Op = SdfOp.Subtract;
            return this;
        }

        /// <summary>sRGB hex → linear vertex colour (the project renders in linear space), alpha = gloss.</summary>
        public Color32 VertexColor()
        {
            if (!ColorUtility.TryParseHtmlString(Color, out Color srgb))
                throw new ArgumentException("FighterPartSpec: bad colour " + Color);
            Color linear = srgb.linear;
            linear.a = Mathf.Clamp01(Gloss);
            return linear;
        }

        public Matrix4x4 Matrix() => Matrix4x4.TRS(Position, Rotation, Scale);

        public SdfPrimitive ToSdf() => new SdfPrimitive(Shape, Op, Position, Rotation, Scale, Size, Radius, VertexColor());
    }
}
