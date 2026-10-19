using Arena.Gameplay.Cameras;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>A.1a: fighter prefab by id with the fallback (D-113) and the light turned with the camera (Art-Spec §6.3).</summary>
    public sealed class FighterViewSelectionTests
    {
        private ViewCatalog _catalog;
        private GameObject _fallbackGo;
        private GameObject _pixGo;

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<ViewCatalog>();
            _fallbackGo = new GameObject("Fallback");
            _pixGo = new GameObject("Pix");
            _catalog.FighterViewPrefab = _fallbackGo.AddComponent<FighterView>();
            _catalog.LightPitch = 50f;
            _catalog.LightYawFromCamera = 35f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_fallbackGo);
            Object.DestroyImmediate(_pixGo);
        }

        [Test]
        public void FighterPrefab_FallsBack_WhenNoEntryOrEmptyPrefab()
        {
            _catalog.FighterVisuals = null;
            Assert.AreSame(_catalog.FighterViewPrefab, _catalog.FighterPrefab("pix"));

            _catalog.FighterVisuals = new[] { new FighterVisual { FighterId = "pix", Prefab = null } };
            Assert.AreSame(_catalog.FighterViewPrefab, _catalog.FighterPrefab("pix"), "empty prefab → fallback");
            Assert.AreSame(_catalog.FighterViewPrefab, _catalog.FighterPrefab(null));
            Assert.AreSame(_catalog.FighterViewPrefab, _catalog.FighterPrefab(""));
        }

        [Test]
        public void FighterPrefab_PicksById()
        {
            FighterView pix = _pixGo.AddComponent<FighterView>();
            _catalog.FighterVisuals = new[]
            {
                new FighterVisual { FighterId = "bruno", Prefab = null },
                new FighterVisual { FighterId = "pix", Prefab = pix }
            };
            Assert.AreSame(pix, _catalog.FighterPrefab("pix"));
            Assert.AreSame(_catalog.FighterViewPrefab, _catalog.FighterPrefab("bruno"));
            Assert.AreSame(_catalog.FighterViewPrefab, _catalog.FighterPrefab("Pix"), "ids are case-sensitive");
        }

        [Test]
        public void LightRig_LightsBothTeamsTheSame_RelativeToTheirCamera()
        {
            Quaternion cam0 = CameraMath.Rotation(55f, false);
            Quaternion cam1 = CameraMath.Rotation(55f, true);
            Vector3 l0 = Quaternion.Inverse(cam0) * (MatchLightRig.Rotation(cam0.eulerAngles.y, 50f, 35f) * Vector3.forward);
            Vector3 l1 = Quaternion.Inverse(cam1) * (MatchLightRig.Rotation(cam1.eulerAngles.y, 50f, 35f) * Vector3.forward);
            Assert.Less(Vector3.Distance(l0, l1), 1e-4f, l0 + " vs " + l1);
            // The light shines away from the camera (lit faces are the ones the camera sees) and down.
            Assert.Greater(l0.z, 0f);
            Assert.Less((MatchLightRig.Rotation(0f, 50f, 35f) * Vector3.forward).y, 0f);
        }

        [Test]
        public void LightRig_Apply_TurnsTheLight_AndIgnoresNulls()
        {
            var go = new GameObject("Light");
            try
            {
                var light = go.AddComponent<Light>();
                MatchLightRig.Apply(light, CameraMath.RotatedYaw, _catalog);
                Vector3 e = go.transform.rotation.eulerAngles;
                Assert.AreEqual(50f, e.x, 1e-3f);
                Assert.AreEqual(Mathf.Repeat(CameraMath.RotatedYaw + 35f, 360f), e.y, 1e-3f);
                Assert.DoesNotThrow(() => MatchLightRig.Apply(null, 0f, _catalog));
                Assert.DoesNotThrow(() => MatchLightRig.Apply(light, 0f, null));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
