using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Thin view of one fighter (capsule + team ring, D-014): placed by the runner with interpolated positions (D-015).
    /// The ring colour is ally/enemy relative to the local player's team; materials are shared, not instanced.
    /// </summary>
    public sealed class FighterView : MonoBehaviour
    {
        [SerializeField] private Transform _body;
        [SerializeField] private Renderer _ring;

        private Renderer _bodyRenderer;
        private Material _bodyMaterial;

        public Transform Body => _body;
        public Renderer Ring => _ring;

        /// <summary>Swaps the shared body material (normal / hidden in a bush); no-op if unchanged.</summary>
        public void SetBodyMaterial(Material material)
        {
            if (material == null || material == _bodyMaterial) return;
            if (_bodyRenderer == null && _body != null) _bodyRenderer = _body.GetComponent<Renderer>();
            if (_bodyRenderer == null) return;
            _bodyRenderer.sharedMaterial = material;
            _bodyMaterial = material;
        }

        public void SetTeamMaterial(Material material)
        {
            if (_ring != null) _ring.sharedMaterial = material;
        }

        /// <param name="facing">World-space look direction on the ground; ignored if zero.</param>
        public void SetPose(Vector3 position, Vector3 facing)
        {
            transform.position = position;
            if (facing.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
