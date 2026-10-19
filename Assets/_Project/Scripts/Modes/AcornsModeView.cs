using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Data;
using Arena.Gameplay.Match;
using Arena.Gameplay.View;
using UnityEngine;
using UnityEngine.Pool;

namespace Arena.Modes
{
    /// <summary>
    /// Views of the Acorns mode (scene bootstrap next to <see cref="MatchRunner"/>; Modes depends on Gameplay, not back):
    /// the hollow in the map centre and the acorns of <see cref="MatchState.Pickups"/> by slot, from two
    /// <see cref="ObjectPool{T}"/> (normal / golden, D-040). Reads the state in LateUpdate after the runner's ticks; a slot
    /// reused by a new acorn (other spawn tick or value) gets a fresh view. Acorns do not move, so no interpolation;
    /// they slowly spin. Disables itself if the match has no Acorns mode. No allocations per frame after warm-up.
    /// </summary>
    public sealed class AcornsModeView : MonoBehaviour
    {
        [SerializeField] private MatchRunner _runner;

        private readonly Transform[] _views = new Transform[SimLimits.MaxPickups];
        private readonly bool[] _golden = new bool[SimLimits.MaxPickups];
        private readonly int[] _spawnTick = new int[SimLimits.MaxPickups];

        private ObjectPool<Transform> _normalPool;
        private ObjectPool<Transform> _goldenPool;
        private ViewCatalog _catalog;
        private Transform _hollow;
        private bool _built;

        public MatchRunner Runner => _runner;

        /// <summary>Acorn views shown now.</summary>
        public int ActiveCount => _built ? _normalPool.CountActive + _goldenPool.CountActive : 0;

        public Transform Hollow => _hollow;

        private void LateUpdate()
        {
            if (_runner == null) return;
            IMatchDriver driver = _runner.Driver;
            if (driver == null) return;
            if (!_built)
            {
                if (driver.Defs.Mode != ModeKind.Acorns)
                {
                    enabled = false;
                    return;
                }
                Build(driver.Defs);
            }
            Render(driver.State);
        }

        private void Build(MatchDefs defs)
        {
            _catalog = _runner.ViewCatalog;
            _normalPool = CreatePool(_catalog.AcornPrefab, "Acorn");
            _goldenPool = CreatePool(_catalog.AcornGoldenPrefab, "AcornGolden");
            MapThemeConfig theme = _runner.MapView != null ? _runner.MapView.Theme : null;
            GameObject hollow = CreateHollow(transform, _catalog, theme, WorldCoords.ToWorld(defs.Map.Hollow.Center));
            if (hollow != null) _hollow = hollow.transform;
            _built = true;
        }

        /// <summary>
        /// The hollow view at <paramref name="position"/>: the map theme's mesh (Map-SnowValley §1.4 В2, the snow spruce-hollow) with
        /// the theme's prop material, else the catalog's <see cref="ViewCatalog.HollowPrefab"/>; null if neither exists.
        /// </summary>
        public static GameObject CreateHollow(Transform parent, ViewCatalog catalog, MapThemeConfig theme, Vector3 position)
        {
            GameObject hollow = null;
            if (theme != null && theme.Hollow != null)
            {
                Material material = theme.Props != null ? theme.Props : catalog.ObstacleProp;
                hollow = MapView.CreatePropObject(HollowName, parent, theme.Hollow, material, position, 0f);
            }
            else if (catalog.HollowPrefab != null)
            {
                hollow = Instantiate(catalog.HollowPrefab, parent);
                hollow.name = HollowName;
                hollow.transform.position = position;
            }
            return hollow;
        }

        private const string HollowName = "Hollow";

        private void Render(MatchState state)
        {
            PickupState[] pickups = state.Pickups;
            Quaternion spin = Quaternion.Euler(0f, Time.time * _catalog.AcornSpinSpeed, 0f);
            for (int i = 0; i < pickups.Length; i++)
            {
                ref PickupState p = ref pickups[i];
                Transform view = _views[i];
                bool golden = p.Value > 1;
                if (view != null && (!p.Active || _spawnTick[i] != p.SpawnTick || _golden[i] != golden))
                {
                    (_golden[i] ? _goldenPool : _normalPool).Release(view);
                    _views[i] = view = null;
                }
                if (!p.Active) continue;
                if (view == null)
                {
                    view = (golden ? _goldenPool : _normalPool).Get();
                    _views[i] = view;
                    _golden[i] = golden;
                    _spawnTick[i] = p.SpawnTick;
                    view.position = WorldCoords.ToWorld(p.Position, _catalog.AcornHeight);
                }
                view.rotation = spin;
            }
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
                false, _catalog.AcornPoolPrewarm, SimLimits.MaxPickups);
            var warm = new Transform[_catalog.AcornPoolPrewarm];
            for (int i = 0; i < warm.Length; i++) warm[i] = pool.Get();
            for (int i = 0; i < warm.Length; i++) pool.Release(warm[i]);
            return pool;
        }
    }
}
