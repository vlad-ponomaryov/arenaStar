using System;
using Arena.Core.Defs;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// An area visible to the viewer's team (Server-Spec §4.4 item 6; hidden mines by D-030): quantized point and radius,
    /// ticks of creation, activation and expiry. The slot is the key of <see cref="SnapshotEntityList{T}"/>.
    /// When adding a field: add it to <see cref="Equals(NetAreaView)"/>, <see cref="WriteDelta"/> and <see cref="ReadDelta"/>.
    /// </summary>
    public struct NetAreaView : IEquatable<NetAreaView>, ISnapshotEntry<NetAreaView>
    {
        public AreaKind Kind;
        public int AbilityRef;
        public int Owner;
        public int Team;
        public ushort PositionX;
        public ushort PositionY;

        /// <summary>Start of a lob's arc (= position for placed areas).</summary>
        public ushort OriginX;
        public ushort OriginY;

        /// <summary><c>Quantize.ToRadius</c>.</summary>
        public byte Radius;

        public int SpawnTick;
        public int ActivateTick;
        public int ExpireTick;
        public bool Triggered;

        public void WriteDelta(BitWriter w, in NetAreaView prev, int baseTick)
        {
            DeltaCodec.WriteInt(w, (int)Kind, (int)prev.Kind);
            DeltaCodec.WriteInt(w, AbilityRef, prev.AbilityRef);
            DeltaCodec.WriteInt(w, Owner, prev.Owner);
            DeltaCodec.WriteInt(w, Team, prev.Team);
            DeltaCodec.WritePoint(w, PositionX, PositionY, prev.PositionX, prev.PositionY);
            DeltaCodec.WritePoint(w, OriginX, OriginY, prev.OriginX, prev.OriginY);
            DeltaCodec.WriteBits(w, Radius, prev.Radius, NetProtocol.RadiusBits);
            DeltaCodec.WriteTick(w, SpawnTick, prev.SpawnTick, baseTick);
            DeltaCodec.WriteTick(w, ActivateTick, prev.ActivateTick, baseTick);
            DeltaCodec.WriteTick(w, ExpireTick, prev.ExpireTick, baseTick);
            DeltaCodec.WriteBool(w, Triggered, prev.Triggered);
        }

        public NetAreaView ReadDelta(BitReader r, int baseTick)
        {
            NetAreaView v = this;
            v.Kind = (AreaKind)DeltaCodec.ReadInt(r, (int)Kind);
            v.AbilityRef = DeltaCodec.ReadInt(r, AbilityRef);
            v.Owner = DeltaCodec.ReadInt(r, Owner);
            v.Team = DeltaCodec.ReadInt(r, Team);
            DeltaCodec.ReadPoint(r, ref v.PositionX, ref v.PositionY);
            DeltaCodec.ReadPoint(r, ref v.OriginX, ref v.OriginY);
            v.Radius = (byte)DeltaCodec.ReadBits(r, Radius, NetProtocol.RadiusBits);
            v.SpawnTick = DeltaCodec.ReadTick(r, SpawnTick, baseTick);
            v.ActivateTick = DeltaCodec.ReadTick(r, ActivateTick, baseTick);
            v.ExpireTick = DeltaCodec.ReadTick(r, ExpireTick, baseTick);
            v.Triggered = DeltaCodec.ReadBool(r, Triggered);
            return v;
        }

        public bool Equals(NetAreaView o) => Kind == o.Kind && AbilityRef == o.AbilityRef && Owner == o.Owner && Team == o.Team
            && PositionX == o.PositionX && PositionY == o.PositionY && OriginX == o.OriginX && OriginY == o.OriginY
            && Radius == o.Radius && SpawnTick == o.SpawnTick && ActivateTick == o.ActivateTick && ExpireTick == o.ExpireTick
            && Triggered == o.Triggered;

        public override bool Equals(object obj) => obj is NetAreaView other && Equals(other);
        public override int GetHashCode() => (SpawnTick * 397) ^ (PositionX * 31) ^ PositionY;
    }
}
