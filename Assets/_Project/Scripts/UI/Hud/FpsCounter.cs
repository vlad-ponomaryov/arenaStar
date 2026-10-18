using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Frames-per-second counter in the top-left corner of the match HUD (S2.10, GDD §8.4 target 30+ FPS): the average over
    /// the last <c>updateSeconds</c> of unscaled time, shown as a cached number string next to a localized label.
    /// </summary>
    public sealed class FpsCounter
    {
        private readonly Text _value;
        private readonly float _updateSeconds;
        private float _elapsed;
        private int _frames;

        public FpsCounter(Transform canvas, UiStyle style, float updateSeconds)
        {
            _updateSeconds = updateSeconds;
            RectTransform box = UiFactory.CreateRect("FpsCounter", canvas);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f);
            box.sizeDelta = style.FpsBoxSize;
            box.anchoredPosition = new Vector2(style.FpsMargin.x, -style.FpsMargin.y);

            Text label = UiFactory.CreateText("Label", box, style, style.SmallFontSize, TextAnchor.MiddleLeft, Loc.Get(LocKeys.HudFps));
            UiFactory.Stretch(label.rectTransform);
            _value = UiFactory.CreateText("Value", box, style, style.SmallFontSize, TextAnchor.MiddleRight, string.Empty);
            UiFactory.Stretch(_value.rectTransform);
        }

        /// <summary>Last shown value (tests).</summary>
        public int Shown { get; private set; } = -1;

        public Text Value => _value;

        /// <param name="unscaledDeltaTime">Frame time, seconds.</param>
        public void Tick(float unscaledDeltaTime)
        {
            _elapsed += unscaledDeltaTime;
            _frames++;
            if (_elapsed < _updateSeconds) return;
            int fps = Mathf.RoundToInt(_frames / _elapsed);
            _elapsed = 0f;
            _frames = 0;
            if (fps == Shown) return;
            Shown = fps;
            _value.text = NumberStringCache.Get(fps);
        }
    }
}
