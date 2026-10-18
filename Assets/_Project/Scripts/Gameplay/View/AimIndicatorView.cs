using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Aim line on the ground: the prefab's line is 1 unit long along local +Z starting at the pivot, so the root's Z scale
    /// is the length in cells (the attack range).
    /// </summary>
    public sealed class AimIndicatorView : MonoBehaviour
    {
        [SerializeField] private Renderer _line;

        public Renderer Line => _line;

        /// <param name="direction">World-space direction on the ground (non-zero).</param>
        public void Show(Vector3 origin, Vector3 direction, float length)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
            Vector3 scale = transform.localScale;
            scale.z = length;
            transform.localScale = scale;
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
