using UnityEngine;

namespace Arena.Editor.Art.Capture
{
    /// <summary>
    /// Colour vision deficiency simulation on the CPU (Machado, Oliveira, Fernandes 2009, severity 1.0) for the control
    /// screenshots (Art-Spec §6.5): sRGB pixels → linear → 3×3 matrix → clamp → sRGB. Editor-only, used on PNGs, not in a match.
    /// </summary>
    public static class ColorBlindFilter
    {
        /// <summary>Row-major 3×3 matrix, linear RGB.</summary>
        public static readonly float[] Protanopia =
        {
            0.152286f, 1.052583f, -0.204868f,
            0.114503f, 0.786281f, 0.099216f,
            -0.003882f, -0.048116f, 1.051998f
        };

        /// <summary>Row-major 3×3 matrix, linear RGB.</summary>
        public static readonly float[] Deuteranopia =
        {
            0.367322f, 0.860646f, -0.227968f,
            0.280085f, 0.672501f, 0.047413f,
            -0.011820f, 0.042940f, 0.968881f
        };

        private const int LinearLutSize = 4096;
        private static float[] _toLinear;
        private static byte[] _toSrgb;

        /// <summary>Applies <paramref name="matrix"/> to every pixel in place; alpha is kept.</summary>
        public static void Apply(Color32[] pixels, float[] matrix)
        {
            EnsureLuts();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                float r = _toLinear[p.r], g = _toLinear[p.g], b = _toLinear[p.b];
                float r2 = matrix[0] * r + matrix[1] * g + matrix[2] * b;
                float g2 = matrix[3] * r + matrix[4] * g + matrix[5] * b;
                float b2 = matrix[6] * r + matrix[7] * g + matrix[8] * b;
                pixels[i] = new Color32(ToSrgb(r2), ToSrgb(g2), ToSrgb(b2), p.a);
            }
        }

        private static byte ToSrgb(float linear)
        {
            if (linear <= 0f) return 0;
            if (linear >= 1f) return 255;
            return _toSrgb[(int)(linear * (LinearLutSize - 1) + 0.5f)];
        }

        private static void EnsureLuts()
        {
            if (_toLinear != null) return;
            var toLinear = new float[256];
            for (int i = 0; i < 256; i++) toLinear[i] = Mathf.GammaToLinearSpace(i / 255f);
            var toSrgb = new byte[LinearLutSize];
            for (int i = 0; i < LinearLutSize; i++)
                toSrgb[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(i / (float)(LinearLutSize - 1)) * 255f), 0, 255);
            _toSrgb = toSrgb;
            _toLinear = toLinear;
        }
    }
}
