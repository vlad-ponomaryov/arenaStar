using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.Cameras
{
    /// <summary>
    /// Thin camera driver (D-016): fixed pitch from <see cref="CameraConfig"/>, follows the local fighter along the long axis,
    /// clamped by the map edges, rotated 180° for team 1; on a free-for-all map (Last Beast) along both axes, not rotated
    /// (<see cref="CameraMath.FocusFree"/>, S3.14). Math lives in <see cref="CameraMath"/>.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MatchCameraController : MonoBehaviour
    {
        private CameraConfig _config;
        private Vector2 _mapSize;
        private bool _rotated;
        private bool _free;
        private Quaternion _rotation;
        private Vector3 _focus;
        private Camera _camera;
        private Vector3 _shakeOffset;

        public Camera Camera => _camera != null ? _camera : (_camera = GetComponent<Camera>());
        public bool Rotated => _rotated;

        /// <summary>Follows along both axes (free-for-all map).</summary>
        public bool FollowsBothAxes => _free;

        /// <summary>Current focus point on the ground (tests).</summary>
        public Vector3 Focus => _focus;

        /// <param name="mapSize">Map width × height in cells (world X × Z).</param>
        /// <param name="followBothAxes">Free-for-all map: follow along X and Z (<paramref name="rotated"/> is ignored).</param>
        public void Init(CameraConfig config, Vector2 mapSize, bool rotated, Vector3 target, bool followBothAxes = false)
        {
            _config = config;
            _mapSize = mapSize;
            _free = followBothAxes;
            _rotated = rotated && !followBothAxes;
            _rotation = CameraMath.Rotation(config.PitchDegrees, _rotated);
            Camera.fieldOfView = config.FieldOfView;
            _focus = Desired(target);
            Apply();
        }

        public void Follow(Vector3 target, float deltaTime)
        {
            if (_config == null) return;
            Vector3 desired = Desired(target);
            _focus = Vector3.Lerp(_focus, desired, CameraMath.DampFactor(_config.FollowSharpness, deltaTime));
            Apply();
        }

        /// <summary>Extra world offset of the camera (shake, <c>Feel.CameraShake</c>); applied on the next <see cref="Follow"/>.</summary>
        public void SetShakeOffset(Vector3 offset) => _shakeOffset = offset;

        private Vector3 Desired(Vector3 target) => _free
            ? CameraMath.FocusFree(target, _mapSize, _config.FreeForwardOffset, _config.EdgeInset, _config.FreeSideInset)
            : CameraMath.Focus(target, _mapSize, _rotated, _config.ForwardOffset, _config.EdgeInset);

        private void Apply() =>
            transform.SetPositionAndRotation(CameraMath.Position(_focus, _rotation, _config.Distance) + _shakeOffset, _rotation);
    }
}
