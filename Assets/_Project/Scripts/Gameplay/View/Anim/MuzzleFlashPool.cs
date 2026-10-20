using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>
    /// Short muzzle flashes at the barrel of a shooting fighter (brief A-TheoAnim §3): a fixed pool of star meshes with an
    /// additive unlit material (<see cref="ViewCatalog.MuzzleFlashPrefab"/>), shown full size and shrinking to 0 over
    /// MuzzleSeconds. Created at match start; no allocations afterwards (the oldest flash is reused when all are busy).
    /// Replaced by particles in A.5b.
    /// </summary>
    public sealed class MuzzleFlashPool
    {
        /// <summary>Each new flash turns by this much around the barrel, so two flashes in a row do not look the same.</summary>
        private const float RollStepDegrees = 37f;

        private readonly FighterAnimConfig _config;
        private readonly Transform[] _items;
        private readonly float[] _age;
        private readonly float[] _size;
        private int _next;
        private float _roll;

        public MuzzleFlashPool(GameObject prefab, Transform parent, FighterAnimConfig config)
        {
            _config = config;
            int count = Mathf.Max(1, config.MuzzlePoolSize);
            _items = new Transform[count];
            _age = new float[count];
            _size = new float[count];
            for (int i = 0; i < count; i++)
            {
                GameObject go = Object.Instantiate(prefab, parent);
                go.name = "MuzzleFlash" + i;
                go.SetActive(false);
                _items[i] = go.transform;
                _age[i] = float.MaxValue;
            }
        }

        public int Capacity => _items.Length;

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _items.Length; i++)
                    if (_items[i].gameObject.activeSelf) n++;
                return n;
            }
        }

        /// <summary>
        /// World point and rotation (+Z along the barrel) of the muzzle of a rig: the end of its prop bone, MuzzleOffset along the
        /// bone's +Z. False if the rig has no prop.
        /// </summary>
        public static bool MuzzlePoint(FighterRig rig, float offset, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            Transform[] bones = rig.Bones;
            int prop = (int)FighterBone.Prop;
            Transform t = bones != null && prop < bones.Length ? bones[prop] : null;
            if (t == null || t.lossyScale.sqrMagnitude <= 0f) return false;
            rotation = t.rotation;
            position = t.position + rotation * new Vector3(0f, 0f, offset);
            return true;
        }

        public void Spawn(Vector3 position, Quaternion rotation, float scale)
        {
            int i = _next;
            _next = (_next + 1) % _items.Length;
            _roll = Mathf.Repeat(_roll + RollStepDegrees, 360f);
            Transform t = _items[i];
            t.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, 0f, _roll));
            _age[i] = 0f;
            _size[i] = _config.MuzzleScale * scale;
            t.localScale = Vector3.one * _size[i];
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        }

        /// <summary>Ages the flashes by <paramref name="dt"/> (frame seconds): each shrinks linearly to 0 and hides.</summary>
        public void Update(float dt)
        {
            float life = _config.MuzzleSeconds;
            for (int i = 0; i < _items.Length; i++)
            {
                Transform t = _items[i];
                if (!t.gameObject.activeSelf) continue;
                _age[i] += dt;
                float k = life > 0f ? 1f - _age[i] / life : 0f;
                if (k <= 0f)
                {
                    t.gameObject.SetActive(false);
                    continue;
                }
                t.localScale = Vector3.one * (_size[i] * k);
            }
        }

        public void HideAll()
        {
            for (int i = 0; i < _items.Length; i++)
            {
                _items[i].gameObject.SetActive(false);
                _age[i] = float.MaxValue;
            }
        }
    }
}
