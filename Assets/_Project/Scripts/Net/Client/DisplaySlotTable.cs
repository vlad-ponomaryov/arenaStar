using System;

namespace Arena.Net.Client
{
    /// <summary>
    /// Display slots of projectiles or areas in the presentation state (Server-Spec §12.2): own entities come from the shadow
    /// (its slots change on every rebuild), foreign ones from server slots, and both share one array — so each entity gets a
    /// slot by a key (owner, spawn tick, ability, ordinal among equal ones in iteration order) that stays the same between
    /// re-simulations; view pools then recognise "the same projectile" by slot and spawn tick as offline. The table also
    /// remembers the tick shift an entity was first shown with, so its shifted tick fields do not wobble when the shift
    /// between the prediction and render clocks changes by a tick. Per build: <see cref="Begin"/>, <see cref="Acquire"/> per
    /// entity, <see cref="End"/> (frees the keys not seen). No allocations.
    /// </summary>
    public sealed class DisplaySlotTable
    {
        private const long OrdinalMask = 0xFF;

        private readonly long[] _key;
        private readonly bool[] _used;
        private readonly bool[] _seen;
        private readonly int[] _shift;

        public DisplaySlotTable(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _key = new long[capacity];
            _used = new bool[capacity];
            _seen = new bool[capacity];
            _shift = new int[capacity];
        }

        public int Capacity => _key.Length;

        /// <summary>Entities that found no free slot (counted, not shown).</summary>
        public long Overflows { get; private set; }

        /// <summary>Key without the ordinal: owner, spawn tick and ability.</summary>
        public static long MakeKey(int owner, int spawnTick, int abilityRef) =>
            ((long)(uint)spawnTick << 32) | ((long)(owner & 0xFF) << 24) | ((long)(abilityRef & 0xFFFF) << 8);

        public void Begin()
        {
            for (int i = 0; i < _seen.Length; i++) _seen[i] = false;
        }

        /// <summary>
        /// The display slot of the entity with <paramref name="key"/> (from <see cref="MakeKey"/>): its old slot, or
        /// <paramref name="preferred"/> if free, or the first free one; −1 if the table is full. <paramref name="shift"/> is stored
        /// for a new entity; <paramref name="birthShift"/> returns the stored one.
        /// </summary>
        public int Acquire(long key, int preferred, int shift, out int birthShift)
        {
            int ordinal = 0;
            for (int i = 0; i < _key.Length; i++)
                if (_seen[i] && (_key[i] & ~OrdinalMask) == key) ordinal++;
            long full = key | (ordinal & OrdinalMask);
            for (int i = 0; i < _key.Length; i++)
            {
                if (!_used[i] || _seen[i] || _key[i] != full) continue;
                _seen[i] = true;
                birthShift = _shift[i];
                return i;
            }
            int slot = (uint)preferred < (uint)_key.Length && !_used[preferred] ? preferred : -1;
            if (slot < 0)
                for (int i = 0; i < _used.Length; i++)
                    if (!_used[i])
                    {
                        slot = i;
                        break;
                    }
            if (slot < 0)
            {
                Overflows++;
                birthShift = shift;
                return -1;
            }
            _used[slot] = true;
            _seen[slot] = true;
            _key[slot] = full;
            _shift[slot] = shift;
            birthShift = shift;
            return slot;
        }

        public void End()
        {
            for (int i = 0; i < _used.Length; i++)
                if (_used[i] && !_seen[i]) _used[i] = false;
        }

        public void Clear()
        {
            for (int i = 0; i < _used.Length; i++) _used[i] = _seen[i] = false;
        }
    }
}
