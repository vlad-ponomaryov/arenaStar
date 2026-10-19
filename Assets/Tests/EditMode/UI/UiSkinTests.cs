using Arena.Editor.Art;
using Arena.Gameplay.View;
using Arena.UI.Common;
using Arena.UI.Style;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.Tests.EditMode.UI
{
    /// <summary>A.6 "wood and leaves" skin: the seeded style (Nunito, sprites, palette) and the toy buttons of <see cref="UiFactory"/>.</summary>
    public sealed class UiSkinTests
    {
        private GameObject _root;

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

        [Test]
        public void SeededStyle_HasNunitoFonts_AndBakedSprites()
        {
            UiStyle s = Style();
            Assert.IsNotNull(s.Font, "body font");
            Assert.IsNotNull(s.HeadingFont, "heading font");
            StringAssert.Contains("Nunito", AssetDatabase.GetAssetPath(s.Font));
            StringAssert.Contains("ExtraBold", AssetDatabase.GetAssetPath(s.HeadingFont));
            Assert.IsTrue(System.IO.File.Exists(ViewAssetBuilder.FontsFolder + "/OFL.txt"), "OFL.txt next to the fonts");
            Sprite[] sliced = { s.PanelSprite, s.ButtonEdgeSprite, s.ButtonFaceSprite, s.ButtonGlossSprite, s.ShadowSprite, s.PlankSprite };
            foreach (Sprite sprite in sliced)
            {
                Assert.IsNotNull(sprite);
                Assert.Greater(sprite.border.x, 0f, sprite.name + " is 9-slice");
                Assert.Less(sprite.border.x + sprite.border.z, sprite.rect.width, sprite.name + " border fits");
                Assert.Less(sprite.border.y + sprite.border.w, sprite.rect.height, sprite.name + " border fits");
            }
            Assert.IsNotNull(s.LeafSprite);
            Assert.IsNotNull(s.BackdropSprite);
            Assert.Greater(s.SliceScale, 0f);
            Assert.Greater(s.ButtonEdgeHeight, 0f);
        }

        [Test]
        public void SeededStyle_UsesPaletteOfArtSpec()
        {
            UiStyle s = Style();
            AssertHex("FFF4DE", s.PanelColor);
            AssertHex("8A5A35", s.PanelBorderColor);
            AssertHex("4CBF56", s.PrimaryButtonColor);
            AssertHex("2F8F3A", s.PrimaryButtonEdgeColor);
            AssertHex("6EC6F0", s.SecondaryButtonColor);
            AssertHex("3B8FBF", s.SecondaryButtonEdgeColor);
            AssertHex("3B2A1E", s.TextDarkColor);
            AssertHex("3B2A1E", s.TextOutlineColor);
            AssertHex("FFFFFF", s.TextColor);
        }

        [Test]
        public void ViewCatalog_TextFont_IsTheHeadingFont()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            Assert.IsNotNull(catalog.TextFont);
            Assert.AreSame(Style().HeadingFont, catalog.TextFont);
            Assert.AreNotEqual(Vector2.zero, catalog.TextOutlineDistance);
        }

        [Test]
        public void FontFor_LargeSizesUseHeadingFont()
        {
            UiStyle s = Style();
            Assert.AreSame(s.HeadingFont, UiFactory.FontFor(s, s.LargeFontSize));
            Assert.AreSame(s.HeadingFont, UiFactory.FontFor(s, s.TitleFontSize));
            Assert.AreSame(s.Font, UiFactory.FontFor(s, s.MediumFontSize));
            var bare = ScriptableObject.CreateInstance<UiStyle>();
            try
            {
                Assert.AreSame(UiFactory.Font, UiFactory.FontFor(bare, 40), "no fonts in the style — built-in");
            }
            finally
            {
                Object.DestroyImmediate(bare);
            }
        }

        [Test]
        public void Button_HasEdgeFaceAndContrastingLabel_Recolours()
        {
            UiStyle s = Style();
            _root = new GameObject("Root", typeof(RectTransform));
            Button button = UiFactory.CreateButton("B", _root.transform, s, s.MenuButtonSize, s.PrimaryButtonColor, s.MediumFontSize,
                "x", null, out Text label);
            ToyButton toy = button.GetComponent<ToyButton>();
            Assert.IsNotNull(toy);
            Assert.AreSame(toy.Face, button.targetGraphic);
            Assert.AreEqual(s.PrimaryButtonEdgeColor, toy.Edge.color);
            Assert.AreEqual(s.TextColor, label.color, "white label on green");
            Assert.IsTrue(label.GetComponent<Outline>().enabled);
            Assert.AreEqual(s.Font, label.font);

            UiFactory.SetButtonColor(button, s, s.ButtonColor);
            Assert.AreEqual(s.ButtonColor, toy.Face.color);
            Assert.AreEqual(s.EdgeFor(s.ButtonColor), toy.Edge.color);
            Assert.AreEqual(s.TextDarkColor, label.color, "dark label on the light choice");
            Assert.IsFalse(label.GetComponent<Outline>().enabled);

            UiFactory.SetButtonColor(button, s, s.ButtonSelectedColor);
            Assert.AreEqual(s.SecondaryButtonEdgeColor, toy.Edge.color);
            Assert.AreEqual(s.TextColor, label.color);
        }

        [Test]
        public void Button_PressMovesFaceOntoEdge()
        {
            UiStyle s = Style();
            _root = new GameObject("Root", typeof(RectTransform));
            Button button = UiFactory.CreateButton("B", _root.transform, s, s.MenuButtonSize, s.PrimaryButtonColor, s.MediumFontSize,
                "x", null, out _);
            ToyButton toy = button.GetComponent<ToyButton>();
            float y = toy.Face.rectTransform.offsetMin.y;
            toy.SetPressed(true);
            Assert.Less(toy.Face.rectTransform.offsetMin.y, y);
            toy.SetPressed(false);
            Assert.AreEqual(y, toy.Face.rectTransform.offsetMin.y, 1e-4f);
            button.interactable = false;
            toy.SetPressed(true);
            Assert.IsFalse(toy.IsPressed, "a disabled button does not sink");
        }

        private static void AssertHex(string hex, Color c)
        {
            Assert.AreEqual(hex, ColorUtility.ToHtmlStringRGB(c), "colour " + hex);
        }
    }
}
