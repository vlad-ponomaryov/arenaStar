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
    /// Host of the match views (S3.10a, Server-Spec §12.2): the ticks come from an <see cref="IMatchDriver"/> — the one given by
    /// <see cref="SetDriver"/> before <c>Start</c> (online bootstrap, S3.10b), else an <see cref="OfflineMatchDriver"/> (simulation
    /// + bots, the former loop of this class). Per frame: <see cref="IMatchDriver.BeginFrame"/>, then that many
    /// <see cref="IMatchDriver.Step"/> with the local player's input from <see cref="InputCollector"/>; after every step the tick
    /// events and the state go to the views. Views read only <see cref="Driver"/> (<c>Defs</c>, <c>State</c>, <c>Events</c>).
    /// It interpolates the views (D-015): map (walls break on <c>TileBroken</c>), fighters (enemies the local team does not
    /// see are hidden, own fighters in bushes are translucent — D-024), projectiles (pooled), health bars; drives the
    /// camera within the map. Thin layer: no game logic here.
    /// Feel (S2.10, GDD §5.3, D-059): <see cref="HitFeedback"/> turns tick events into flashes, damage numbers, sounds,
    /// vibration, ult hit stop (views freeze, the simulation keeps going — D-015) and camera shake; area telegraphs, the own
    /// health number; the local manual aim gets the <see cref="AimMagnet"/> if the setting is on; the colour-blind palette
    /// swaps the enemy materials. Settings are read from <see cref="PlayerSettingsStore.Shared"/> at match start (defaults from
    /// <see cref="FeelConfig"/>), the frame rate is applied then (<see cref="FrameRateApplier"/>, S2.13).
    /// </summary>
    public sealed class MatchRunner : MonoBehaviour
    {
        [SerializeField] private GameDatabase _database;
        [SerializeField] private ViewCatalog _viewCatalog;
        [SerializeField] private MatchCameraController _cameraController;

        [Tooltip("Directional light of the scene, turned with the camera at match start (MatchLightRig).")]
        [SerializeField] private Light _mainLight;

        private readonly LocalInputCollector _inputCollector = new LocalInputCollector();
        private readonly KeyboardMouseInput _keyboardMouse = new KeyboardMouseInput();
        private readonly MatchViewState _viewState = new MatchViewState();
        private readonly FighterView[] _fighterViews = new FighterView[SimLimits.MaxFighters];
        private readonly HealthBarView[] _healthBars = new HealthBarView[SimLimits.MaxFighters];

        private IMatchDriver _driver;
        private IMatchDriver _pendingDriver;
        private AimIndicatorView _aimIndicator;
        private ProjectileViewPool _projectileViews;
        private ProjectileViewPool _logViews;
        private AreaViewPool _areaViews;
        private MapView _mapView;

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
        public Light MainLight => _mainLight;

        /// <summary>Local input sink for the HUD (virtual sticks). Exists from construction.</summary>
        public LocalInputCollector InputCollector => _inputCollector;

        /// <summary>Source of the ticks (null before the match starts).</summary>
        public IMatchDriver Driver => _driver;

        /// <summary>Simulation of the offline driver; null online.</summary>
        public MatchSimulation Simulation => (_driver as OfflineMatchDriver)?.Simulation;

        /// <summary>Feedback of the tick events (counters for tests).</summary>
        public HitFeedback Feedback => _feedback;
        public HitStop HitStop => _hitStop;
        public CameraShake Shake => _shake;
        public DamageNumberPool DamageNumbers => _damageNumbers;
        public TelegraphViewPool Telegraphs => _telegraphs;

        /// <summary>Settings this match was started with.</summary>
        public GameSettings Settings => _settings;

        /// <summary>Bots of all non-local slots.</summary>
        public BotController Bots => (_driver as OfflineMatchDriver)?.Bots;
        public MapView MapView => _mapView;
        public int LocalSlot => _driver != null ? _driver.LocalSlot : 0;
        public int LocalTeam => _driver != null ? _driver.LocalTeam : 0;

        /// <summary>Menu choice this match was started with, or null (scene opened directly: config line-up and seed).</summary>
        public MatchLaunchParams LaunchParams { get; private set; }

        /// <summary>The mode has ended: the simulation stands still, the HUD shows the results.</summary>
        public bool IsMatchEnded => _driver != null && _driver.IsEnded;

        /// <summary>Uses <paramref name="driver"/> instead of the offline one; call before <c>Start</c> (a scene bootstrap that runs
        /// earlier than the runner).</summary>
        public void SetDriver(IMatchDriver driver)
        {
            if (_driver != null) throw new InvalidOperationException("MatchRunner: the match has already started");
            _pendingDriver = driver;
        }

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

        /// <summary>Takes the driver (the given one, else an offline driver for the menu choice from <see cref="SceneFlow.Current"/>,
        /// else for the config's line-up) and creates the views.</summary>
        public void StartMatch()
        {
            if (_database == null) throw new InvalidOperationException("MatchRunner: GameDatabase is not set");
            if (_viewCatalog == null) throw new InvalidOperationException("MatchRunner: ViewCatalog is not set");
            if (_viewCatalog.ProjectilePrefab == null) throw new InvalidOperationException("MatchRunner: ViewCatalog.ProjectilePrefab is not set");
            if (_database.Camera == null) throw new InvalidOperationException("MatchRunner: GameDatabase.Camera is not set");

            if (_database.Feel == null) throw new InvalidOperationException("MatchRunner: GameDatabase.Feel is not set");
            _feel = _database.Feel;
            _settings = PlayerSettingsStore.SharedFor(_feel).Current.Clone();
            FrameRateApplier.Apply(_settings, _feel);

            LaunchParams = SceneFlow.Current;
            _driver = _pendingDriver ?? new OfflineMatchDriver(_database, LaunchParams, _settings);
            _pendingDriver = null;
            MatchDefs defs = _driver.Defs;
            MatchSetup setup = _driver.Setup;
            MatchState state = _driver.State;

            _inputCollector.Clear();
            // Team modes: team 1 sees the map from the other side; free-for-all (Last Beast) is never rotated (S3.14).
            bool freeForAll = defs.Mode == Arena.Core.Modes.ModeKind.LastBeast;
            _inputCollector.ViewRotated = !freeForAll && LocalTeam != 0;
            _keyboardMouse.Reset();

            _mapView = MapView.Create(transform, _viewCatalog, state.Grid);
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

            _viewState.Reset(state);
            _projectileViews.Capture(state);
            if (_logViews != null) _logViews.Capture(state);
            if (_cameraController != null)
                _cameraController.Init(_database.Camera, new Vector2(defs.Map.Width, defs.Map.Height), _inputCollector.ViewRotated,
                    LocalWorldPosition(0f), freeForAll);
            MatchLightRig.Apply(_mainLight, _inputCollector.ViewRotated ? CameraMath.RotatedYaw : 0f, _viewCatalog);
            RenderViews(0f);
        }

        private void Update()
        {
            if (_driver == null) return;

            ref FighterState local = ref _driver.State.Fighters[LocalSlot];
            FighterDef localDef = _driver.Defs.Fighters[local.DefIndex];
            UnityEngine.Camera cam = _cameraController != null ? _cameraController.Camera : null;
            _keyboardMouse.Poll(_inputCollector, cam, local.Position, localDef.AttackRange, UltRange(localDef));

            // Results: the state stands still; keep the last pose, drop pending presses.
            if (_driver.IsEnded) _inputCollector.Clear();
            int ticks = _driver.BeginFrame(Time.deltaTime);
            for (int i = 0; i < ticks; i++) RunTick();

            float alpha = _driver.Alpha;
            float dt = Time.unscaledDeltaTime;
            // Hit stop (D-015): the views keep the pose of the moment of the hit; the simulation above keeps ticking.
            if (!_hitStop.IsActive)
            {
                RenderViews(alpha);
                RenderHealthBars(alpha);
                _telegraphs.Render(_driver.State, _telegraphTicks, alpha, LocalTeam, _viewCatalog.TelegraphAllyZone,
                    _viewCatalog.TelegraphAllyFill, EnemyTelegraphZone, EnemyTelegraphFill);
                _areaViews.Render(_driver.State, alpha, LocalTeam, _viewCatalog.ProjectileHeight, _viewCatalog.TelegraphAllyZone,
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
            ref FighterState me = ref _driver.State.Fighters[LocalSlot];
            _ownHealth.Update(cam, LocalWorldPosition(alpha), me.Health, _viewState.Alive(LocalSlot));
        }

        private void RunTick()
        {
            _driver.Step(_inputCollector);
            SimEventBuffer events = _driver.Events;
            MatchState state = _driver.State;
            _feedback.Process(events, state, Time.unscaledTime);
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == SimEventType.TileBroken) _mapView.OnTileBroken(events[i].Value);
            _viewState.Capture(state);
            _projectileViews.Capture(state);
            if (_logViews != null) _logViews.Capture(state);
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
                ref FighterState f = ref _driver.State.Fighters[i];
                // Own team hidden in a bush: translucent body; a revealed enemy in a bush looks normal.
                bool hiddenAlly = f.Team == LocalTeam && (f.InBush || f.Invisible) && !IsSeenByAnotherTeam(in f);
                Material body = hiddenAlly ? _viewCatalog.FighterHidden : _viewCatalog.FighterBody;
                if (_feedback.IsFlashing(i, Time.unscaledTime) && _viewCatalog.FighterFlash != null) body = _viewCatalog.FighterFlash;
                view.SetBodyMaterial(body);
                view.SetPose(WorldCoords.ToWorld(_viewState.Position(i, alpha)), WorldCoords.DirectionToWorld(_viewState.Facing(i)));
            }

            ref FighterState localFighter = ref _driver.State.Fighters[LocalSlot];
            _mapView.UpdateSeeThrough(_viewState.Position(LocalSlot, alpha), localFighter.Alive && localFighter.InBush);

            _projectileViews.Render(alpha);
            if (_logViews != null) _logViews.Render(alpha);

            Vec2 aim = _inputCollector.Aim;
            if (aim.IsZero || !_viewState.Alive(LocalSlot))
            {
                _aimIndicator.Hide();
                return;
            }
            FighterDef localDef = _driver.Defs.Fighters[_driver.State.Fighters[LocalSlot].DefIndex];
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
            int maxCharges = _driver.Defs.Rules.MaxCharges;
            int count = _viewState.Count;
            for (int i = 0; i < count; i++)
            {
                HealthBarView bar = _healthBars[i];
                if (bar == null) continue;
                bool shown = IsShown(i);
                bar.SetVisible(shown);
                if (!shown) continue;
                ref FighterState f = ref _driver.State.Fighters[i];
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
            _floatingText = new FloatingTextLayer(transform, _feel, _viewCatalog.TextFont, _viewCatalog.TextOutlineColor,
                _viewCatalog.TextOutlineDistance);
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

        internal static float UltRange(FighterDef def) => def.Ult != null && def.Ult.Range > 0f ? def.Ult.Range : def.AttackRange;

        private Material EnemyTelegraphZone => _settings.ColorblindPalette ? _viewCatalog.TelegraphEnemyAltZone : _viewCatalog.TelegraphEnemyZone;
        private Material EnemyTelegraphFill => _settings.ColorblindPalette ? _viewCatalog.TelegraphEnemyAltFill : _viewCatalog.TelegraphEnemyFill;

        private Vector3 LocalWorldPosition(float alpha) => WorldCoords.ToWorld(_viewState.Position(LocalSlot, alpha));

        /// <summary>Alive and seen by the local team (allies always, enemies by the visibility rules, D-024).</summary>
        private bool IsShown(int slot) => _viewState.Alive(slot) && _driver.State.Fighters[slot].IsVisibleTo(LocalTeam);

        /// <summary>True if some team other than the fighter's own sees it (it is not actually hidden).</summary>
        private static bool IsSeenByAnotherTeam(in FighterState f) => (f.VisibleToTeams & ~(1u << f.Team)) != 0;

        private void CreateFighterViews(MatchSetup setup)
        {
            for (int i = 0; i < setup.SlotCount; i++)
            {
                string fighterId = _driver.Defs.Fighters[setup.Slots[i].FighterIndex].Id;
                FighterView view = Instantiate(_viewCatalog.FighterPrefab(fighterId), transform);
                view.name = "Fighter" + i + "_" + fighterId;
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
