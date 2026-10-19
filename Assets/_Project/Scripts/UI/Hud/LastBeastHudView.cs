using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Core.State;
using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Last Beast panel at the top of the match HUD (S3.14, Server-Spec §9.2, §12.4) in place of the Acorns score bar: fighters
    /// left ("Left" + N, <c>ModeState.AliveCount</c>), the time to the next fog step ("Fog in" + clock; hidden once the safe square
    /// is at its smallest or the match ended) and the own berry power ("Berry power" + "+N%", D-131: the bonus, not the count —
    /// <see cref="LastBeastRules.DamageBonusPercent"/>). Labels are set once per language, numbers come from
    /// <see cref="NumberStringCache"/> and change only with the value — no allocations per frame. Hidden in other modes.
    /// </summary>
    public sealed class LastBeastHudView
    {
        private readonly GameObject _root;
        private readonly Text _alive;
        private readonly Text _fog;
        private readonly GameObject _fogBox;
        private readonly Text _berries;
        private int _shownAlive = -1;
        private int _shownFog = -1;
        private int _shownBerries = -1;
        private readonly PercentTextCache _percents = new PercentTextCache("+{0}%");

        public GameObject Root => _root;
        public Text AliveText => _alive;
        public Text FogText => _fog;
        public bool FogTimerShown => _fogBox.activeSelf;
        public Text BerriesText => _berries;

        private LastBeastHudView(GameObject root, Text alive, GameObject fogBox, Text fog, Text berries)
        {
            _root = root;
            _alive = alive;
            _fogBox = fogBox;
            _fog = fog;
            _berries = berries;
        }

        public static LastBeastHudView Build(Transform canvasRoot, UiStyle style)
        {
            RectTransform root = UiFactory.CreateRect("LastBeastBar", canvasRoot);
            root.anchorMin = new Vector2(0.5f, 1f);
            root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -style.ScoreBarTopMargin);
            root.sizeDelta = new Vector2(style.TimerBoxSize.x * 3f + style.ScoreBarSpacing * 2f, style.TimerBoxSize.y);

            RectTransform row = UiFactory.CreateRow("Row", root, style.ScoreBarSpacing, style.TimerBoxSize.y);
            UiFactory.Stretch(row);
            Text alive = CreateBox("Alive", row, style, LocKeys.HudAliveLeft, out _);
            Text fog = CreateBox("Fog", row, style, LocKeys.HudFogIn, out GameObject fogBox);
            Text berries = CreateBox("Berries", row, style, LocKeys.HudBerryPower, out _);
            root.gameObject.SetActive(false);
            return new LastBeastHudView(root.gameObject, alive, fogBox, fog, berries);
        }

        /// <summary>Updates the texts from the state (call every frame).</summary>
        public void Apply(MatchDefs defs, MatchState state, int localSlot)
        {
            if (defs == null || state == null) return;
            bool on = defs.Mode == ModeKind.LastBeast && defs.LastBeast != null;
            if (_root.activeSelf != on) _root.SetActive(on);
            if (!on) return;

            ref ModeState m = ref state.Mode;
            int alive = m.AliveCount > 0 ? m.AliveCount : LastBeastRules.CountAlive(state);
            SetNumber(_alive, ref _shownAlive, alive);

            int fogTicks = FogTicksLeft(defs, state);
            bool timer = fogTicks > 0;
            if (_fogBox.activeSelf != timer) _fogBox.SetActive(timer);
            if (timer)
            {
                int seconds = ScoreBarView.CeilSeconds(fogTicks);
                if (seconds != _shownFog)
                {
                    _shownFog = seconds;
                    _fog.text = NumberStringCache.Clock(seconds);
                }
            }

            int berries = (uint)localSlot < (uint)state.Fighters.Length ? state.Fighters[localSlot].Berries : 0;
            int percent = LastBeastRules.DamageBonusPercent(defs.LastBeast, berries);
            if (percent != _shownBerries)
            {
                _shownBerries = percent;
                _berries.text = _percents.Get(percent);
            }
        }

        /// <summary>Ticks to the next fog step; 0 when the match ended or the safe square cannot shrink any more.</summary>
        public static int FogTicksLeft(MatchDefs defs, MatchState state)
        {
            ref ModeState m = ref state.Mode;
            if (m.IsEnded || defs.LastBeast == null) return 0;
            if (m.FogStep > 0)
            {
                float minSide = defs.LastBeast.FogMinHalfSize * 2f;
                if (m.SafeMaxX - m.SafeMinX <= minSide && m.SafeMaxY - m.SafeMinY <= minSide) return 0;
            }
            int left = m.NextFogStepTick - state.Globals.Tick;
            return left > 0 ? left : 0;
        }

        private static void SetNumber(Text text, ref int shown, int value)
        {
            if (value == shown) return;
            shown = value;
            text.text = NumberStringCache.Get(value);
        }

        /// <summary>A wooden toy plate (A.7b, like the timer of the score bar): the label in its upper part, the value below.</summary>
        private static Text CreateBox(string name, Transform parent, UiStyle style, string labelKey, out GameObject box)
        {
            RectTransform face = UiFactory.CreateTablet(name, parent, style, style.TimerBoxSize, style.PanelBorderColor, style.PlankSprite);
            box = face.parent.gameObject;
            Text label = UiFactory.CreateText("Label", face, style, style.SmallFontSize, TextAnchor.UpperCenter, Loc.Get(labelKey));
            UiFactory.Stretch(label.rectTransform);
            Text value = UiFactory.CreateText("Value", face, style, style.MediumFontSize, TextAnchor.LowerCenter);
            UiFactory.Stretch(value.rectTransform);
            return value;
        }
    }
}
