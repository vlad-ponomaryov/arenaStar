using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Health and charge bar above a fighter (GDD §5.3, Art-Spec §4.1, A.7a): a rounded "pill" frame quad, the fill inside it
    /// (a child of a pivot at the bar's left edge, so scaling the pivot's X shows the health fraction, with a gloss strip),
    /// and round charge beads under it — a dark socket per charge of the rules and a bright bead for every charge available;
    /// the own fighter's beads are bigger. The height follows the model (<see cref="SetTopHeight"/>, <c>FighterRig.TopHeight</c>).
    /// Faces the camera (billboard). Geometry and materials come from the prefab (ViewAssetBuilder).
    /// </summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [SerializeField] private Transform _fillPivot;
        [SerializeField] private Renderer _fill;
        [SerializeField] private Renderer[] _chargeSegments;
        [SerializeField] private Renderer[] _chargeSockets;

        [Tooltip("Height of the bar above the fighter's feet, world units (fallback when the model has no TopHeight).")]
        [SerializeField] private float _height;

        [Tooltip("Gap from the top of the model (FighterRig.TopHeight) to the bar centre, world units.")]
        [SerializeField] private float _aboveTop;

        [Tooltip("Diameter of a charge bead quad, world units.")]
        [SerializeField] private float _beadSize;

        [Tooltip("Scale of the charge beads (and sockets) of the local player's own fighter.")]
        [SerializeField] private float _ownBeadScale = 1f;

        private float _shownFraction = -1f;
        private int _shownCharges = -1;
        private int _shownSegments = -1;

        public Transform FillPivot => _fillPivot;
        public Renderer Fill => _fill;
        public int SegmentCount => _chargeSegments == null ? 0 : _chargeSegments.Length;
        public float Height => _height;

        public void SetFillMaterial(Material material)
        {
            if (_fill != null) _fill.sharedMaterial = material;
        }

        /// <summary>Puts the bar <c>_aboveTop</c> over the model's top; a non-positive height keeps the prefab default.</summary>
        public void SetTopHeight(float topHeight)
        {
            if (topHeight > 0f) _height = topHeight + _aboveTop;
        }

        /// <summary>The local player's own fighter: bigger charge beads.</summary>
        public void SetOwn(bool own)
        {
            float k = own ? _ownBeadScale : 1f;
            ScaleAll(_chargeSegments, k);
            ScaleAll(_chargeSockets, k);
        }

        /// <param name="feet">Fighter position on the ground.</param>
        /// <param name="cameraRotation">Rotation of the camera (the bar faces it).</param>
        public void SetPose(Vector3 feet, Quaternion cameraRotation)
        {
            transform.SetPositionAndRotation(feet + Vector3.up * _height, cameraRotation);
        }

        /// <param name="healthFraction">0..1.</param>
        /// <param name="charges">Charges available now.</param>
        /// <param name="maxCharges">Beads to show (at most the prefab's bead count).</param>
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
                SetActive(_chargeSegments[i], i < maxCharges && i < charges);
            if (_chargeSockets == null) return;
            for (int i = 0; i < _chargeSockets.Length; i++)
                SetActive(_chargeSockets[i], i < maxCharges);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        private static void SetActive(Renderer r, bool on)
        {
            if (r == null) return;
            GameObject go = r.gameObject;
            if (go.activeSelf != on) go.SetActive(on);
        }

        private void ScaleAll(Renderer[] renderers, float k)
        {
            if (renderers == null) return;
            float size = _beadSize * k;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].transform.localScale = new Vector3(size, size, 1f);
        }
    }
}
