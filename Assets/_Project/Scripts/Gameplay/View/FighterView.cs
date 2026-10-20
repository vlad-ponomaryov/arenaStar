using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Thin view of one fighter (model or fallback capsule + team ring + blob shadow, D-014, D-027): placed by the runner with interpolated positions (D-015).
    /// The ring colour is ally/enemy relative to the local player's team; materials are shared, not instanced.
    /// </summary>
    public sealed class FighterView : MonoBehaviour
    {
        [SerializeField] private Transform _body;
        [SerializeField] private Renderer _ring;
        [SerializeField] private Renderer _shadow;
        [SerializeField] private FighterRig _rig;

        private Renderer _bodyRenderer;
        private Material _bodyMaterial;

        public Transform Body => _body;
        public Renderer Ring => _ring;

        /// <summary>Bones of a procedural model (A.1b); null on the fallback capsule.</summary>
        public FighterRig Rig => _rig;

        /// <summary>Blob shadow quad under the fighter (D-027); hidden together with the view.</summary>
        public Renderer Shadow => _shadow;

        /// <summary>Renderer of the body (skinned mesh of a model, mesh of the capsule), cached on first use; null if none.</summary>
        public Renderer BodyRenderer
        {
            get
            {
                if (_bodyRenderer == null && _body != null) _bodyRenderer = _body.GetComponent<Renderer>();
                return _bodyRenderer;
            }
        }

        /// <summary>Swaps the shared body material (normal / hidden in a bush); no-op if unchanged.</summary>
        public void SetBodyMaterial(Material material)
        {
            if (material == null || material == _bodyMaterial) return;
            if (BodyRenderer == null) return;
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
