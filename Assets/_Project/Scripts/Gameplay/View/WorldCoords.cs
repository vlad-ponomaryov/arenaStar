using Arena.Core.Math;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>Core <c>Vec2(x, y)</c> ↔ Unity <c>(x, 0, y)</c>, 1 cell = 1 unit (D-009).</summary>
    public static class WorldCoords
    {
        public static Vector3 ToWorld(Vec2 v, float height = 0f) => new Vector3(v.X, height, v.Y);

        public static Vec2 ToSim(Vector3 v) => new Vec2(v.x, v.z);

        public static Vector3 DirectionToWorld(Vec2 dir) => new Vector3(dir.X, 0f, dir.Y);
    }
}
