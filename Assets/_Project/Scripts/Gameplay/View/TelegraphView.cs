using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Ground marker of a pending area attack (GDD §5.3: the zone shows 0.5 s before it triggers): a translucent disc of the
    /// zone and an inner disc that grows to the edge as the trigger approaches. Both discs have diameter 1 in the prefab;
    /// the root's X/Z scale is the zone diameter. Built by ViewAssetBuilder; materials are shared (ally / enemy, D-014).
    /// </summary>
    public sealed class TelegraphView : MonoBehaviour
    {
        [SerializeField] private Renderer _zone;
        [SerializeField] private Transform _fill;
        [SerializeField] private Renderer _fillRenderer;

        public Renderer Zone => _zone;
        public Transform Fill => _fill;

        public void SetMaterials(Material zone, Material fill)
        {
            if (_zone != null && zone != null && _zone.sharedMaterial != zone) _zone.sharedMaterial = zone;
            if (_fillRenderer != null && fill != null && _fillRenderer.sharedMaterial != fill) _fillRenderer.sharedMaterial = fill;
        }

        /// <param name="progress">0 = just shown, 1 = triggers now.</param>
        public void Show(Vector3 centre, float radius, float progress)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            float d = radius * 2f;
            transform.position = centre;
            transform.localScale = new Vector3(d, 1f, d);
            if (_fill != null)
            {
                float p = Mathf.Clamp01(progress);
                Vector3 s = _fill.localScale;
                s.x = p;
                s.z = p;
                _fill.localScale = s;
            }
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
