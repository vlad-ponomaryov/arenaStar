using System.Collections.Generic;
using Arena.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Pop-up damage / heal numbers (GDD §5.3): pooled uGUI texts on the <see cref="FloatingTextLayer"/>; each rises from the
    /// hit point for <see cref="FeelConfig.NumberSeconds"/> and fades out in its second half. Strings come from
    /// <see cref="NumberStringCache"/>. The pool is prewarmed at match start and grows only if more numbers are shown at once.
    /// </summary>
    public sealed class DamageNumberPool
    {
        private sealed class Entry
        {
            public Text Text;
            public Vector3 World;
            public float Age;
            public Color Color;
            public bool Active;
        }

        // Fractional part of the golden ratio: a low-discrepancy sequence of scatter offsets.
        private const float GoldenFraction = 0.618034f;

        // Numbers keep full opacity for the first half of their life, then fade out.
        private const float FadeStart = 0.5f;

        private readonly FloatingTextLayer _layer;
        private readonly FeelConfig _feel;
        private readonly List<Entry> _entries = new List<Entry>();
        private int _spawnCounter;

        public DamageNumberPool(FloatingTextLayer layer, FeelConfig feel)
        {
            _layer = layer;
            _feel = feel;
            for (int i = 0; i < feel.NumberPoolPrewarm; i++) _entries.Add(CreateEntry());
        }

        public int Capacity => _entries.Count;

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Count; i++)
                    if (_entries[i].Active) n++;
                return n;
            }
        }

        /// <summary>Shows <paramref name="amount"/> at <paramref name="world"/> (the target's feet; the height comes from the config).</summary>
        public void Spawn(Vector3 world, int amount, Color color, bool large)
        {
            Entry e = FindFree();
            // Deterministic sideways scatter (golden-ratio sequence), so numbers of a burst do not overlap.
            _spawnCounter++;
            float side = (_spawnCounter * GoldenFraction % 1f - 0.5f) * 2f * _feel.NumberScatter;
            e.World = world + new Vector3(side, _feel.NumberHeight, 0f);
            e.Age = 0f;
            e.Color = color;
            e.Active = true;
            e.Text.text = NumberStringCache.Get(amount);
            e.Text.fontSize = large ? _feel.UltNumberFontSize : _feel.NumberFontSize;
            e.Text.color = color;
        }

        /// <param name="deltaTime">Unscaled frame time, seconds.</param>
        public void Update(Camera camera, float deltaTime)
        {
            float life = _feel.NumberSeconds;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (!e.Active) continue;
                e.Age += deltaTime;
                if (e.Age >= life)
                {
                    Release(e);
                    continue;
                }
                Vector3 world = e.World + Vector3.up * (_feel.NumberRiseSpeed * e.Age);
                bool placed = FloatingTextLayer.Place(e.Text, camera, world);
                if (e.Text.gameObject.activeSelf != placed) e.Text.gameObject.SetActive(placed);
                float t = e.Age / life;
                Color c = e.Color;
                c.a = t < FadeStart ? e.Color.a : e.Color.a * (1f - (t - FadeStart) / (1f - FadeStart));
                e.Text.color = c;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Active) Release(_entries[i]);
        }

        private Entry FindFree()
        {
            for (int i = 0; i < _entries.Count; i++)
                if (!_entries[i].Active) return _entries[i];
            Entry e = CreateEntry();
            _entries.Add(e);
            return e;
        }

        private Entry CreateEntry() =>
            new Entry { Text = _layer.CreateText("DamageNumber" + _entries.Count, _feel.NumberFontSize, _feel.DamageColor) };

        private static void Release(Entry e)
        {
            e.Active = false;
            if (e.Text.gameObject.activeSelf) e.Text.gameObject.SetActive(false);
        }
    }
}
