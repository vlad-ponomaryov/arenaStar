using Arena.Data;
using Arena.Editor.Data;
using Arena.Gameplay.View;
using Arena.Gameplay.View.Anim;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay.Anim
{
    public sealed class MuzzleFlashPoolTests
    {
        private const float Dt = 1f / 60f;
        private FighterAnimConfig _config;
        private GameObject _prefab;
        private GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<FighterAnimConfig>();
            DataSeeder.FillFighterAnim(_config);
            _prefab = new GameObject("FlashPrefab");
            _parent = new GameObject("Parent");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_parent);
            Object.DestroyImmediate(_prefab);
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void Spawn_ShowsThenShrinksAndHides()
        {
            var pool = new MuzzleFlashPool(_prefab, _parent.transform, _config);
            Assert.AreEqual(_config.MuzzlePoolSize, pool.Capacity);
            Assert.AreEqual(0, pool.ActiveCount);

            pool.Spawn(Vector3.one, Quaternion.identity, 1f);
            Assert.AreEqual(1, pool.ActiveCount);
            Transform flash = _parent.transform.GetChild(0);
            Assert.AreEqual(_config.MuzzleScale, flash.localScale.x, 1e-4f);
            pool.Update(_config.MuzzleSeconds * 0.5f);
            Assert.AreEqual(_config.MuzzleScale * 0.5f, flash.localScale.x, 1e-3f);
            pool.Update(_config.MuzzleSeconds);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void Spawn_ReusesOldestWhenFull_NoAllocations()
        {
            var pool = new MuzzleFlashPool(_prefab, _parent.transform, _config);
            for (int i = 0; i < pool.Capacity + 2; i++) pool.Spawn(Vector3.zero, Quaternion.identity, 1f);
            Assert.AreEqual(pool.Capacity, pool.ActiveCount);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 60; i++)
                {
                    pool.Spawn(Vector3.zero, Quaternion.identity, 1.5f);
                    pool.Update(Dt);
                }
            });
        }

        [Test]
        public void MuzzlePoint_IsAlongProp()
        {
            var root = new GameObject("Rig");
            try
            {
                FighterRig rig = root.AddComponent<FighterRig>();
                rig.Bones = new Transform[PoseBuffer.BoneCount];
                Assert.IsFalse(MuzzleFlashPool.MuzzlePoint(rig, 0.4f, out _, out _), "no prop");
                var prop = new GameObject("Prop").transform;
                prop.SetParent(root.transform, false);
                prop.position = new Vector3(1f, 1f, 1f);
                prop.rotation = Quaternion.Euler(0f, 90f, 0f);
                rig.Bones[(int)FighterBone.Prop] = prop;
                Assert.IsTrue(MuzzleFlashPool.MuzzlePoint(rig, 0.4f, out Vector3 at, out _));
                Assert.AreEqual(0f, (at - new Vector3(1.4f, 1f, 1f)).magnitude, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
