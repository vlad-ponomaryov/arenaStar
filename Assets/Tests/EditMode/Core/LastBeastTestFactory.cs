using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Test defs of the Last Beast mode: the test fighters on an all-floor 21×21 map with 4 shared spawns and berry boxes
    /// at (10, 14) and (14, 10), GDD numbers and the PROTO numbers of D-089 (in ticks).
    /// </summary>
    public static class LastBeastTestFactory
    {
        public const int Size = 21;
        public const int FogStart = 1200;
        public const int FogInterval = 600;
        public const float FogShrink = 2f;
        public const float FogMinHalf = 2.5f;
        public const int FogDps = 500;
        public const int FogGrowth = 250;
        public const int FogPulse = 15;
        public const int BoxHealth = 3600;
        public const int BerryPercent = 8;
        public const float BerryRadius = 0.3f;
        public const int BerryDelay = 15;
        public const int MaxDuration = 10800;
        public static readonly GridCell BoxA = new GridCell(10, 14);
        public static readonly GridCell BoxB = new GridCell(14, 10);

        public static LastBeastModeDef Def(int maxDuration = MaxDuration) => new LastBeastModeDef
        {
            FogStartTicks = FogStart, FogStepIntervalTicks = FogInterval, FogShrinkCells = FogShrink, FogMinHalfSize = FogMinHalf,
            FogDamagePerSecond = FogDps, FogDamageGrowthPerStep = FogGrowth, FogPulseTicks = FogPulse, BoxHealth = BoxHealth,
            BerriesPerBox = 1, BerryDamagePercent = BerryPercent, BerryHealthPercent = BerryPercent, ExtraBerriesOnDefeat = 1,
            BerryPickupRadius = BerryRadius, BerryDropScatterRadius = 1.2f, BerryPickupDelayTicks = BerryDelay, ScatterAttempts = 8,
            MaxDurationTicks = maxDuration
        };

        public static MapDef Map()
        {
            var map = new MapDef { Id = "lb_test", Width = Size, Height = Size, Tiles = new TileType[Size * Size] };
            map.Tiles[BoxA.Row * Size + BoxA.Column] = TileType.BerryBox;
            map.Tiles[BoxB.Row * Size + BoxB.Column] = TileType.BerryBox;
            map.Spawns = new[]
            {
                new MapSpawn(MapSpawn.SharedTeam, new GridCell(2, 2)), new MapSpawn(MapSpawn.SharedTeam, new GridCell(18, 2)),
                new MapSpawn(MapSpawn.SharedTeam, new GridCell(2, 18)), new MapSpawn(MapSpawn.SharedTeam, new GridCell(18, 18))
            };
            map.Validate();
            return map;
        }

        public static MatchDefs Defs(int maxDuration = MaxDuration)
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            defs.Map = Map();
            defs.Mode = ModeKind.LastBeast;
            defs.LastBeast = Def(maxDuration);
            defs.NoRespawn = true;
            return defs;
        }

        /// <summary>Fighters of def 0 at the given points, slot i = team i.</summary>
        public static MatchSimulation Create(MatchDefs defs, params Vec2[] positions)
        {
            var slots = new PlayerSlotSetup[positions.Length];
            for (int i = 0; i < positions.Length; i++) slots[i] = new PlayerSlotSetup(0, i, 1, positions[i]);
            return TestMatchFactory.CreateWith(defs, slots);
        }

        /// <summary>Three fighters far from the boxes and inside the safe square for a long time.</summary>
        public static MatchSimulation CreateThree(int maxDuration = MaxDuration) =>
            Create(Defs(maxDuration), new Vec2(10.5f, 10.5f), new Vec2(6.5f, 6.5f), new Vec2(14.5f, 14.5f));
    }
}
