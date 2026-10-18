using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Health and charge bar above a fighter (GDD §5.3): plain quads, no text. The fill is a child of a pivot at the bar's
    /// left edge, so scaling the pivot's X shows the health fraction; charge segments are shown left to right.
    /// Faces the camera (billboard). Geometry and materials come from the prefab (ViewAssetBuilder).
    /// </summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [SerializeField] private Transform _fillPivot;
        [SerializeField] private Renderer _fill;
        [SerializeField] private Renderer[] _chargeSegments;

        [Tooltip("Height of the bar above the fighter's feet, world units.")]
        [SerializeField] private float _height;

        private float _shownFraction = -1f;
        private int _shownCharges = -1;
        private int _shownSegments = -1;

        public Transform FillPivot => _fillPivot;
        public Renderer Fill => _fill;
        public int SegmentCount => _chargeSegments == null ? 0 : _chargeSegments.Length;

        public void SetFillMaterial(Material material)
        {
            if (_fill != null) _fill.sharedMaterial = material;
        }

        /// <param name="feet">Fighter position on the ground.</param>
        /// <param name="cameraRotation">Rotation of the camera (the bar faces it).</param>
        public void SetPose(Vector3 feet, Quaternion cameraRotation)
        {
            transform.SetPositionAndRotation(feet + Vector3.up * _height, cameraRotation);
        }

        /// <param name="healthFraction">0..1.</param>
        /// <param name="charges">Charges available now.</param>
        /// <param name="maxCharges">Segments to show (at most the prefab's segment count).</param>
        public void SetValues(float healthFraction, int charges, int maxCharges)
        {
            if (healthFraction < 0f) healthFraction = 0f;
            else if (healthFraction > 1f) healthFraction = 1f;
            if (healthFraction != _shownFraction && _fillPivot != null)
            {
                _shownFraction = healthFraction;
                Vector3 s = _fillPivot.localScale;
                s.x = healthFraction;
                _fillPivot.localScale = s;
            }

            if (_chargeSegments == null || (charges == _shownCharges && maxCharges == _shownSegments)) return;
            _shownCharges = charges;
            _shownSegments = maxCharges;
            for (int i = 0; i < _chargeSegments.Length; i++)
            {
                bool on = i < maxCharges && i < charges;
                GameObject go = _chargeSegments[i].gameObject;
                if (go.activeSelf != on) go.SetActive(on);
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
