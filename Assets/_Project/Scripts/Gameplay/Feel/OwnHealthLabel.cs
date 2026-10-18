using Arena.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Health number above the local fighter (GDD §5.3: "health numbers above your own"), on the <see cref="FloatingTextLayer"/>.
    /// The text changes only when the value changes (cached strings, no allocations per frame).
    /// </summary>
    public sealed class OwnHealthLabel
    {
        private readonly Text _text;
        private readonly float _height;
        private int _shown = -1;

        public OwnHealthLabel(FloatingTextLayer layer, FeelConfig feel)
        {
            _text = layer.CreateText("OwnHealth", feel.OwnHealthFontSize, feel.OwnHealthColor);
            _height = feel.OwnHealthHeight;
        }

        public Text Text => _text;

        /// <param name="feet">The local fighter's interpolated position on the ground.</param>
        public void Update(Camera camera, Vector3 feet, int health, bool visible)
        {
            bool placed = visible && FloatingTextLayer.Place(_text, camera, feet + Vector3.up * _height);
            if (_text.gameObject.activeSelf != placed) _text.gameObject.SetActive(placed);
            if (!placed || health == _shown) return;
            _shown = health;
            _text.text = NumberStringCache.Get(health);
        }
    }
}
