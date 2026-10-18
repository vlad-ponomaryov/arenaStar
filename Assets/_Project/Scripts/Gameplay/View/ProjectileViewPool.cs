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
    /// </summary>
    public sealed class ProjectileViewPool
    {
        private readonly Transform[] _views = new Transform[SimLimits.MaxProjectiles];
        private readonly Vec2[] _prev = new Vec2[SimLimits.MaxProjectiles];
        private readonly Vec2[] _curr = new Vec2[SimLimits.MaxProjectiles];
        private readonly int[] _spawnTick = new int[SimLimits.MaxProjectiles];
        private readonly bool[] _active = new bool[SimLimits.MaxProjectiles];
        private readonly float[] _radius = new float[SimLimits.MaxProjectiles];
        private readonly Transform[] _warm;

        private readonly ObjectPool<Transform> _pool;
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly float _height;

        /// <param name="prefab">Unit-diameter view (sphere); scaled to the projectile's hit diameter.</param>
        /// <param name="height">Flight height above the ground in world units (view only).</param>
        public ProjectileViewPool(GameObject prefab, Transform parent, int prewarm, float height)
        {
            _prefab = prefab;
            _parent = parent;
            _height = height;
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
                bool fresh = p.Active && (!_active[i] || _spawnTick[i] != p.SpawnTick);
                _prev[i] = fresh ? p.Position : _curr[i];
                _curr[i] = p.Position;
                _spawnTick[i] = p.SpawnTick;
                _active[i] = p.Active;
                _radius[i] = p.Radius;
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
                view.localScale = new Vector3(diameter, diameter, diameter);
                view.position = WorldCoords.ToWorld(Vec2.Lerp(_prev[i], _curr[i], alpha), _height);
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
