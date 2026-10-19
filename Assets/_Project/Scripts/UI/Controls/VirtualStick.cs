using UnityEngine;
using UnityEngine.EventSystems;

namespace Arena.UI.Controls
{
    /// <summary>
    /// Virtual touch stick on uGUI pointer events (D-017; works with InputSystemUIInputModule for touch and mouse).
    /// This component sits on the touch zone (a transparent raycast Image); the base and knob are its children.
    /// Floating: the base jumps to the touch point and returns home on release. Fixed: the base stays put. Anchored: the base
    /// stays put, the offset is measured from the touch point (attack / ult: a tap anywhere on the zone is a tap, D-129).
    /// Gesture classification is in <see cref="StickGesture"/>; the HUD polls <see cref="Value"/>, <see cref="Aim"/> and
    /// <see cref="TryConsumeRelease"/> every frame (no events, no allocations).
    /// </summary>
    public sealed class VirtualStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const int NoPointer = int.MinValue;

        [SerializeField] private RectTransform _base;
        [SerializeField] private RectTransform _knob;
        [SerializeField] private StickMode _mode;
        [SerializeField] private float _radius = 1f;
        [SerializeField] private StickGestureSettings _settings;

        private RectTransform _zone;
        private Vector2 _home;
        private Vector2 _origin;
        private int _pointerId = NoPointer;
        private StickGesture _gesture;
        private bool _hasRelease;
        private StickGestureResult _releaseResult;
        private Vector2 _releaseAim;

        public StickMode Mode => _mode;
        public float Radius => _radius;
        public StickGestureSettings Settings => _settings;
        public bool IsHeld => _gesture.IsActive;

        /// <summary>Normalized offset (length ≤ 1) with the dead zone applied; zero when not held. For movement.</summary>
        public Vector2 Value
        {
            get
            {
                if (!_gesture.IsActive) return Vector2.zero;
                Vector2 v = _gesture.Current;
                return v.magnitude < _settings.DeadZone ? Vector2.zero : v;
            }
        }

        /// <summary>Manual aim while dragging (zero before the drag and inside the cancel zone). For the aim line.</summary>
        public Vector2 Aim => _gesture.CurrentAim(_settings);

        /// <summary>Held without a drag: releasing now fires the auto-aim (the HUD previews its target).</summary>
        public bool HeldStill => _gesture.HeldStill(_settings);

        public void Configure(RectTransform stickBase, RectTransform knob, StickMode mode, float radius, StickGestureSettings settings)
        {
            _base = stickBase;
            _knob = knob;
            _mode = mode;
            _radius = radius;
            _settings = settings;
            CacheHome();
        }

        /// <summary>Returns the last release (once) since the previous call.</summary>
        public bool TryConsumeRelease(out StickGestureResult result, out Vector2 aim)
        {
            result = _releaseResult;
            aim = _releaseAim;
            if (!_hasRelease) return false;
            _hasRelease = false;
            return true;
        }

        private void Awake() => CacheHome();

        private void OnDisable()
        {
            _gesture.Abort();
            _pointerId = NoPointer;
            ResetVisuals();
        }

        private void CacheHome()
        {
            _zone = (RectTransform)transform;
            if (_base != null) _home = _base.localPosition;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId != NoPointer || _base == null) return;
            _pointerId = eventData.pointerId;
            bool inside = ToLocal(eventData, out Vector2 local);
            if (_mode == StickMode.Floating && inside) _base.localPosition = local;
            _origin = _mode == StickMode.Anchored && inside ? local : (Vector2)_base.localPosition;
            _gesture.Begin();
            UpdateOffset(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            UpdateOffset(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            UpdateOffset(eventData);
            _releaseResult = _gesture.End(_settings, out _releaseAim);
            _hasRelease = true;
            _pointerId = NoPointer;
            ResetVisuals();
        }

        private void UpdateOffset(PointerEventData eventData)
        {
            if (!ToLocal(eventData, out Vector2 local)) return;
            Vector2 offset = (local - _origin) / (_radius > 0f ? _radius : 1f);
            _gesture.Move(offset);
            if (_knob != null) _knob.localPosition = _gesture.Current * _radius;
        }

        private void ResetVisuals()
        {
            if (_knob != null) _knob.localPosition = Vector3.zero;
            if (_base != null && _mode == StickMode.Floating) _base.localPosition = _home;
        }

        private bool ToLocal(PointerEventData eventData, out Vector2 local)
        {
            if (_zone == null) _zone = (RectTransform)transform;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_zone, eventData.position, eventData.pressEventCamera, out local);
        }
    }
}
