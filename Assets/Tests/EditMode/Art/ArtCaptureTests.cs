using Arena.Editor.Art.Capture;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Art
{
    /// <summary>Pure parts of the screenshot tools (Art-Spec §6.5): colour-blind filter and the step argument.</summary>
    public sealed class ArtCaptureTests
    {
        [Test]
        public void ColorBlindFilter_KeepsGreysAndAlpha()
        {
            var px = new[] { new Color32(0, 0, 0, 255), new Color32(128, 128, 128, 7), new Color32(255, 255, 255, 255) };
            foreach (float[] matrix in new[] { ColorBlindFilter.Protanopia, ColorBlindFilter.Deuteranopia })
            {
                var copy = (Color32[])px.Clone();
                ColorBlindFilter.Apply(copy, matrix);
                for (int i = 0; i < px.Length; i++)
                {
                    Assert.LessOrEqual(Mathf.Abs(copy[i].r - px[i].r), 2, i.ToString());
                    Assert.LessOrEqual(Mathf.Abs(copy[i].g - px[i].g), 2, i.ToString());
                    Assert.LessOrEqual(Mathf.Abs(copy[i].b - px[i].b), 2, i.ToString());
                    Assert.AreEqual(px[i].a, copy[i].a);
                }
            }
        }

        [Test]
        public void ColorBlindFilter_MakesRedAndGreenCloser()
        {
            var red = new Color32(220, 40, 40, 255);
            var green = new Color32(40, 180, 40, 255);
            foreach (float[] matrix in new[] { ColorBlindFilter.Protanopia, ColorBlindFilter.Deuteranopia })
            {
                var px = new[] { red, green };
                ColorBlindFilter.Apply(px, matrix);
                // Red-green difference shrinks; both end up mostly yellow-brown (red ≈ green channel).
                Assert.Less(Mathf.Abs(px[0].r - px[1].r) + Mathf.Abs(px[0].g - px[1].g),
                    Mathf.Abs(red.r - green.r) + Mathf.Abs(red.g - green.g));
            }
        }

        [Test]
        public void ReadStep_ParsesAndSanitizes()
        {
            Assert.AreEqual("A.1a", ArtCapture.ReadStep(new[] { "Unity.exe", "-artStep", "A.1a" }));
            Assert.AreEqual(ArtCapture.DefaultStep, ArtCapture.ReadStep(new[] { "Unity.exe" }));
            Assert.AreEqual(ArtCapture.DefaultStep, ArtCapture.ReadStep(new[] { "-artStep", "a/b" }));
            Assert.AreEqual(ArtCapture.DefaultStep, ArtCapture.ReadStep(new[] { "-artStep", ".." }));
            Assert.AreEqual(ArtCapture.DefaultStep, ArtCapture.ReadStep(new[] { "-artStep" }));
        }
    }
}
