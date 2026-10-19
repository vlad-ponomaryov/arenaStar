using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>One planned floor detail (A.3e): kind, world XZ position, size, aspect, yaw, variant.</summary>
    public struct FloorDecorItem
    {
        public FloorDecorKind Kind;
        /// <summary>World XZ (Core <c>Vec2</c> axes): x = column axis, y = row axis.</summary>
        public Vector2 Position;
        /// <summary>Radius along the item's own x axis, world units.</summary>
        public float Size;
        /// <summary>Radius along the item's own z axis relative to <see cref="Size"/> (stones are ellipses).</summary>
        public float Aspect;
        public float YawDegrees;
        /// <summary>Colour index (flowers) / shape seed.</summary>
        public int Variant;

        public FloorDecorItem(FloorDecorKind kind, Vector2 position, float size, float aspect, float yawDegrees, int variant)
        {
            Kind = kind;
            Position = position;
            Size = size;
            Aspect = aspect;
            YawDegrees = yawDegrees;
            Variant = variant;
        }
    }
}
