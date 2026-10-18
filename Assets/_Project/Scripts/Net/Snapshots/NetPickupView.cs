using System;
using Arena.Core.State;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// A pickup (acorn, berry) on the map (Server-Spec §4.4 item 7). <see cref="SpawnTick"/> tells a reused slot from the old item.
    /// The slot is the key of <see cref="SnapshotEntityList{T}"/>.
    /// When adding a field: add it to <see cref="Equals(NetPickupView)"/>, <see cref="WriteDelta"/> and <see cref="ReadDelta"/>.
    /// </summary>
    public struct NetPickupView : IEquatable<NetPickupView>, ISnapshotEntry<NetPickupView>
    {
        public PickupKind Kind;
        public int Value;
        public ushort PositionX;
        public ushort PositionY;
        public int PickableFromTick;
        public int SpawnTick;

        public void WriteDelta(BitWriter w, in NetPickupView prev, int baseTick)
        {
            DeltaCodec.WriteInt(w, (int)Kind, (int)prev.Kind);
            DeltaCodec.WriteInt(w, Value, prev.Value);
            DeltaCodec.WritePoint(w, PositionX, PositionY, prev.PositionX, prev.PositionY);
            DeltaCodec.WriteTick(w, PickableFromTick, prev.PickableFromTick, baseTick);
            DeltaCodec.WriteTick(w, SpawnTick, prev.SpawnTick, baseTick);
        }

        public NetPickupView ReadDelta(BitReader r, int baseTick)
        {
            NetPickupView v = this;
            v.Kind = (PickupKind)DeltaCodec.ReadInt(r, (int)Kind);
            v.Value = DeltaCodec.ReadInt(r, Value);
            DeltaCodec.ReadPoint(r, ref v.PositionX, ref v.PositionY);
            v.PickableFromTick = DeltaCodec.ReadTick(r, PickableFromTick, baseTick);
            v.SpawnTick = DeltaCodec.ReadTick(r, SpawnTick, baseTick);
            return v;
        }

        public bool Equals(NetPickupView o) => Kind == o.Kind && Value == o.Value && PositionX == o.PositionX
            && PositionY == o.PositionY && PickableFromTick == o.PickableFromTick && SpawnTick == o.SpawnTick;

        public override bool Equals(object obj) => obj is NetPickupView other && Equals(other);
        public override int GetHashCode() => (SpawnTick * 397) ^ (PositionX * 31) ^ PositionY;
    }
}
