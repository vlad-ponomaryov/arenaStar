using System;
using Arena.Bots;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Input;
using Arena.Gameplay.Settings;

namespace Arena.Gameplay.Match
{
    /// <summary>
    /// Offline <see cref="IMatchDriver"/> (S3.10a): the former loop of <see cref="MatchRunner"/>, behaviour unchanged.
    /// Builds defs and the setup from data (menu choice <see cref="MatchLaunchParams"/>, else the config's line-up and seed;
    /// <see cref="MatchLaunchParams.Mode"/> Last Beast — the royale map with one slot per shared spawn, S3.14),
    /// runs <see cref="MatchSimulation"/> with a fixed 1/30 s step (accumulator, at most
    /// <see cref="MatchRunnerConfig.MaxTicksPerFrame"/> per frame, the backlog is dropped), every non-local slot is a bot
    /// (<see cref="BotController"/>), the local manual aim gets the <see cref="AimMagnet"/> if the setting is on.
    /// Plain C# — usable without a scene (EditMode tests).
    /// </summary>
    public sealed class OfflineMatchDriver : IMatchDriver
    {
        private readonly GameDatabase _database;
        private readonly MatchSimulation _simulation;
        private readonly BotController _bots;
        private readonly PlayerInput[] _inputs = new PlayerInput[SimLimits.MaxFighters];
        private readonly int _maxTicksPerFrame;
        private readonly bool _aimMagnet;
        private readonly float _aimMagnetDegrees;
        private float _accumulator;

        /// <param name="launch">Menu choice, or null (scene opened directly: config line-up and seed).</param>
        /// <param name="settings">Player settings of this match (aim magnet).</param>
        public OfflineMatchDriver(GameDatabase database, MatchLaunchParams launch, GameSettings settings)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (database.MatchRunner == null) throw new InvalidOperationException("OfflineMatchDriver: GameDatabase.MatchRunner is not set");
            if (database.BotBehaviour == null) throw new InvalidOperationException("OfflineMatchDriver: GameDatabase.BotBehaviour is not set");
            if (database.Feel == null) throw new InvalidOperationException("OfflineMatchDriver: GameDatabase.Feel is not set");
            _database = database;
            _aimMagnet = settings.AimMagnet;
            _aimMagnetDegrees = database.Feel.AimMagnetDegrees;

            MatchRunnerConfig config = database.MatchRunner;
            MatchDefs defs;
            MatchSetup setup;
            if (launch != null && launch.Mode == ModeKind.LastBeast)
            {
                // Training in Last Beast (S3.14): 1 player + bots on the royale map, defs as the server builds them.
                if (database.LastBeastMode == null || database.LastBeastMaps == null || database.LastBeastMaps.Count == 0)
                    throw new InvalidOperationException("OfflineMatchDriver: Last Beast is not in the GameDatabase");
                defs = ConfigConverter.ToLastBeastMatchDefs(database, database.LastBeastMaps[0]);
                setup = MatchSetupFactory.CreateFreeForAll(config, defs, launch.FighterId, launch.Seed);
            }
            else
            {
                defs = ConfigConverter.ToMatchDefs(database);
                setup = launch != null
                    ? MatchSetupFactory.Create(config, defs, launch.FighterId, launch.Seed, config.RandomBotFighters)
                    : MatchSetupFactory.Create(config, defs, null, config.Seed, config.RandomBotFighters);
            }
            _simulation = new MatchSimulation(defs, setup);
            _maxTicksPerFrame = config.MaxTicksPerFrame;
            LocalSlot = config.LocalSlot;
            LocalTeam = setup.Slots[LocalSlot].Team;
            _bots = CreateBots(setup, launch != null ? launch.Bots : config.DefaultBots);
        }

        public MatchSimulation Simulation => _simulation;

        /// <summary>Bots of all non-local slots.</summary>
        public BotController Bots => _bots;

        public MatchDefs Defs => _simulation.Defs;
        public MatchSetup Setup => _simulation.Setup;
        public MatchState State => _simulation.State;
        public SimEventBuffer Events => _simulation.Events;
        public int LocalSlot { get; }
        public int LocalTeam { get; }
        public bool IsEnded => _simulation.IsEnded;
        public float Alpha => _accumulator / SimTime.Dt;

        /// <summary>Match menu open (D-129): no ticks, the clock does not run (the views stand still at the same alpha).</summary>
        public bool Paused { get; set; }

        public int BeginFrame(float deltaTime)
        {
            if (Paused) return 0;
            // Results: the state stands still.
            if (_simulation.IsEnded) _accumulator = 0f;
            else _accumulator += deltaTime;
            int ticks = 0;
            while (_accumulator >= SimTime.Dt && ticks < _maxTicksPerFrame)
            {
                _accumulator -= SimTime.Dt;
                ticks++;
            }
            // Too far behind (hitch, breakpoint): drop the backlog instead of spiralling.
            if (_accumulator >= SimTime.Dt) _accumulator %= SimTime.Dt;
            return ticks;
        }

        public void Step(LocalInputCollector input)
        {
            int count = _simulation.FighterCount;
            for (int i = 0; i < count; i++) _inputs[i] = default;
            _bots.Update(_simulation.State, _simulation.Defs, _simulation.Events, _inputs);
            _inputs[LocalSlot] = ApplyAimMagnet(input.ConsumeForTick());
            _simulation.Tick(_inputs);
        }

        /// <summary>Manual attack/ult aim of the local player turns to a visible enemy within a few degrees (setting, GDD §5.1).</summary>
        private PlayerInput ApplyAimMagnet(PlayerInput input)
        {
            if (!_aimMagnet || input.Has(InputButtons.AutoAim) || input.Aim.IsZero) return input;
            bool attack = input.Has(InputButtons.Attack);
            if (!attack && !input.Has(InputButtons.Ult)) return input;
            FighterDef def = _simulation.Defs.Fighters[_simulation.State.Fighters[LocalSlot].DefIndex];
            float range = attack ? def.AttackRange : MatchRunner.UltRange(def);
            input.Aim = AimMagnet.Apply(_simulation.State, LocalSlot, input.Aim, range, _aimMagnetDegrees);
            return input;
        }

        private BotController CreateBots(MatchSetup setup, BotDifficulty difficulty)
        {
            BotDifficultyConfig difficultyConfig = _database.FindBotDifficulty(difficulty);
            if (difficultyConfig == null) throw new InvalidOperationException("OfflineMatchDriver: no BotDifficultyConfig for " + difficulty);
            var bots = new BotController(ConfigConverter.ToBotBehaviourDef(_database.BotBehaviour));
            var difficultyDef = ConfigConverter.ToBotDifficultyDef(difficultyConfig);
            for (int i = 0; i < setup.SlotCount; i++)
                if (i != LocalSlot) bots.AddBot(i, difficultyDef, setup.Seed);
            return bots;
        }
    }
}
