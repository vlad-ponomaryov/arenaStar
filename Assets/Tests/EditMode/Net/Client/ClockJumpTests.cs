using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Net.Client;
using Arena.Net.Server.Match;
using Arena.Tests.EditMode.Net.Server;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>
    /// The prediction clock jumping back (hard resync, resimulation limit; audit F2): the own fighter's view is smoothed, not
    /// snapped, and a button pressed on a replayed tick reaches the server in the first new tick (D-006).
    /// </summary>
    public sealed class ClockJumpTests
    {
        private const int Own = 0;
        private const int Back = 5;

        private static PlayerInput Walk(int tick)
        {
            PlayerInput w = MatchServerRig.Wander(tick, 1);
            return new PlayerInput(w.Move, w.Aim, InputButtons.None);
        }

        private static MatchClientRig Start(int seed)
        {
            var rig = new MatchClientRig(ModeKind.Acorns, new[] { 0, 3 }, seed: seed);
            rig.Scripts[0] = Walk;
            rig.Scripts[1] = tick => MatchServerRig.Wander(tick, 5);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 120
                && rig.Sessions[Own].Predictor != null && rig.Sessions[Own].Predictor.Ready, 30f), "the match runs");
            return rig;
        }

        [Test]
        public void PressOnAReplayedTick_IsCarriedIntoTheFirstNewTick()
        {
            MatchClientRig rig = Start(11);
            MatchClientSession s = rig.Sessions[Own];
            int t0 = s.PredictionTick;
            s.Clock.JumpTo(t0 - Back, 0L);
            var press = new PlayerInput(Vec2.Zero, new Vec2(1f, 0f), InputButtons.Attack);
            s.Step(in press, rig.Now); // tick t0 − 5 was sent before: its input stays
            Assert.IsFalse(s.Inputs.Get(t0 - Back).Has(InputButtons.Attack), "a sent tick keeps its first input");
            for (int t = t0 - Back + 1; t < t0; t++) s.Step(default, rig.Now);
            Assert.AreEqual(t0, s.PredictionTick);
            s.Step(default, rig.Now);
            Assert.IsTrue(s.Inputs.Get(t0).Has(InputButtons.Attack), "the press is not lost");
            s.Step(default, rig.Now);
            Assert.IsFalse(s.Inputs.Get(t0 + 1).Has(InputButtons.Attack), "carried once");
        }

        [Test]
        public void PredictionClockJumpsBack_OwnViewIsSmoothed_NotSnapped()
        {
            MatchClientRig rig = Start(13);
            MatchClientSession s = rig.Sessions[Own];
            Predictor p = s.Predictor;
            long snaps = p.Snaps;
            s.Clock.JumpTo(s.PredictionTick - Back, 0L);
            long reconciliations = p.Reconciliations;
            Assert.IsTrue(rig.RunUntil(() => p.Reconciliations > reconciliations, 1f), "a snapshot came");
            Assert.AreEqual(snaps, p.Snaps, "no snap for a short clock jump");
            // Before the fix a changed tick reset the offset to exactly zero (the view jumped by the whole difference).
            Assert.Greater(p.CorrectionOffset.Length, 1e-3f, "the jump is eased out by the correction offset");
        }
    }
}
