using System;
using UnityEngine;

namespace Arena.UI.Style
{
    /// <summary>Layout and colours of one virtual stick, in reference-resolution canvas units.</summary>
    [Serializable]
    public struct StickStyle
    {
        [Tooltip("Centre of the stick from its anchor corner (bottom-left for movement, bottom-right for the others), inward.")]
        public Vector2 Position;

        [Tooltip("Side of the square touch zone of a fixed stick.")]
        public float HitSize;

        public float BaseSize;
        public float KnobSize;

        [Tooltip("Knob travel = offset that counts as full deflection.")]
        public float Radius;

        public Color BaseColor;
        public Color KnobColor;
    }
}
