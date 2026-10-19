using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Arena.UI.Common
{
    /// <summary>
    /// Look of a "toy" button built by <see cref="UiFactory.CreateButton"/> (A.6, Art-Spec §4.2): a dark bottom edge, a tinted
    /// face with a gloss and the label. Pressing moves the face (with the label) down onto the edge; colours change through
    /// <see cref="UiFactory.SetButtonColor"/>. View only — the click is the <see cref="Button"/> on the same object.
    /// </summary>
    public sealed class ToyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private Image _edge;
        [SerializeField] private Image _face;
        [SerializeField] private Text _label;
        [SerializeField] private float _pressOffset;
        [SerializeField] private Graphic _gloss;
        [SerializeField] private float _disabledLabelAlpha;

        private RectTransform _faceRect;
        private Vector2 _faceMin;
        private Vector2 _faceMax;
        private bool _pressed;
        private Button _button;
        private bool _shownInteractable = true;

        public Image Edge => _edge;
        public Image Face => _face;
        public Text Label => _label;
        public bool IsPressed => _pressed;

        /// <param name="gloss">Hidden while the button is not interactable (the face is greyed by the button tint).</param>
        public void Init(Image edge, Image face, Text label, float pressOffset, Graphic gloss = null, float disabledLabelAlpha = 1f)
        {
            _gloss = gloss;
            _disabledLabelAlpha = disabledLabelAlpha;
            _edge = edge;
            _face = face;
            _label = label;
            _pressOffset = pressOffset;
            _faceRect = face.rectTransform;
            _faceMin = _faceRect.offsetMin;
            _faceMax = _faceRect.offsetMax;
        }

        public void OnPointerDown(PointerEventData eventData) => SetPressed(true);

        public void OnPointerUp(PointerEventData eventData) => SetPressed(false);

        public void OnPointerExit(PointerEventData eventData) => SetPressed(false);

        private void OnDisable() => SetPressed(false);

        // Menu screens only (no match HUD buttons): one bool check per frame, no allocations.
        private void LateUpdate()
        {
            if (_button == null) _button = GetComponent<Button>();
            bool interactable = _button == null || _button.IsInteractable();
            if (interactable == _shownInteractable) return;
            _shownInteractable = interactable;
            if (_gloss != null) _gloss.enabled = interactable;
            if (_label != null) _label.canvasRenderer.SetAlpha(interactable ? 1f : _disabledLabelAlpha);
        }

        public void SetPressed(bool pressed)
        {
            if (_faceRect == null) return;
            Button button = GetComponent<Button>();
            if (pressed && button != null && !button.IsInteractable()) pressed = false;
            if (pressed == _pressed) return;
            _pressed = pressed;
            var shift = new Vector2(0f, pressed ? -_pressOffset : 0f);
            _faceRect.offsetMin = _faceMin + shift;
            _faceRect.offsetMax = _faceMax + shift;
        }
    }
}
