using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Gameplay.Match;
using Arena.Gameplay.View;
using UnityEngine;
using UnityEngine.Pool;

namespace Arena.Modes
{
    /// <summary>
    /// Views of the Last Beast mode (S3.14, Server-Spec §9.2; scene bootstrap next to <see cref="MatchRunner"/>, like
    /// <see cref="AcornsModeView"/>): the fog — four translucent strips covering the map outside the safe rectangle of
    /// <see cref="ModeState"/> (hidden before the first step; the shown edges follow a new step smoothly,
    /// <see cref="ViewCatalog.FogFollowSharpness"/>) — and the berries of <see cref="MatchState.Pickups"/> by slot from an
    /// <see cref="ObjectPool{T}"/> (a slot reused by a new berry gets a fresh view; berries do not move, they bob). Berry boxes
    /// are map tiles (<see cref="MapView"/>, removed on <c>TileBroken</c>), so the same views work online, where the display
    /// state has no box entities. Reads the driver's state in LateUpdate; disables itself without the mode. No allocations per
    /// frame after warm-up.
    /// </summary>
    public sealed class LastBeastModeView : MonoBehaviour
    {
        private const int FogStrips = 4;

        [SerializeField] private MatchRunner _runner;

        private readonly Transform[] _views = new Transform[SimLimits.MaxPickups];
        private readonly int[] _spawnTick = new int[SimLimits.MaxPickups];
        private readonly Transform[] _fog = new Transform[FogStrips];

        private ObjectPool<Transform> _berryPool;
        private ViewCatalog _catalog;
        private bool _built;
        private float _mapWidth, _mapHeight;
        private float _minX, _minY, _maxX, _maxY;
        private bool _fogShown;

        public MatchRunner Runner => _runner;

        /// <summary>Berry views shown now.</summary>
        public int BerryCount => _built ? _berryPool.CountActive : 0;

        /// <summary>The fog strips are shown (a fog step happened).</summary>
        public bool FogShown => _fogShown;

        /// <summary>Shown safe rectangle (world X, Z), follows the state's one smoothly.</summary>
        public Rect ShownSafeRect => Rect.MinMaxRect(_minX, _minY, _maxX, _maxY);

        private void LateUpdate()
        {
            if (_runner == null) return;
            IMatchDriver driver = _runner.Driver;
            if (driver == null) return;
            if (!_built)
            {
                if (driver.Defs.Mode != ModeKind.LastBeast)
                {
                    enabled = false;
                    return;
                }
                Build(driver.Defs.Map.Width, driver.Defs.Map.Height);
            }
            RenderBerries(driver.State);
            RenderFog(driver.State, Time.deltaTime);
        }

        private void Build(int width, int height)
        {
            _catalog = _runner.ViewCatalog;
            _mapWidth = width;
            _mapHeight = height;
            _minX = 0f;
            _minY = 0f;
            _maxX = width;
            _maxY = height;
            _berryPool = CreatePool(_catalog.BerryPrefab, "Berry");
            for (int i = 0; i < FogStrips; i++)
            {
                GameObject strip = Instantiate(_catalog.FogPrefab, transform);
                strip.name = "Fog" + i;
                strip.SetActive(false);
                _fog[i] = strip.transform;
            }
            _built = true;
        }

        private void RenderBerries(MatchState state)
        {
            PickupState[] pickups = state.Pickups;
            float bob = Mathf.Sin(Time.time * _catalog.BerryBobSpeed) * _catalog.BerryBobHeight;
            for (int i = 0; i < pickups.Length; i++)
            {
                ref PickupState p = ref pickups[i];
                bool berry = p.Active && p.Kind == PickupKind.Berry;
                Transform view = _views[i];
                if (view != null && (!berry || _spawnTick[i] != p.SpawnTick))
                {
                    _berryPool.Release(view);
                    _views[i] = view = null;
                }
                if (!berry) continue;
                if (view == null)
                {
                    view = _berryPool.Get();
                    _views[i] = view;
                    _spawnTick[i] = p.SpawnTick;
                }
                view.position = WorldCoords.ToWorld(p.Position, _catalog.BerryHeight + bob);
            }
        }

        private void RenderFog(MatchState state, float deltaTime)
        {
            ref ModeState m = ref state.Mode;
            bool fog = m.FogStep > 0;
            if (fog != _fogShown)
            {
                _fogShown = fog;
                for (int i = 0; i < FogStrips; i++) _fog[i].gameObject.SetActive(fog);
            }
            if (!fog) return;
            float k = _catalog.FogFollowSharpness <= 0f ? 1f : 1f - Mathf.Exp(-_catalog.FogFollowSharpness * deltaTime);
            _minX = Mathf.Lerp(_minX, m.SafeMinX, k);
            _minY = Mathf.Lerp(_minY, m.SafeMinY, k);
            _maxX = Mathf.Lerp(_maxX, m.SafeMaxX, k);
            _maxY = Mathf.Lerp(_maxY, m.SafeMaxY, k);
            // Bottom and top strips span the whole width; left and right fill between them.
            PlaceStrip(_fog[0], 0f, 0f, _mapWidth, _minY);
            PlaceStrip(_fog[1], 0f, _maxY, _mapWidth, _mapHeight);
            PlaceStrip(_fog[2], 0f, _minY, _minX, _maxY);
            PlaceStrip(_fog[3], _maxX, _minY, _mapWidth, _maxY);
        }

        /// <summary>A unit quad on the ground (XZ), centred, scaled to the rectangle; empty ones are made tiny.</summary>
        private void PlaceStrip(Transform strip, float x0, float z0, float x1, float z1)
        {
            float w = Mathf.Max(x1 - x0, 0f), d = Mathf.Max(z1 - z0, 0f);
            strip.position = new Vector3((x0 + x1) * 0.5f, _catalog.FogHeight, (z0 + z1) * 0.5f);
            strip.localScale = new Vector3(w, 1f, d);
        }

        private ObjectPool<Transform> CreatePool(GameObject prefab, string viewName)
        {
            Transform parent = transform;
            var pool = new ObjectPool<Transform>(
                () =>
                {
                    GameObject go = Instantiate(prefab, parent);
                    go.name = viewName;
                    go.SetActive(false);
                    return go.transform;
                },
                t => t.gameObject.SetActive(true),
                t => t.gameObject.SetActive(false),
                t => Destroy(t.gameObject),
                false, _catalog.BerryPoolPrewarm, SimLimits.MaxPickups);
            var warm = new Transform[_catalog.BerryPoolPrewarm];
            for (int i = 0; i < warm.Length; i++) warm[i] = pool.Get();
            for (int i = 0; i < warm.Length; i++) pool.Release(warm[i]);
            return pool;
        }
    }
}
