using Arena.Core.Math;
using Arena.Core.State;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>ProjectileViewPool on UnityEngine.Pool (D-040): views follow active slots, are reused, interpolate.</summary>
    public sealed class ProjectileViewPoolTests
    {
        private GameObject _root;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PoolTestRoot");
            _prefab = new GameObject("ProjectilePrefab");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_prefab);
        }

        [Test]
        public void Views_FollowActiveSlots_ReusePrewarmed_AndInterpolate()
        {
            const int prewarm = 8;
            var pool = new ProjectileViewPool(_prefab, _root.transform, prewarm, 0.5f);
            Assert.AreEqual(prewarm, pool.CreatedCount);
            Assert.AreEqual(0, pool.ActiveCount);

            var state = new MatchState();
            for (int i = 0; i < 3; i++)
            {
                state.Projectiles[i].Active = true;
                state.Projectiles[i].Radius = 0.2f;
                state.Projectiles[i].Position = new Vec2(i, 0f);
            }
            pool.Capture(state);
            pool.Render(0f);
            Assert.AreEqual(3, pool.ActiveCount);

            state.Projectiles[0].Position = new Vec2(0f, 2f);
            pool.Capture(state);
            pool.Render(0.5f);
            Transform view = FindActiveAt(new Vector3(0f, 0.5f, 1f));
            Assert.IsNotNull(view, "halfway between the two latest ticks");
            Assert.AreEqual(0.4f, view.localScale.x, 1e-5f, "scaled to the hit diameter");

            state.Projectiles[1].Active = false;
            pool.Capture(state);
            pool.Render(0f);
            Assert.AreEqual(2, pool.ActiveCount);
            Assert.AreEqual(prewarm, pool.CreatedCount, "no new views while the prewarmed ones suffice");

            pool.ReleaseAll();
            Assert.AreEqual(0, pool.ActiveCount);
        }

        private Transform FindActiveAt(Vector3 position)
        {
            foreach (Transform t in _root.transform)
                if (t.gameObject.activeSelf && (t.position - position).sqrMagnitude < 1e-6f) return t;
            return null;
        }
    }
}
