using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Texture of a fighter model built from a textured source (A-Theo3D v3): sets <c>_BaseMap</c> and <c>_BaseMapOn = 1</c> on the body
    /// renderer through a property block once on enable. The block survives <see cref="FighterView.SetBodyMaterial"/> swapping the
    /// shared materials (normal / flash / hidden), so the materials stay shared by all fighters. Runs in edit mode too (art captures).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public sealed class FighterSkin : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapOnId = Shader.PropertyToID("_BaseMapOn");
        private static MaterialPropertyBlock _block;

        /// <summary>RGB = sRGB colour, A = gloss.</summary>
        public Texture2D BaseMap;

        private void OnEnable() => Apply();

        public void Apply()
        {
            if (BaseMap == null) return;
            var target = GetComponent<Renderer>();
            if (_block == null) _block = new MaterialPropertyBlock();
            target.GetPropertyBlock(_block);
            _block.SetTexture(BaseMapId, BaseMap);
            _block.SetFloat(BaseMapOnId, 1f);
            target.SetPropertyBlock(_block);
        }
    }
}
