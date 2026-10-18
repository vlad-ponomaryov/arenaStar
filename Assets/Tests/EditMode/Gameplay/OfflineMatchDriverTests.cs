using Arena.Bots;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Input;
using Arena.Gameplay.Match;
using Arena.Gameplay.Settings;
using Arena.Net.Diagnostics;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>
    /// S3.10a: <see cref="OfflineMatchDriver"/> reproduces the former <c>MatchRunner</c> loop exactly — same seed, same frame
    /// times and same local input give the same <see cref="StateHash"/> every frame, the same tick counts and alpha.
    /// </summary>
    public sealed class OfflineMatchDriverTests
    {
        private const int Seed = 7;
        private const int MaxFrames = 20000;

        // Frame times, including hitches longer than MaxTicksPerFrame ticks (the backlog is dropped).
        private static readonly float[] FrameTimes = { 0.016f, 0.0167f, 0.033f, 0.02f, 0.25f, 0.011f, 0.05f, 0.016f };

        [Test]
        public void OfflineDriver_MatchesTheFormerLoop_TickForTick()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db, "seeded database");
            var settings = GameSettings.FromDefaults(db.Feel);
            settings.AimMagnet = true;
            var launch = new MatchLaunchParams("bruno", BotDifficulty.Easy, Seed);

            var driver = new OfflineMatchDriver(db, launch, settings);
            var legacy = new LegacyLoop(db, launch, settings);
            var driverInput = new LocalInputCollector();
            var legacyInput = new LocalInputCollector();

            Assert.AreEqual(legacy.LocalSlot, driver.LocalSlot);
            Assert.AreEqual(legacy.Sim.Setup.Slots[legacy.LocalSlot].Team, driver.LocalTeam);
            Assert.AreEqual(StateHash.Compute(legacy.Sim.State), StateHash.Compute(driver.State), "initial state");

            int frame = 0;
            int totalTicks = 0;
            for (; frame < MaxFrames && !driver.IsEnded; frame++)
            {
                Script(driverInput, frame);
                Script(legacyInput, frame);
                float dt = FrameTimes[frame % FrameTimes.Length];

                if (driver.IsEnded) driverInput.Clear();
                int ticks = driver.BeginFrame(dt);
                for (int i = 0; i < ticks; i++) driver.Step(driverInput);
                int legacyTicks = legacy.Frame(dt, legacyInput);

                Assert.AreEqual(legacyTicks, ticks, "ticks of frame " + frame);
                Assert.AreEqual(legacy.Alpha, driver.Alpha, "alpha of frame " + frame);
                Assert.AreEqual(legacy.Sim.IsEnded, driver.IsEnded, "end of frame " + frame);
                if (StateHash.Compute(legacy.Sim.State) != StateHash.Compute(driver.State))
                    Assert.Fail("state differs after frame " + frame + " (tick " + driver.State.Tick + ")");
                totalTicks += ticks;
            }

            Assert.IsTrue(driver.IsEnded, "the match ended within " + MaxFrames + " frames");
            Assert.Greater(totalTicks, 0);
            Assert.AreEqual(legacy.Sim.State.Mode.WinnerTeam, driver.State.Mode.WinnerTeam);
            Assert.AreSame(driver.Simulation.State, driver.State, "offline: the shown state is the simulation's");
            Assert.AreSame(driver.Simulation.Events, driver.Events);

            // After the end the state stands still.
            ulong ended = StateHash.Compute(driver.State);
            Assert.AreEqual(0, driver.BeginFrame(1f));
            Assert.AreEqual(0f, driver.Alpha);
            Assert.AreEqual(ended, StateHash.Compute(driver.State));
        }

        /// <summary>Moves in a slow circle, aims manually and fires now and then, uses the trick.</summary>
        private static void Script(LocalInputCollector input, int frame)
        {
            float angle = frame * 0.02f;
            var dir = new Vec2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle));
            input.SetMove(InputChannel.Touch, dir);
            if (frame % 17 == 0) input.PressAttack(new Vec2(dir.Y, -dir.X), false);
            if (frame % 41 == 0) input.PressAttack(Vec2.Zero, true);
            if (frame % 97 == 0) input.PressTrick();
        }

        /// <summary>The loop of <c>MatchRunner</c> before S3.10a (Update + RunTick + ApplyAimMagnet + CreateBots), verbatim.</summary>
        private sealed class LegacyLoop
        {
            public readonly MatchSimulation Sim;
            public readonly int LocalSlot;
            private readonly BotController _bots;
            private readonly PlayerInput[] _inputs = new PlayerInput[SimLimits.MaxFighters];
            private readonly int _maxTicksPerFrame;
            private readonly bool _aimMagnet;
            private readonly float _aimMagnetDegrees;
            private float _accumulator;

            public LegacyLoop(GameDatabase db, MatchLaunchParams launch, GameSettings settings)
            {
                MatchDefs defs = ConfigConverter.ToMatchDefs(db);
                MatchRunnerConfig config = db.MatchRunner;
                MatchSetup setup = MatchSetupFactory.Create(config, defs, launch.FighterId, launch.Seed, config.RandomBotFighters);
                Sim = new MatchSimulation(defs, setup);
                _maxTicksPerFrame = config.MaxTicksPerFrame;
                LocalSlot = config.LocalSlot;
                _aimMagnet = settings.AimMagnet;
                _aimMagnetDegrees = db.Feel.AimMagnetDegrees;
                _bots = new BotController(ConfigConverter.ToBotBehaviourDef(db.BotBehaviour));
                var difficulty = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(launch.Bots));
                for (int i = 0; i < setup.SlotCount; i++)
                    if (i != LocalSlot) _bots.AddBot(i, difficulty, setup.Seed);
            }

            public float Alpha => _accumulator / SimTime.Dt;

            public int Frame(float deltaTime, LocalInputCollector input)
            {
                if (Sim.IsEnded)
                {
                    input.Clear();
                    _accumulator = 0f;
                }
                else
                {
                    _accumulator += deltaTime;
                }
                int ticks = 0;
                while (_accumulator >= SimTime.Dt && ticks < _maxTicksPerFrame)
                {
                    _accumulator -= SimTime.Dt;
                    RunTick(input);
                    ticks++;
                }
                if (_accumulator >= SimTime.Dt) _accumulator %= SimTime.Dt;
                return ticks;
            }

            private void RunTick(LocalInputCollector input)
            {
                int count = Sim.FighterCount;
                for (int i = 0; i < count; i++) _inputs[i] = default;
                _bots.Update(Sim.State, Sim.Defs, Sim.Events, _inputs);
                _inputs[LocalSlot] = ApplyAimMagnet(input.ConsumeForTick());
                Sim.Tick(_inputs);
            }

            private PlayerInput ApplyAimMagnet(PlayerInput input)
            {
                if (!_aimMagnet || input.Has(InputButtons.AutoAim) || input.Aim.IsZero) return input;
                bool attack = input.Has(InputButtons.Attack);
                if (!attack && !input.Has(InputButtons.Ult)) return input;
                FighterDef def = Sim.Defs.Fighters[Sim.State.Fighters[LocalSlot].DefIndex];
                float range = attack ? def.AttackRange : (def.Ult != null && def.Ult.Range > 0f ? def.Ult.Range : def.AttackRange);
                input.Aim = AimMagnet.Apply(Sim.State, LocalSlot, input.Aim, range, _aimMagnetDegrees);
                return input;
            }
        }
    }
}
