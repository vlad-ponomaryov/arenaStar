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

        public Transform Body => _body;
        public Renderer Ring => _ring;

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
