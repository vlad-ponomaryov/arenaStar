using Arena.Editor.Art;
using Arena.Gameplay.View;
using Arena.Tests.EditMode.Core;
using Arena.UI.Hud;
using Arena.UI.Style;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.Tests.EditMode.UI
{
    /// <summary>A.7a match HUD skin: baked HUD sprites and icons, the ult ring (A.7c), the light attack stick, trick beads, zero allocations, mirrored layout, the health bar.</summary>
    public sealed class MatchHudSkinTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("HudTestRoot");

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private static UiStyle Style()
        {
            var style = AssetDatabase.LoadAssetAtPath<UiStyle>(ViewAssetBuilder.UiStylePath);
            Assert.IsNotNull(style, ViewAssetBuilder.UiStylePath);
            return style;
        }

        private static AbilityHudInfo Info(float charge, bool charged, int usesLeft, bool trickReady) => new AbilityHudInfo
        {
            HasUlt = true, UltCharge = charge, UltCharged = charged, HasTrick = true, TrickReady = trickReady,
            TrickUsesLeft = usesLeft, TrickMaxUses = 3
        };

        [Test]
        public void SeededStyle_HasHudSpritesAndIcons()
        {
            UiStyle s = Style();
            Sprite[] sprites = { s.StickBaseSprite, s.KnobSprite, s.UltRingSprite, s.GlowSprite, s.AcornIcon, s.PawIcon, s.LeafMedalIcon,
                s.StarsIcon, s.UltIcon, s.TrickIcon };
            foreach (Sprite sprite in sprites) Assert.IsNotNull(sprite);
            Assert.AreEqual(s.TrickButtonSize, s.UltStick.KnobSize, 1e-3f, "the ult button is trick-button sized (A.7c)");
            Assert.Greater(s.UltStick.BaseSize, s.UltStick.KnobSize, "the charge ring goes around the ult button");
            Assert.AreNotEqual(s.UltRingFillColor, s.UltStick.BaseColor);
            Assert.Less(s.AttackStick.KnobColor.a, 0.9f, "the attack knob is translucent at rest");
            Assert.Greater(s.AttackStick.KnobColor.g, 0.8f, "the attack knob is light, not red, at rest");
        }

        [Test]
        public void Build_UsesToySprites_FlowerAndIcons()
        {
            UiStyle s = Style();
            MatchHudControls c = MatchHudBuilder.Build(_root.transform, s);
            Assert.IsNotNull(c.UltRingFill);
            Assert.AreEqual(Image.Type.Filled, c.UltRingFill.type);
            Assert.AreSame(s.UltRingSprite, c.UltBase.sprite);
            Assert.AreSame(s.StickBaseSprite, c.AttackBase.sprite);
            Assert.AreSame(s.KnobSprite, c.UltKnob.sprite);
            Assert.AreSame(s.KnobSprite, c.TrickImage.sprite);
            Assert.IsNotNull(c.UltIcon);
            Assert.IsNotNull(c.TrickIcon);
            Assert.IsNotNull(c.UltGlow);
            Assert.IsFalse(c.UltGlow.enabled, "no glow before the ult is charged");
        }

        [Test]
        public void Ring_FillsWithCharge_AndTurnsGoldWhenCharged()
        {
            UiStyle s = Style();
            MatchHudControls c = MatchHudBuilder.Build(_root.transform, s);
            var view = new AbilityHudView(c, s);

            view.Apply(Info(0.4f, false, 3, true));
            Assert.AreEqual(0.4f, c.UltRingFill.fillAmount, 1e-5f);
            Assert.AreEqual(s.UltRingFillColor, c.UltRingFill.color);
            Assert.AreEqual(s.UltNotReadyBaseColor, c.UltBase.color);
            Assert.AreEqual(s.UltNotReadyKnobColor, c.UltKnob.color);
            Assert.IsFalse(c.UltGlow.enabled);

            view.Apply(Info(1f, true, 3, true));
            Assert.AreEqual(1f, c.UltRingFill.fillAmount, 1e-5f);
            Assert.AreEqual(s.UltStick.BaseColor, c.UltRingFill.color);
            Assert.AreEqual(s.UltStick.KnobColor, c.UltKnob.color);
            Assert.IsTrue(c.UltGlow.enabled, "glow when charged");
            Assert.AreEqual(s.IconReadyColor, c.UltIcon.color);
        }

        [Test]
        public void AttackStick_IsLightAtRest_AccentedWhileHeld()
        {
            UiStyle s = Style();
            MatchHudControls c = MatchHudBuilder.Build(_root.transform, s);
            var view = new AbilityHudView(c, s);
            view.ShowAttackHeld(false);
            Assert.AreEqual(s.AttackStick.KnobColor, c.AttackKnob.color);
            view.ShowAttackHeld(true);
            Assert.AreEqual(s.AttackHeldKnobColor, c.AttackKnob.color);
            Assert.AreEqual(s.AttackHeldBaseColor, c.AttackBase.color);
            view.ShowAttackHeld(false);
            Assert.AreEqual(s.AttackStick.BaseColor, c.AttackBase.color);
        }

        [Test]
        public void TrickBeads_ShowUsesLeft()
        {
            UiStyle s = Style();
            MatchHudControls c = MatchHudBuilder.Build(_root.transform, s);
            var view = new AbilityHudView(c, s);
            view.Apply(Info(0f, false, 2, false));
            Assert.AreEqual(3, c.TrickDots.Length);
            Assert.AreSame(s.AcornIcon, c.TrickDots[0].sprite, "acorn beads");
            Assert.AreEqual(s.TrickDotOnColor, c.TrickDots[1].color);
            Assert.AreEqual(s.TrickDotOffColor, c.TrickDots[2].color);
            Assert.AreEqual(s.IconNotReadyColor, c.TrickIcon.color);
            Assert.AreEqual(s.TrickButtonDisabledColor, c.TrickImage.color);
        }

        [Test]
        public void Apply_AndGlow_DoNotAllocate()
        {
            UiStyle s = Style();
            MatchHudControls c = MatchHudBuilder.Build(_root.transform, s);
            var view = new AbilityHudView(c, s);
            AbilityHudInfo a = Info(0.3f, false, 3, true), b = Info(0.45f, false, 2, false), charged = Info(1f, true, 2, true);
            view.Apply(in a);
            view.Apply(in charged);
            view.Apply(in b);
            float t = 0f;
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    view.Apply(in a);
                    view.Apply(in b);
                    view.Apply(in charged);
                    view.AnimateUltGlow(true, t += 0.1f, 1.5f, 0.08f);
                    view.ShowAttackHeld((i & 1) == 0);
                }
            }, "AbilityHudView.Apply / AnimateUltGlow / ShowAttackHeld");
        }

        [Test]
        public void MirroredLayout_PutsButtonsInTheBottomLeftCorner()
        {
            UiStyle s = Style();
            MatchHudControls c = MatchHudBuilder.Build(_root.transform, s, true);
            var ult = (RectTransform)c.UltStick.transform;
            Assert.AreEqual(Vector2.zero, ult.anchorMin);
            Assert.Greater(ult.anchoredPosition.x, 0f);
            Assert.IsTrue(c.UltRingFill.transform.IsChildOf(ult), "the ring goes with the ult stick");
            var trick = (RectTransform)c.TrickButton.transform;
            Assert.Greater(trick.anchoredPosition.x, 0f);
        }

        [Test]
        public void HealthBar_HasSocketsAndBeads_HeightFollowsModel_OwnBeadsBigger()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            Assert.IsNotNull(catalog);
            HealthBarView bar = Object.Instantiate(catalog.HealthBarPrefab, _root.transform);
            Transform charge0 = bar.transform.Find("Charge0");
            Transform socket0 = bar.transform.Find("Socket0");
            Assert.IsNotNull(charge0);
            Assert.IsNotNull(socket0);
            float before = charge0.localScale.x;
            bar.SetOwn(true);
            Assert.Greater(charge0.localScale.x, before * 1.05f, "own beads are bigger");
            Assert.AreEqual(charge0.localScale.x, socket0.localScale.x, 1e-5f);
            bar.SetOwn(false);
            Assert.AreEqual(before, charge0.localScale.x, 1e-5f);

            float fallback = bar.Height;
            bar.SetTopHeight(0f);
            Assert.AreEqual(fallback, bar.Height, "no model height: prefab default");
            bar.SetTopHeight(1.2f);
            Assert.Greater(bar.Height, 1.2f);
            Assert.Less(bar.Height, fallback, "a short model gets a lower bar");

            bar.SetValues(0.5f, 1, 3);
            Assert.IsTrue(socket0.gameObject.activeSelf);
            Assert.IsTrue(bar.transform.Find("Socket2").gameObject.activeSelf);
            Assert.IsTrue(charge0.gameObject.activeSelf);
            Assert.IsFalse(bar.transform.Find("Charge1").gameObject.activeSelf);
        }
    }
}
