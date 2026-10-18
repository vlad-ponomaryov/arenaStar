using System;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;
using S = Arena.Tests.EditMode.Core.ScoutFighterTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>No GC allocations in ticks with Nova and Vex: pierce falloff, marks, team reveal, boosted shots, push-back, hooks, smoke, on-kill refills (CLAUDE.md §1).</summary>
    public sealed class ScoutFighterAllocationTests
    {
        private const int WarmupTicks = 320;
        private const int MeasuredTicks = 320;

        [Test]
        public void Tick_WithNovaAndVexAbilities_DoesNotAllocate()
        {
            AllocationAssert.InitializeTypes(typeof(MatchSimulation).Assembly);
            var defs = S.CreateDefsOnMap(
                "....................",
                "....%.....%.........",
                "....................",
                "....#.....#.........",
                "........\"\"\".........",
                "....................",
                "....................",
                "....................");
            var sim = S.Create(true, defs,
                S.Slot(S.Nova, 0, 2.5f, 1.5f), S.Slot(S.Vex, 0, 4.5f, 2.5f), S.Slot(S.Shooter, 0, 6.5f, 1.5f),
                S.Slot(S.Shooter, 1, 12.5f, 5.5f), S.Slot(S.Vex, 1, 14.5f, 6.5f), S.Slot(S.Nova, 1, 16.5f, 5.5f));
            var inputs = F.Inputs();

            for (int t = 0; t < WarmupTicks; t++) Step(sim, inputs, t);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = WarmupTicks; t < WarmupTicks + MeasuredTicks; t++) Step(sim, inputs, t);
            }, MeasuredTicks + " ticks with Nova and Vex abilities");
        }

        private static void Step(MatchSimulation sim, PlayerInput[] inputs, int t)
        {
            int count = sim.FighterCount;
            for (int i = 0; i < count; i++)
            {
                Vec2 toCentre = new Vec2(10f, 4f) - sim.State.Fighters[i].Position;
                InputButtons buttons = InputButtons.None;
                int phase = (t + i * 7) % 40;
                if (phase == 0) buttons = InputButtons.Attack | InputButtons.AutoAim;
                else if (phase == 10) buttons = InputButtons.Attack;
                else if (phase == 20)
                {
                    A.ChargeUlt(sim, i);
                    buttons = InputButtons.Ult;
                }
                else if (phase == 30) buttons = InputButtons.Trick;
                Vec2 aim = toCentre.IsZero ? Vec2.Zero : toCentre.Normalized * 0.6f;
                inputs[i] = new PlayerInput(new Vec2(MathF.Sin(t * 0.05f + i), MathF.Cos(t * 0.07f + i)) * 0.5f, aim, buttons);
            }
            sim.Tick(inputs);
        }
    }
}
