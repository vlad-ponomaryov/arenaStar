using Arena.Gameplay.View.Anim;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Gameplay.Anim
{
    public sealed class SpringMathTests
    {
        private const float Frequency = 3.5f;
        private const float Damping = 0.55f;

        [Test]
        public void Step_SettlesOnTarget()
        {
            var s = new SpringState();
            for (int i = 0; i < 300; i++) SpringMath.Step(ref s, 10f, Frequency, Damping, 1f / 60f);
            Assert.AreEqual(10f, s.Angle, 0.01f);
            Assert.AreEqual(0f, s.Velocity, 0.1f);
        }

        [Test]
        public void Step_UnderDamped_Overshoots()
        {
            var s = new SpringState();
            float max = 0f;
            for (int i = 0; i < 120; i++)
            {
                SpringMath.Step(ref s, 10f, Frequency, Damping, 1f / 60f);
                if (s.Angle > max) max = s.Angle;
            }
            Assert.Greater(max, 10f, "secondary motion swings past the target");
        }

        [Test]
        public void Step_IsStableAtMaxDt()
        {
            var s = new SpringState { Angle = 25f };
            for (int i = 0; i < 1000; i++) SpringMath.Step(ref s, 0f, Frequency, Damping, 0.05f);
            Assert.IsFalse(float.IsNaN(s.Angle));
            Assert.AreEqual(0f, s.Angle, 0.01f);
        }

        [Test]
        public void Clamp_LimitsAngle_DropsOutwardVelocity()
        {
            var s = new SpringState { Angle = 40f, Velocity = 5f };
            SpringMath.Clamp(ref s, 25f);
            Assert.AreEqual(25f, s.Angle);
            Assert.AreEqual(0f, s.Velocity);

            s = new SpringState { Angle = -40f, Velocity = 5f };
            SpringMath.Clamp(ref s, 25f);
            Assert.AreEqual(-25f, s.Angle);
            Assert.AreEqual(5f, s.Velocity, "inward velocity is kept");
        }

        [Test]
        public void Step_ZeroDt_DoesNothing()
        {
            var s = new SpringState { Angle = 3f, Velocity = 1f };
            SpringMath.Step(ref s, 10f, Frequency, Damping, 0f);
            Assert.AreEqual(3f, s.Angle);
            Assert.AreEqual(1f, s.Velocity);
        }
    }
}
