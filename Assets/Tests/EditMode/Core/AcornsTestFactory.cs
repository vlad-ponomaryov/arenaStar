using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.State;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Test defs of the Acorns mode: the all-floor test map with the hollow at (10, 15) and GDD numbers.</summary>
    public static class AcornsTestFactory
    {
        public const int SpawnInterval = 180;
        public const int GoldenInterval = 1800;
        public const int GoldenValue = 3;
        public const int Target = 12;
        public const int Hold = 360;
        public const int Duration = 4500;
        public const float SpawnScatter = 1.5f;
        public const float DropScatter = 1.2f;
        public const float PickupRadius = 0.3f;
        public const int PickupDelay = 15;
        public static readonly GridCell HollowCell = new GridCell(10, 15);

        public static AcornsModeDef Def(int duration = Duration) => new AcornsModeDef
        {
            SpawnIntervalTicks = SpawnInterval, GoldenIntervalTicks = GoldenInterval, GoldenValue = GoldenValue, TargetAcorns = Target,
            HoldTicks = Hold, DurationTicks = duration, SpawnScatterRadius = SpawnScatter, DropScatterRadius = DropScatter,
            PickupRadius = PickupRadius, PickupDelayTicks = PickupDelay, ScatterAttempts = 8,
            MvpKillWeight = 1000f, MvpDamageWeight = 1f, MvpHealWeight = 1f, MvpAcornWeight = 400f, MvpDeathWeight = 500f
        };

        public static MatchDefs Defs(int duration = Duration)
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            defs.Map.HasHollow = true;
            defs.Map.Hollow = HollowCell;
            defs.Mode = ModeKind.Acorns;
            defs.Acorns = Def(duration);
            return defs;
        }

        /// <summary>Team 0 fighter at (5, 5), team 1 fighter at (15, 25) — both far from the hollow — plus optional more.</summary>
        public static MatchSimulation Create(int duration = Duration) =>
            TestMatchFactory.CreateWith(Defs(duration), TestMatchFactory.Slot(0, 0, 5.5f, 5.5f), TestMatchFactory.Slot(0, 1, 15.5f, 25.5f));

        public static int ActivePickups(MatchState state, out int totalValue)
        {
            int n = 0;
            totalValue = 0;
            for (int i = 0; i < state.Pickups.Length; i++)
            {
                if (!state.Pickups[i].Active) continue;
                n++;
                totalValue += state.Pickups[i].Value;
            }
            return n;
        }

        public static void Place(MatchState state, int slot, Vec2 position, int value = 1, int pickableFrom = 0)
        {
            state.Pickups[slot] = new PickupState
            {
                Active = true, Value = value, Position = position, PickableFromTick = pickableFrom, SpawnTick = state.Tick
            };
        }
    }
}
