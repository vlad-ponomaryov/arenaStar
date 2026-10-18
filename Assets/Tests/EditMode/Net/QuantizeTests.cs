using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net
{
    public sealed class QuantizeTests
    {
        private const int Samples = 5000;

        [Test]
        public void Position_ErrorWithinHalfStep()
        {
            var rng = DeterministicRng.FromSeed(11);
            float half = Quantize.PositionStep * 0.5f;
            for (int i = 0; i < Samples; i++)
            {
                float v = rng.NextFloat01() * (NetProtocol.PositionRangeCells - 1);
                float back = Quantize.FromPosition(Quantize.ToPosition(v));
                Assert.LessOrEqual(MathUtil.Abs(back - v), half + 1e-6f, "value " + v);
            }
        }

        [Test]
        public void Position_StepIsOne1024thCell_AndClamps()
        {
            Assert.AreEqual(1f / 1024f, Quantize.PositionStep);
            Assert.AreEqual(0, Quantize.ToPosition(-5f));
            Assert.AreEqual(ushort.MaxValue, Quantize.ToPosition(1000f));
            Assert.AreEqual(0, Quantize.ToPosition(float.NaN));
            Assert.AreEqual(10.5f, Quantize.FromPosition(Quantize.ToPosition(10.5f)));
        }

        [Test]
        public void Position_DecodeIsExactDivision()
        {
            for (int q = 0; q <= ushort.MaxValue; q += 97)
                Assert.AreEqual(q / 1024f, Quantize.FromPosition((ushort)q));
        }

        [Test]
        public void Aim_DecodeIsDivision_AndRequantizesToSameBits()
        {
            var rng = DeterministicRng.FromSeed(12);
            for (int i = 0; i < Samples; i++)
            {
                Vec2 aim = RandomInDisk(ref rng);
                Quantize.ToAim(aim, out short qx, out short qy);
                Vec2 back = Quantize.FromAim(qx, qy);
                Assert.AreEqual(qx / 32767f, back.X);
                Assert.AreEqual(qy / 32767f, back.Y);
                Quantize.ToAim(back, out short qx2, out short qy2);
                Assert.AreEqual(qx, qx2, "idempotent x");
                Assert.AreEqual(qy, qy2, "idempotent y");
            }
        }

        [Test]
        public void Aim_ErrorWithinStep()
        {
            var rng = DeterministicRng.FromSeed(13);
            float step = 1f / NetProtocol.AimScale;
            for (int i = 0; i < Samples; i++)
            {
                Vec2 aim = RandomInDisk(ref rng);
                Quantize.ToAim(aim, out short qx, out short qy);
                Vec2 back = Quantize.FromAim(qx, qy);
                Assert.LessOrEqual(MathUtil.Abs(back.X - aim.X), step, "x of " + aim);
                Assert.LessOrEqual(MathUtil.Abs(back.Y - aim.Y), step, "y of " + aim);
            }
        }

        [Test]
        public void Move_ErrorWithinStep()
        {
            var rng = DeterministicRng.FromSeed(14);
            float step = 1f / NetProtocol.MoveScale;
            for (int i = 0; i < Samples; i++)
            {
                Vec2 move = RandomInDisk(ref rng);
                Quantize.ToMove(move, out sbyte qx, out sbyte qy);
                Vec2 back = Quantize.FromMove(qx, qy);
                Assert.LessOrEqual(MathUtil.Abs(back.X - move.X), step, "x of " + move);
                Assert.LessOrEqual(MathUtil.Abs(back.Y - move.Y), step, "y of " + move);
            }
        }

        [Test]
        public void UnitVectors_NeverReclampedBySanitize()
        {
            var rng = DeterministicRng.FromSeed(15);
            for (int i = 0; i < Samples; i++)
            {
                float angle = rng.NextFloat01() * 360f;
                Vec2 dir = MathUtil.AngleToDir(angle) * (1f + rng.NextFloat01() * 0.01f);
                Quantize.ToAim(dir, out short ax, out short ay);
                Quantize.ToMove(dir, out sbyte mx, out sbyte my);
                Vec2 aim = Quantize.FromAim(ax, ay);
                Vec2 move = Quantize.FromMove(mx, my);
                Assert.IsTrue(WithinUnitLimit(ax, ay, NetProtocol.AimScale), "aim " + ax + ", " + ay);
                Assert.IsTrue(WithinUnitLimit(mx, my, NetProtocol.MoveScale), "move " + mx + ", " + my);
                var input = new PlayerInput(move, aim, InputButtons.Attack);
                PlayerInput sanitized = input.Sanitized();
                Assert.AreEqual(input, sanitized, "angle " + angle);
                Assert.Greater(aim.Length, 0.9999f);
                Assert.Greater(move.Length, 0.99f);
            }
        }

        [Test]
        public void UnitVectors_ExtremesAndInvalid()
        {
            Quantize.ToAim(new Vec2(5f, 0f), out short x, out short y);
            Assert.AreEqual(NetProtocol.AimScale, x, "axis-aligned full deflection is exact");
            Assert.AreEqual(0, y);
            Assert.AreEqual(1f, Quantize.FromAim(x, y).Length);
            Quantize.ToAim(new Vec2(0.70710678f, 0.70710678f), out x, out y);
            Assert.IsTrue(WithinUnitLimit(x, y, NetProtocol.AimScale));
            Quantize.ToAim(new Vec2(float.NaN, 1f), out x, out y);
            Assert.AreEqual(0, x);
            Assert.AreEqual(0, y);
            Quantize.ToAim(Vec2.Zero, out x, out y);
            Assert.AreEqual(0, x, "zero aim stays 'no manual aim'");
            Assert.AreEqual(0, y);
            Quantize.ToMove(new Vec2(-1f, 0f), out sbyte mx, out sbyte my);
            Assert.Less(mx, 0);
            Assert.GreaterOrEqual(mx, -NetProtocol.MoveScale);
        }

        [Test]
        public void Angle_ErrorWithinHalfStep()
        {
            var rng = DeterministicRng.FromSeed(16);
            int[] widths = { NetProtocol.FacingAngleBits, NetProtocol.ProjectileAngleBits };
            for (int w = 0; w < widths.Length; w++)
            {
                int bits = widths[w];
                float half = Quantize.AngleStep(bits) * 0.5f;
                for (int i = 0; i < Samples; i++)
                {
                    float deg = rng.NextFloat01() * 720f - 360f;
                    uint q = Quantize.ToAngle(deg, bits);
                    Assert.Less(q, 1u << bits);
                    float back = Quantize.FromAngle(q, bits);
                    float diff = MathUtil.Abs(back - deg) % 360f;
                    if (diff > 180f) diff = 360f - diff;
                    Assert.LessOrEqual(diff, half + 1e-3f, bits + " bits, " + deg + "°");
                }
            }
        }

        [Test]
        public void Radius_ErrorWithinHalfStep_AndClamps()
        {
            float half = 0.5f / NetProtocol.RadiusStepsPerCell;
            for (float r = 0f; r < 7.9f; r += 0.013f)
                Assert.LessOrEqual(MathUtil.Abs(Quantize.FromRadius(Quantize.ToRadius(r)) - r), half + 1e-6f, "radius " + r);
            Assert.AreEqual(0, Quantize.ToRadius(-1f));
            Assert.AreEqual(255, Quantize.ToRadius(100f));
        }

        [Test]
        public void Unit_ScalarRoundTrip()
        {
            Assert.AreEqual(NetProtocol.AimScale, Quantize.ToUnit(1f, NetProtocol.AimScale));
            Assert.AreEqual(-NetProtocol.AimScale, Quantize.ToUnit(-2f, NetProtocol.AimScale));
            Assert.AreEqual(0, Quantize.ToUnit(float.PositiveInfinity, NetProtocol.AimScale));
            Assert.AreEqual(0.5f, Quantize.FromUnit(Quantize.ToUnit(0.5f, 128), 128));
        }

        private static bool WithinUnitLimit(int x, int y, int scale)
        {
            if ((x == 0 && (y == scale || y == -scale)) || (y == 0 && (x == scale || x == -scale))) return true;
            return (long)x * x + (long)y * y <= Quantize.UnitLengthLimit(scale);
        }

        private static Vec2 RandomInDisk(ref DeterministicRng rng)
        {
            while (true)
            {
                var v = new Vec2(rng.NextFloat01() * 2f - 1f, rng.NextFloat01() * 2f - 1f);
                if (v.SqrLength <= 0.98f) return v;
            }
        }
    }
}
