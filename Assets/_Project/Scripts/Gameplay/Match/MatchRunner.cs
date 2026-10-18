using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Gameplay.Cameras;
using Arena.Gameplay.Input;
using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Gameplay.Match
{
    /// <summary>
    /// Local (offline) implementation of the match loop (D-038): accumulates frame time, runs <see cref="MatchSimulation.Tick"/>
    /// with a fixed 1/30 s step (at most <see cref="MatchRunnerConfig.MaxTicksPerFrame"/> per frame), feeds the local player's
    /// <see cref="PlayerInput"/> from <see cref="InputCollector"/> (other slots get zero input — dummies until bots, S2.8),
    /// then interpolates the views (D-015): map (walls break on <c>TileBroken</c>), fighters (enemies the local team does not
    /// see are hidden, own fighters in bushes are translucent — D-024), projectiles (pooled), health bars; drives the
    /// camera within the map. Thin layer: no game logic here.
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
        private AimIndicatorView _aimIndicator;
        private ProjectileViewPool _projectileViews;
        private MapView _mapView;
        private int _maxTicksPerFrame;
        private float _accumulator;

        public GameDatabase Database => _database;
        public ViewCatalog ViewCatalog => _viewCatalog;
        public MatchCameraController CameraController => _cameraController;

        /// <summary>Local input sink for the HUD (virtual sticks). Exists from construction.</summary>
        public LocalInputCollector InputCollector => _inputCollector;

        public MatchSimulation Simulation => _simulation;
        public MapView MapView => _mapView;
        public int LocalSlot { get; private set; }
        public int LocalTeam { get; private set; }

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

        /// <summary>Builds defs and the default setup from data, creates the simulation and the views.</summary>
        public void StartMatch()
        {
            if (_database == null) throw new InvalidOperationException("MatchRunner: GameDatabase is not set");
            if (_viewCatalog == null) throw new InvalidOperationException("MatchRunner: ViewCatalog is not set");
            if (_viewCatalog.ProjectilePrefab == null) throw new InvalidOperationException("MatchRunner: ViewCatalog.ProjectilePrefab is not set");
            if (_database.MatchRunner == null) throw new InvalidOperationException("MatchRunner: GameDatabase.MatchRunner is not set");
            if (_database.Camera == null) throw new InvalidOperationException("MatchRunner: GameDatabase.Camera is not set");

            MatchDefs defs = ConfigConverter.ToMatchDefs(_database);
            MatchSetup setup = MatchSetupFactory.CreateDefault(_database.MatchRunner, defs);
            _simulation = new MatchSimulation(defs, setup);
            _maxTicksPerFrame = _database.MatchRunner.MaxTicksPerFrame;
            LocalSlot = _database.MatchRunner.LocalSlot;
            LocalTeam = setup.Slots[LocalSlot].Team;

            _inputCollector.Clear();
            _inputCollector.ViewRotated = LocalTeam != 0;
            _keyboardMouse.Reset();
            _accumulator = 0f;

            _mapView = MapView.Create(transform, _viewCatalog, _simulation.State.Grid);
            CreateFighterViews(setup);
            _aimIndicator = Instantiate(_viewCatalog.AimIndicatorPrefab, transform);
            _aimIndicator.name = "AimIndicator";
            _aimIndicator.Hide();
            _projectileViews = new ProjectileViewPool(_viewCatalog.ProjectilePrefab, transform, _viewCatalog.ProjectilePoolPrewarm,
                _viewCatalog.ProjectileHeight);

            _viewState.Reset(_simulation.State);
            _projectileViews.Capture(_simulation.State);
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
            _keyboardMouse.Poll(_inputCollector, cam, local.Position, localDef.AttackRange);

            _accumulator += Time.deltaTime;
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
            RenderViews(alpha);
            if (_cameraController != null) _cameraController.Follow(LocalWorldPosition(alpha), Time.deltaTime);
            RenderHealthBars(alpha);
        }

        private void RunTick()
        {
            int count = _simulation.FighterCount;
            for (int i = 0; i < count; i++) _inputs[i] = default;
            _inputs[LocalSlot] = _inputCollector.ConsumeForTick();
            _simulation.Tick(_inputs);
            SimEventBuffer events = _simulation.Events;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == SimEventType.TileBroken) _mapView.OnTileBroken(events[i].Value);
            _viewState.Capture(_simulation.State);
            _projectileViews.Capture(_simulation.State);
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
                view.SetBodyMaterial(hiddenAlly ? _viewCatalog.FighterHidden : _viewCatalog.FighterBody);
                view.SetPose(WorldCoords.ToWorld(_viewState.Position(i, alpha)), WorldCoords.DirectionToWorld(_viewState.Facing(i)));
            }

            ref FighterState localFighter = ref _simulation.State.Fighters[LocalSlot];
            _mapView.UpdateSeeThrough(_viewState.Position(LocalSlot, alpha), localFighter.Alive && localFighter.InBush);

            _projectileViews.Render(alpha);

            Vec2 aim = _inputCollector.Aim;
            if (aim.IsZero || !_viewState.Alive(LocalSlot))
            {
                _aimIndicator.Hide();
                return;
            }
            float range = _simulation.Defs.Fighters[_simulation.State.Fighters[LocalSlot].DefIndex].AttackRange;
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
                view.SetTeamMaterial(setup.Slots[i].Team == LocalTeam ? _viewCatalog.TeamAlly : _viewCatalog.TeamEnemy);
                _fighterViews[i] = view;

                if (_viewCatalog.HealthBarPrefab == null) continue;
                HealthBarView bar = Instantiate(_viewCatalog.HealthBarPrefab, transform);
                bar.name = "HealthBar" + i;
                bar.SetFillMaterial(setup.Slots[i].Team == LocalTeam ? _viewCatalog.BarAlly : _viewCatalog.BarEnemy);
                _healthBars[i] = bar;
            }
        }
    }
}
