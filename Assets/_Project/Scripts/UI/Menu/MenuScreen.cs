using Arena.Gameplay.Flow;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Arena.UI.Menu
{
    /// <summary>
    /// Minimal main menu built at runtime: version label and a Play button that loads the Match scene.
    /// Layout values are temporary serialized fields until UiStyle appears (D-018, stage 2).
    /// </summary>
    public sealed class MenuScreen : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField] private Vector2 _buttonSize = new Vector2(480f, 160f);
        [SerializeField] private int _buttonFontSize = 64;
        [SerializeField] private int _versionFontSize = 32;
        [SerializeField] private Vector2 _versionMargin = new Vector2(24f, 24f);
        [SerializeField] private Color _buttonColor = new Color(0.25f, 0.55f, 0.95f, 1f);
        [SerializeField] private Color _textColor = Color.white;

        private Font _font;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            var canvas = CreateCanvas();
            CreateVersionLabel(canvas.transform);
            CreatePlayButton(canvas.transform);
        }

        private Canvas CreateCanvas()
        {
            var go = new GameObject("MenuCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private void CreateVersionLabel(Transform parent)
        {
            var text = CreateText("Version", parent, _versionFontSize, TextAnchor.LowerLeft);
            // TODO(S2.7): Loc
            text.text = "v" + Application.version;
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = _versionMargin;
            rt.sizeDelta = _buttonSize;
        }

        private void CreatePlayButton(Transform parent)
        {
            var go = new GameObject("PlayButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = _buttonSize;
            var image = go.AddComponent<Image>();
            image.color = _buttonColor;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(OnPlay);

            var label = CreateText("Label", go.transform, _buttonFontSize, TextAnchor.MiddleCenter);
            // TODO(S2.7): Loc
            label.text = "Play";
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
        }

        private Text CreateText(string name, Transform parent, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = _textColor;
            text.raycastTarget = false;
            return text;
        }

        private void OnPlay()
        {
            SceneManager.LoadScene(SceneNames.Match);
        }
    }
}
