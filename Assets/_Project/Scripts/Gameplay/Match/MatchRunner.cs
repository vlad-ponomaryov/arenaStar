using System;
using Arena.Bots;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Gameplay.Cameras;
using Arena.Gameplay.Feel;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Input;
using Arena.Gameplay.Settings;
using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Gameplay.Match
{
    /// <summary>
    /// Local (offline) implementation of the match loop (D-038): accumulates frame time, runs <see cref="MatchSimulation.Tick"/>
    /// with a fixed 1/30 s step (at most <see cref="MatchRunnerConfig.MaxTicksPerFrame"/> per frame), feeds the local player's
    /// <see cref="PlayerInput"/> from <see cref="InputCollector"/>; every other slot is a bot (<see cref="BotController"/>, difficulty
    /// from the menu or <see cref="MatchRunnerConfig.DefaultBots"/>, random fighters by the match seed),
    /// then interpolates the views (D-015): map (walls break on <c>TileBroken</c>), fighters (enemies the local team does not
    /// see are hidden, own fighters in bushes are translucent — D-024), projectiles (pooled), health bars; drives the
    /// camera within the map. Thin layer: no game logic here.
    /// Feel (S2.10, GDD §5.3, D-059): <see cref="HitFeedback"/> turns tick events into flashes, damage numbers, sounds,
    /// vibration, ult hit stop (views freeze, the simulation keeps going — D-015) and camera shake; area telegraphs, the own
    /// health number; the local manual aim gets the <see cref="AimMagnet"/> if the setting is on; the colour-blind palette
    /// swaps the enemy materials. Settings are read from <see cref="PlayerSettingsStore.Shared"/> at match start.
    /// </summary>
    public sealed class MatchRunner : MonoBehaviour
    {
        [SerializeField] private GameDatabase _database;
        [SerializeField] private ViewCatalog _viewCatalog;
        [SerializeField] private MatchCameraController _cameraController;

        private readonly LocalInputCollector _inputCollector = new LocalInputCollector();
        private readonly KeyboardMouseInput _keyboardMouse = new KeyboardMouseInput();
        private readonly MatchViewState _viewState = new MatchViewState();
        private readonly PlayerInput[] _inputs = new PlayerInput[SimLimits.MaxFighters];
        private readonly FighterView[] _fighterViews = new FighterView[SimLimits.MaxFighters];
        private readonly HealthBarView[] _healthBars = new HealthBarView[SimLimits.MaxFighters];

        private MatchSimulation _simulation;
        private BotController _bots;
        private AimIndicatorView _aimIndicator;
        private ProjectileViewPool _projectileViews;
        private ProjectileViewPool _logViews;
        private AreaViewPool _areaViews;
        private MapView _mapView;
        private int _maxTicksPerFrame;
        private float _accumulator;

        private FeelConfig _feel;
        private GameSettings _settings;
        private readonly HitStop _hitStop = new HitStop();
        private readonly CameraShake _shake = new CameraShake();
        private Haptics _haptics;
        private ProceduralSounds _sounds;
        private FloatingTextLayer _floatingText;
        private DamageNumberPool _damageNumbers;
        private OwnHealthLabel _ownHealth;
        private HitFeedback _feedback;
        private TelegraphViewPool _telegraphs;
        private int _telegraphTicks;

        public GameDatabase Database => _database;
        public ViewCatalog ViewCatalog => _viewCatalog;
        public MatchCameraController CameraController => _cameraController;

        /// <summary>Local input sink for the HUD (virtual sticks). Exists from construction.</summary>
        public LocalInputCollector InputCollector => _inputCollector;

        public MatchSimulation Simulation => _simulation;

        /// <summary>Feedback of the tick events (counters for tests).</summary>
        public HitFeedback Feedback => _feedback;
        public HitStop HitStop => _hitStop;
        public CameraShake Shake => _shake;
        public DamageNumberPool DamageNumbers => _damageNumbers;
        public TelegraphViewPool Telegraphs => _telegraphs;

        /// <summary>Settings this match was started with.</summary>
        public GameSettings Settings => _settings;

        /// <summary>Bots of all non-local slots.</summary>
        public BotController Bots => _bots;
        public MapView MapView => _mapView;
        public int LocalSlot { get; private set; }
        public int LocalTeam { get; private set; }

        /// <summary>Menu choice this match was started with, or null (scene opened directly: config line-up and seed).</summary>
        public MatchLaunchParams LaunchParams { get; private set; }

        /// <summary>The mode has ended: the simulation stands still, the HUD shows the results.</summary>
        public bool IsMatchEnded => _simulation != null && _simulation.IsEnded;

        private void Start()
        {
            try
            {
                StartMatch();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                enabled = false;
            }
        }

        /// <summary>Builds defs and the setup (menu choice from <see cref="SceneFlow.Current"/>, else the config's) from data,
        /// creates the simulation and the views.</summary>
        public void StartMatch()
        {
            if (_database == null) throw new InvalidOperationException("MatchRunner: GameDatabase is not set");
            if (_viewCatalog == null) throw new InvalidOperationException("MatchRunner: ViewCatalog is not set");
            if (_viewCatalog.ProjectilePrefab == null) throw new InvalidOperationException("MatchRunner: ViewCatalog.ProjectilePrefab is not set");
            if (_database.MatchRunner == null) throw new InvalidOperationException("MatchRunner: GameDatabase.MatchRunner is not set");
            if (_database.Camera == null) throw new InvalidOperationException("MatchRunner: GameDatabase.Camera is not set");

            if (_database.BotBehaviour == null) throw new InvalidOperationException("MatchRunner: GameDatabase.BotBehaviour is not set");
            if (_database.Feel == null) throw new InvalidOperationException("MatchRunner: GameDatabase.Feel is not set");
            _feel = _database.Feel;
            _settings = PlayerSettingsStore.Shared.Current.Clone();

            MatchDefs defs = ConfigConverter.ToMatchDefs(_database);
            LaunchParams = SceneFlow.Current;
            MatchRunnerConfig config = _database.MatchRunner;
            MatchSetup setup = LaunchParams != null
                ? MatchSetupFactory.Create(config, defs, LaunchParams.FighterId, LaunchParams.Seed, config.RandomBotFighters)
                : MatchSetupFactory.Create(config, defs, null, config.Seed, config.RandomBotFighters);
            _simulation = new MatchSimulation(defs, setup);
            _maxTicksPerFrame = config.MaxTicksPerFrame;
            LocalSlot = config.LocalSlot;
            LocalTeam = setup.Slots[LocalSlot].Team;
            _bots = CreateBots(setup, LaunchParams != null ? LaunchParams.Bots : config.DefaultBots);

            _inputCollector.Clear();
            _inputCollector.ViewRotated = LocalTeam != 0;
            _keyboardMouse.Reset();
            _accumulator = 0f;

            _mapView = MapView.Create(transform, _viewCatalog, _simulation.State.Grid);
            CreateFighterViews(setup);
            _aimIndicator = Instantiate(_viewCatalog.AimIndicatorPrefab, transform);
            _aimIndicator.name = "AimIndicator";
            _aimIndicator.Hide();
            bool[] logs = ProjectileViewMask(defs, ProjectileViewKind.Log, true);
            _projectileViews = new ProjectileViewPool(_viewCatalog.ProjectilePrefab, transform, _viewCatalog.ProjectilePoolPrewarm,
                _viewCatalog.ProjectileHeight, ProjectileViewMask(defs, ProjectileViewKind.Log, false));
            _logViews = _viewCatalog.LogPrefab != null
                ? new ProjectileViewPool(_viewCatalog.LogPrefab, transform, 0, _viewCatalog.LogThickness * 0.5f, logs, _viewCatalog.LogThickness)
                : null;
            _areaViews = new AreaViewPool(_viewCatalog, transform);
            _telegraphs = new TelegraphViewPool(_viewCatalog.TelegraphPrefab, transform);
            _telegraphTicks = SimTime.SecondsToTicks(_feel.TelegraphSeconds);
            CreateFeel(defs);

            _viewState.Reset(_simulation.State);
            _projectileViews.Capture(_simulation.State);
            if (_logViews != null) _logViews.Capture(_simulation.State);
            if (_cameraController != null)
                _cameraController.Init(_database.Camera, new Vector2(defs.Map.Width, defs.Map.Height), _inputCollector.ViewRotated,
                    LocalWorldPosition(0f));
            RenderViews(0f);
        }

        private void Update()
        {
            if (_simulation == null) return;

            ref FighterState local = ref _simulation.State.Fighters[LocalSlot];
            FighterDef localDef = _simulation.Defs.Fighters[local.DefIndex];
            UnityEngine.Camera cam = _cameraController != null ? _cameraController.Camera : null;
            _keyboardMouse.Poll(_inputCollector, cam, local.Position, localDef.AttackRange, UltRange(localDef));

            if (_simulation.IsEnded)
            {
                // Results: the state stands still; keep the last pose, drop pending presses.
                _inputCollector.Clear();
                _accumulator = 0f;
            }
            else
            {
                _accumulator += Time.deltaTime;
            }
            int ticks = 0;
            while (_accumulator >= SimTime.Dt && ticks < _maxTicksPerFrame)
            {
                _accumulator -= SimTime.Dt;
                RunTick();
                ticks++;
            }
            // Too far behind (hitch, breakpoint): drop the backlog instead of spiralling.
            if (_accumulator >= SimTime.Dt) _accumulator %= SimTime.Dt;

            float alpha = _accumulator / SimTime.Dt;
            float dt = Time.unscaledDeltaTime;
            // Hit stop (D-015): the views keep the pose of the moment of the hit; the simulation above keeps ticking.
            if (!_hitStop.IsActive)
            {
                RenderViews(alpha);
                RenderHealthBars(alpha);
                _telegraphs.Render(_simulation.State, _telegraphTicks, alpha, LocalTeam, _viewCatalog.TelegraphAllyZone,
                    _viewCatalog.TelegraphAllyFill, EnemyTelegraphZone, EnemyTelegraphFill);
                _areaViews.Render(_simulation.State, alpha, LocalTeam, _viewCatalog.ProjectileHeight, _viewCatalog.TelegraphAllyZone,
                    EnemyTelegraphZone);
            }
            _hitStop.Update(dt);
            _shake.Update(dt);
            if (_cameraController != null)
            {
                _cameraController.SetShakeOffset(_shake.Offset);
                _cameraController.Follow(LocalWorldPosition(alpha), Time.deltaTime);
            }
            _damageNumbers.Update(cam, dt);
            ref FighterState me = ref _simulation.State.Fighters[LocalSlot];
            _ownHealth.Update(cam, LocalWorldPosition(alpha), me.Health, _viewState.Alive(LocalSlot));
        }

        private void RunTick()
        {
            int count = _simulation.FighterCount;
            for (int i = 0; i < count; i++) _inputs[i] = default;
            _bots.Update(_simulation.State, _simulation.Defs, _simulation.Events, _inputs);
            _inputs[LocalSlot] = ApplyAimMagnet(_inputCollector.ConsumeForTick());
            _simulation.Tick(_inputs);
            SimEventBuffer events = _simulation.Events;
            _feedback.Process(events, _simulation.State, Time.unscaledTime);
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == SimEventType.TileBroken) _mapView.OnTileBroken(events[i].Value);
            _viewState.Capture(_simulation.State);
            _projectileViews.Capture(_simulation.State);
            if (_logViews != null) _logViews.Capture(_simulation.State);
        }

        /// <summary>
        /// Per ability reference (<see cref="MatchDefs.MakeAbilityRef"/>): true if its <see cref="AbilityConfig.ProjectileView"/>
        /// is (<paramref name="equals"/>) or is not <paramref name="kind"/>. Fighter configs and defs share the order (ConfigConverter).
        /// </summary>
        private bool[] ProjectileViewMask(MatchDefs defs, ProjectileViewKind kind, bool equals)
        {
            var mask = new bool[defs.Fighters.Length * MatchDefs.AbilitySlotsPerFighter];
            for (int i = 0; i < defs.Fighters.Length && i < _database.Fighters.Count; i++)
            {
                FighterConfig c = _database.Fighters[i];
                mask[MatchDefs.MakeAbilityRef(i, AbilitySlot.Attack)] = (c.Attack != null && c.Attack.ProjectileView == kind) == equals;
                mask[MatchDefs.MakeAbilityRef(i, AbilitySlot.Ult)] = (c.Ult != null && c.Ult.ProjectileView == kind) == equals;
                mask[MatchDefs.MakeAbilityRef(i, AbilitySlot.Trick)] = (c.Trick != null && c.Trick.ProjectileView == kind) == equals;
            }
            return mask;
        }

        private void RenderViews(float alpha)
        {
            int count = _viewState.Count;
            for (int i = 0; i < count; i++)
            {
                FighterView view = _fighterViews[i];
                if (view == null) continue;
                bool shown = IsShown(i);
                view.SetVisible(shown);
                if (!shown) continue;
                ref FighterState f = ref _simulation.State.Fighters[i];
                // Own team hidden in a bush: translucent body; a revealed enemy in a bush looks normal.
                bool hiddenAlly = f.Team == LocalTeam && (f.InBush || f.Invisible) && !IsSeenByAnotherTeam(in f);
                Material body = hiddenAlly ? _viewCatalog.FighterHidden : _viewCatalog.FighterBody;
                if (_feedback.IsFlashing(i, Time.unscaledTime) && _viewCatalog.FighterFlash != null) body = _viewCatalog.FighterFlash;
                view.SetBodyMaterial(body);
                view.SetPose(WorldCoords.ToWorld(_viewState.Position(i, alpha)), WorldCoords.DirectionToWorld(_viewState.Facing(i)));
            }

            ref FighterState localFighter = ref _simulation.State.Fighters[LocalSlot];
            _mapView.UpdateSeeThrough(_viewState.Position(LocalSlot, alpha), localFighter.Alive && localFighter.InBush);

            _projectileViews.Render(alpha);
            if (_logViews != null) _logViews.Render(alpha);

            Vec2 aim = _inputCollector.Aim;
            if (aim.IsZero || !_viewState.Alive(LocalSlot))
            {
                _aimIndicator.Hide();
                return;
            }
            FighterDef localDef = _simulation.Defs.Fighters[_simulation.State.Fighters[LocalSlot].DefIndex];
            AbilityDef aimed = _inputCollector.AimIsUlt && localDef.Ult != null ? localDef.Ult : localDef.Attack;
            float range = _inputCollector.AimIsUlt ? UltRange(localDef) : localDef.AttackRange;
            // A lob lands at aim length × range (D-006): the line ends at the landing point.
            if (aimed != null && aimed.Delivery == DeliveryKind.Lob) range *= Mathf.Min(1f, aim.Length);
            _aimIndicator.Show(LocalWorldPosition(alpha), WorldCoords.DirectionToWorld(aim.Normalized), range);
        }

        private void RenderHealthBars(float alpha)
        {
            UnityEngine.Camera cam = _cameraController != null ? _cameraController.Camera : null;
            Quaternion facing = cam != null ? cam.transform.rotation : Quaternion.identity;
            int maxCharges = _simulation.Defs.Rules.MaxCharges;
            int count = _viewState.Count;
            for (int i = 0; i < count; i++)
            {
                HealthBarView bar = _healthBars[i];
                if (bar == null) continue;
                bool shown = IsShown(i);
                bar.SetVisible(shown);
                if (!shown) continue;
                ref FighterState f = ref _simulation.State.Fighters[i];
                bar.SetPose(WorldCoords.ToWorld(_viewState.Position(i, alpha)), facing);
                bar.SetValues(f.MaxHealth > 0 ? (float)f.Health / f.MaxHealth : 0f, f.Charges, maxCharges);
            }
        }

        /// <summary>Feedback objects of the match (D-059): sounds on a 2D source of this object, vibration, floating texts.</summary>
        private void CreateFeel(MatchDefs defs)
        {
            AudioSource source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            _sounds = new ProceduralSounds(source, _feel);
            _haptics = new Haptics(_settings.VibrationStrength, _feel.VibrationMinInterval);
            _floatingText = new FloatingTextLayer(transform, _feel);
            _damageNumbers = new DamageNumberPool(_floatingText, _feel);
            _ownHealth = new OwnHealthLabel(_floatingText, _feel);
            _hitStop.Reset();
            _shake.Reset();
            _feedback = new HitFeedback(_feel, LocalSlot, LocalTeam, _hitStop, _shake, _haptics, _sounds, _damageNumbers);

            // Damage and health numbers of this match do not allocate after the start.
            int maxHealth = 0;
            for (int i = 0; i < defs.Fighters.Length; i++)
                if (defs.Fighters[i].MaxHealth > maxHealth) maxHealth = defs.Fighters[i].MaxHealth;
            NumberStringCache.Prewarm(maxHealth);
        }

        /// <summary>Manual attack/ult aim of the local player turns to a visible enemy within a few degrees (setting, GDD §5.1).</summary>
        private PlayerInput ApplyAimMagnet(PlayerInput input)
        {
            if (!_settings.AimMagnet || input.Has(InputButtons.AutoAim) || input.Aim.IsZero) return input;
            bool attack = input.Has(InputButtons.Attack);
            if (!attack && !input.Has(InputButtons.Ult)) return input;
            FighterDef def = _simulation.Defs.Fighters[_simulation.State.Fighters[LocalSlot].DefIndex];
            float range = attack ? def.AttackRange : UltRange(def);
            input.Aim = AimMagnet.Apply(_simulation.State, LocalSlot, input.Aim, range, _feel.AimMagnetDegrees);
            return input;
        }

        private static float UltRange(FighterDef def) => def.Ult != null && def.Ult.Range > 0f ? def.Ult.Range : def.AttackRange;

        private Material EnemyTelegraphZone => _settings.ColorblindPalette ? _viewCatalog.TelegraphEnemyAltZone : _viewCatalog.TelegraphEnemyZone;
        private Material EnemyTelegraphFill => _settings.ColorblindPalette ? _viewCatalog.TelegraphEnemyAltFill : _viewCatalog.TelegraphEnemyFill;

        private BotController CreateBots(MatchSetup setup, BotDifficulty difficulty)
        {
            BotDifficultyConfig difficultyConfig = _database.FindBotDifficulty(difficulty);
            if (difficultyConfig == null) throw new InvalidOperationException("MatchRunner: no BotDifficultyConfig for " + difficulty);
            var bots = new BotController(ConfigConverter.ToBotBehaviourDef(_database.BotBehaviour));
            var difficultyDef = ConfigConverter.ToBotDifficultyDef(difficultyConfig);
            for (int i = 0; i < setup.SlotCount; i++)
                if (i != LocalSlot) bots.AddBot(i, difficultyDef, setup.Seed);
            return bots;
        }

        private Vector3 LocalWorldPosition(float alpha) => WorldCoords.ToWorld(_viewState.Position(LocalSlot, alpha));

        /// <summary>Alive and seen by the local team (allies always, enemies by the visibility rules, D-024).</summary>
        private bool IsShown(int slot) => _viewState.Alive(slot) && _simulation.State.Fighters[slot].IsVisibleTo(LocalTeam);

        /// <summary>True if some team other than the fighter's own sees it (it is not actually hidden).</summary>
        private static bool IsSeenByAnotherTeam(in FighterState f) => (f.VisibleToTeams & ~(1u << f.Team)) != 0;

        private void CreateFighterViews(MatchSetup setup)
        {
            for (int i = 0; i < setup.SlotCount; i++)
            {
                FighterView view = Instantiate(_viewCatalog.FighterViewPrefab, transform);
                view.name = "Fighter" + i + "_" + _simulation.Defs.Fighters[setup.Slots[i].FighterIndex].Id;
                bool ally = setup.Slots[i].Team == LocalTeam;
                bool alt = _settings.ColorblindPalette;
                view.SetTeamMaterial(ally ? _viewCatalog.TeamAlly : alt ? _viewCatalog.TeamEnemyAlt : _viewCatalog.TeamEnemy);
                _fighterViews[i] = view;

                if (_viewCatalog.HealthBarPrefab == null) continue;
                HealthBarView bar = Instantiate(_viewCatalog.HealthBarPrefab, transform);
                bar.name = "HealthBar" + i;
                bar.SetFillMaterial(ally ? _viewCatalog.BarAlly : alt ? _viewCatalog.BarEnemyAlt : _viewCatalog.BarEnemy);
                _healthBars[i] = bar;
            }
        }
    }
}
