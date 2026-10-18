using Arena.Core.Defs;
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
        private ArenaBounds _bounds;
        private bool _rotated;
        private Quaternion _rotation;
        private Vector3 _focus;
        private Camera _camera;

        public Camera Camera => _camera != null ? _camera : (_camera = GetComponent<Camera>());
        public bool Rotated => _rotated;

        public void Init(CameraConfig config, ArenaBounds bounds, bool rotated, Vector3 target)
        {
            _config = config;
            _bounds = bounds;
            _rotated = rotated;
            _rotation = CameraMath.Rotation(config.PitchDegrees, rotated);
            Camera.fieldOfView = config.FieldOfView;
            _focus = CameraMath.Focus(target, bounds, rotated, config.ForwardOffset, config.EdgeInset);
            Apply();
        }

        public void Follow(Vector3 target, float deltaTime)
        {
            if (_config == null) return;
            Vector3 desired = CameraMath.Focus(target, _bounds, _rotated, _config.ForwardOffset, _config.EdgeInset);
            _focus = Vector3.Lerp(_focus, desired, CameraMath.DampFactor(_config.FollowSharpness, deltaTime));
            Apply();
        }

        private void Apply() =>
            transform.SetPositionAndRotation(CameraMath.Position(_focus, _rotation, _config.Distance), _rotation);
    }
}
