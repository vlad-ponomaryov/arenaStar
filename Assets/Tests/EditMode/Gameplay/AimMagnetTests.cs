using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Gameplay.Input;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class AimMagnetTests
    {
        private const float Range = 8f;
        private const float MaxDegrees = 5f;

        /// <summary>Shooter at (10, 10), enemy 5 cells straight up; one tick so the visibility is computed.</summary>
        private static MatchSimulation Scene()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 15f));
            sim.Tick(F.Inputs());
            Assert.IsTrue(sim.State.Fighters[1].IsVisibleTo(0), "precondition: the enemy is visible");
            return sim;
        }

        private static Vec2 Rotated(float degrees, float length = 1f) => MathUtil.Rotate(Vec2.Up, degrees) * length;

        [Test]
        public void FourDegreesOff_TurnsToTheEnemy_KeepingTheLength()
        {
            var sim = Scene();
            Vec2 result = AimMagnet.Apply(sim.State, 0, Rotated(4f, 0.6f), Range, MaxDegrees);

            Assert.AreEqual(0f, result.X, 1e-4f, "aim points straight at the enemy");
            Assert.AreEqual(0.6f, result.Y, 1e-4f, "length (fraction of range) is kept");
        }

        [Test]
        public void SixDegreesOff_IsNotChanged()
        {
            var sim = Scene();
            Vec2 aim = Rotated(-6f);
            Vec2 result = AimMagnet.Apply(sim.State, 0, aim, Range, MaxDegrees);

            Assert.AreEqual(aim.X, result.X, 1e-6f);
            Assert.AreEqual(aim.Y, result.Y, 1e-6f);
        }

        [Test]
        public void HiddenEnemy_IsIgnored()
        {
            var sim = Scene();
            sim.State.Fighters[1].VisibleToTeams = 1u << 1; // only its own team sees it (in a bush)
            Vec2 aim = Rotated(4f);
            Vec2 result = AimMagnet.Apply(sim.State, 0, aim, Range, MaxDegrees);

            Assert.AreEqual(aim.X, result.X, 1e-6f);
            Assert.AreEqual(aim.Y, result.Y, 1e-6f);
        }

        [Test]
        public void EnemyOutOfRange_Ally_AndZeroAim_AreIgnored()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 0, 10f, 13f), A.Slot(A.Dummy, 1, 10.5f, 25f));
            sim.Tick(F.Inputs());
            Vec2 aim = Rotated(3f);

            Vec2 result = AimMagnet.Apply(sim.State, 0, aim, Range, MaxDegrees);
            Assert.AreEqual(aim.X, result.X, 1e-6f, "ally in line and enemy beyond the range do not attract");
            Assert.IsTrue(AimMagnet.Apply(sim.State, 0, Vec2.Zero, Range, MaxDegrees).IsZero);
            Assert.AreEqual(aim.X, AimMagnet.Apply(sim.State, 0, aim, Range, 0f).X, 1e-6f, "0° = off");
        }
    }
}
