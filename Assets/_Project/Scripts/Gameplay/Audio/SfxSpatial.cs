namespace Arena.Gameplay.Audio
{
    /// <summary>
    /// "3D" of the top-down view without moving the listener (the camera is high above the arena): sounds of other fighters fade
    /// linearly with the distance from the local fighter and pan by their sideways offset on screen.
    /// </summary>
    public static class SfxSpatial
    {
        /// <summary>1 up to <paramref name="full"/>, 0 from <paramref name="silent"/>, linear in between.</summary>
        public static float DistanceGain(float distance, float full, float silent)
        {
            if (distance <= full) return 1f;
            if (distance >= silent || silent <= full) return 0f;
            return 1f - (distance - full) / (silent - full);
        }

        /// <summary>Stereo pan −1…1 of a sound <paramref name="lateral"/> world units to the right on screen (negative = left).</summary>
        public static float Pan(float lateral, float panDistance, float strength)
        {
            if (panDistance <= 0f) return 0f;
            float k = lateral / panDistance;
            if (k > 1f) k = 1f;
            else if (k < -1f) k = -1f;
            return k * strength;
        }
    }
}
