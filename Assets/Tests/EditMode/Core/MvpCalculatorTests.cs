using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>MVP: best weighted score of the winning team (all on a draw), ties to the lower slot.</summary>
    public sealed class MvpCalculatorTests
    {
        private static MatchSimulation CreateFour()
        {
            return TestMatchFactory.CreateWith(AcornsTestFactory.Defs(),
                TestMatchFactory.Slot(0, 0, 3f, 3f), TestMatchFactory.Slot(0, 0, 6f, 3f),
                TestMatchFactory.Slot(0, 1, 3f, 27f), TestMatchFactory.Slot(0, 1, 6f, 27f));
        }

        private static void End(MatchState state, int winner)
        {
            state.Mode.Phase = ModePhase.Ended;
            state.Mode.WinnerTeam = winner;
        }

        [Test]
        public void Score_UsesWeights()
        {
            AcornsModeDef w = AcornsTestFactory.Def();
            var s = new FighterStats { Kills = 2, Deaths = 1, DamageDealt = 3000, HealingDone = 100, AcornsCollected = 5 };
            Assert.AreEqual(2 * 1000f + 3000f + 100f + 5 * 400f - 500f, MvpCalculator.Score(in s, w), 1e-3f);
        }

        [Test]
        public void Mvp_IsFromTheWinningTeam()
        {
            MatchSimulation sim = CreateFour();
            MatchState state = sim.State;
            state.Stats[1].DamageDealt = 2000;
            state.Stats[0].AcornsCollected = 6; // 2400 > 2000
            state.Stats[2].Kills = 10;          // enemy has the best score but lost
            End(state, 0);
            Assert.AreEqual(0, MvpCalculator.Compute(state, sim.Defs.Acorns));
        }

        [Test]
        public void Mvp_OnDraw_IsBestOfAll_TiesToLowerSlot()
        {
            MatchSimulation sim = CreateFour();
            MatchState state = sim.State;
            state.Stats[3].Kills = 1;
            End(state, AcornsRules.NoTeam);
            Assert.AreEqual(3, MvpCalculator.Compute(state, sim.Defs.Acorns));

            state.Stats[3].Kills = 0;
            Assert.AreEqual(0, MvpCalculator.Compute(state, sim.Defs.Acorns), "all equal → slot 0");
        }
    }
}
