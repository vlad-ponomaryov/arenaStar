using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;
using UnityEngine;
using UnityEngine.Pool;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Views of <see cref="MatchState.Projectiles"/> by slot (D-014, D-040: <see cref="ObjectPool{T}"/> from UnityEngine.Pool).
    /// The pool is prewarmed at match start; it only grows if more projectiles fly at once than were prewarmed.
    /// Positions of the two latest ticks are kept per slot for interpolation (D-015); a slot reused by a new projectile
    /// (different spawn tick) snaps instead of sliding. No allocations per frame after warm-up.
    /// One pool per look: an optional mask by ability reference selects the projectiles it draws (spheres vs Chomp's logs,
    /// <c>ProjectileViewKind</c>); a "rolling" pool lays its prefab (axis along local X) across the flight direction, the
    /// hit diameter being its length.
    /// </summary>
    public sealed class ProjectileViewPool
    {
        private readonly Transform[] _views = new Transform[SimLimits.MaxProjectiles];
        private readonly Vec2[] _prev = new Vec2[SimLimits.MaxProjectiles];
        private readonly Vec2[] _curr = new Vec2[SimLimits.MaxProjectiles];
        private readonly int[] _spawnTick = new int[SimLimits.MaxProjectiles];
        private readonly bool[] _active = new bool[SimLimits.MaxProjectiles];
        private readonly float[] _radius = new float[SimLimits.MaxProjectiles];
        private readonly Vec2[] _dir = new Vec2[SimLimits.MaxProjectiles];
        private readonly bool[] _include;
        private readonly float _rollThickness;
        private readonly Transform[] _warm;

        private readonly ObjectPool<Transform> _pool;
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly float _height;

        /// <param name="prefab">Unit-diameter view (sphere); scaled to the projectile's hit diameter.</param>
        /// <param name="height">Flight height above the ground in world units (view only).</param>
        /// <param name="includeByAbilityRef">Draw only projectiles whose ability reference is true here (null = all; references
        /// outside the array are not drawn).</param>
        /// <param name="rollThickness">&gt; 0: rolling look — scale (hit diameter, thickness, thickness), turned across the flight.</param>
        public ProjectileViewPool(GameObject prefab, Transform parent, int prewarm, float height, bool[] includeByAbilityRef = null,
            float rollThickness = 0f)
        {
            _prefab = prefab;
            _parent = parent;
            _height = height;
            _include = includeByAbilityRef;
            _rollThickness = rollThickness;
            _pool = new ObjectPool<Transform>(Create, OnGet, OnRelease, OnDestroy, false, prewarm, SimLimits.MaxProjectiles);
            _warm = new Transform[prewarm];
            for (int i = 0; i < prewarm; i++) _warm[i] = _pool.Get();
            for (int i = 0; i < prewarm; i++) _pool.Release(_warm[i]);
        }

        /// <summary>Number of views currently shown.</summary>
        public int ActiveCount => _pool.CountActive;

        /// <summary>Total views created (shown + pooled).</summary>
        public int CreatedCount => _pool.CountAll;

        /// <summary>Call after every simulation tick.</summary>
        public void Capture(MatchState state)
        {
            ProjectileState[] items = state.Projectiles;
            for (int i = 0; i < items.Length; i++)
            {
                ref ProjectileState p = ref items[i];
                bool active = p.Active && Includes(p.AbilityRef);
                bool fresh = active && (!_active[i] || _spawnTick[i] != p.SpawnTick);
                _prev[i] = fresh ? p.Position : _curr[i];
                _curr[i] = p.Position;
                _spawnTick[i] = p.SpawnTick;
                _active[i] = active;
                _radius[i] = p.Radius;
                _dir[i] = p.Dir;
            }
        }

        /// <param name="alpha">Fraction of the tick elapsed since the latest tick, 0..1.</param>
        public void Render(float alpha)
        {
            for (int i = 0; i < _views.Length; i++)
            {
                Transform view = _views[i];
                if (!_active[i])
                {
                    if (view != null)
                    {
                        _pool.Release(view);
                        _views[i] = null;
                    }
                    continue;
                }
                if (view == null)
                {
                    view = _pool.Get();
                    _views[i] = view;
                }
                float diameter = _radius[i] * 2f;
                Vector3 position = WorldCoords.ToWorld(Vec2.Lerp(_prev[i], _curr[i], alpha), _height);
                if (_rollThickness > 0f && !_dir[i].IsZero)
                {
                    view.localScale = new Vector3(diameter, _rollThickness, _rollThickness);
                    view.SetPositionAndRotation(position, Quaternion.LookRotation(WorldCoords.DirectionToWorld(_dir[i]), Vector3.up));
                }
                else
                {
                    view.localScale = new Vector3(diameter, diameter, diameter);
                    view.position = position;
                }
            }
        }

        /// <summary>Hides every view (match end / restart).</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < _views.Length; i++)
            {
                if (_views[i] == null) continue;
                _pool.Release(_views[i]);
                _views[i] = null;
                _active[i] = false;
            }
        }

        private bool Includes(int abilityRef) =>
            _include == null || (abilityRef >= 0 && abilityRef < _include.Length && _include[abilityRef]);

        private Transform Create()
        {
            GameObject go = Object.Instantiate(_prefab, _parent);
            go.name = "Projectile";
            go.SetActive(false);
            return go.transform;
        }

        private static void OnGet(Transform t) => t.gameObject.SetActive(true);
        private static void OnRelease(Transform t) => t.gameObject.SetActive(false);
        private static void OnDestroy(Transform t) => Object.Destroy(t.gameObject);
    }
}
