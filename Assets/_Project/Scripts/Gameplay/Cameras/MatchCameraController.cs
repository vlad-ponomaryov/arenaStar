using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.Cameras
{
    /// <summary>
    /// Thin camera driver (D-016): fixed pitch from <see cref="CameraConfig"/>, follows the local fighter along the long axis,
    /// clamped by the map edges, rotated 180° for team 1. Math lives in <see cref="CameraMath"/>.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MatchCameraController : MonoBehaviour
    {
        private CameraConfig _config;
        private Vector2 _mapSize;
        private bool _rotated;
        private Quaternion _rotation;
        private Vector3 _focus;
        private Camera _camera;
        private Vector3 _shakeOffset;

        public Camera Camera => _camera != null ? _camera : (_camera = GetComponent<Camera>());
        public bool Rotated => _rotated;

        /// <param name="mapSize">Map width × height in cells (world X × Z).</param>
        public void Init(CameraConfig config, Vector2 mapSize, bool rotated, Vector3 target)
        {
            _config = config;
            _mapSize = mapSize;
            _rotated = rotated;
            _rotation = CameraMath.Rotation(config.PitchDegrees, rotated);
            Camera.fieldOfView = config.FieldOfView;
            _focus = CameraMath.Focus(target, mapSize, rotated, config.ForwardOffset, config.EdgeInset);
            Apply();
        }

        public void Follow(Vector3 target, float deltaTime)
        {
            if (_config == null) return;
            Vector3 desired = CameraMath.Focus(target, _mapSize, _rotated, _config.ForwardOffset, _config.EdgeInset);
            _focus = Vector3.Lerp(_focus, desired, CameraMath.DampFactor(_config.FollowSharpness, deltaTime));
            Apply();
        }

        /// <summary>Extra world offset of the camera (shake, <c>Feel.CameraShake</c>); applied on the next <see cref="Follow"/>.</summary>
        public void SetShakeOffset(Vector3 offset) => _shakeOffset = offset;

        private void Apply() =>
            transform.SetPositionAndRotation(CameraMath.Position(_focus, _rotation, _config.Distance) + _shakeOffset, _rotation);
    }
}
