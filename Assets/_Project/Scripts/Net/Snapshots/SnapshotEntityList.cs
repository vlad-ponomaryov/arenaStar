using System;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// Compact list of snapshot entries keyed by simulation slot (Server-Spec §4.4 "вид хранит компактные списки"): at most
    /// <see cref="Capacity"/> entries out of <see cref="SlotRange"/> slots, an index by slot for O(1) lookup. Preallocated,
    /// no allocations after construction. Order of entries depends on the order of operations; <see cref="ContentEquals"/>
    /// compares by slot.
    /// </summary>
    public sealed class SnapshotEntityList<T> where T : struct, IEquatable<T>, ISnapshotEntry<T>
    {
        private const short NoIndex = -1;

        private readonly T[] _items;
        private readonly int[] _slots;
        private readonly short[] _indexBySlot;

        public SnapshotEntityList(int capacity, int slotRange)
        {
            if (capacity <= 0 || slotRange <= 0 || slotRange > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
            _slots = new int[capacity];
            _indexBySlot = new short[slotRange];
            for (int i = 0; i < slotRange; i++) _indexBySlot[i] = NoIndex;
        }

        public int Count { get; private set; }

        public int Capacity => _items.Length;

        public int SlotRange => _indexBySlot.Length;

        public bool IsFull => Count >= _items.Length;

        public int SlotAt(int index) => _slots[index];

        public ref T ItemAt(int index) => ref _items[index];

        /// <summary>Index of the entry of <paramref name="slot"/>, or −1.</summary>
        public int IndexOf(int slot) => (uint)slot < (uint)_indexBySlot.Length ? _indexBySlot[slot] : -1;

        public bool Contains(int slot) => IndexOf(slot) >= 0;

        /// <summary>The entry of <paramref name="slot"/>, or <c>default</c> (what a new entry is coded against).</summary>
        public T GetOrDefault(int slot)
        {
            int i = IndexOf(slot);
            return i >= 0 ? _items[i] : default;
        }

        /// <summary>Adds or replaces the entry of <paramref name="slot"/>; false if the slot is out of range or the list is full.</summary>
        public bool Set(int slot, in T item)
        {
            if ((uint)slot >= (uint)_indexBySlot.Length) return false;
            int i = _indexBySlot[slot];
            if (i >= 0)
            {
                _items[i] = item;
                return true;
            }
            if (Count >= _items.Length) return false;
            i = Count++;
            _items[i] = item;
            _slots[i] = slot;
            _indexBySlot[slot] = (short)i;
            return true;
        }

        /// <summary>Removes the entry of <paramref name="slot"/> (the last entry takes its place); false if there is none.</summary>
        public bool Remove(int slot)
        {
            int i = IndexOf(slot);
            if (i < 0) return false;
            int last = Count - 1;
            if (i != last)
            {
                _items[i] = _items[last];
                _slots[i] = _slots[last];
                _indexBySlot[_slots[i]] = (short)i;
            }
            _items[last] = default;
            _indexBySlot[slot] = NoIndex;
            Count = last;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++)
            {
                _indexBySlot[_slots[i]] = NoIndex;
                _items[i] = default;
            }
            Count = 0;
        }

        public void CopyFrom(SnapshotEntityList<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return;
            if (other.Capacity != Capacity || other.SlotRange != SlotRange) throw new ArgumentException("SnapshotEntityList: shapes differ");
            Clear();
            int n = other.Count;
            Array.Copy(other._items, _items, n);
            Array.Copy(other._slots, _slots, n);
            for (int i = 0; i < n; i++) _indexBySlot[_slots[i]] = (short)i;
            Count = n;
        }

        /// <summary>Same slots with equal entries, in any order.</summary>
        public bool ContentEquals(SnapshotEntityList<T> other)
        {
            if (other == null || other.Count != Count) return false;
            for (int i = 0; i < Count; i++)
            {
                int j = other.IndexOf(_slots[i]);
                if (j < 0 || !_items[i].Equals(other._items[j])) return false;
            }
            return true;
        }
    }
}
