using System;

namespace Arena.Gameplay.Audio
{
    /// <summary>
    /// Per fighter slot and cue: when the cue may play again (cooldown) and which clip variant played last (a random variant,
    /// never the same twice in a row). No allocations after construction.
    /// </summary>
    public sealed class SfxCueState
    {
        private readonly int _kinds;
        private readonly float[] _readyAt;
        private readonly int[] _lastClip;

        public SfxCueState(int slots, int kinds)
        {
            if (slots < 1) throw new ArgumentOutOfRangeException(nameof(slots));
            if (kinds < 1) throw new ArgumentOutOfRangeException(nameof(kinds));
            _kinds = kinds;
            _readyAt = new float[slots * kinds];
            _lastClip = new int[slots * kinds];
            Reset();
        }

        /// <summary>True (and the cooldown starts) if the cue of the slot is off cooldown at <paramref name="now"/>.</summary>
        public bool TryUse(int slot, int kind, float cooldown, float now)
        {
            int i = Index(slot, kind);
            if (i < 0 || now < _readyAt[i]) return false;
            _readyAt[i] = now + cooldown;
            return true;
        }

        /// <summary>Random variant 0…<paramref name="count"/>−1, not the last one of this slot and cue (when there are two or more).</summary>
        public int PickClip(int slot, int kind, int count, Random rng)
        {
            if (count <= 1) return 0;
            int i = Index(slot, kind);
            int last = i >= 0 ? _lastClip[i] : -1;
            int pick;
            if (last < 0 || last >= count)
            {
                pick = rng.Next(count);
            }
            else
            {
                pick = rng.Next(count - 1);
                if (pick >= last) pick++;
            }
            if (i >= 0) _lastClip[i] = pick;
            return pick;
        }

        public void Reset()
        {
            for (int i = 0; i < _readyAt.Length; i++)
            {
                _readyAt[i] = float.NegativeInfinity;
                _lastClip[i] = -1;
            }
        }

        private int Index(int slot, int kind)
        {
            if (slot < 0 || kind < 0 || kind >= _kinds) return -1;
            int i = slot * _kinds + kind;
            return i < _readyAt.Length ? i : -1;
        }
    }
}
