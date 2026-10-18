using UnityEngine;
using UnityEngine.EventSystems;

namespace Arena.UI.Controls
{
    /// <summary>One-tap button on uGUI pointer events (trick, GDD §5.1): the press is registered on touch down and polled once.</summary>
    public sealed class TapButton : MonoBehaviour, IPointerDownHandler
    {
        private bool _pressed;

        public void OnPointerDown(PointerEventData eventData) => _pressed = true;

        /// <summary>True once per press since the previous call.</summary>
        public bool ConsumePress()
        {
            if (!_pressed) return false;
            _pressed = false;
            return true;
        }

        private void OnDisable() => _pressed = false;
    }
}
