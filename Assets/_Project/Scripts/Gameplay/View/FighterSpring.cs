using System;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Secondary motion of one bone (Art-Spec §3.1): a damped oscillator on the bone's lag behind the body. Written by the model builder.
    /// </summary>
    [Serializable]
    public struct FighterSpring
    {
        public FighterBone Bone;
        /// <summary>Natural frequency, Hz.</summary>
        public float Frequency;
        /// <summary>Damping ratio (1 = critical).</summary>
        public float Damping;
        /// <summary>Degrees of swing per unit of body acceleration, clamped by the animator.</summary>
        public float Amount;
    }
}
